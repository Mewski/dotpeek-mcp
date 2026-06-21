using System.Text.Json;
using DotPeekMcp.Protocol;

namespace DotPeekMcp.Plugin.Metadata;

internal sealed class MetadataProjectExporter {
  private readonly MetadataSourceWriter _sourceWriter = new();

  public MetadataProjectExportResult Export(AssemblySession session, string outputDirectory, bool createSolution, bool createPdb) {
    if (string.IsNullOrWhiteSpace(outputDirectory)) {
      throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
    }

    var fullOutputDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputDirectory));
    if (Directory.Exists(fullOutputDirectory) && Directory.EnumerateFileSystemEntries(fullOutputDirectory).Any()) {
      throw new InvalidOperationException("Output directory already exists and is not empty: " + fullOutputDirectory);
    }

    Directory.CreateDirectory(fullOutputDirectory);
    var projectName = SanitizeFileName(session.Metadata.Name);
    var sourceDirectory = Path.Combine(fullOutputDirectory, "src");
    Directory.CreateDirectory(sourceDirectory);

    var writtenFiles = new List<string>();
    foreach (var type in session.Metadata.Types) {
      var relativeDirectory = NamespaceToPath(type.Namespace);
      var directory = string.IsNullOrEmpty(relativeDirectory) ? sourceDirectory : Path.Combine(sourceDirectory, relativeDirectory);
      Directory.CreateDirectory(directory);
      var fileName = SanitizeFileName(type.Name) + ".metadata.cs";
      var path = Path.Combine(directory, fileName);
      File.WriteAllText(path, _sourceWriter.WriteType(session, type));
      writtenFiles.Add(path);
    }

    var projectPath = Path.Combine(fullOutputDirectory, projectName + ".csproj");
    File.WriteAllText(projectPath, BuildProjectFile(session));
    writtenFiles.Add(projectPath);

    string solutionPath = string.Empty;
    if (createSolution) {
      solutionPath = Path.Combine(fullOutputDirectory, projectName + ".sln");
      File.WriteAllText(solutionPath, BuildSolutionFile(projectName));
      writtenFiles.Add(solutionPath);
    }

    var manifestPath = Path.Combine(fullOutputDirectory, "dotpeek-mcp-export.json");
    File.WriteAllText(manifestPath, JsonSerializer.Serialize(new {
      mode = "metadata_stubs",
      assembly = session.Metadata.Name,
      assembly_id = session.Id,
      assembly_path = session.Path,
      exported_at = DateTimeOffset.UtcNow,
      type_count = session.Metadata.Types.Length,
      create_solution = createSolution,
      create_pdb_requested = createPdb,
      create_pdb_supported = false
    }, JsonDefaults.Options));
    writtenFiles.Add(manifestPath);

    var warnings = new List<string> {
      "Generated metadata-only C# declarations; method bodies are stubs, not dotPeek decompiler output."
    };
    if (createPdb) {
      warnings.Add("PDB generation is not available in metadata-stub export mode.");
    }

    return new MetadataProjectExportResult {
      OutputDirectory = fullOutputDirectory,
      ProjectPath = projectPath,
      SolutionPath = solutionPath,
      WrittenFiles = writtenFiles.ToArray(),
      Warnings = warnings.ToArray()
    };
  }

  private static string BuildProjectFile(AssemblySession session) {
    return "<Project Sdk=\"Microsoft.NET.Sdk\">" + Environment.NewLine
        + "  <PropertyGroup>" + Environment.NewLine
        + "    <TargetFramework>net8.0</TargetFramework>" + Environment.NewLine
        + "    <AssemblyName>" + EscapeXml(session.Metadata.Name) + ".MetadataStubs</AssemblyName>" + Environment.NewLine
        + "  </PropertyGroup>" + Environment.NewLine
        + "</Project>" + Environment.NewLine;
  }

  private static string BuildSolutionFile(string projectName) {
    var projectGuid = Guid.NewGuid().ToString("B").ToUpperInvariant();
    return "Microsoft Visual Studio Solution File, Format Version 12.00" + Environment.NewLine
        + "# Visual Studio Version 17" + Environment.NewLine
        + "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"" + projectName + "\", \"" + projectName + ".csproj\", \"" + projectGuid + "\"" + Environment.NewLine
        + "EndProject" + Environment.NewLine
        + "Global" + Environment.NewLine
        + "EndGlobal" + Environment.NewLine;
  }

  private static string NamespaceToPath(string @namespace) {
    if (string.IsNullOrEmpty(@namespace)) {
      return string.Empty;
    }

    return Path.Combine(@namespace.Split('.').Select(SanitizeFileName).ToArray());
  }

  private static string SanitizeFileName(string text) {
    var invalid = Path.GetInvalidFileNameChars();
    var chars = text.Select(character => invalid.Contains(character) || character == '<' || character == '>' || character == '`' ? '_' : character).ToArray();
    var sanitized = new string(chars).Trim('_');
    return string.IsNullOrEmpty(sanitized) ? "Assembly" : sanitized;
  }

  private static string EscapeXml(string value) {
    return value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");
  }
}

internal sealed class MetadataProjectExportResult {
  public string OutputDirectory { get; set; } = string.Empty;
  public string ProjectPath { get; set; } = string.Empty;
  public string SolutionPath { get; set; } = string.Empty;
  public string[] WrittenFiles { get; set; } = Array.Empty<string>();
  public string[] Warnings { get; set; } = Array.Empty<string>();
}
