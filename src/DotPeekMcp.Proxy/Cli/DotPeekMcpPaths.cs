using System.Text.Json;
using System.Xml.Linq;
using DotPeekMcp.Protocol;

namespace DotPeekMcp.Proxy.Cli;

internal static class DotPeekMcpPaths {
  public static string DefaultInstallRoot => Path.Combine(LocalAppData, "JetBrains", "dotpeek-mcp");

  private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

  public static string ResolveInstallRoot(CommandLine options) {
    return Path.GetFullPath(Environment.ExpandEnvironmentVariables(
      options.GetOption(DefaultInstallRoot, "--install-root")));
  }

  public static string ResolveDotPeekPath(CommandLine options) {
    var configuredPath = options.GetOption(string.Empty, "--dotpeek");
    if (!string.IsNullOrWhiteSpace(configuredPath)) {
      var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredPath));
      if (!File.Exists(fullPath)) {
        throw new FileNotFoundException("dotPeek executable was not found.", fullPath);
      }

      return fullPath;
    }

    var installRoot = Path.Combine(LocalAppData, "JetBrains", "Installations");
    if (!Directory.Exists(installRoot)) {
      throw new DirectoryNotFoundException("JetBrains install root was not found: " + installRoot);
    }

    var candidates = Directory.EnumerateDirectories(installRoot, "dotPeek*")
        .Select(path => new DirectoryInfo(path))
        .Select(directory => new {
          Directory = directory,
          Executable = Path.Combine(directory.FullName, "dotPeek64.exe")
        })
        .Where(candidate => File.Exists(candidate.Executable))
        .OrderByDescending(candidate => candidate.Directory.LastWriteTimeUtc)
        .ThenByDescending(candidate => candidate.Directory.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (candidates.Length == 0) {
      throw new FileNotFoundException("No dotPeek64.exe was found under " + installRoot + ". Pass --dotpeek explicitly.");
    }

    return candidates[0].Executable;
  }

  public static string? FindRepoRoot(string startDirectory) {
    var directory = new DirectoryInfo(startDirectory);
    while (directory is not null) {
      if (File.Exists(Path.Combine(directory.FullName, "DotPeekMcp.slnx"))) {
        return directory.FullName;
      }

      directory = directory.Parent;
    }

    return null;
  }

  public static string GetPluginDir(string installRoot) {
    return Path.Combine(installRoot, "plugin");
  }

  public static string GetProxyDir(string installRoot) {
    return Path.Combine(installRoot, "proxy");
  }

  public static string GetPackagesFile(string installRoot) {
    return Path.Combine(installRoot, "additional-packages.xml");
  }

  public static string GetMcpConfigFile(string installRoot) {
    return Path.Combine(installRoot, "mcp.json");
  }

  public static string GetManifestFile(string installRoot) {
    return Path.Combine(installRoot, "install-manifest.json");
  }

  public static string GetInstalledProxyCommand(string installRoot) {
    var exe = Path.Combine(GetProxyDir(installRoot), "dotpeek-mcp.exe");
    if (File.Exists(exe)) {
      return exe;
    }

    var dll = Path.Combine(GetProxyDir(installRoot), "dotpeek-mcp.dll");
    return File.Exists(dll) ? dll : exe;
  }

  public static string GetPluginBuildDir(string repoRoot, string configuration) {
    return Path.Combine(repoRoot, "src", "DotPeekMcp.Plugin", "bin", configuration, "net472");
  }

  public static string GetProxyBuildDir(string repoRoot, string configuration) {
    return Path.Combine(repoRoot, "src", "DotPeekMcp.Proxy", "bin", configuration, "net10.0");
  }

  public static string FindDefaultTestAssembly(string installRoot) {
    var installed = Path.Combine(GetPluginDir(installRoot), "DotPeekMcp.Protocol.dll");
    if (File.Exists(installed)) {
      return installed;
    }

    var repoRoot = FindRepoRoot(Environment.CurrentDirectory);
    if (repoRoot is not null) {
      foreach (var configuration in new[] { "Debug", "Release" }) {
        var candidate = Path.Combine(repoRoot, "src", "DotPeekMcp.Protocol", "bin", configuration, "net472", "DotPeekMcp.Protocol.dll");
        if (File.Exists(candidate)) {
          return candidate;
        }
      }
    }

    throw new FileNotFoundException("No default test assembly was found. Pass --assembly explicitly.");
  }

  public static void WritePackageFile(string pluginDir, string packagesFile) {
    var pluginDll = Path.Combine(pluginDir, "DotPeekMcp.Plugin.dll");
    var protocolDll = Path.Combine(pluginDir, "DotPeekMcp.Protocol.dll");
    if (!File.Exists(pluginDll)) {
      throw new FileNotFoundException("Plugin DLL was not found.", pluginDll);
    }

    if (!File.Exists(protocolDll)) {
      throw new FileNotFoundException("Protocol DLL was not found.", protocolDll);
    }

    Directory.CreateDirectory(Path.GetDirectoryName(packagesFile)!);
    var document = new XDocument(
      new XElement("Packages",
        new XElement("AdHocMetadata",
          new XAttribute("BaseDir", pluginDir),
          new XAttribute("Version", "1.0.0"),
          new XAttribute("BuiltOn", DateTime.UtcNow.ToString("O")),
          new XAttribute("CompanyNameHuman", "Project Takoyaki Technologies"),
          new XAttribute("SubplatformName", "DotPeekMcp\\Common"),
          new XElement("PackageFile",
            new XAttribute("RelativePath", "DotPeekMcp.Plugin.dll"),
            new XAttribute("AssemblyName", "*")),
          new XElement("PackageFile",
            new XAttribute("RelativePath", "DotPeekMcp.Protocol.dll"),
            new XAttribute("AssemblyName", "*")))));

    File.WriteAllText(packagesFile, document.ToString(SaveOptions.DisableFormatting));
  }

  public static void WriteMcpConfig(string installRoot) {
    var command = GetInstalledProxyCommand(installRoot);
    var config = new {
      mcpServers = new Dictionary<string, object> {
        ["dotpeek"] = command.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? new { command = "dotnet", args = new[] { command } }
            : new { command, args = Array.Empty<string>() }
      }
    };

    Directory.CreateDirectory(installRoot);
    File.WriteAllText(GetMcpConfigFile(installRoot), JsonSerializer.Serialize(config, JsonDefaults.Options));
  }
}
