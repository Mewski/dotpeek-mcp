namespace DotPeekMcp.Protocol;

public static class ToolCatalog {
  public const string Health = "dotpeek_health";
  public const string OpenAssembly = "dotpeek_open_assembly";
  public const string ListAssemblies = "dotpeek_list_assemblies";
  public const string SurveyAssembly = "dotpeek_survey_assembly";
  public const string ListTypes = "dotpeek_list_types";
  public const string SearchSymbols = "dotpeek_search_symbols";
  public const string DecompileType = "dotpeek_decompile_type";
  public const string DecompileMember = "dotpeek_decompile_member";
  public const string ExportProject = "dotpeek_export_project";
  public const string ListResources = "dotpeek_list_resources";

  public static IReadOnlyList<ToolDefinition> All { get; } = new ToolDefinition[]
  {
    new(Health, "Probe the dotPeek MCP plugin running inside the dotPeek GUI.", EmptyObjectSchema()),
    new(OpenAssembly, "Register an assembly in the dotPeek MCP plugin and read CLR metadata.", ObjectSchema(
      Required("path"),
      StringProperty("path", "Assembly path to open."))),
    new(ListAssemblies, "List assemblies currently visible to the dotPeek plugin.", EmptyObjectSchema()),
    new(SurveyAssembly, "Return a compact metadata overview for an opened assembly.", ObjectSchema(
      Required("assembly"),
      StringProperty("assembly", "Assembly session ID or path returned by dotpeek_open_assembly."))),
    new(ListTypes, "List types from an opened dotPeek assembly.", ObjectSchema(
      Required("assembly"),
      StringProperty("assembly", "Assembly session ID or path returned by dotpeek_open_assembly."),
      StringProperty("filter", "Optional case-insensitive type-name filter."),
      IntegerProperty("offset", "Optional result offset."),
      IntegerProperty("count", "Optional result count; capped by the plugin."))),
    new(SearchSymbols, "Search types and members in dotPeek.", ObjectSchema(
      Required("query"),
      StringProperty("query", "Search text."),
      StringProperty("assembly", "Optional assembly session ID or path to scope the search."),
      IntegerProperty("count", "Optional result count; capped by the plugin."))),
    new(DecompileType, "Return dotPeek decompiler output for a type, with metadata-stub fallback diagnostics.", ObjectSchema(
      Required("assembly", "type"),
      StringProperty("assembly", "Assembly session ID or path returned by dotpeek_open_assembly."),
      StringProperty("type", "Full metadata type name."))),
    new(DecompileMember, "Return dotPeek decompiler output for a member's declaring type, with metadata-stub fallback diagnostics.", ObjectSchema(
      Required("assembly", "member"),
      StringProperty("assembly", "Assembly session ID or path returned by dotpeek_open_assembly."),
      StringProperty("member", "Member metadata name, signature, or token."))),
    new(ExportProject, "Export metadata-backed C# declaration stubs for an assembly.", ObjectSchema(
      Required("assembly", "output_directory"),
      StringProperty("assembly", "Assembly session ID or path returned by dotpeek_open_assembly."),
      StringProperty("output_directory", "Destination directory."),
      BooleanProperty("create_solution", "Create a .sln file."),
      BooleanProperty("create_pdb", "Create a .pdb file."))),
    new(ListResources, "List resources from an opened dotPeek assembly.", ObjectSchema(
      Required("assembly"),
      StringProperty("assembly", "Assembly session ID or path returned by dotpeek_open_assembly.")))
  };

  public static bool Contains(string toolName) {
    return All.Any(tool => string.Equals(tool.Name, toolName, StringComparison.Ordinal));
  }

  private static Dictionary<string, object?> EmptyObjectSchema() {
    return ObjectSchema([]);
  }

  private static Dictionary<string, object?> ObjectSchema(
      IReadOnlyList<string> required,
      params (string Name, Dictionary<string, object?> Schema)[] properties) {
    return new Dictionary<string, object?> {
      ["type"] = "object",
      ["properties"] = properties.ToDictionary(property => property.Name, property => (object?)property.Schema),
      ["required"] = required,
      ["additionalProperties"] = false
    };
  }

  private static IReadOnlyList<string> Required(params string[] names) {
    return names;
  }

  private static (string Name, Dictionary<string, object?> Schema) StringProperty(string name, string description) {
    return (name, new Dictionary<string, object?> {
      ["type"] = "string",
      ["description"] = description
    });
  }

  private static (string Name, Dictionary<string, object?> Schema) BooleanProperty(string name, string description) {
    return (name, new Dictionary<string, object?> {
      ["type"] = "boolean",
      ["description"] = description
    });
  }

  private static (string Name, Dictionary<string, object?> Schema) IntegerProperty(string name, string description) {
    return (name, new Dictionary<string, object?> {
      ["type"] = "integer",
      ["description"] = description,
      ["minimum"] = 0
    });
  }
}

public sealed class ToolDefinition {
  public ToolDefinition(string name, string description, IReadOnlyDictionary<string, object?> inputSchema) {
    Name = name;
    Description = description;
    InputSchema = inputSchema;
  }

  public string Name { get; }
  public string Description { get; }
  public IReadOnlyDictionary<string, object?> InputSchema { get; }
}
