# Replay HDT UI transitions in isolation

Run the real HDT main window and Arena controls on a private X11 desktop. These tests leave the running game and tracker alone. See [component boundaries](../reference/components.md) for the distinction between draft updates, match tracking, and presentation.

## Prepare the private seed

Use a complete HDT installation and an existing Proton prefix with native .NET. Run commands from this checkout.

1. Set the project configuration to the HDT installation and Steam shortcut you want to copy. Follow the [configuration reference](../reference/commands.md).
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

The host checks card identities, displayed score text, actual plaque levels, visible particle emitters, panel visibility, Settings state, and dialog type.

The normal sequence also compares actual screen pixels before, during and after
the original Settings flyout and the generic transparent popup. Each comparison
uses the same 320×180 screen region within one run; no baseline is approved or
updated automatically. At least 1% of pixels must change while shown. After
closing, at most 0.1% may differ from the baseline, with ImageMagick's 2% color
tolerance using its [PDC metric](https://imagemagick.org/command-line-options/#metric)
(the count of differing pixels, rather than absolute color error). Inspect `settings-visual/result.json`, `popup-visual/result.json`, the
three crops and difference images. A visibility flag alone cannot pass these
checks. Missing images, different dimensions and image-processing errors fail.

Run the classifier's positive and negative controls without HDT:

```sh
devenv shell -- bash tests/replay/visual-triplet-checks.sh
```

Those controls reject a never-visible popup, retained popup pixels and a white
surface after closing. They validate the classifier, not WPF rendering. The
replay establishes the actual WPF path separately. This first visual test does
not cover HDT's real card-preview control, every popup, game-relative alignment,
Hyprland stacking, Alt-Tab or live-game input. Those remain distinct gates.

## Capture original views

After preparing the private seed, run:

```sh
devenv shell -- hdt-replay run --with-plugin --without-particles --reference-views
```

This is a separate capture journey. It enumerates the installed HDT binary's
actual settings tree, temporarily reveals advanced pages, and selects each leaf
through the original `ShowPage` handler. It captures the rendered main-window
settings viewport with overlapping scroll tiles, bounded to 32 tiles per page.
Exceeding that bound fails the run rather than silently truncating a page.
The native Settings button is exercised before capture; subsequent category and
scene selections use the fixture protocol, not native pointer input.

Open `reference/index.html` in the printed evidence directory for a local
expandable screenshot gallery. The directory also contains `reference/index.json`, per-image
state records, `reference-options-index.json`, source hashes and `result.json`.
Require status zero, the expected page identities and visible original control
types, and inspect the images. File existence alone does not prove correct
pixels. Existing Arena/Battlegrounds scenes use synthetic state and ratings;
no account session or game client runs. The New dialog is the original command's
actual `DeckTypeDialog`.

This captures reachable settings and selected fixture views, not every possible
HDT screen or state. Consult the [view trees and coverage limits](../reference/wpf-view-map.md).
Keep screenshots private: they contain upstream artwork/resources and are not
new assets licensed for the native client. No application assets are committed.

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

See [freeze diagnostics](diagnostics.md) for the local flight recorder, `hdt-proton trace-export`, and the replay `--diagnostics` option.

## Exercise the installed event pipeline

Run the canonical event pilot:

```sh
devenv shell -- hdt-replay run --canonical-events --with-plugin
```

Confirm exit status zero. Inspect `canonical-session1.jsonl`, `canonical.jsonl`,
and `journal/journal.jsonl` in the printed evidence directory.

This option replaces the normal UI sequence. The host feeds recorded-format
mode-transition fixtures into the installed `LogWatcherManager.OnNewLines`
method. It subscribes to the installed `GameEvents` fields and `Log.OnLogLine`.
It records the HDT and HearthWatcher hashes, canonical event names, argument
types, and parser entry/return observations. Enum arguments use their HDT names.
Other arguments use Newtonsoft's public JSON projection; capture failures are
explicit. This projection does not preserve every property of a CLR object.

The test repeats Arena/menu/Battlegrounds/menu inputs, freezes and kills the
isolated application, and starts a new application process. The native journal
must remain alive throughout. Its retained publisher bytes must match both
application sessions exactly. These checks do not establish complete game-state
equivalence, per-handler completion, or reproduction of the intermittent freeze
after Arena battles. Live HearthMirror and remote ratings are outside the test.

## Experimental portable input

`--desktop-input` requires `--with-plugin` and a separate run from
`--canonical-events`. It drives the native Rust controller and actual WPF button
through private X11 mouse input. See [portable X11 input](portable-x11.md) for the
journey, failure behavior and desktop-acceptance limits.
