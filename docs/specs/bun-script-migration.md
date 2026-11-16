# Bun script migration

## Contract

Preserve command arguments, exit codes, real process ownership, watcher backoff,
configuration rejection, and protection of the game's service boundary while
moving shell control flow into readable Bun TypeScript. Use the pinned Bun from
devenv. Never install or change a live prefix as part of migration tests.

## Acceptance

Run the real scripts against temporary filesystem/process fixtures. Verify
watcher lock contention and release, stopped child ownership, three-failure cap,
5/10 second backoff, healthy budget reset, normal closure, exact Steam arguments
and exit code, setup stop-on-error, and refusal to restart a game-owned service.
Compile source bundles and native helper checks. Record remaining shell scripts
explicitly; native replay isolation requires separate runtime validation.

## Revisions

- 2026-11-16: Initial migration contract; retain compatibility entry points.

## Implementation and shell inventory

Every tracked shell entry now delegates directly to Bun. Substantive filesystem,
command selection, retry, PID/service validation, locking, replay, and native
fixture orchestration is TypeScript under `scripts/`.

| Compatibility entry | Implementation |
| --- | --- |
| `autostart.sh`, `scripts/autostart.sh` | `scripts/autostart.ts` |
| `scripts/hdt.sh` | `scripts/hdt.ts` |
| `scripts/desktop.sh` | `scripts/desktop.ts` |
| `scripts/steam-launch.sh`, `scripts/steam-options.sh` | Corresponding `.ts` |
| `scripts/setup.sh` | `scripts/setup.ts` |
| `scripts/check.sh`, `scripts/test-unit.sh` | Corresponding `.ts` |
| `scripts/check-xinput.sh` | `scripts/check-xinput.ts` |
| `scripts/replay.sh`, `scripts/replay-inside.sh` | Corresponding `.ts` |

The shell files remain for existing Steam launch options, desktop declarations,
and documented commands. No substantial Bash implementation remains. Nix and
devenv invoke Bun directly. Existing external boundary tools (flock, timeout,
Bubblewrap, Steam runtime, native compiler, X11 tools, Wine) remain dependencies;
TypeScript controls their arguments without constructing shell programs.

The Steam runtime rewrites PATH. The isolated runner consequently invokes Bun
by its absolute store path, then explicitly supplies the restored tool environment
to its subprocesses. This boundary was discovered by running the actual isolated
Wine/WPF fixture, rather than inferred from unit tests.

## Verification recorded on 2026-11-16

- `devenv shell -- bun scripts/check.ts`: 47 tests, 156 assertions; bundles and
  native helpers compile with warnings treated as errors. Tests run actual Bun
  entrypoints, temporary filesystem/process fixtures, and the compatibility shim
  for a Steam options path containing spaces and an apostrophe.
- Strict TypeScript 7 checking passed against the pinned Bun type definitions.
- `devenv shell -- bun scripts/check-xinput.ts`: all six native protocol cases
  passed inside the private Bubblewrap/Xvfb environment.
- `devenv shell -- bun scripts/replay.ts run --with-plugin --without-particles`:
  actual Wine/.NET/WPF fixture passed native Settings input, mode changes, score
  bindings, resize, popup, New dialog, and shutdown.
- `devenv shell -- bun scripts/replay.ts run --with-plugin --diagnostics --battlegrounds-tabs`:
  actual fixture passed tab interaction, stall/recovery recording, and normal UI
  transitions with particle suppression still active.
- `devenv shell -- bun scripts/replay.ts run --with-plugin --canonical-events`:
  actual installed parser events and independent collector byte continuity passed
  across stopping, killing, and replacing the isolated HDT host process.

The replay seed is account-free and uses installed Stable Proton 11.0-2. These
checks establish script boundary preservation and isolated WPF behavior; they do
not establish live Steam/Hearthstone tracking, actual Hyprland alignment, account
features, or resolution of desktop performance/crash bugs. Nothing was installed
or activated, and the real prefix was unchanged.

The Nix package and sandboxed test derivation also passed. The new replay CLI
boundary test exposed a missing `ripgrep` test dependency, which is now declared
in the check environment. Packaged Steam-options output and command rejection
are checked without starting the desktop service.
