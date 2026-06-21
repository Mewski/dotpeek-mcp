using JetBrains.Application;
using JetBrains.Application.Components;
using JetBrains.Application.Parts;

namespace DotPeekMcp.Plugin;

[ShellComponent(Instantiation.DemandAnyThreadUnsafe)]
public sealed class DotPeekMcpStartupActivity : IStartupActivity {
  public DotPeekMcpStartupActivity() {
    DotPeekMcpPluginBootstrap.Log("Startup activity created.");
    DotPeekMcpPluginBootstrap.EnsureStarted("startup-activity");
  }
}
