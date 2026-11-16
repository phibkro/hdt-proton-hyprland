# Format and analyze source

Use the pinned development environment. These commands do not start HDT or
Hearthstone, install a plugin, or change the game prefix.

## Tools and coverage

| Source | Formatting | Analysis |
| --- | --- | --- |
| TypeScript | Oxfmt with Ultracite | Oxlint with Ultracite core, all anti-slop rules, and type-aware rules; TypeScript 7 checked separately |
| C# | SDK `dotnet format whitespace` and EditorConfig | SDK Roslyn recommended analyzers against .NET Framework 4.7.2 references |
| C | ClangFormat | Clang-Tidy analyzer, bug-prone and selected CERT checks; compiler warnings are errors |
| Lua | StyLua | Selene with the declared Hyprland host API |
| Bash scripts | shfmt | ShellCheck |
| GitHub workflow | Oxfmt | actionlint |

JavaScript tool versions are in `package.json` and `bun.lock`. Native tools and
the .NET SDK come from `devenv.lock`, shared by the development shell and flake.
The project uses Bun 1.4.4. Use `devenv shell -- bun --version` to check it;
the host's ambient Bun can be older.

Vite+ was considered. This repository contains a Bun CLI and a Framework/WPF
plugin, with no Vite application to build. Standalone Oxfmt and Oxlint supply the
requested checks without adding a second application build system.

The [Ultracite initializer](https://www.ultracite.ai/docs/setup) generated the
Oxc configuration. Its [anti-slop preset](https://www.ultracite.ai/docs/presets)
bundles the upstream rules with attribution; no unaudited snippet was copied.
Anti-slop checks source patterns. It does not judge prose or prove correctness.

## Install and run

```sh
devenv shell -- bun install --frozen-lockfile --ignore-scripts
devenv shell -- bun run check
```

The check exits nonzero on a finding. It searches the owned source directories,
not private HDT installations, logs, backups, or downloaded reference source.

**Strict TypeScript lint currently reports existing debt.** Formatting and
compiler checks passing do not establish a clean lint result. The new checks
remain enabled; there is no baseline that hides those findings. This branch is
not ready to merge until its full checks pass. Follow the
[tooling contract](specs/ecosystem-tooling.md) for the recorded verification.

Run one category when working through a finding:

```sh
devenv shell -- bun scripts/quality.ts check ts
devenv shell -- bun scripts/quality.ts check types
devenv shell -- bun scripts/quality.ts check c
devenv shell -- bun scripts/quality.ts check lua
devenv shell -- bun scripts/quality.ts check csharp
devenv shell -- bun scripts/quality.ts check csharp-core
```

Use `fix` instead of `check` for formatting and safe TypeScript fixes. Review the
diff and rerun the check. Suggestions that can change behavior are not applied by
the command. Fixes do not replace process tests or the WPF replay.

## C# analysis boundaries

The production plugin still builds with the prefix's Framework compiler.
`tooling/net/HdtAnalysis.csproj` is an analysis project, not a replacement runtime or
release artifact. It retains C# 5 compatibility and uses locked Microsoft
Framework reference assemblies, allowing Roslyn to run on Linux.

`csharp-core` analyzes the recorder, geometry probe, and recorder fixture. Public
CI can run it without distributing HDT. Full plugin/replay analysis requires a
complete local HDT installation:

```sh
HDT_APP_DIR=/absolute/path/to/hdt \
  devenv shell -- bun scripts/quality.ts check csharp-analysis
```

Missing original assemblies fail explicitly. No HDT stubs are substituted. Full
analysis writes a private SARIF report under `local/analysis/`; it is not a UI or
runtime test. It must accompany changes to HDT-dependent C# before merge.

[Roslyn analyzers](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview)
and [SDK formatting](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-format)
were chosen over an additional formatter or style-only analyzer dependency.
Global public type names remain stable because HDT/replay reflection uses them.
The isolated replay process owns its canonical writer and static event
subscriptions together. The corresponding exceptions are documented at their
configuration or source locations.

## C and Lua boundaries

The C check supplies the real X11 include flags to
[Clang-Tidy](https://clang.llvm.org/extra/clang-tidy/index.html), builds the surface
probe, and tests malformed/overflowing CLI arguments without a display. Annex K
`*_s` suggestions are excluded because those functions are unavailable in the
target glibc environment. Other analyzer/security checks remain enabled.

An additional compiler comparison is available:

```sh
devenv shell -- bun scripts/quality.ts check zig
```

This builds the native helpers with `zig cc`, the same explicit warning policy,
and the Nix X11 libraries. It does not change the shipped Nix compiler or prove
desktop behavior. Zig compilation passed locally; adopting it for release builds
would require the native runtime checks as well.

[StyLua](https://github.com/JohnnyMorganz/StyLua) and
[Selene](https://kampfkarren.github.io/selene/usage/std.html) check the Lua template.
The template uses a Lua 5.3 standard-library subset plus the explicit compositor
declarations in `tooling/lua/hdt-hyprland.yml`. Those declarations are not a complete model
of Hyprland's runtime objects. Lua lint does not verify live stacking or input.

## Fast commit checks and full PR checks

Install the hook for the current checkout:

```sh
devenv shell -- bun run hooks:install
```

The installer refuses to replace an existing different hooks path. It uses
Git's per-worktree configuration so another checkout's hooks remain untouched.
The pre-commit framework temporarily hides unstaged tracked edits, checks the
staged version, and restores those edits. Hook caches stay under `cache/`.
The hook checks without rewriting files or downloading dependencies.

| Boundary | Checks |
| --- | --- |
| Pre-commit | Changed-file formatting/lint, TypeScript types when TS changes, C syntax/warnings, Lua lint, C# whitespace, ShellCheck/shfmt, actionlint |
| PR and merge queue | Frozen dependency restore; all owned TS format/lint/types; portable C# analysis; Nix package, unit tests, native C analysis/input parsing, Lua, C# formatting, ShellCheck/shfmt, workflow lint |
| Local pre-merge evidence | Full C# semantic analysis with original HDT; isolated native/WPF replay for affected runtime boundaries |

The workflow's aggregate `check` job fails if either full job fails or is
cancelled/skipped. GitHub `main` protection was verified to require a pull request
and the up-to-date Actions `check` status, including for admins. This branch
expands that existing status into the aggregate; until it is merged, the default
branch still runs the previous Nix-only workflow. A local hook alone can be bypassed.

## Enter the shell automatically

The checked-in `.envrc` uses devenv's
[direnv integration](https://devenv.sh/integrations/direnv/). Review it, then run
`direnv allow` if you use direnv. This loads development tools when entering the
checkout. It does not watch for Hearthstone or replace the HDT user service.
`autostart.sh` remains a compatibility entry point to the Bash watcher.

## Configuration layout and test coverage

```text
tooling/
  ts/      lint.ts, format.ts, tsconfig.json
  net/     HdtAnalysis.csproj, packages.lock.json
  c/       .clang-format, .clang-tidy
  lua/     stylua.toml, selene.toml, hdt-hyprland.yml
  shell/   shellcheckrc
```

Root Oxc imports, TypeScript extends, and native-tool symlinks preserve editor
and CLI discovery while keeping each configuration authoritative in tooling/.
EditorConfig, package metadata, the pre-commit entry point, and .envrc remain at
the root where their tools expect them. Nix copies native configurations from
their authoritative paths so relative discovery links cannot dangle in a build.

Tests have the same static analysis rules as source. Oxlint and TypeScript
include tests/; Clang-Tidy includes tests/xinput-race.c; both Roslyn scopes
include tests/*.cs. No test directory is excluded to make checks pass. Runtime
tests and static analysis are separate commands and evidence.

Bash implementations remain Bash. The application still uses Bun for existing
TypeScript components, tests, and development check drivers. [ShellCheck](https://github.com/koalaman/shellcheck)
analyzes shell scripts; [shfmt](https://github.com/mvdan/sh) formats them using
root EditorConfig. Both run in staged hooks and the full native CI check.

```sh
devenv shell -- bun scripts/quality.ts check shell
devenv shell -- bun scripts/quality.ts fix shell
devenv shell -- bun run lint:tests
```
