using System.Reflection;
using DotPeekMcp.Plugin.Metadata;

namespace DotPeekMcp.Plugin.Bridge;

internal sealed class DotPeekNativeProjectExporter {
  private readonly object _lock = new();

  public object GetStatus() {
    var services = DotPeekMcpSolutionServices.Snapshot();
    return new {
      available = services.ProjectGenerationManager is not null && services.AssemblyFactory is not null,
      mode = services.ProjectGenerationManager is null ? "unavailable" : "dotpeek_export_project",
      registered_at = services.RegisteredAt,
      project_generation_manager_type = services.ProjectGenerationManager?.GetType().FullName ?? string.Empty,
      assembly_factory_type = services.AssemblyFactory?.GetType().FullName ?? string.Empty,
      assembly_cookie_count = services.AssemblyCookieCount
    };
  }

  public NativeProjectExportResult TryExport(AssemblySession session, string outputDirectory, bool createSolution, bool createPdb) {
    lock (_lock) {
      var diagnostics = new List<string>();
      try {
        var fullOutputDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputDirectory));
        if (Directory.Exists(fullOutputDirectory) && Directory.EnumerateFileSystemEntries(fullOutputDirectory).Any()) {
          throw new InvalidOperationException("Output directory already exists and is not empty: " + fullOutputDirectory);
        }

        Directory.CreateDirectory(fullOutputDirectory);
        var services = DotPeekMcpSolutionServices.Snapshot();
        var manager = services.ProjectGenerationManager
            ?? throw new InvalidOperationException("dotPeek ProjectGenerationManager is not available.");

        var exportMethod = manager.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method => {
              var args = method.GetParameters();
              return method.Name == "Export"
                  && args.Length == 1
                  && args[0].ParameterType.FullName == "JetBrains.ReSharper.Feature.Services.ExternalSources.CSharp.AssemblyExport.IAssemblyExportParameters";
            }) ?? throw new InvalidOperationException("ProjectGenerationManager.Export(IAssemblyExportParameters) was not found.");

        var task = DotPeekMcpSolutionServices.InvokeOnPrimaryThread(() => {
          var assemblyFile = DotPeekMcpSolutionServices.GetAssemblyFile(session.Path, diagnostics)
              ?? throw new InvalidOperationException("dotPeek could not create an IAssemblyFile for " + session.Path + ".");
          var parameters = CreateExportParameters(session, assemblyFile, fullOutputDirectory, createSolution, createPdb);
          diagnostics.Add("export_parameters=created_on_primary_thread");
          return exportMethod.Invoke(manager, new[] { parameters })
              ?? throw new InvalidOperationException("ProjectGenerationManager.Export returned null.");
        });
        var result = JetBrainsReflection.WaitForTaskResult(task);
        if (result is null) {
          return NativeProjectExportResult.Failed("dotPeek ProjectGenerationManager returned null.", fullOutputDirectory, diagnostics);
        }

        diagnostics.Add("export_result_type=" + result.GetType().FullName);
        return NativeProjectExportResult.Succeeded(fullOutputDirectory, diagnostics);
      }
      catch (Exception exception) {
        return NativeProjectExportResult.Failed(JetBrainsReflection.FormatException(exception), outputDirectory, diagnostics);
      }
    }
  }

  private static object CreateExportParameters(AssemblySession session, object assemblyFile, string outputDirectory, bool createSolution, bool createPdb) {
    var parametersType = JetBrainsReflection.RequireType(
      "JetBrains.ReSharper.Feature.Services.ExternalSources.CSharp",
      "JetBrains.ReSharper.Feature.Services.ExternalSources.CSharp.AssemblyExport.AssemblyExportParameters");
    var folderActionType = JetBrainsReflection.RequireType(
      "JetBrains.ReSharper.Feature.Services.ExternalSources.CSharp",
      "JetBrains.ReSharper.Feature.Services.ExternalSources.CSharp.AssemblyExport.FolderIsNotEmptyAction");
    var outputFolder = JetBrainsReflection.ToVirtualFileSystemPath(outputDirectory);
    var rewriteExistingFiles = Enum.Parse(folderActionType, "RewriteExistingFiles");
    var projectName = SanitizeFileName(session.Metadata.Name);
    var constructor = parametersType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
        .FirstOrDefault(ctor => ctor.GetParameters().Length == 17)
        ?? throw new InvalidOperationException("AssemblyExportParameters constructor was not found.");

    return constructor.Invoke(new[] {
      assemblyFile,
      outputFolder,
      rewriteExistingFiles,
      projectName,
      true,
      createSolution,
      true,
      true,
      createPdb,
      null,
      null,
      false,
      true,
      true,
      true,
      false,
      false
    });
  }

  private static string SanitizeFileName(string text) {
    var invalid = Path.GetInvalidFileNameChars();
    var chars = text.Select(character => invalid.Contains(character) || character == '<' || character == '>' || character == '`' ? '_' : character).ToArray();
    var sanitized = new string(chars).Trim('_');
    return string.IsNullOrEmpty(sanitized) ? "Assembly" : sanitized;
  }
}

internal sealed class NativeProjectExportResult {
  public bool Success { get; set; }
  public string Error { get; set; } = string.Empty;
  public string OutputDirectory { get; set; } = string.Empty;
  public string[] WrittenFiles { get; set; } = Array.Empty<string>();
  public string ProjectPath { get; set; } = string.Empty;
  public string SolutionPath { get; set; } = string.Empty;
  public string PdbPath { get; set; } = string.Empty;
  public string[] Diagnostics { get; set; } = Array.Empty<string>();

  public static NativeProjectExportResult Succeeded(string outputDirectory, IEnumerable<string> diagnostics) {
    var fullOutputDirectory = Path.GetFullPath(outputDirectory);
    var files = Directory.Exists(fullOutputDirectory)
        ? Directory.EnumerateFiles(fullOutputDirectory, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()
        : Array.Empty<string>();

    return new NativeProjectExportResult {
      Success = true,
      OutputDirectory = fullOutputDirectory,
      WrittenFiles = files,
      ProjectPath = files.FirstOrDefault(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) ?? string.Empty,
      SolutionPath = files.FirstOrDefault(path => path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)) ?? string.Empty,
      PdbPath = files.FirstOrDefault(path => path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) ?? string.Empty,
      Diagnostics = diagnostics.ToArray()
    };
  }

  public static NativeProjectExportResult Failed(string error, string outputDirectory, IEnumerable<string> diagnostics) {
    return new NativeProjectExportResult {
      Error = error,
      OutputDirectory = outputDirectory,
      Diagnostics = diagnostics.ToArray()
    };
  }
}
