# Native development workspace

Use this directory as the editor workspace. All tools, caches, editor profile and
evidence are project-local; no global .NET tool or machine configuration is needed.

```sh
# From the repository root:
nix-shell avalonia/shell.nix
cargo build --locked -j2
bash avalonia/dev.sh setup
bash avalonia/dev.sh build
bash avalonia/dev.sh tools
# In another terminal in the same shell:
bash avalonia/dev.sh run
```

`Hdt.slnx` groups the F# views, C# app, acceptance driver and development host.
`Hdt.Development` reuses the ordinary application's initialization and MainWindow,
then adds `AvaloniaUI.DiagnosticsSupport` 2.2.3. It is a separate Debug-only project:
ordinary app and test dependency graphs do not reference it. Ship
`Hdt.Avalonia`, never `Hdt.Development`. The inspector is started explicitly from
the local manifest; F12 connects the development app to it. No inspector is
started automatically by a game session, a test or a normal app launch.

## Tools and limits

| Tool | Configuration | What needs separate acceptance |
|---|---|---|
| Avalonia Developer Tools 2.2.3 | Local `dotnet-tools.json`; `dev.sh tools`; separate instrumented host | Account/license activation and a live inspector attachment |
| Build documentation MCP | Authoritative `.mcp.json`; `dev.sh setup` derives local VS Code and Codex configurations | Reload the trusted project client to expose its tools in that session |
| DevTools MCP | `.mcp.devtools.example.json`; `dev.sh mcp` refuses to start without a supplied license | Paid tools license and actual app attachment; free/Community licenses do not cover this feature |
| F# formatting | Pinned Fantomas; existing fast hook and CI include F# tests | No language migration is implied for remaining C# views |
| VS Code languages/debugger | Ionide and C# recommendations; attach configuration | Editor activation/debugger attachment is separate from extension installation |
| Edit/rebuild loop | `dev.sh watch` uses `dotnet watch --no-hot-reload` | File discovery is checkable; restarting loses in-memory UI state |

The [current tools guide](https://docs.avaloniaui.net/tools/) distinguishes the
open-source framework from licensed tooling. The [inspector setup](https://docs.avaloniaui.net/tools/developer-tools/installation)
requires activation; the [DevTools MCP guide](https://docs.avaloniaui.net/tools/developer-tools/mcp)
specifically excludes that MCP feature from free and Community licenses. Supply a
license through your private local environment, never through a committed file
or chat message. This workspace does not create an account, start a trial or buy
a license.

The [hot-reload guide](https://docs.avaloniaui.net/tools/hot-reload/) documents
licensed XAML/C# support. These F# code-only views use a full restart loop; no
paid hot-reload package is installed and F# live patching is not claimed.
Parcel and XAML preview extensions are not installed: Nix currently packages the
application, and this slice has no AXAML files to preview.

## Editor and MCP setup

To use the project-local VS Code profile installed during this setup:

```sh
# Run inside the pinned shell, from the repository root:
code --extensions-dir "$PWD/local/vscode-extensions" \
  --user-data-dir "$PWD/local/vscode-data" "$PWD/avalonia"
```

It contains Ionide and the C# extension, including their required .NET runtime
extension. A fresh clone can install those recommendations with the same paths
and `--install-extension Ionide.Ionide-fsharp --install-extension ms-dotnettools.csharp`.
F# formatting-on-save and an explicit attach-to-process debugger configuration
are provided. Select only your owned native preview process when attaching.

`dev.sh setup` generates ignored `.vscode/mcp.json` and `.codex/config.toml` from
the single `.mcp.json` declaration. It refuses to overwrite differing existing
configuration. [Codex project MCP configuration](https://learn.chatgpt.com/docs/extend/mcp#connect-codex-to-an-mcp-server)
is loaded only for trusted projects. Existing sessions may need reopening; this
does not modify global trust or global agent configuration. The free endpoint
supports documentation lookup and WPF mapping queries without app access.

To use the licensed application-inspection MCP later, merge the example's server
entry into the relevant client's local configuration and supply
`AVALONIA_TOOLS_LICENSE_KEY` through its environment. The example assumes the
workspace is this directory. Do not assume that a successful CLI help command
or MCP initialization proves licensed runtime attachment.

## Verification

The real F# restart journey is `bash avalonia/check-watch.sh` from the pinned
shell after the Rust build and dependency setup above. It archives committed
source into an owned private working copy, starts the documented watch command
without host display or network access, edits the F# palette, then requires a
new application PID, changed assembly bytes, loaded replay card pixels and the
changed palette. Evidence stays under `local/native-watch-*`; the original
source checkout is never edited by the test.

Run the existing recipes in [README.md](README.md) and the
[verification feature map](../.cursor/skills/verify-hdt-proton/features/native-avalonia.md).
Also build the development host in locked mode, check its formatting, verify the
ordinary app/test dependency manifests exclude DiagnosticsSupport, and require a
Release build of the development host to fail explicitly. Keep paid activation
and real Hearthstone acceptance marked untested until actually observed.
