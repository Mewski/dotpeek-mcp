# dotpeek-mcp

MCP server for JetBrains dotPeek.

`dotpeek-mcp` loads a plugin into the dotPeek GUI process and exposes dotPeek operations through a stdio MCP proxy. The proxy only handles MCP framing and forwards requests to the in-process plugin bridge.

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) 10.0 or newer.
- [JetBrains dotPeek](https://www.jetbrains.com/decompiler/) 2026.1 or newer.
- An MCP client that supports stdio servers.

## Installation

Download `dotpeek-mcp-win-x64.zip` from the latest release and extract it to:

```text
%LOCALAPPDATA%\JetBrains\dotpeek-mcp
```

Or install from the repository root:

```powershell
dotnet run --project src\DotPeekMcp.Proxy -- install
```

The installer builds the solution, publishes the proxy, copies the dotPeek plugin, writes dotPeek's deployed-package file, and writes an MCP config snippet under:

```text
%LOCALAPPDATA%\JetBrains\dotpeek-mcp\mcp.json
```

If dotPeek is not auto-detected, pass the executable path:

```powershell
dotnet run --project src\DotPeekMcp.Proxy -- install --dotpeek "C:\Path\To\dotPeek64.exe"
```

## Run dotPeek

Start dotPeek through the installed proxy so the plugin package is loaded:

```powershell
& "$env:LOCALAPPDATA\JetBrains\dotpeek-mcp\proxy\dotpeek-mcp.exe" launch --wait
```

`--wait` returns only after the plugin bridge and native dotPeek services are available.

## MCP Config

Print the MCP client config:

```powershell
& "$env:LOCALAPPDATA\JetBrains\dotpeek-mcp\proxy\dotpeek-mcp.exe" config
```

Generic stdio config:

```json
{
  "mcpServers": {
    "dotpeek": {
      "command": "C:\\Users\\YOU\\AppData\\Local\\JetBrains\\dotpeek-mcp\\proxy\\dotpeek-mcp.exe",
      "args": []
    }
  }
}
```

## Test

With dotPeek running through `launch --wait`:

```powershell
& "$env:LOCALAPPDATA\JetBrains\dotpeek-mcp\proxy\dotpeek-mcp.exe" test --create-pdb
```

The test command checks bridge health, Assembly Explorer open, metadata survey, symbol search, native type decompilation, member extraction, and native Export to Project/PDB.

## Commands

- `dotpeek-mcp`: start the stdio MCP proxy.
- `dotpeek-mcp install`: build and install the plugin/proxy bundle.
- `dotpeek-mcp launch`: start dotPeek with the plugin package enabled.
- `dotpeek-mcp config`: print the MCP config JSON.
- `dotpeek-mcp test`: run an end-to-end bridge/tool test.
- `dotpeek-mcp uninstall`: remove the local install root.

## Tools

- `dotpeek_health`: probe the plugin bridge and native dotPeek services.
- `dotpeek_open_assembly`: open an assembly in dotPeek Assembly Explorer and create an MCP assembly session.
- `dotpeek_list_assemblies`: list opened assembly sessions.
- `dotpeek_survey_assembly`: return a compact assembly metadata overview.
- `dotpeek_list_types`: list types with pagination and optional filtering.
- `dotpeek_search_symbols`: search types and members.
- `dotpeek_decompile_type`: return native dotPeek C# output for a type.
- `dotpeek_decompile_member`: return extracted native dotPeek C# output for a member when a safe span is found.
- `dotpeek_export_project`: run dotPeek Export to Project with optional solution and PDB generation.
- `dotpeek_list_resources`: list manifest resources.

## Development

Build:

```powershell
dotnet build DotPeekMcp.slnx
```

Format check:

```powershell
dotnet format DotPeekMcp.slnx whitespace --verify-no-changes
```

Stop dotPeek before rebuilding after a plugin launch because dotPeek locks loaded plugin assemblies.

If your dotPeek installation is not under `%LOCALAPPDATA%\JetBrains\Installations`, pass the install directory to MSBuild:

```powershell
dotnet build DotPeekMcp.slnx -p:DotPeekInstallDir="C:\Path\To\dotPeek"
```

## Architecture

- `DotPeekMcp.Plugin`: dotPeek plugin loaded as an ad-hoc deployed package.
- `DotPeekMcp.Proxy`: stdio MCP proxy and product CLI.
- `DotPeekMcp.Protocol`: shared tool catalog and bridge contracts.

The plugin listens on `127.0.0.1:8767` by default. Override the bridge with `DOTPEEK_MCP_BRIDGE_URL` or `DOTPEEK_MCP_BRIDGE_PORT`.

## Troubleshooting

- If an MCP client cannot connect, start dotPeek with `dotpeek-mcp launch --wait` first.
- If builds fail because DLLs are locked, stop dotPeek and rebuild.
- If dotPeek is not detected, pass `--dotpeek "C:\Path\To\dotPeek64.exe"` to `install` or `launch`.
- Plugin logs are written to `%LOCALAPPDATA%\JetBrains\dotpeek-mcp\plugin.log`.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.
