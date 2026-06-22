using System.Text.Json;
using DotPeekMcp.Protocol;

namespace DotPeekMcp.Proxy.Cli;

internal static class DotPeekMcpTestCommand {
  public static async Task<int> RunAsync(CommandLine options, TextWriter stdout, CancellationToken cancellationToken) {
    var installRoot = DotPeekMcpPaths.ResolveInstallRoot(options);
    var assemblyPath = options.GetOption(DotPeekMcpPaths.FindDefaultTestAssembly(installRoot), "--assembly");
    var outputDirectory = options.GetOption(
      Path.Combine(Path.GetTempPath(), "dotpeek-mcp-test-" + Guid.NewGuid().ToString("N")),
      "--output");
    var timeoutSeconds = options.GetIntOption(60, "--timeout");

    if (!File.Exists(assemblyPath)) {
      throw new FileNotFoundException("Test assembly was not found.", assemblyPath);
    }

    var health = await DotPeekMcpLauncher.WaitForBridgeAsync(
      TimeSpan.FromSeconds(timeoutSeconds),
      requireNativeServices: true,
      cancellationToken).ConfigureAwait(false);

    using var client = new HttpClient {
      Timeout = TimeSpan.FromSeconds(120)
    };

    var open = await CallToolAsync(client, ToolCatalog.OpenAssembly, new {
      path = Path.GetFullPath(assemblyPath)
    }, cancellationToken).ConfigureAwait(false);
    var assemblyId = open.GetProperty("assembly").GetProperty("id").GetString()
        ?? throw new InvalidOperationException("dotpeek_open_assembly did not return an assembly id.");

    var survey = await CallToolAsync(client, ToolCatalog.SurveyAssembly, new {
      assembly = assemblyId
    }, cancellationToken).ConfigureAwait(false);
    var types = await CallToolAsync(client, ToolCatalog.ListTypes, new {
      assembly = assemblyId,
      count = 5
    }, cancellationToken).ConfigureAwait(false);
    var search = await CallToolAsync(client, ToolCatalog.SearchSymbols, new {
      assembly = assemblyId,
      query = "ToolCatalog",
      count = 20
    }, cancellationToken).ConfigureAwait(false);
    var decompileType = await CallToolAsync(client, ToolCatalog.DecompileType, new {
      assembly = assemblyId,
      type = "DotPeekMcp.Protocol.ToolCatalog"
    }, cancellationToken).ConfigureAwait(false);
    var memberSearch = await CallToolAsync(client, ToolCatalog.SearchSymbols, new {
      assembly = assemblyId,
      query = "Contains",
      count = 20
    }, cancellationToken).ConfigureAwait(false);
    var memberToken = FindMemberToken(memberSearch, "Contains");
    JsonElement? decompileMember = null;
    if (!string.IsNullOrEmpty(memberToken)) {
      decompileMember = await CallToolAsync(client, ToolCatalog.DecompileMember, new {
        assembly = assemblyId,
        member = memberToken
      }, cancellationToken).ConfigureAwait(false);
    }

    JsonElement? export = null;
    if (!options.HasFlag("--skip-export")) {
      export = await CallToolAsync(client, ToolCatalog.ExportProject, new {
        assembly = assemblyId,
        output_directory = outputDirectory,
        create_solution = true,
        create_pdb = options.HasFlag("--create-pdb")
      }, cancellationToken).ConfigureAwait(false);
    }

    var result = new {
      ok = true,
      health = new {
        process_id = health.ProcessId,
        native_decompiler = health.NativeDecompiler,
        assembly_explorer = health.AssemblyExplorer,
        native_export = health.NativeExport
      },
      open = new {
        mode = GetString(open, "mode"),
        gui_opened = GetBool(open, "gui_opened"),
        assembly_id = assemblyId
      },
      survey = new {
        type_count = survey.GetProperty("counts").GetProperty("types").GetInt32(),
        member_count = survey.GetProperty("counts").GetProperty("members").GetInt32()
      },
      list_types_count = types.GetProperty("count").GetInt32(),
      search_results = search.GetProperty("count").GetInt32(),
      decompile_type_mode = GetString(decompileType, "mode"),
      decompile_member_mode = decompileMember is null ? null : GetString(decompileMember.Value, "mode"),
      decompile_member_scope = decompileMember is null ? null : GetString(decompileMember.Value, "source_scope"),
      export = export is null ? null : new {
        mode = GetString(export.Value, "mode"),
        output_directory = GetString(export.Value, "output_directory"),
        project_path = GetString(export.Value, "project_path"),
        solution_path = GetString(export.Value, "solution_path"),
        pdb_path = GetString(export.Value, "pdb_path"),
        written_file_count = export.Value.GetProperty("written_file_count").GetInt32()
      }
    };

    var optionsIndented = new JsonSerializerOptions(JsonDefaults.Options) {
      WriteIndented = true
    };
    await stdout.WriteLineAsync(JsonSerializer.Serialize(result, optionsIndented)).ConfigureAwait(false);
    return 0;
  }

  private static async Task<JsonElement> CallToolAsync(HttpClient client, string name, object arguments, CancellationToken cancellationToken) {
    var call = new BridgeToolCall(name, JsonSerializer.SerializeToElement(arguments, JsonDefaults.Options));
    using var content = new StringContent(JsonSerializer.Serialize(call, JsonDefaults.Options));
    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

    using var response = await client.PostAsync(new Uri(BridgeDefaults.GetDefaultBaseUri(), "tools/call"), content, cancellationToken).ConfigureAwait(false);
    var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    if (!response.IsSuccessStatusCode) {
      throw new InvalidOperationException("dotPeek bridge returned HTTP " + (int)response.StatusCode + ".");
    }

    var result = JsonSerializer.Deserialize<BridgeToolResult>(bytes, JsonDefaults.Options)
        ?? throw new InvalidOperationException("dotPeek bridge returned an empty response.");
    if (!result.Success) {
      throw new InvalidOperationException(name + " failed: " + result.Error?.Message);
    }

    return result.Data?.Clone() ?? throw new InvalidOperationException(name + " returned no data.");
  }

  private static string FindMemberToken(JsonElement search, string memberName) {
    foreach (var result in search.GetProperty("results").EnumerateArray()) {
      if (GetString(result, "kind") == "method" && GetString(result, "name") == memberName) {
        return GetString(result, "token");
      }
    }

    return string.Empty;
  }

  private static string GetString(JsonElement element, string propertyName) {
    return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? string.Empty
        : string.Empty;
  }

  private static bool GetBool(JsonElement element, string propertyName) {
    return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;
  }
}
