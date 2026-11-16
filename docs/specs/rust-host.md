# Native Rust host migration

## Goal and boundary

Replace as much repository-owned Linux host TypeScript and C as possible with
one Rust executable, preserving HDT compatibility, existing command contracts,
independent byte-journal evidence, and process ownership. C# remains required by
the Windows HDT plugin/CLR interface; Lua remains the compositor-hosted rule.
Bash may remain thin orchestration where its existing isolated environment owns
external tools. No game session, prefix, installed service, or live configuration
is changed during this migration.

## Contract

- One Cargo package and one pinned dependency graph; Nix supplies Rust tools.
- Maintain config precedence, argument boundaries, exit codes, namespace joining,
  exact running Proton selection, Steam preload exclusion, and cleanup custody.
- Journal preserves exact source bytes, offsets, generations, recorder discovery,
  private permissions, exclusive output ownership, capacity failure and rotation.
- Native X11 operations preserve identity checks and never write game state.
- Use typed JSON boundaries and safe Rust APIs. No unchecked JSON or broad X11
  error suppression. No credentials written into diagnostic output.
- Replace runtime implementations once verified; keep legacy sources only as
  explicitly labeled differential-test evidence until equivalent tests migrate.
- Hook and CI checks share Rust commands; no new competing hook manager.
- Current tier-five particle suppression remains enabled.

## Acceptance

Cargo format/Clippy/types/tests pass; nextest and doctests pass; dependency checks
report actual results. Existing process/file fixtures exercise the routed native
commands. Differential trace/journal checks compare semantics with original TS.
Isolated native XInput and WPF replay require fresh measured resource headroom.
Nix builds the Rust package without Cargo network access in normal build phases.
No inferred pass from lint or mocked boundaries substitutes for runtime evidence.
Migration coverage and any legacy runtime dependencies are recorded explicitly.

## Integration contract

Lead owns Cargo.toml/Cargo.lock, src/host/{main,lib,config,status,setup}.rs, Nix,
shared check/hook integration, docs, shell routing, and acceptance integration.
Diagnostic lane owns src/host/{journal,trace}.rs and Rust diagnostic tests.
Launcher lane owns src/host/{launch,control}.rs and its Rust process tests.
X11 lane owns src/host/x11.rs and Rust native tests. Each lane has a separate
worktree and cannot modify the shared dependency graph or live runtime.

Shared library: anyhow::Result; Config has source_root, project_root, steam_root,
prefix, hdt_data, app_directory, native_directory as PathBuf; app_id and
window_class as String. Public CLI modules expose run(&[OsString])->Result<()>
for diagnostics, overlay_link/surface_probe for X11; launch/control take &Config.
CLI returns preserved child status where specified. No module performs work on
import. Dependencies available: serde/serde_json, anyhow, base64, nix, signal-hook,
uuid, chrono, x11rb, sha2, quick-xml; remove unused dependencies at integration.

## Revisions

- 2026-11-16: Operator chose Rust to subsume the Linux host rather than add Zig.
  Cargo's native scaffold and Nix buildRustPackage are the initial build tools;
  Vite+/Meson would duplicate orchestration for a Rust host and are not adopted.
  Full Linux desktop/runtime verification remains separate from migration checks.
