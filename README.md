# dotpeek-mcp

MCP access to JetBrains dotPeek through the dotPeek GUI/plugin model.

This project intentionally follows the `ida-pro-mcp` GUI-plugin shape: dotPeek stays open, a plugin runs in the dotPeek process, and a small external MCP proxy forwards MCP client requests into that plugin.

## Shape

- `src/DotPeekMcp.Plugin`: dotPeek plugin loaded as an ad-hoc deployed package.
- `src/DotPeekMcp.Proxy`: stdio MCP proxy for MCP clients.
- `src/DotPeekMcp.Protocol`: shared tool catalog and bridge message contracts.

The proxy does not decompile assemblies. All dotPeek work must happen inside `DotPeekMcp.Plugin` using dotPeek/JetBrains APIs.

## Current Status

Implemented:

- Buildable .NET solution: the dotPeek plugin targets `net472` for JetBrains component-catalog compatibility, while the stdio proxy targets `net10.0`.
- dotPeek plugin startup component with a JetBrains zone marker.
- Localhost bridge inside the plugin on `127.0.0.1:8767`.
- MCP stdio framing in the proxy.
- `tools/list` with the dotPeek MCP tool surface.
- `dotpeek_health` bridge/tool path.
- Metadata-backed assembly sessions inside the dotPeek plugin.
- Implementations for all declared tools:
  - `dotpeek_open_assembly`
  - `dotpeek_list_assemblies`
  - `dotpeek_survey_assembly`
  - `dotpeek_list_types`
  - `dotpeek_search_symbols`
  - `dotpeek_decompile_type` using JetBrains dotPeek decompiler output with metadata-stub fallback diagnostics.
  - `dotpeek_decompile_member` using the member's native-decompiled declaring type with metadata-stub fallback diagnostics.
  - `dotpeek_export_project`
  - `dotpeek_list_resources`

Current limitations:

- `dotpeek_open_assembly` registers assemblies in the plugin session model; it does not yet mirror them into dotPeek's Assembly Explorer UI.
- `dotpeek_decompile_member` returns the native-decompiled declaring type as `source` and marks `source_scope = "declaring_type"`; it does not yet slice the exact member body out of that source.
- `dotpeek_export_project` writes metadata-backed declaration stubs and a small project shell. It does not yet call dotPeek's Export to Project pipeline or generate PDBs.

The next implementation step is connecting safe Assembly Explorer UI mirroring and dotPeek Export to Project services once the required solution-level services are mapped reliably.

## Tool Behavior

- `dotpeek_open_assembly`: validates a PE/CLR assembly path, reads metadata without loading the target assembly into dotPeek's AppDomain, and returns a stable `asm_N` session ID.
- `dotpeek_list_assemblies`: returns all assemblies opened through the plugin bridge.
- `dotpeek_survey_assembly`: returns counts, type-kind breakdowns, top namespaces, references, and resources.
- `dotpeek_list_types`: returns paged type summaries. Optional arguments: `filter`, `offset`, `count`.
- `dotpeek_search_symbols`: searches opened assemblies for matching types and members. Optional arguments: `assembly`, `count`.
- `dotpeek_decompile_type`: returns `mode = "dotpeek_decompiler"` and native dotPeek C# output when available. If the native path fails, returns `mode = "metadata_stub"` with `native_error` and `native_diagnostics`.
- `dotpeek_decompile_member`: returns `mode = "dotpeek_decompiler"`, `source_scope = "declaring_type"`, and native dotPeek C# output for the member's declaring type when available. If the native path fails, returns `mode = "metadata_stub"` with `native_error` and `native_diagnostics`. Use member tokens from search results to disambiguate overloads.
- `dotpeek_export_project`: returns `mode = "metadata_stubs"` and writes generated `.metadata.cs` files plus a project file to an empty output directory.
- `dotpeek_list_resources`: returns manifest resources from assembly metadata.

## Build

```powershell
dotnet build DotPeekMcp.slnx
```

Stop dotPeek before rebuilding after a launch; the running process locks `DotPeekMcp.Plugin.dll`.

## Format

Formatting is controlled by `.editorconfig` and uses 2-space indentation.

Apply formatting:

```powershell
dotnet format DotPeekMcp.slnx whitespace
```

Check formatting without changing files:

```powershell
dotnet format DotPeekMcp.slnx whitespace --verify-no-changes
```

The plugin project defaults to this dotPeek install path:

```text
%LOCALAPPDATA%\JetBrains\Installations\dotPeek261
```

Override it with MSBuild if needed:

```powershell
dotnet build DotPeekMcp.slnx -p:DotPeekInstallDir="C:\Path\To\dotPeek"
```

## Run

Start dotPeek with the plugin loaded:

```powershell
.\scripts\Start-DotPeekMcp.ps1
```

Current dotPeek builds load plugins as deployed packages. The launch script writes an ad-hoc package XML file for `src\DotPeekMcp.Plugin\bin\Debug\net472` and sets `JET_ADDITIONAL_DEPLOYED_PACKAGES_FILE` before starting dotPeek.

Then configure an MCP client to run the stdio proxy:

```powershell
dotnet run --project src\DotPeekMcp.Proxy --no-build
```

The proxy connects to the plugin bridge at `http://127.0.0.1:8767/` by default.

Override the bridge URL:

```powershell
dotnet run --project src\DotPeekMcp.Proxy --no-build -- --bridge-url http://127.0.0.1:8767/
```

Or set environment variables:

```powershell
$env:DOTPEEK_MCP_BRIDGE_URL = "http://127.0.0.1:8767/"
$env:DOTPEEK_MCP_BRIDGE_PORT = "8767"
```

## Design Rules

- DotPeek is the implementation target. There is no ILSpy or generic decompiler backend.
- The plugin owns dotPeek API calls because those calls run in the dotPeek process.
- The proxy only speaks MCP and forwards tool calls.
- Tool responses should stay structured and explicit, following the `ida-pro-mcp` style.
- Add batch/pagination before returning potentially large results.
