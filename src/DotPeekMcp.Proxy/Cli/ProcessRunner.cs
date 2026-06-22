using System.Diagnostics;

namespace DotPeekMcp.Proxy.Cli;

internal static class ProcessRunner {
  public static async Task<int> RunAsync(
      string fileName,
      IEnumerable<string> arguments,
      TextWriter stdout,
      TextWriter stderr,
      CancellationToken cancellationToken) {
    var startInfo = new ProcessStartInfo(fileName) {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false
    };

    foreach (var argument in arguments) {
      startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start process: " + fileName);
    var stdoutTask = CopyLinesAsync(process.StandardOutput, stdout, cancellationToken);
    var stderrTask = CopyLinesAsync(process.StandardError, stderr, cancellationToken);

    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
    return process.ExitCode;
  }

  private static async Task CopyLinesAsync(TextReader reader, TextWriter writer, CancellationToken cancellationToken) {
    while (!cancellationToken.IsCancellationRequested) {
      var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
      if (line is null) {
        return;
      }

      await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
    }
  }
}
