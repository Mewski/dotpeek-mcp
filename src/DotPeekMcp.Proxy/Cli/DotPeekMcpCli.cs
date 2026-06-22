using System.Reflection;
using System.Text.Json;
using DotPeekMcp.Protocol;
using DotPeekMcp.Proxy.Bridge;
using DotPeekMcp.Proxy.Mcp;

namespace DotPeekMcp.Proxy.Cli;

internal static class DotPeekMcpCli {
  public static async Task<int> RunAsync(
      string[] args,
      TextWriter stdout,
      TextWriter stderr,
      CancellationToken cancellationToken) {
    try {
      if (args.Any(arg => arg is "--help" or "-h" or "/?")) {
        PrintHelp(stdout);
        return 0;
      }

      if (args.Any(arg => arg is "--version" or "-v")) {
        await stdout.WriteLineAsync(GetVersion()).ConfigureAwait(false);
        return 0;
      }

      if (args.Any(arg => arg == "--config")) {
        return await PrintConfigAsync(new CommandLine(args), stdout).ConfigureAwait(false);
      }

      var command = args.Length > 0 && !args[0].StartsWith("-", StringComparison.Ordinal) ? args[0] : "stdio";
      var commandArgs = command == "stdio" ? args : args.Skip(1).ToArray();
      var options = new CommandLine(commandArgs);

      return command switch {
        "stdio" or "serve" => await RunStdioServerAsync(options, stderr, cancellationToken).ConfigureAwait(false),
        "install" => await DotPeekMcpInstaller.InstallAsync(options, stdout, stderr, cancellationToken).ConfigureAwait(false),
        "launch" => await DotPeekMcpLauncher.LaunchAsync(options, stdout, cancellationToken).ConfigureAwait(false),
        "config" => await PrintConfigAsync(options, stdout).ConfigureAwait(false),
        "test" => await DotPeekMcpTestCommand.RunAsync(options, stdout, cancellationToken).ConfigureAwait(false),
        "uninstall" => await DotPeekMcpInstaller.UninstallAsync(options, stdout).ConfigureAwait(false),
        _ => throw new ArgumentException("Unknown command: " + command)
      };
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
      return 130;
    }
    catch (Exception exception) {
      await stderr.WriteLineAsync("error: " + exception.Message).ConfigureAwait(false);
      return 1;
    }
  }

  private static async Task<int> RunStdioServerAsync(CommandLine options, TextWriter stderr, CancellationToken cancellationToken) {
    using var bridgeClient = new DotPeekBridgeClient(GetBridgeUri(options));
    var server = new McpServer(
      Console.OpenStandardInput(),
      Console.OpenStandardOutput(),
      bridgeClient,
      GetVersion(),
      token => DotPeekMcpLauncher.EnsureBridgeReadyOrLaunchAsync(options, stderr, token));
    await server.RunAsync(cancellationToken).ConfigureAwait(false);
    return 0;
  }

  private static async Task<int> PrintConfigAsync(CommandLine options, TextWriter stdout) {
    var installRoot = DotPeekMcpPaths.ResolveInstallRoot(options);
    var command = DotPeekMcpPaths.GetInstalledProxyCommand(installRoot);
    var server = command.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        ? new { command = "dotnet", args = new[] { command } }
        : new { command, args = Array.Empty<string>() };
    var config = new {
      mcpServers = new Dictionary<string, object> {
        ["dotpeek"] = server
      }
    };

    var optionsIndented = new JsonSerializerOptions(JsonDefaults.Options) {
      WriteIndented = true
    };
    await stdout.WriteLineAsync(JsonSerializer.Serialize(config, optionsIndented)).ConfigureAwait(false);
    return 0;
  }

  private static Uri GetBridgeUri(CommandLine options) {
    return options.TryGetOption(out var bridgeUrl, "--bridge-url")
        ? new Uri(bridgeUrl, UriKind.Absolute)
        : BridgeDefaults.GetDefaultBaseUri();
  }

  private static string GetVersion() {
    return typeof(DotPeekMcpCli).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(DotPeekMcpCli).Assembly.GetName().Version?.ToString()
        ?? "unknown";
  }

  private static void PrintHelp(TextWriter output) {
    output.WriteLine("dotpeek-mcp");
    output.WriteLine();
    output.WriteLine("Usage:");
    output.WriteLine("  dotpeek-mcp [--bridge-url URL] [--no-auto-launch]");
    output.WriteLine("  dotpeek-mcp install [--dotpeek PATH] [--install-root PATH] [--configuration Release] [--self-contained]");
    output.WriteLine("  dotpeek-mcp launch [--dotpeek PATH] [--install-root PATH] [--wait] [--no-stop]");
    output.WriteLine("  dotpeek-mcp config [--install-root PATH]");
    output.WriteLine("  dotpeek-mcp test [--assembly PATH] [--create-pdb] [--skip-export]");
    output.WriteLine("  dotpeek-mcp uninstall [--install-root PATH] [--stop]");
    output.WriteLine();
    output.WriteLine("Default mode is stdio MCP proxying to the dotPeek plugin bridge. It auto-starts dotPeek when needed.");
  }
}
