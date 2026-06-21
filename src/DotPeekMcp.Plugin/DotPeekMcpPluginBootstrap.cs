using System.Runtime.CompilerServices;
using System.Diagnostics;
using DotPeekMcp.Plugin.Bridge;
using DotPeekMcp.Protocol;

namespace DotPeekMcp.Plugin;

internal static class DotPeekMcpPluginBootstrap {
  private static readonly object Gate = new();
  private static PluginBridgeServer? _server;

#pragma warning disable CA2255
  [ModuleInitializer]
#pragma warning restore CA2255
  public static void Initialize() {
    EnsureStarted("module-initializer");
  }

  public static void EnsureStarted(string reason) {
    lock (Gate) {
      if (_server is not null) {
        Log($"Bridge already started; reason={reason}");
        return;
      }

      try {
        var dispatcher = new DotPeekToolDispatcher();
        var server = new PluginBridgeServer(dispatcher);
        server.Start();
        _server = server;
        Log($"Bridge started; reason={reason}; url={BridgeDefaults.GetDefaultBaseUri()}; pid={Process.GetCurrentProcess().Id}");
      }
      catch (Exception exception) {
        Log($"Bridge start failed; reason={reason}; {exception}");
      }
    }
  }

  public static void Stop(string reason) {
    lock (Gate) {
      if (_server is null) {
        return;
      }

      _server.Dispose();
      _server = null;
      Log($"Bridge stopped; reason={reason}");
    }
  }

  internal static void Log(string message) {
    try {
      var directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JetBrains",
        "dotpeek-mcp");
      Directory.CreateDirectory(directory);

      var path = Path.Combine(directory, "plugin.log");
      File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
    }
    catch {
      // Logging must never prevent dotPeek from loading the plugin.
    }
  }
}
