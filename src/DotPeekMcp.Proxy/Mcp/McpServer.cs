using System.Buffers;
using System.Text;
using System.Text.Json;
using DotPeekMcp.Protocol;
using DotPeekMcp.Proxy.Bridge;

namespace DotPeekMcp.Proxy.Mcp;

internal sealed class McpServer {
  private const int MaxHeaderBytes = 64 * 1024;
  private const int MaxBodyBytes = 8 * 1024 * 1024;

  private readonly Stream _input;
  private readonly Stream _output;
  private readonly DotPeekBridgeClient _bridgeClient;
  private readonly string _version;

  public McpServer(Stream input, Stream output, DotPeekBridgeClient bridgeClient, string version) {
    _input = input;
    _output = output;
    _bridgeClient = bridgeClient;
    _version = version;
  }

  public async Task RunAsync(CancellationToken cancellationToken) {
    while (!cancellationToken.IsCancellationRequested) {
      using var document = await ReadMessageAsync(cancellationToken).ConfigureAwait(false);
      if (document is null) {
        break;
      }

      var response = await HandleMessageAsync(document.RootElement, cancellationToken).ConfigureAwait(false);
      if (response is not null) {
        await WriteMessageAsync(response, cancellationToken).ConfigureAwait(false);
      }
    }
  }

  private async Task<object?> HandleMessageAsync(JsonElement message, CancellationToken cancellationToken) {
    var id = GetId(message);
    if (!message.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String) {
      return id is null ? null : Error(id, -32600, "Invalid JSON-RPC request.");
    }

    var method = methodElement.GetString();
    return method switch {
      "initialize" => Response(id, new {
        protocolVersion = "2024-11-05",
        capabilities = new {
          tools = new { listChanged = false }
        },
        serverInfo = new {
          name = "dotpeek-mcp",
          version = _version
        }
      }),
      "notifications/initialized" => null,
      "ping" => Response(id, new { }),
      "tools/list" => Response(id, new {
        tools = ToolCatalog.All.Select(tool => new {
          name = tool.Name,
          description = tool.Description,
          inputSchema = tool.InputSchema
        }).ToArray()
      }),
      "tools/call" => await HandleToolCallAsync(id, message, cancellationToken).ConfigureAwait(false),
      _ => id is null ? null : Error(id, -32601, $"Method not found: {method}")
    };
  }

  private async Task<object> HandleToolCallAsync(object? id, JsonElement message, CancellationToken cancellationToken) {
    if (id is null) {
      return Error(null, -32600, "tools/call requires a JSON-RPC id.");
    }

    if (!message.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object) {
      return Error(id, -32602, "tools/call params object is required.");
    }

    if (!parameters.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String) {
      return Error(id, -32602, "tools/call params.name is required.");
    }

    var name = nameElement.GetString();
    if (string.IsNullOrWhiteSpace(name)) {
      return Error(id, -32602, "tools/call params.name must be a non-empty string.");
    }

    using var emptyArguments = JsonDocument.Parse("{}");
    var arguments = parameters.TryGetProperty("arguments", out var argumentsElement)
        ? argumentsElement
        : emptyArguments.RootElement;

    var bridgeResult = await _bridgeClient.CallToolAsync(name, arguments, cancellationToken).ConfigureAwait(false);
    return Response(id, ToMcpToolResult(bridgeResult));
  }

  private static object ToMcpToolResult(BridgeToolResult bridgeResult) {
    var text = bridgeResult.Success
        ? JsonSerializer.Serialize(bridgeResult.Data, JsonDefaults.Options)
        : JsonSerializer.Serialize(bridgeResult.Error, JsonDefaults.Options);

    return new {
      content = new[]
      {
        new {
          type = "text",
          text
        }
      },
      isError = !bridgeResult.Success
    };
  }

  private async Task<JsonDocument?> ReadMessageAsync(CancellationToken cancellationToken) {
    var headerBuffer = new ArrayBufferWriter<byte>();
    var oneByte = new byte[1];

    while (headerBuffer.WrittenCount < MaxHeaderBytes) {
      var read = await _input.ReadAsync(oneByte, cancellationToken).ConfigureAwait(false);
      if (read == 0) {
        return null;
      }

      headerBuffer.Write(oneByte);
      if (EndsWithHeaderTerminator(headerBuffer.WrittenSpan)) {
        break;
      }
    }

    if (!EndsWithHeaderTerminator(headerBuffer.WrittenSpan)) {
      throw new InvalidOperationException("MCP headers exceed limit.");
    }

    var contentLength = ParseContentLength(headerBuffer.WrittenSpan);
    if (contentLength < 0 || contentLength > MaxBodyBytes) {
      throw new InvalidOperationException("MCP content length is invalid.");
    }

    var body = new byte[contentLength];
    if (contentLength > 0) {
      await _input.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
    }

    return JsonDocument.Parse(body);
  }

  private async Task WriteMessageAsync(object message, CancellationToken cancellationToken) {
    var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonDefaults.Options);
    var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");

    await _output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
    await _output.WriteAsync(body, cancellationToken).ConfigureAwait(false);
    await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
  }

  private static int ParseContentLength(ReadOnlySpan<byte> headers) {
    var text = Encoding.ASCII.GetString(headers);
    foreach (var line in text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)) {
      var separator = line.IndexOf(':', StringComparison.Ordinal);
      if (separator < 0) {
        continue;
      }

      var name = line[..separator].Trim();
      var value = line[(separator + 1)..].Trim();
      if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var length)) {
        return length;
      }
    }

    return -1;
  }

  private static bool EndsWithHeaderTerminator(ReadOnlySpan<byte> bytes) {
    return bytes.Length >= 4
        && bytes[^4] == '\r'
        && bytes[^3] == '\n'
        && bytes[^2] == '\r'
        && bytes[^1] == '\n';
  }

  private static object? GetId(JsonElement message) {
    return message.TryGetProperty("id", out var id) ? id.Clone() : null;
  }

  private static object Response(object? id, object result) {
    return new {
      jsonrpc = "2.0",
      id,
      result
    };
  }

  private static object Error(object? id, int code, string message) {
    return new {
      jsonrpc = "2.0",
      id,
      error = new {
        code,
        message
      }
    };
  }
}
