# Replay HDT UI transitions in isolation

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
3. Inspect that directory's `result.json`, `supervisor.log`, state snapshots, and screenshots.

Each run creates a fresh prefix and app directory from the seed. The sequence opens Settings with native mouse input, enters the Arena lobby, displays two synthetic offers, returns to the menu, resizes the main window, shows and hides a WPF popup, and executes HDT's New command. The New command must open HDT's actual deck-type dialog.

The host checks card identities, displayed score text, panel visibility, Settings state, and dialog type. Screenshots support visual inspection. The test does not automatically prove pixel clearing after every popup.

## Check failure handling

Run the deliberate assertion failure:

```sh
devenv shell -- hdt-replay run --inject-failure
```

Expect a nonzero exit status and `negativeAssertionTest: true` in `result.json`. Inspect `supervisor.log` for the deliberate assertion message. This command tests the harness's failure path; it does not inject an exception into the live tracker.

## Interpret a result

The host uses the installed HDT assembly, WPF resources, main window, overlay, Arena card models, and visibility methods. It bypasses normal HDT startup and supplies synthetic game scenes and ratings directly. Constructor work can attempt network access, but the network namespace blocks external connectivity.

The run owns a separate PID namespace, network namespace, home directory, prefix, and Xvfb display. The live prefix and desktop sockets are masked. A timeout bounds execution, and namespace exit terminates the test descendants. Commands reject simultaneous access to the lab.

A passing replay establishes that the exercised WPF path works under the isolated X11 configuration. It does not establish a fix for the live Arena failure. HearthMirror polling, watcher dispatch, ArenaSmith requests, real picks, the compatibility plugin, and Hyprland composition are outside this fixture's coverage. The generic popup is not HDT's card-preview implementation.
