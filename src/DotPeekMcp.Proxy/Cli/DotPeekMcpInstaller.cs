using System.Text.Json;
using DotPeekMcp.Protocol;

namespace DotPeekMcp.Proxy.Cli;

internal static class DotPeekMcpInstaller {
  public static async Task<int> InstallAsync(CommandLine options, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken) {
    var installRoot = DotPeekMcpPaths.ResolveInstallRoot(options);
    var configuration = options.GetOption("Release", "--configuration");
    var dotPeekPath = DotPeekMcpPaths.ResolveDotPeekPath(options);
    var dotPeekInstallDir = Path.GetDirectoryName(dotPeekPath) ?? string.Empty;
    var repoRoot = DotPeekMcpPaths.FindRepoRoot(Environment.CurrentDirectory)
        ?? throw new DirectoryNotFoundException("DotPeekMcp.slnx was not found. Run install from the repository root or a child directory.");

    if (!options.HasFlag("--no-stop")) {
      DotPeekMcpLauncher.StopDotPeek();
    }

    PrepareInstallRoot(installRoot);

    if (!options.HasFlag("--no-build")) {
      await RunRequiredAsync(
        "dotnet",
        new[] { "build", Path.Combine(repoRoot, "DotPeekMcp.slnx"), "-c", configuration, "-p:DotPeekInstallDir=" + dotPeekInstallDir },
        stdout,
        stderr,
        cancellationToken).ConfigureAwait(false);

      var proxyProject = Path.Combine(repoRoot, "src", "DotPeekMcp.Proxy", "DotPeekMcp.Proxy.csproj");
      var publishArguments = new List<string> { "publish", proxyProject, "-c", configuration, "-o", DotPeekMcpPaths.GetProxyDir(installRoot) };
      if (options.HasFlag("--self-contained")) {
        publishArguments.AddRange(new[] {
          "-r",
          options.GetOption("win-x64", "--runtime"),
          "--self-contained",
          "true",
          "-p:PublishSingleFile=true"
        });
      }
      else {
        publishArguments.Add("--no-self-contained");
      }

      await RunRequiredAsync("dotnet", publishArguments, stdout, stderr, cancellationToken).ConfigureAwait(false);
    }
    else {
      CopyDirectory(DotPeekMcpPaths.GetProxyBuildDir(repoRoot, configuration), DotPeekMcpPaths.GetProxyDir(installRoot));
    }

    var pluginBuildDir = DotPeekMcpPaths.GetPluginBuildDir(repoRoot, configuration);
    CopyPlugin(pluginBuildDir, DotPeekMcpPaths.GetPluginDir(installRoot));
    DotPeekMcpPaths.WritePackageFile(DotPeekMcpPaths.GetPluginDir(installRoot), DotPeekMcpPaths.GetPackagesFile(installRoot));
    DotPeekMcpPaths.WriteMcpConfig(installRoot);
    WriteManifest(installRoot, dotPeekPath);

    await stdout.WriteLineAsync("Installed dotpeek-mcp to " + installRoot).ConfigureAwait(false);
    await stdout.WriteLineAsync("MCP config: " + DotPeekMcpPaths.GetMcpConfigFile(installRoot)).ConfigureAwait(false);
    await stdout.WriteLineAsync("Launch command: " + DotPeekMcpPaths.GetInstalledProxyCommand(installRoot) + " launch --wait").ConfigureAwait(false);

    if (options.HasFlag("--start")) {
      var launchOptions = new CommandLine(new[] { "--install-root", installRoot, "--dotpeek", dotPeekPath, "--wait" });
      return await DotPeekMcpLauncher.LaunchAsync(launchOptions, stdout, cancellationToken).ConfigureAwait(false);
    }

    return 0;
  }

  public static async Task<int> UninstallAsync(CommandLine options, TextWriter stdout) {
    var installRoot = DotPeekMcpPaths.ResolveInstallRoot(options);
    if (options.HasFlag("--stop")) {
      DotPeekMcpLauncher.StopDotPeek();
    }

    if (Directory.Exists(installRoot)) {
      Directory.Delete(installRoot, recursive: true);
      await stdout.WriteLineAsync("Removed " + installRoot).ConfigureAwait(false);
    }
    else {
      await stdout.WriteLineAsync("Install root does not exist: " + installRoot).ConfigureAwait(false);
    }

    return 0;
  }

  private static async Task RunRequiredAsync(
      string fileName,
      IEnumerable<string> arguments,
      TextWriter stdout,
      TextWriter stderr,
      CancellationToken cancellationToken) {
    var exitCode = await ProcessRunner.RunAsync(fileName, arguments, stdout, stderr, cancellationToken).ConfigureAwait(false);
    if (exitCode != 0) {
      throw new InvalidOperationException(fileName + " exited with code " + exitCode + ".");
    }
  }

  private static void CopyPlugin(string pluginBuildDir, string pluginDir) {
    var requiredFiles = new[] { "DotPeekMcp.Plugin.dll", "DotPeekMcp.Protocol.dll" };
    foreach (var fileName in requiredFiles) {
      var source = Path.Combine(pluginBuildDir, fileName);
      if (!File.Exists(source)) {
        throw new FileNotFoundException("Plugin build output was not found.", source);
      }
    }

    Directory.CreateDirectory(pluginDir);
    foreach (var pattern in new[] { "DotPeekMcp.Plugin.dll", "DotPeekMcp.Protocol.dll", "DotPeekMcp.Plugin.pdb", "DotPeekMcp.Protocol.pdb" }) {
      var source = Path.Combine(pluginBuildDir, pattern);
      if (File.Exists(source)) {
        File.Copy(source, Path.Combine(pluginDir, pattern), overwrite: true);
      }
    }
  }

  private static void PrepareInstallRoot(string installRoot) {
    Directory.CreateDirectory(installRoot);
    CleanDirectory(DotPeekMcpPaths.GetPluginDir(installRoot));

    var proxyDir = DotPeekMcpPaths.GetProxyDir(installRoot);
    if (IsCurrentProcessUnder(proxyDir)) {
      throw new InvalidOperationException("Cannot install over the running installed proxy. Run install from the repository source tree.");
    }

    CleanDirectory(proxyDir);
  }

  private static void CleanDirectory(string path) {
    if (Directory.Exists(path)) {
      Directory.Delete(path, recursive: true);
    }

    Directory.CreateDirectory(path);
  }

  private static bool IsCurrentProcessUnder(string directory) {
    var processPath = Environment.ProcessPath;
    if (string.IsNullOrEmpty(processPath)) {
      return false;
    }

    var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    var fullProcessPath = Path.GetFullPath(processPath);
    return fullProcessPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
  }

  private static void CopyDirectory(string sourceDirectory, string destinationDirectory) {
    if (!Directory.Exists(sourceDirectory)) {
      throw new DirectoryNotFoundException("Build output was not found: " + sourceDirectory);
    }

    Directory.CreateDirectory(destinationDirectory);
    foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)) {
      var relativePath = Path.GetRelativePath(sourceDirectory, file);
      var destination = Path.Combine(destinationDirectory, relativePath);
      Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
      File.Copy(file, destination, overwrite: true);
    }
  }

  private static void WriteManifest(string installRoot, string dotPeekPath) {
    var manifest = new {
      installed_at = DateTimeOffset.UtcNow,
      install_root = installRoot,
      dotpeek_path = dotPeekPath,
      packages_file = DotPeekMcpPaths.GetPackagesFile(installRoot),
      plugin_dir = DotPeekMcpPaths.GetPluginDir(installRoot),
      proxy_command = DotPeekMcpPaths.GetInstalledProxyCommand(installRoot),
      mcp_config = DotPeekMcpPaths.GetMcpConfigFile(installRoot)
    };

    File.WriteAllText(DotPeekMcpPaths.GetManifestFile(installRoot), JsonSerializer.Serialize(manifest, JsonDefaults.Options));
  }
}
