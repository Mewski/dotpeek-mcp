using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DotPeekMcp.Protocol;

namespace DotPeekMcp.Plugin.Bridge;

internal sealed class PluginBridgeServer : IDisposable {
  private const int MaxHeaderBytes = 64 * 1024;
  private const int MaxBodyBytes = 8 * 1024 * 1024;

  private readonly DotPeekToolDispatcher _dispatcher;
  private readonly CancellationTokenSource _stop = new();
  private readonly TcpListener _listener;
  private bool _disposed;

  public PluginBridgeServer(DotPeekToolDispatcher dispatcher) {
    _dispatcher = dispatcher;
    _listener = new TcpListener(IPAddress.Loopback, BridgeDefaults.GetDefaultBaseUri().Port);
  }

  public void Start() {
    if (_disposed) {
      throw new ObjectDisposedException(nameof(PluginBridgeServer));
    }

    _listener.Start();
    _ = Task.Run(() => AcceptLoopAsync(_stop.Token));
  }

  public void Dispose() {
    if (_disposed) {
      return;
    }

    _disposed = true;
    _stop.Cancel();
    _listener.Stop();
    _stop.Dispose();
  }

  private async Task AcceptLoopAsync(CancellationToken cancellationToken) {
    while (!cancellationToken.IsCancellationRequested) {
      TcpClient client;
      try {
        client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
      }
      catch (OperationCanceledException) {
        break;
      }
      catch (ObjectDisposedException) {
        break;
      }

      _ = Task.Run(() => HandleClientAsync(client, cancellationToken), CancellationToken.None);
    }
  }

  private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken) {
    using (client) {
      var stream = client.GetStream();

      try {
        var request = await ReadHttpRequestAsync(stream, cancellationToken).ConfigureAwait(false);
        if (request is null) {
          return;
        }

        if (request.Method == "GET" && request.Path == "/health") {
          await WriteJsonAsync(stream, HttpStatusCode.OK, _dispatcher.Dispatch(new BridgeToolCall(ToolCatalog.Health, default)), cancellationToken).ConfigureAwait(false);
          return;
        }

        if (request.Method == "POST" && request.Path == "/tools/call") {
          BridgeToolResult result;
          try {
            var requestJson = Encoding.UTF8.GetString(request.Body);
            var call = JsonSerializer.Deserialize<BridgeToolCall>(requestJson, JsonDefaults.Options);
            result = call is null
                ? BridgeToolResult.FromError("invalid_request", "Request body is empty.")
                : _dispatcher.Dispatch(call);
          }
          catch (JsonException exception) {
            result = DotPeekToolDispatcher.InvalidJson(exception);
          }

          await WriteJsonAsync(stream, HttpStatusCode.OK, result, cancellationToken).ConfigureAwait(false);
          return;
        }

        await WriteJsonAsync(stream, HttpStatusCode.NotFound, BridgeToolResult.FromError("not_found", "Unknown bridge endpoint."), cancellationToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException) {
        // dotPeek is shutting down.
      }
    }
  }

  private static async Task<HttpRequest?> ReadHttpRequestAsync(NetworkStream stream, CancellationToken cancellationToken) {
    using var headerBuffer = new MemoryStream();
    var oneByte = new byte[1];

    while (headerBuffer.Length < MaxHeaderBytes) {
      var read = await stream.ReadAsync(oneByte, 0, oneByte.Length, cancellationToken).ConfigureAwait(false);
      if (read == 0) {
        return null;
      }

      headerBuffer.WriteByte(oneByte[0]);
      if (EndsWithHeaderTerminator(headerBuffer)) {
        break;
      }
    }

    if (!EndsWithHeaderTerminator(headerBuffer)) {
      throw new InvalidOperationException("HTTP headers exceed bridge limit.");
    }

    var headerText = Encoding.ASCII.GetString(headerBuffer.ToArray());
    var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
    var requestLine = lines[0].Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
    if (requestLine.Length < 2) {
      return null;
    }

    var contentLength = 0;
    foreach (var line in lines.Skip(1)) {
      var separator = line.IndexOf(':');
      if (separator < 0) {
        continue;
      }

      var name = line.Substring(0, separator).Trim();
      var value = line.Substring(separator + 1).Trim();
      if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
          && int.TryParse(value, out var parsedLength)) {
        contentLength = parsedLength;
      }
    }

    if (contentLength < 0 || contentLength > MaxBodyBytes) {
      throw new InvalidOperationException("HTTP body exceeds bridge limit.");
    }

    var body = new byte[contentLength];
    if (contentLength > 0) {
      await ReadExactlyAsync(stream, body, cancellationToken).ConfigureAwait(false);
    }

    return new HttpRequest(requestLine[0], requestLine[1], body);
  }

  private static bool EndsWithHeaderTerminator(MemoryStream stream) {
    if (stream.Length < 4) {
      return false;
    }

    var bytes = stream.GetBuffer();
    var length = (int)stream.Length;
    return bytes[length - 4] == '\r'
        && bytes[length - 3] == '\n'
        && bytes[length - 2] == '\r'
        && bytes[length - 1] == '\n';
  }

  private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, CancellationToken cancellationToken) {
    var offset = 0;
    while (offset < buffer.Length) {
      var read = await stream.ReadAsync(buffer, offset, buffer.Length - offset, cancellationToken).ConfigureAwait(false);
      if (read == 0) {
        throw new InvalidOperationException("Unexpected end of HTTP request body.");
      }

      offset += read;
    }
  }

  private static async Task WriteJsonAsync(NetworkStream stream, HttpStatusCode statusCode, object body, CancellationToken cancellationToken) {
    var json = JsonSerializer.Serialize(body, JsonDefaults.Options);
    var payload = Encoding.UTF8.GetBytes(json);
    var headers = Encoding.ASCII.GetBytes(
        $"HTTP/1.1 {(int)statusCode} {statusCode}\r\n" +
        "Content-Type: application/json; charset=utf-8\r\n" +
        $"Content-Length: {payload.Length}\r\n" +
        "Connection: close\r\n" +
        "\r\n");

    await stream.WriteAsync(headers, 0, headers.Length, cancellationToken).ConfigureAwait(false);
    await stream.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
  }

  private sealed class HttpRequest {
    public HttpRequest(string method, string path, byte[] body) {
      Method = method;
      Path = path;
      Body = body;
    }

    public string Method { get; }
    public string Path { get; }
    public byte[] Body { get; }
  }
}
