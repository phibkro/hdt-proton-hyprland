# Replay HDT UI transitions in isolation

For the optional stall detector and trace-export checks, see [freeze diagnostics](DIAGNOSTICS.md). Add `--diagnostics` to a run with `--with-plugin` to exercise them.

Run the real HDT main window and Arena controls on a private X11 desktop. These tests leave the running game and tracker alone. See [component boundaries](COMPONENTS.md) for the distinction between draft updates, match tracking, and presentation.

## Prepare the private seed

Use a complete HDT installation and an existing Proton prefix with native .NET. Run commands from this checkout.

1. Set the project configuration to the HDT installation and Steam shortcut you want to copy. Follow the [configuration reference](REFERENCE.md).
2. Select the installed Proton and Steam runtime directories. For the tested Steam layout, run:

   ```sh
   devenv shell -- hdt-replay prepare \
     --proton "$HOME/.local/share/Steam/steamapps/common/Proton - Experimental" \
     --runtime "$HOME/.local/share/Steam/steamapps/common/SteamLinuxRuntime_4"
   ```

3. Confirm that the command reports `Prepared isolated HDT seed` and exits with status zero.

Preparation copies Windows components, HDT files, Wine preferences, the Wine initialization marker, and the selected Steam runtime into `local/replay`. Application data under the source user's Roaming directory is excluded. The source prefix stays unchanged. The registry copies are checked individually for changes during copying; preparation is not an atomic snapshot of a running prefix.

If a project-owned seed already exists, add `--reuse` to validate its CLR and HDT executable against the selected source. To refresh the seed, wait for all replay commands to finish, archive `local/replay` elsewhere under the project, then prepare again. Keep copied third-party components private.

## Run the UI sequence

1. Start a replay:

   ```sh
   devenv shell -- hdt-replay run
   ```

2. Confirm exit status zero and the printed evidence directory.
3. Inspect that directory's `result.json`, `supervisor.log`, state snapshots, and screenshots. `result.json` records the source commit and whether the checkout had changes at run start.

Each run creates a fresh prefix and app directory from the seed. The sequence opens Settings with native mouse input, enters the Arena lobby, alternates two synthetic offers across 12 decisions, returns to the menu, resizes the main window, shows and hides a WPF popup, and executes HDT's New command. The New command must open HDT's actual deck-type dialog. `transitions.jsonl` records each scene and its card models.

The host checks card identities, displayed score text, actual plaque levels, visible particle emitters, panel visibility, Settings state, and dialog type. Screenshots support visual inspection. The test does not automatically prove pixel clearing after every popup.

## Compare the Arena particle workaround

Run the current compatibility plugin with three level-five badges:

```sh
devenv shell -- hdt-replay run --with-plugin --plaque-level 5
```

Expect status zero. The state snapshots must retain three level-five models and their scores, with zero visible particle emitters. The plugin's input snapshot must report suppressed emitters.

To test the original particle path without the plugin, run:

```sh
devenv shell -- hdt-replay run --plaque-level 5
```

This command reproduced `88980406` in the tested environment and is expected to fail there. The score graphics can freeze while exported model state still advances. Keep its logs and failure screenshot as regression evidence.

To retain level-five artwork while suppressing particles in the fixture alone, add `--without-particles`. To compare badges without the level-five particle effect, use `--plaque-level 4`. Omitting `--plaque-level` tests levels 1, 3, and 5 together.

## Check the Battlegrounds card-back tab

Run the installed tab command and render its minion-browser view:

```sh
devenv shell -- hdt-replay run --with-plugin --plaque-level 5 --battlegrounds-tabs
```

Inspect `bgs-tab-*.json`, the matching screenshots, and `host.log`. The sequence opens and closes the tab three times and clicks Settings after each transition. An open tab must contain a visible `BattlegroundsMinions` view with positive height.

This fixture does not replay combat, live minion-pool updates, or a pointer click on the card-back icon. A passing command sequence does not rule out the reported combat-time freeze. The [compatibility record](COMPATIBILITY.md) tracks that separate failure.

## Check portable X11 input

Run the actual plugin-exported control regions through the portable bridge:

```sh
devenv shell -- hdt-replay run --with-plugin --plaque-level 5 --desktop-input
```

The test adds a registered WPF button and a separate Win32 game-window stand-in to the private display. It checks empty-space input, the WPF button's native click handler, and input after leaving the button. It never launches Hearthstone.

If the control click fails, the test retains focus and activation diagnostics and returns failure. Those diagnostic attempts cannot turn the failed journey into a pass. See [Desktop backend development](DESKTOPS.md) for current limits.

## Check failure handling

Run the deliberate assertion failure:

```sh
devenv shell -- hdt-replay run --inject-failure
```

Expect a nonzero exit status and `negativeAssertionTest: true` in `result.json`. Inspect `supervisor.log` for the deliberate assertion message. This command tests the harness's failure path; it does not inject an exception into the live tracker.

## Interpret a result

The host uses the installed HDT assembly, WPF resources, main window, overlay, Arena card models, and visibility methods. It bypasses normal HDT startup and supplies synthetic game scenes and ratings directly. Constructor work can attempt network access, but the network namespace blocks external connectivity.

The run owns a separate PID namespace, network namespace, home directory, prefix, and Xvfb display. It mounts selected system paths and this project, leaving unrelated host volumes outside the test environment. The live prefix and desktop sockets are masked. A timeout bounds execution, and namespace exit terminates the test descendants. Commands reject simultaneous access to the lab.

A passing replay establishes that the exercised WPF path works under the isolated X11 configuration. It does not establish a fix for the live Arena failure. HearthMirror polling, watcher dispatch, ArenaSmith requests, real picks, and Hyprland composition are outside this fixture's coverage. The compatibility plugin runs only with `--with-plugin`; its updates run on the fixture's UI timer rather than HDT's plugin scheduler. The generic popup is not HDT's card-preview implementation.
