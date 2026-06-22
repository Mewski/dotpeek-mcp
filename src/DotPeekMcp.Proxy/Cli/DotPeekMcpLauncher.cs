using System.Diagnostics;
using System.Text.Json;
using DotPeekMcp.Protocol;

namespace DotPeekMcp.Proxy.Cli;

internal static class DotPeekMcpLauncher {
  public static async Task<int> LaunchAsync(CommandLine options, TextWriter stdout, CancellationToken cancellationToken) {
    var installRoot = DotPeekMcpPaths.ResolveInstallRoot(options);
    var pluginDir = options.GetOption(DotPeekMcpPaths.GetPluginDir(installRoot), "--plugin-dir");
    var packagesFile = DotPeekMcpPaths.GetPackagesFile(installRoot);
    var dotPeekPath = DotPeekMcpPaths.ResolveDotPeekPath(options, installRoot);

    DotPeekMcpPaths.WritePackageFile(pluginDir, packagesFile);

    if (!options.HasFlag("--no-stop")) {
      StopDotPeek();
    }

    StartDotPeek(dotPeekPath, packagesFile);
    await stdout.WriteLineAsync("Started dotPeek with JET_ADDITIONAL_DEPLOYED_PACKAGES_FILE=" + packagesFile).ConfigureAwait(false);

    if (options.HasFlag("--wait")) {
      var timeoutSeconds = options.GetIntOption(60, "--timeout");
      var health = await WaitForBridgeAsync(TimeSpan.FromSeconds(timeoutSeconds), requireNativeServices: true, cancellationToken).ConfigureAwait(false);
      await stdout.WriteLineAsync("dotPeek MCP bridge and native services are healthy: process_id=" + health.ProcessId).ConfigureAwait(false);
    }

    return 0;
  }

  private static void StartDotPeek(string dotPeekPath, string packagesFile) {
    var startInfo = new ProcessStartInfo(dotPeekPath) {
      UseShellExecute = false,
      WorkingDirectory = Path.GetDirectoryName(dotPeekPath) ?? Environment.CurrentDirectory
    };
    startInfo.Environment["JET_ADDITIONAL_DEPLOYED_PACKAGES_FILE"] = packagesFile;

    using var _ = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start dotPeek: " + dotPeekPath);
  }

  public static async Task EnsureBridgeReadyOrLaunchAsync(
      CommandLine options,
      TextWriter stderr,
      CancellationToken cancellationToken) {
    try {
      await WaitForBridgeAsync(TimeSpan.FromSeconds(2), requireNativeServices: true, cancellationToken).ConfigureAwait(false);
      return;
    }
    catch (TimeoutException) {
    }

    if (options.HasFlag("--no-auto-launch")) {
      return;
    }

    var installRoot = DotPeekMcpPaths.ResolveInstallRoot(options);
    var pluginDir = options.GetOption(DotPeekMcpPaths.GetPluginDir(installRoot), "--plugin-dir");
    var packagesFile = DotPeekMcpPaths.GetPackagesFile(installRoot);
    var dotPeekPath = DotPeekMcpPaths.ResolveDotPeekPath(options, installRoot);
    var timeoutSeconds = options.GetIntOption(60, "--auto-launch-timeout");

    DotPeekMcpPaths.WritePackageFile(pluginDir, packagesFile);

    await stderr.WriteLineAsync("[dotpeek-mcp] dotPeek bridge is not running; starting dotPeek.").ConfigureAwait(false);
    StartDotPeek(dotPeekPath, packagesFile);

    try {
      var health = await WaitForBridgeAsync(TimeSpan.FromSeconds(timeoutSeconds), requireNativeServices: true, cancellationToken).ConfigureAwait(false);
      await stderr.WriteLineAsync("[dotpeek-mcp] dotPeek MCP bridge is ready: process_id=" + health.ProcessId).ConfigureAwait(false);
    }
    catch (TimeoutException exception) when (Process.GetProcessesByName("dotPeek64").Length > 0) {
      throw new TimeoutException(
        exception.Message + " If dotPeek was already running without the plugin package, close it and run `dotpeek-mcp launch --wait` once.",
        exception);
    }
  }

  public static void StopDotPeek() {
    foreach (var process in Process.GetProcessesByName("dotPeek64")) {
      try {
        process.Kill(entireProcessTree: true);
        process.WaitForExit(5000);
      }
      catch (InvalidOperationException) {
      }
      finally {
        process.Dispose();
      }
    }
  }

  public static async Task<BridgeHealth> WaitForBridgeAsync(TimeSpan timeout, bool requireNativeServices, CancellationToken cancellationToken) {
    using var httpClient = new HttpClient {
      Timeout = TimeSpan.FromSeconds(2)
    };
    var deadline = DateTimeOffset.UtcNow.Add(timeout);
    var healthUri = new Uri(BridgeDefaults.GetDefaultBaseUri(), "health");

    while (DateTimeOffset.UtcNow < deadline) {
      cancellationToken.ThrowIfCancellationRequested();
      try {
        using var response = await httpClient.GetAsync(healthUri, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode) {
          var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
          var health = TryReadHealth(bytes);
          if (health is not null && (!requireNativeServices || health.NativeServicesAvailable)) {
            return health;
          }
        }
      }
      catch (HttpRequestException) {
      }
      catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) {
      }
      catch (JsonException) {
      }

      await Task.Delay(500, cancellationToken).ConfigureAwait(false);
    }

    throw new TimeoutException("dotPeek MCP bridge did not become ready at " + healthUri + " within " + timeout.TotalSeconds + " seconds.");
  }

  private static BridgeHealth? TryReadHealth(byte[] bytes) {
    using var document = JsonDocument.Parse(bytes);
    var root = document.RootElement;
    if (!root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True) {
      return null;
    }

    if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) {
      return null;
    }

    return new BridgeHealth(
      GetInt(data, "process_id"),
      GetAvailable(data, "native_decompiler"),
      GetAvailable(data, "assembly_explorer"),
      GetAvailable(data, "native_export"));
  }

  private static int GetInt(JsonElement element, string propertyName) {
    return element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var number) ? number : 0;
  }

  private static bool GetAvailable(JsonElement element, string propertyName) {
    if (!element.TryGetProperty(propertyName, out var section) || section.ValueKind != JsonValueKind.Object) {
      return false;
    }

    return section.TryGetProperty("available", out var available) && available.ValueKind == JsonValueKind.True;
  }
}

internal sealed class BridgeHealth {
  public BridgeHealth(int processId, bool nativeDecompiler, bool assemblyExplorer, bool nativeExport) {
    ProcessId = processId;
    NativeDecompiler = nativeDecompiler;
    AssemblyExplorer = assemblyExplorer;
    NativeExport = nativeExport;
  }

  public int ProcessId { get; }
  public bool NativeDecompiler { get; }
  public bool AssemblyExplorer { get; }
  public bool NativeExport { get; }
  public bool NativeServicesAvailable => NativeDecompiler && AssemblyExplorer && NativeExport;
}
