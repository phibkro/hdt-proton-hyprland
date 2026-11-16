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

## Integration observations, 2026-11-16

The normal package command routing now uses Rust for config, setup/status,
launch/input control, journal/export and X11 helpers. Nix runtime dependencies
remove Bun and C/Xlib executables. The shell watcher/desktop/Steam wrappers remain
as tested orchestration; their config reads now use the Rust host. Legacy TS
application sources remain differential references, not installed application
code, and their lint backlog is not waived by this migration.

Initial integration: 28 Rust tests and 47 process/file tests (156 assertions)
pass. The new X11 Bubblewrap/Xvfb test passes real transient identity/refusal/
restore cases and exact four-pixel ARGB/hash comparison against the C reference.
Diagnostics matched 23 normalized journal records and 21 trace cases against
legacy TS. JSON field order/timestamps/error wording differ; invalid overflowed
JSON numbers are rejected rather than JS nonfinite coercion.

Dependency policy found two quick-xml advisories in the initial 0.38 selection;
it was upgraded to 0.42 and adapted, rather than ignoring the advisories. The
unused sha2 dependency was removed. cargo-deny advisories/licenses/bans/sources
then passed; transitive duplicate-version/unused-allowlist warnings are distinct
from advisory failures. Offline package and final committed checks follow.

## Retirement revision, 2026-11-16

After differential and isolated acceptance, retire the superseded runtime TS
modules and C overlay-link helper. Keep the original C surface probe only under
tests/reference for pixel comparison and the minimal XInput C reproducer.
No production TypeScript is retained. Bash keeps the already-tested service/Steam
watcher boundaries; those scripts contain no Bun runtime calls.

Regression coverage mapping before retiring the four legacy TS suites:

| Previous suite | Native coverage |
| --- | --- |
| config.test.ts | config::tests plus rust-config mutable/source/CLI rejection fixtures |
| tracker-environment.test.ts | rust-launcher exact preload preservation/absence/similar-name tests |
| input-journal.test.ts | journal::tests stat/open replacement/FIFO races; rust-diagnostics byte/rotation/rewrite/capacity/discovery/producer lifecycle fixtures |
| trace-export.test.ts | rust-diagnostics duration/session/duplicate/malformed/private-output/overwrite fixtures |

The retained TS watcher, desktop, Steam-wrapper and replay-boundary suites still
exercise real Bash/native process boundaries. Test counts change because the
port groups parameter cases differently; passing counts alone are not coverage
equivalence. Original sources remain in Git history for future comparisons.
The old 601 runtime TS lint findings disappear with retired implementations; no
preset or lint exclusion is changed to hide them. Final source checks must pass.

## Final migration gates

The retired-source configuration passes 31 Rust tests and 22 retained Bash/process
fixtures (69 assertions), strict TypeScript lint/types/format, Rust format/Clippy,
C analysis, Lua analysis and C# whitespace checks. The isolated X11 acceptance
also passes after the C reference move. Nix package, native-source, subprocess
and Rust release tests passed with Cargo build phases offline.

The byte-journal and trace comparisons above are semantic comparisons, not
byte-identical JSON serialization. The isolated X11 test is skipped by ordinary
Cargo discovery and was executed separately; its result is not inferred from the
31 default tests. Live Steam namespace joining and Hyprland input remain untested
for the migrated host. No installed service has been switched to this build.

Clean revision `e653143` also passes the real isolated WPF replay on the seed's
Proton 11.0 with plugin, particles suppressed, Battlegrounds tabs and diagnostics.
The native collector/exporter executes in that replay. An Experimental attempt
timed out before UI execution; a mutable outer-script attempt has no accepted
completion result. Neither is counted as a pass. See [Rust host](../RUST.md).
