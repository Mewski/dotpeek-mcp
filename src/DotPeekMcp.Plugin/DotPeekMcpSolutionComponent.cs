using JetBrains.Application.Parts;
using JetBrains.Application.Threading;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.Assemblies.Impl;
using JetBrains.ProjectModel.Model2.Assemblies.Interfaces;
using JetBrains.ReSharper.ExternalSources.AssemblyExplorer;
using JetBrains.ReSharper.Feature.Services.ExternalSources.CSharp.AssemblyExport;

namespace DotPeekMcp.Plugin;

[SolutionComponent(Instantiation.ContainerAsyncAnyThreadUnsafe)]
public sealed class DotPeekMcpSolutionComponent {
  public DotPeekMcpSolutionComponent(
      IAssemblyExplorerManager assemblyExplorerManager,
      ProjectGenerationManager projectGenerationManager,
      AssemblyFactory assemblyFactory,
      IAssemblyCollection assemblyCollection,
      IShellLocks shellLocks) {
    DotPeekMcpPluginBootstrap.Log("Solution component created.");
    DotPeekMcpSolutionServices.Register(
      assemblyExplorerManager,
      projectGenerationManager,
      assemblyFactory,
      assemblyCollection,
      shellLocks);
    DotPeekMcpPluginBootstrap.EnsureStarted("solution-component");
  }
}
