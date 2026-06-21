using DotPeekMcp.Protocol;
using DotPeekMcp.Proxy.Bridge;
using DotPeekMcp.Proxy.Mcp;

var bridgeUri = GetBridgeUri(args);
var bridgeClient = new DotPeekBridgeClient(bridgeUri);
var server = new McpServer(Console.OpenStandardInput(), Console.OpenStandardOutput(), bridgeClient);

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => {
  eventArgs.Cancel = true;
  shutdown.Cancel();
};

await server.RunAsync(shutdown.Token).ConfigureAwait(false);

static Uri GetBridgeUri(string[] args) {
  for (var index = 0; index < args.Length; index++) {
    if (args[index] == "--bridge-url" && index + 1 < args.Length) {
      return new Uri(args[index + 1], UriKind.Absolute);
    }
  }

  return BridgeDefaults.GetDefaultBaseUri();
}
