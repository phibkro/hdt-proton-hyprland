# Launch and configure the tracker

Users install the compatibility tools, select their existing Steam shortcut,
and open HDT in the game's running Proton session without replacing that prefix.

## Sub-features

- `config`: local settings and explicit overrides choose the intended data/prefix paths.
- `package`: installed commands work without development runtimes.
- `launch`: HDT joins the one matching running Proton session and preserves child status.
- `setup`: compatibility DLL/settings install with backups and stopped-tracker guards.
- `desktop`: Open, Restart and Steam startup preserve game process ownership.

## How to get to it (user POV)

Use the Nix `hdt-proton` command or devenv's `hdt-launch`, `hdt-setup`,
`hdt-status`, `hdt-steam-options`. The desktop entry exposes Open and Restart;
Steam Launch Options call `hdt-proton steam-run %command%` via the generated line.

## Driving it with the CLI and existing fixtures

Preconditions: parent doctor passed; private empty data directory; original HDT
assemblies and a matching Steam session only for the actual launch/setup routes.

- **Read config.** Run `env -i HOME="$HDT_VERIFY_ROOT/home" HS_APP_ID=123 HDT_PROJECT_DIR="$HDT_VERIFY_ROOT/data" "$HDT_HOST_BINARY" config >"$HDT_VERIFY_ROOT/config.json"`. Independently read the JSON: `windowClass` is `steam_app_123`, `projectRoot` is the owned data directory and prefix ends in `compatdata/123/pfx`. Repeat with `HS_APP_ID=../invalid`; require nonzero and no prefix creation.
- **Build/install boundary.** Run `nix build .#default --no-link --json --max-jobs 1 --cores 2` and save its output. Invoke that output's `bin/hdt-proton config windowClass` with the same empty environment. Expect `steam_app_123`. Do not install or activate it into the operator's service merely to verify the build.
- **Regression fixtures.** Run `cargo nextest run --locked --lib --test rust-config --test rust-launcher --test-threads 2` and `bash scripts/test-unit.sh`. These cover actual CLI/subprocess/file seams, plus mocked external service commands; they do not prove a live Steam launch or actual service restart.
- **Real session.** With the user's existing Battle.net shortcut running, use an explicit local configuration and run `hdt-launch --window-probe`, then `hdt-overlay-launch`. Compare identified process/loaded CLR, prefix and Proton with `hdt-status`. Keep Hearthstone entry/matches under the user. If no session exists, attempt the namespace launcher with the known app directory/config, retain its refusal, and mark `launch` verified-unreachable; do not manufacture a second shared wineserver.
- **Setup.** Run `hdt-setup` only with copied data plus an isolated compositor session, or with authorization to close the identified live tracker. A copied data directory alone does not isolate the compositor close action. Verify the resulting DLL hash, one enabled plugins.xml entry, preserved unrelated settings and backups. This is a mutating route; a compile-only run is not setup coverage.
- **Desktop entry points.** Inspect the installed desktop file; exercise Open/Restart only when their configured service is owned and permitted for the test. Preserve existing Steam arguments. An unavailable operator service is a concrete missing prerequisite, not a reason to change it.

## Gotchas

A saved Steam shortcut ID is required; automatic discovery is not implemented.
The normal installed wrapper supplies mutable data paths and the host binary.
A checkout command needs its built binary or explicit `HDT_HOST_BINARY`.
Do not conflate subprocess fixtures, isolated namespace tests and actual Steam
acceptance. Missing game sessions, native CLR or permitted service ownership must
be recorded per entry point. Setup first closes one identified tracker and waits up to ten seconds. It then refuses writes if any accessible HDT remains.
