# Check and diagnose HDT

Run these procedures from the repository checkout. For command behavior and options, see the [command reference](REFERENCE.md).

## Run the source checks

Run the selected checks:

```sh
devenv shell -- hdt-check
```

Confirm that the command exits with status zero. The command runs the tests listed in `scripts/check.sh`, shellcheck, TypeScript bundles, and native helper builds.

Do not run unbounded test discovery in a checkout that contains private prefix backups. Those backups can contain unrelated test files.

## Run the CI checks locally

Run the same check that GitHub Actions runs:

```sh
nix flake check --print-build-logs --no-write-lock-file --max-jobs 1 --cores 2
```

Confirm that the command exits with status zero. The flake builds the package and runs its declared tests, shellcheck, and workflow lint. The [workflow](../.github/workflows/check.yml) runs for pull requests and pushes to `main`.

CI does not launch HDT or Hearthstone. Use the [isolated replay](UI-REPLAY.md) and [desktop tests](TESTING.md) to check rendering and live state updates.

## Compile the plugin against HDT

This procedure requires a complete HDT installation and native .NET in the running Steam prefix.

1. Start Battle.net through the existing Steam shortcut.
2. Compile the plugin:

   ```sh
   devenv shell -- hdt-launch --build-plugin
   ```

3. Confirm that the compiler exits with status zero.
4. When HDT can close normally, follow [Apply the plugin](INSTALL.md#apply-the-plugin).

Compilation writes `evidence/ProtonCompatibility.dll`. It does not install the DLL or restart HDT.

## Inspect the running tracker

Run the status command:

```sh
devenv shell -- hdt-status
```

Inspect `trackerRuntime`, `controlInput`, the window positions, the log timestamps, and `savedMatchSummary`. Compare the results with the [desktop test procedures](TESTING.md).

Keep raw HDT logs and account data out of commits. `hdt-status` filters its output to selected diagnostic fields. It does not print the inherited process environment, account names, or card choices.

For intermittent slowdowns, use [Measure HDT performance](PERFORMANCE.md) to
compare process and thread activity with the flight recorder.

For repeatable tests without account activity, use [Replay HDT UI transitions in isolation](UI-REPLAY.md). That procedure tests real WPF controls with synthetic state. It does not exercise live HearthMirror or ArenaSmith requests.

## Inspect window geometry

With Battle.net and HDT running in the existing Steam session, run the Win32 probe:

```sh
devenv shell -- hdt-launch --window-probe
```

Inspect the private output in `evidence/window-probe.json`. Compare the probe's geometry with the compositor positions from `hdt-status`.

## Inspect a preview that leaves pixels behind

Finish the current tracking test before you restart HDT with diagnostics.

1. Close HDT normally.
2. Start HDT with preview diagnostics:

   ```sh
   HDT_HOVER_DIAGNOSTICS=1 devenv shell -- hdt-overlay-launch
   ```

3. Hover an overlay card.
4. Move the pointer to empty Hearthstone space.
5. Build the probe:

   ```sh
   devenv shell -- hdt-build-surface-probe
   ```

6. While the preview state is fresh, sample its last rectangle:

   ```sh
   devenv shell -- bun src/diagnostics/surface-check.ts
   ```

7. Inspect `evidence/surface-check.json` alongside `evidence/hover-state.json`.

The sampler rejects stale state. If the sampler reports stale state, repeat the hover before you sample again. The probe reads the identified overlay's backing pixmap, not game pixels or root-window pixels.

For the limits of this measurement, see [Preview clearing](EXPLANATION.md#preview-clearing).

## Compare an overlay option

Finish play before you change launch options. Keep the original setting available for recovery.

1. Close HDT normally.
2. Select one variable from the [overlay options](REFERENCE.md#overlay-options).
3. Start HDT with that variable. For example, omit the faint background with this command:

   ```sh
   HDT_ALPHA_FLOOR=0 devenv shell -- hdt-overlay-launch
   ```

4. Repeat the same [overlay test](TESTING.md#test-the-overlay).
5. Close HDT normally.
6. Restore the compatibility defaults:

   ```sh
   devenv shell -- hdt-overlay-launch
   ```

## Remove the runtime rules

This cleanup path has source review coverage. It was not part of the accepted desktop test.

1. Close HDT normally.
2. Wait for its launcher and input bridge to exit.
3. Remove the runtime rules:

   ```sh
   devenv shell -- bun src/hyprland/overlay-rule.ts --disable
   ```

The runtime rules do not edit a host configuration file.

## Remove the compatibility plugin

1. Close HDT normally.
2. Remove only `ProtonCompatibility.dll` from HDT's Roaming `Plugins` directory.
3. If you need to restore settings, compare the saved copies under `backups/hdt-before-render/` with the current files.
4. Restore the affected settings without overwriting unrelated changes.

Plugin removal does not undo .NET installation. Do not terminate the shared wineserver to close HDT.

## Review documentation changes

1. Read the pinned pstack [technical-writing skill](https://github.com/cursor/plugins/blob/bf0c7cd80abdbf7fe47b634978a7d1b4071f71a7/pstack/skills/technical-writing/SKILL.md).
2. Apply its required [unslop skill](https://github.com/cursor/plugins/blob/bf0c7cd80abdbf7fe47b634978a7d1b4071f71a7/pstack/skills/unslop/SKILL.md).
3. Give each page one purpose. Separate procedures, reference facts, and explanations.
4. Compare each documented command, option, and path with the source.
5. Check local links and heading anchors.
6. Keep failed and untested results explicit in `docs/COMPATIBILITY.md`.
7. Preserve frozen feature specs as historical records.
