using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DotPeekMcp.Protocol;

namespace DotPeekMcp.Proxy.Bridge;

internal sealed class DotPeekBridgeClient {
  private readonly Uri _baseUri;
  private readonly HttpClient _httpClient;

  public DotPeekBridgeClient(Uri baseUri) {
    _baseUri = baseUri;
    _httpClient = new HttpClient {
      Timeout = TimeSpan.FromSeconds(30)
    };
  }

  public async Task<BridgeToolResult> CallToolAsync(string name, JsonElement arguments, CancellationToken cancellationToken) {
    var call = new BridgeToolCall(name, arguments);
    var body = JsonSerializer.Serialize(call, JsonDefaults.Options);
    using var content = new StringContent(body, Encoding.UTF8);
    content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

    try {
      using var response = await _httpClient.PostAsync(new Uri(_baseUri, "tools/call"), content, cancellationToken).ConfigureAwait(false);
      var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode) {
        return BridgeToolResult.FromError("bridge_http_error", $"dotPeek bridge returned HTTP {(int)response.StatusCode}.");
      }

      var result = JsonSerializer.Deserialize<BridgeToolResult>(bytes, JsonDefaults.Options);
      return result ?? BridgeToolResult.FromError("bridge_invalid_response", "dotPeek bridge returned an empty response.");
    }
    catch (HttpRequestException exception) {
      return BridgeToolResult.FromError("bridge_unreachable", $"dotPeek MCP plugin is not reachable at {_baseUri}: {exception.Message}");
    }
    catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested) {
      return BridgeToolResult.FromError("bridge_timeout", $"dotPeek MCP plugin did not respond at {_baseUri}: {exception.Message}");
    }
    catch (JsonException exception) {
      return BridgeToolResult.FromError("bridge_invalid_json", exception.Message);
    }
  }
}
