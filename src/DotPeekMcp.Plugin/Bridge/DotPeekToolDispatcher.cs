using System.Text.Json;
using System.Diagnostics;
using DotPeekMcp.Protocol;
using DotPeekMcp.Plugin.Metadata;

namespace DotPeekMcp.Plugin.Bridge;

internal sealed class DotPeekToolDispatcher {
  private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
  private readonly DotPeekAssemblyStore _assemblies = new();
  private readonly MetadataSourceWriter _sourceWriter = new();
  private readonly MetadataProjectExporter _projectExporter = new();
  private readonly DotPeekNativeDecompiler _nativeDecompiler = new();

  public BridgeToolResult Dispatch(BridgeToolCall call) {
    if (string.IsNullOrWhiteSpace(call.Name)) {
      return BridgeToolResult.FromError("invalid_tool", "Tool name is required.");
    }

    try {
      return call.Name switch {
        ToolCatalog.Health => Health(),
        ToolCatalog.OpenAssembly => OpenAssembly(call.Arguments),
        ToolCatalog.ListAssemblies => ListAssemblies(),
        ToolCatalog.SurveyAssembly => SurveyAssembly(call.Arguments),
        ToolCatalog.ListTypes => ListTypes(call.Arguments),
        ToolCatalog.SearchSymbols => SearchSymbols(call.Arguments),
        ToolCatalog.DecompileType => DecompileType(call.Arguments),
        ToolCatalog.DecompileMember => DecompileMember(call.Arguments),
        ToolCatalog.ExportProject => ExportProject(call.Arguments),
        ToolCatalog.ListResources => ListResources(call.Arguments),
        _ => BridgeToolResult.FromError("unknown_tool", $"Unknown dotPeek MCP tool: {call.Name}")
      };
    }
    catch (ArgumentException exception) {
      return BridgeToolResult.FromError("invalid_arguments", exception.Message);
    }
    catch (FileNotFoundException exception) {
      return BridgeToolResult.FromError("file_not_found", exception.Message);
    }
    catch (BadImageFormatException exception) {
      return BridgeToolResult.FromError("invalid_assembly", exception.Message);
    }
    catch (KeyNotFoundException exception) {
      return BridgeToolResult.FromError("assembly_not_found", exception.Message);
    }
    catch (UnauthorizedAccessException exception) {
      return BridgeToolResult.FromError("access_denied", exception.Message);
    }
    catch (IOException exception) {
      return BridgeToolResult.FromError("io_error", exception.Message);
    }
    catch (InvalidOperationException exception) {
      return BridgeToolResult.FromError("operation_failed", exception.Message);
    }
  }

  private BridgeToolResult Health() {
    var data = new {
      ok = true,
      host = "dotPeek",
      process_id = Process.GetCurrentProcess().Id,
      started_at = _startedAt,
      bridge_url = BridgeDefaults.GetDefaultBaseUri().ToString(),
      open_assemblies = _assemblies.List().Length,
      native_decompiler = _nativeDecompiler.GetStatus(),
      tools = ToolCatalog.All.Select(tool => tool.Name).ToArray()
    };

    return BridgeToolResult.FromData(data);
  }

  private BridgeToolResult OpenAssembly(JsonElement arguments) {
    var path = RequiredString(arguments, "path");
    var session = _assemblies.Open(path);
    return BridgeToolResult.FromData(new {
      mode = "metadata",
      gui_opened = false,
      assembly = ToAssemblySummary(session)
    });
  }

  private BridgeToolResult ListAssemblies() {
    return BridgeToolResult.FromData(new {
      assemblies = _assemblies.List().Select(ToAssemblySummary).ToArray()
    });
  }

  private BridgeToolResult SurveyAssembly(JsonElement arguments) {
    var session = ResolveAssembly(arguments);
    var metadata = session.Metadata;
    return BridgeToolResult.FromData(new {
      mode = "metadata",
      assembly = ToAssemblySummary(session),
      counts = new {
        types = metadata.Types.Length,
        members = metadata.Types.Sum(CountMembers),
        fields = metadata.Types.Sum(type => type.Fields.Length),
        properties = metadata.Types.Sum(type => type.Properties.Length),
        events = metadata.Types.Sum(type => type.Events.Length),
        methods = metadata.Types.Sum(type => type.Methods.Length),
        resources = metadata.Resources.Length,
        references = metadata.References.Length
      },
      type_kinds = metadata.Types
          .GroupBy(type => type.Kind)
          .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
          .ToDictionary(group => group.Key, group => group.Count()),
      top_namespaces = metadata.Types
          .GroupBy(type => string.IsNullOrEmpty(type.Namespace) ? "<global>" : type.Namespace)
          .OrderByDescending(group => group.Count())
          .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
          .Take(20)
          .Select(group => new { name = group.Key, type_count = group.Count() })
          .ToArray(),
      references = metadata.References.Take(50).ToArray(),
      resources = metadata.Resources.Take(50).ToArray()
    });
  }

  private BridgeToolResult ListTypes(JsonElement arguments) {
    var session = ResolveAssembly(arguments);
    var filter = OptionalString(arguments, "filter");
    var offset = OptionalInt(arguments, "offset", 0);
    var count = Math.Min(OptionalInt(arguments, "count", 200), 1000);
    var query = session.Metadata.Types.AsEnumerable();
    if (!string.IsNullOrWhiteSpace(filter)) {
      query = query.Where(type => Contains(type.FullName, filter) || Contains(type.MetadataName, filter));
    }

    var matches = query.ToArray();
    var page = matches.Skip(offset).Take(count).Select(type => new {
      type.Token,
      full_name = type.FullName,
      metadata_name = type.MetadataName,
      type.Kind,
      type.Accessibility,
      base_type = type.BaseType,
      member_count = CountMembers(type)
    }).ToArray();

    return BridgeToolResult.FromData(new {
      assembly = ToAssemblySummary(session),
      total = matches.Length,
      offset,
      count = page.Length,
      truncated = offset + page.Length < matches.Length,
      types = page
    });
  }

  private BridgeToolResult SearchSymbols(JsonElement arguments) {
    var query = RequiredString(arguments, "query");
    var count = Math.Min(OptionalInt(arguments, "count", 200), 1000);
    var sessions = OptionalString(arguments, "assembly") is { Length: > 0 } assembly
        ? new[] { _assemblies.Resolve(assembly) }
        : _assemblies.List();
    var results = new List<object>();

    foreach (var session in sessions) {
      foreach (var type in session.Metadata.Types) {
        if (Contains(type.FullName, query) || Contains(type.MetadataName, query)) {
          results.Add(new {
            assembly_id = session.Id,
            assembly_name = session.Metadata.Name,
            kind = "type",
            type.Token,
            name = type.Name,
            full_name = type.FullName,
            metadata_name = type.MetadataName,
            signature = BuildTypeResultSignature(type)
          });
        }

        foreach (var member in EnumerateMembers(type)) {
          if (!Contains(member.FullName, query) && !Contains(member.Name, query) && !Contains(member.Signature, query) && !Contains(member.Token, query)) {
            continue;
          }

          results.Add(new {
            assembly_id = session.Id,
            assembly_name = session.Metadata.Name,
            kind = member.Kind,
            member.Token,
            name = member.Name,
            full_name = member.FullName,
            declaring_type = type.FullName,
            signature = member.Signature
          });
        }
      }
    }

    var page = results.Take(count).ToArray();
    return BridgeToolResult.FromData(new {
      query,
      searched_assemblies = sessions.Length,
      total = results.Count,
      count = page.Length,
      truncated = page.Length < results.Count,
      results = page
    });
  }

  private BridgeToolResult DecompileType(JsonElement arguments) {
    var session = ResolveAssembly(arguments);
    var type = FindType(session, RequiredString(arguments, "type"));
    var native = _nativeDecompiler.TryDecompileType(session, type);
    if (native.Success) {
      return BridgeToolResult.FromData(new {
        mode = native.Mode,
        assembly = ToAssemblySummary(session),
        type = ToTypeSummary(type),
        source = native.Source,
        native_diagnostics = native.Diagnostics
      });
    }

    return BridgeToolResult.FromData(new {
      mode = "metadata_stub",
      assembly = ToAssemblySummary(session),
      type = ToTypeSummary(type),
      native_error = native.Error,
      native_diagnostics = native.Diagnostics,
      source = _sourceWriter.WriteType(session, type)
    });
  }

  private BridgeToolResult DecompileMember(JsonElement arguments) {
    var session = ResolveAssembly(arguments);
    var memberQuery = RequiredString(arguments, "member");
    var matches = FindMembers(session, memberQuery).ToArray();
    if (matches.Length == 0) {
      throw new KeyNotFoundException("Member was not found: " + memberQuery);
    }

    if (matches.Length > 1 && !LooksLikeToken(memberQuery)) {
      return BridgeToolResult.FromError("ambiguous_member", "Member query matched multiple members. Use a metadata token from search results.");
    }

    var match = matches[0];
    var native = _nativeDecompiler.TryDecompileType(session, match.Type);
    if (native.Success) {
      return BridgeToolResult.FromData(new {
        mode = native.Mode,
        source_scope = "declaring_type",
        assembly = ToAssemblySummary(session),
        type = ToTypeSummary(match.Type),
        member = match.Member,
        source = native.Source,
        native_diagnostics = native.Diagnostics
      });
    }

    return BridgeToolResult.FromData(new {
      mode = "metadata_stub",
      assembly = ToAssemblySummary(session),
      type = ToTypeSummary(match.Type),
      member = match.Member,
      native_error = native.Error,
      native_diagnostics = native.Diagnostics,
      source = _sourceWriter.WriteMember(session, match.Type, match.Member)
    });
  }

  private BridgeToolResult ExportProject(JsonElement arguments) {
    var session = ResolveAssembly(arguments);
    var outputDirectory = RequiredString(arguments, "output_directory");
    var createSolution = OptionalBool(arguments, "create_solution", false);
    var createPdb = OptionalBool(arguments, "create_pdb", false);
    var result = _projectExporter.Export(session, outputDirectory, createSolution, createPdb);
    return BridgeToolResult.FromData(new {
      mode = "metadata_stubs",
      assembly = ToAssemblySummary(session),
      output_directory = result.OutputDirectory,
      project_path = result.ProjectPath,
      solution_path = string.IsNullOrEmpty(result.SolutionPath) ? null : result.SolutionPath,
      written_file_count = result.WrittenFiles.Length,
      written_files = result.WrittenFiles.Take(100).ToArray(),
      written_files_truncated = result.WrittenFiles.Length > 100,
      warnings = result.Warnings
    });
  }

  private BridgeToolResult ListResources(JsonElement arguments) {
    var session = ResolveAssembly(arguments);
    return BridgeToolResult.FromData(new {
      assembly = ToAssemblySummary(session),
      count = session.Metadata.Resources.Length,
      resources = session.Metadata.Resources
    });
  }

  public static BridgeToolResult InvalidJson(JsonException exception) {
    return BridgeToolResult.FromError("invalid_json", exception.Message);
  }

  private AssemblySession ResolveAssembly(JsonElement arguments) {
    return _assemblies.Resolve(RequiredString(arguments, "assembly"));
  }

  private static object ToAssemblySummary(AssemblySession session) {
    return new {
      id = session.Id,
      path = session.Path,
      name = session.Metadata.Name,
      version = session.Metadata.Version,
      culture = session.Metadata.Culture,
      public_key_token = session.Metadata.PublicKeyToken,
      module_name = session.Metadata.ModuleName,
      mvid = session.Metadata.Mvid,
      metadata_version = session.Metadata.MetadataVersion,
      target_framework = session.Metadata.TargetFramework,
      machine = session.Metadata.Machine,
      is_executable = session.Metadata.IsExecutable,
      opened_at = session.OpenedAt,
      type_count = session.Metadata.Types.Length,
      member_count = session.Metadata.Types.Sum(CountMembers),
      resource_count = session.Metadata.Resources.Length,
      reference_count = session.Metadata.References.Length
    };
  }

  private static object ToTypeSummary(TypeMetadata type) {
    return new {
      type.Token,
      full_name = type.FullName,
      metadata_name = type.MetadataName,
      type.Kind,
      type.Accessibility,
      base_type = type.BaseType,
      type.GenericParameters,
      member_count = CountMembers(type)
    };
  }

  private static string BuildTypeResultSignature(TypeMetadata type) {
    return string.Join(" ", new[] { type.Accessibility, type.Kind, type.FullName }.Where(part => !string.IsNullOrWhiteSpace(part)));
  }

  private static int CountMembers(TypeMetadata type) {
    return type.Fields.Length + type.Properties.Length + type.Events.Length + type.Methods.Length;
  }

  private static IEnumerable<MemberMetadata> EnumerateMembers(TypeMetadata type) {
    return type.Fields.Concat(type.Properties).Concat(type.Events).Concat(type.Methods);
  }

  private TypeMetadata FindType(AssemblySession session, string query) {
    var matches = session.Metadata.Types.Where(type => IsTypeMatch(type, query)).ToArray();
    if (matches.Length == 0) {
      throw new KeyNotFoundException("Type was not found: " + query);
    }

    if (matches.Length > 1 && !LooksLikeToken(query)) {
      throw new InvalidOperationException("Type query matched multiple types. Use a metadata token or full name.");
    }

    return matches[0];
  }

  private static bool IsTypeMatch(TypeMetadata type, string query) {
    return EqualsIgnoreCase(type.Token, query)
        || EqualsIgnoreCase(type.FullName, query)
        || EqualsIgnoreCase(type.MetadataName, query)
        || EqualsIgnoreCase(type.Name, query);
  }

  private static IEnumerable<MemberMatch> FindMembers(AssemblySession session, string query) {
    foreach (var type in session.Metadata.Types) {
      foreach (var member in EnumerateMembers(type)) {
        if (EqualsIgnoreCase(member.Token, query)
            || EqualsIgnoreCase(member.FullName, query)
            || EqualsIgnoreCase(member.Name, query)
            || Contains(member.Signature, query)) {
          yield return new MemberMatch(type, member);
        }
      }
    }
  }

  private static string RequiredString(JsonElement arguments, string name) {
    if (arguments.ValueKind != JsonValueKind.Object
        || !arguments.TryGetProperty(name, out var value)
        || value.ValueKind != JsonValueKind.String) {
      throw new ArgumentException("Argument '" + name + "' is required.");
    }

    var text = value.GetString() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(text)) {
      throw new ArgumentException("Argument '" + name + "' must be a non-empty string.");
    }

    return text;
  }

  private static string OptionalString(JsonElement arguments, string name) {
    if (arguments.ValueKind != JsonValueKind.Object
        || !arguments.TryGetProperty(name, out var value)
        || value.ValueKind == JsonValueKind.Null
        || value.ValueKind == JsonValueKind.Undefined) {
      return string.Empty;
    }

    if (value.ValueKind != JsonValueKind.String) {
      throw new ArgumentException("Argument '" + name + "' must be a string.");
    }

    return value.GetString() ?? string.Empty;
  }

  private static bool OptionalBool(JsonElement arguments, string name, bool fallback) {
    if (arguments.ValueKind != JsonValueKind.Object
        || !arguments.TryGetProperty(name, out var value)
        || value.ValueKind == JsonValueKind.Null
        || value.ValueKind == JsonValueKind.Undefined) {
      return fallback;
    }

    if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False) {
      throw new ArgumentException("Argument '" + name + "' must be a boolean.");
    }

    return value.GetBoolean();
  }

  private static int OptionalInt(JsonElement arguments, string name, int fallback) {
    if (arguments.ValueKind != JsonValueKind.Object
        || !arguments.TryGetProperty(name, out var value)
        || value.ValueKind == JsonValueKind.Null
        || value.ValueKind == JsonValueKind.Undefined) {
      return fallback;
    }

    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < 0) {
      throw new ArgumentException("Argument '" + name + "' must be a non-negative integer.");
    }

    return number;
  }

  private static bool Contains(string text, string query) {
    return text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
  }

  private static bool EqualsIgnoreCase(string left, string right) {
    return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
  }

  private static bool LooksLikeToken(string query) {
    return query.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
  }

  private sealed class MemberMatch {
    public MemberMatch(TypeMetadata type, MemberMetadata member) {
      Type = type;
      Member = member;
    }

    public TypeMetadata Type { get; }
    public MemberMetadata Member { get; }
  }
}
