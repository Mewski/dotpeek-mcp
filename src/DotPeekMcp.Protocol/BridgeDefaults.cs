namespace DotPeekMcp.Protocol;

public static class BridgeDefaults {
  public const string Host = "127.0.0.1";
  public const int Port = 8767;
  public const string UrlEnvironmentVariable = "DOTPEEK_MCP_BRIDGE_URL";
  public const string PortEnvironmentVariable = "DOTPEEK_MCP_BRIDGE_PORT";

  public static Uri GetDefaultBaseUri() {
    var configuredUrl = Environment.GetEnvironmentVariable(UrlEnvironmentVariable);
    if (!string.IsNullOrWhiteSpace(configuredUrl) && Uri.TryCreate(configuredUrl, UriKind.Absolute, out var uri)) {
      return uri;
    }

    var port = Port;
    var configuredPort = Environment.GetEnvironmentVariable(PortEnvironmentVariable);
    if (int.TryParse(configuredPort, out var parsedPort) && parsedPort > 0 && parsedPort <= 65535) {
      port = parsedPort;
    }

    return new Uri($"http://{Host}:{port}/");
  }
}
