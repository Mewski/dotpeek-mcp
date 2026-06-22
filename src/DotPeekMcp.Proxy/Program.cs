using DotPeekMcp.Proxy.Cli;

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => {
  eventArgs.Cancel = true;
  shutdown.Cancel();
};

Environment.ExitCode = await DotPeekMcpCli.RunAsync(args, Console.Out, Console.Error, shutdown.Token).ConfigureAwait(false);
