# Observed compatibility

This record describes desktop tests from 2026-11-15 through 2026-11-16. It distinguishes user observations, process inspection, log inspection, and saved HDT records. A verified result covers the stated observation, not every combination of settings.

## Tested environment

| Component | Observed configuration |
|---|---|
| Operating system | NixOS |
| Compositor | Hyprland 0.56.2 at commit `efb50993780079460b0cbed1363e2166a2de1d9f` |
| Display | One 3440 × 1440 monitor at scale 1 |
| Window system | XWayland |
| Game launcher | Battle.net through a non-store Steam shortcut |
| Current compatibility tool | Proton Experimental `experimental-11.0-20261112-x86_64`, with Wine 11.0 |
| Compared compatibility tool | Steam's Proton Stable 11.0-2, internal build `proton-11.0-2e-x86_64` |
| HDT | Release 1.60.1, assembly version 1.60.1.9028 |
| .NET | Native Microsoft .NET Framework 4.8 |

Native Wayland, fractional scaling, mixed DPI, multiple monitors, and other compositors remain untested. The current path uses X11 windows through XWayland on a Wayland desktop. It is not a native Wayland port of HDT. See [the current execution path](EXPLANATION.md#the-current-execution-path).

## Proton version comparison

Experimental is the current desktop selection after the operator compared it
with Stable 11.0-2 in the existing Steam shortcut. The launcher follows the
running game's Proton version. Installing this project does not select
Experimental for Steam.

| Observation | Stable 11.0-2 | Experimental |
|---|---|---|
| Startup and menu overlay | The operator confirmed working HDT Settings, transparency, movement, resizing, Alt-Tab, controls, and click-through | These paths also worked in the desktop tests |
| Repeated Settings transitions | The operator reported progressively slower animations and a temporary freeze after about two open/close cycles. HDT recovered after a delay, then stalled again on another cycle | After switching back, repeated Settings transitions worked with slight jitter and without the same progressive slowdown in that check |
| Battlegrounds compositions submenu | Opening the panel made the overlay very slow and difficult to close, according to the operator | A controlled live comparison of this same panel remains untested |
| Plugins and other main-window interaction | A Plugins click was followed by an unresponsive tracker. Captures also found UI stalls on native input and rendering paths | Intermittent freezes remain unresolved. Working Settings transitions do not establish that every submenu is reliable |
| Isolated Settings comparison | Ten cycles completed with every open and close acknowledged | The same ten-cycle fixture also completed. Neither run reproduced the desktop slowdown |
| Arena recording after the return to Experimental | Not the comparison used for these later records | One complete Arena match was saved with no dispatcher stall during the measured match interval. A later result was saved after tracker restarts, with incomplete match continuity |
| Fatal X11 mapping errors | These two later exits were not observed in the Stable session | Two later HDT exits reported `XI_BadDevice` on `X_GetDeviceButtonMapping`, with exit status 1 |

The observations suggest a version-dependent difference in submenu performance
on this machine. They do not establish a general Proton 11.0-2 regression or
identify animations, Wine input, DXVK, or another component as the sole cause.
The isolated comparison used Xvfb and Openbox without normal HDT startup,
live watchers, account services, or the Hyprland desktop. It cannot establish
equivalent live performance between versions.

The operator returned to Experimental because the Settings interaction behaved
better there. Experimental is the current working baseline for this project,
not a crash-free configuration. Complete acceptance across Standard matches,
Arena matches and drafts, and Battlegrounds matches remains incomplete for
Stable 11.0-2. The [test procedure](TESTING.md#test-stable-proton) covers a future
comparison. The [XInput investigation](specs/xinput-device-race.md) describes
the separate fatal-error reproduction and its limits.

## Rust host acceptance, 2026-11-16

The reviewed Rust package was activated through a temporary user-service override
while Hearthstone kept running under Proton Experimental. Process inspection
confirmed the native host, Microsoft CLR, and the shared Steam mount namespace.
This test changes the launcher and Linux helpers; HDT remains the Windows WPF app.

| Path | Result | Evidence and limits |
|---|---|---|
| HDT startup and game detection | Verified | Same-prefix launch completed; HDT selected the correct log directory, and the overlay matched the game client bounds |
| Live Arena offers and picks | Verified | After the Rust-backed restart, the operator made another pick and confirmed the following offer updated |
| ArenaSmith ratings | Verified | The operator confirmed ratings on the paused offer and updates after another pick |
| Overlay alignment and input | Verified | The operator confirmed movement, resizing, Alt-Tab, preview clearing, transparent click-through, and working overlay controls |
| Main-window menus | Verified | The operator confirmed Settings and other menus responded. Settings reportedly slid smoothly without jitter; this is an observation, not a performance benchmark |
| Ordinary Arena and other match tracking | Untested in this acceptance session | Earlier saved-match results below remain separate evidence; the draft checks do not establish match recording with this package |
| Long-session reliability | Untested | A later tracker restart was manual, as confirmed by the operator; these checks do not establish crash-free operation |

The runtime override is temporary. Building or merging the package does not
update the workstation's declarative Nix configuration. Other desktops and
mixed-DPI layouts remain outside this acceptance result.

## Tracking results

| Path | Result | Evidence and limits |
|---|---|---|
| HDT startup | Verified | Process maps showed native Microsoft CLR. HDT stayed open after the earlier Wine Mono startup crashes |
| Game detection | Verified | HDT selected the correct log directory, consumed fresh `Power.log` output, and imported an existing complete Arena deck |
| Standard match recording | Verified | HDT saved three Ranked results in Standard format. One match continued through an HDT-only restart |
| Arena match recording | Verified for two later results | `DeckStats.xml` contained two Arena wins of 10 turns each, with distinct start and end times. Two earlier completions were not saved during a UI stall |
| Live Arena redraft recording | Verified for saved events | HDT saved five picks with three choices per pick. The test did not compare individual card identities on screen. A fresh full draft remains untested |
| ArenaSmith ratings | Verified for the recorded redraft | After the Premium upgrade, all five picks reported ArenaSmith available and the overlay visible. The records contained 15 nonzero ratings |
| HDT update check | Verified | The full Velopack installation reported no available update. The earlier `NotInstalledException` was absent. Downloading and applying a future version remain untested |
| Long-session UI updates | Failed in an earlier observation | HDT stayed alive while its UI snapshots, control exports, and log stopped advancing. No matching HearthMirror RPC exception was found. The cause remains unresolved |

The later saved Arena wins establish successful recording after the earlier failures. They do not establish a fix for the UI stall.

## Overlay results

| Path | Result | Evidence and limits |
|---|---|---|
| Transparent background | Verified with a workaround | The software-rendering configuration and black background at 1/255 opacity produced transparency. The faint tint is intentional |
| Overlay preview clearing | Verified | The user confirmed that previews disappear after the pointer leaves. The defective graphics function was not identified |
| Main-window preview clearing | Verified | The user confirmed that previews disappear. The effects of restart and the combined settings were not isolated |
| Movement, resizing, and Alt-Tab | Verified for the preceding visual configuration | The user confirmed that the deck follows the game and game input works. The full sequence was not repeated after the final input-rule change |
| Control input and empty-space input | Verified for one control | The user closed the blue Mulligan Guide notice and confirmed that empty space still interacts with Hearthstone. Other controls were not individually tested |
| Stacking and flicker | Verified with the X11 transient link | After the link was verified, repeated raising stopped. The user confirmed visibility, preview clearing, and control input before and after HDT relaunch |
| Residual Arena analysis | Not reproduced after recovery | After restart and departure from Arena, the user reported normal behavior. WPF reported the Arena panel as `Collapsed`. No cause was isolated |
| Apparent main-window freeze | Not reproduced after recovery | The user reported recovery after restart. In a later inspection, the common UI thread and mouse counters still advanced, so that inspection did not establish a full deadlock |
| Stale deck list | Resolved by deck selection | The user confirmed that the wrong deck had been selected in HDT |
| White main and crash windows during drafting | Failed in later tests | New, repeated Settings clicks, and a floating-state change were associated with failures. A live dispatcher capture recorded COMException `88980406`. The overlay remained drawn, but repainting was not established |
| Fresh offer after a draft pick | Failed in a later user observation | The previous offer's ratings remained visible. UI exports continued. The failing stage remains unknown |
| Arena score particle path | Verified workaround in replay and the completed live draft | Actual level-five badges reproduced `88980406`, stale pixels, and white window regions without the compatibility plugin. Version 0.5.0 prevented particle initialization and passed 12 simulated decisions with native Settings clicks. After an HDT-only restart, the operator confirmed offer updates, responsive main-window controls, and successful completion of the current draft |
| Battlegrounds menu and match | Verified by user observation | The operator reports working main-window interaction in both contexts and a working in-game overlay. This comparison did not instrument every Battlegrounds update |

The rendering and input mechanisms are described in [HDT compatibility design](EXPLANATION.md).

## Setup and startup results

| Path | Result | Evidence and limits |
|---|---|---|
| Desktop automatic startup | Verified for the current session | The enabled user service detected the real game, reused an existing tracker, and launched the compatibility configuration without duplicates. Fresh login and a new game process remain untested |
| Steam wrapper arguments and exit status | Verified in subprocess tests | Tests covered service success and failure, preserved arguments, shell quoting, and game-command exit status |
| Steam wrapper service request | Verified for an active service | The wrapper requested the real, already-active desktop service and then ran a harmless host command |
| Steam-client launch through the wrapper | Untested | Battle.net has not been launched through the new Steam option in the recorded acceptance test |
| Setup command order and failure handling | Verified in subprocess tests | Tests covered build, plugin installation, configuration order, and stopping after a failed step |
| Plugin build in the real prefix | Verified | The native .NET compiler built the moved plugin against the installed HDT assemblies |
| Setup applied to the live tracker | Untested | `hdt-setup` has not closed and reconfigured the accepted live tracker |
| Stable Proton 11.0-2 | Partially tested, with reported regressions | Baseline overlay checks worked. Repeated Settings transitions and the Battlegrounds compositions panel showed slowdowns or temporary freezes. See the [version comparison](#proton-version-comparison) |

The source checks at commit `6bddf1e` passed from a clean worktree. They covered 10 tests with 33 assertions, shellcheck, seven TypeScript entry bundles, and both native helper builds. The checks are defined by `scripts/check.sh` and exposed as `hdt-check`.

## Nix package and desktop results

| Path | Result | Evidence and limits |
|---|---|---|
| Nix package | Verified | `nix build` produced the tools and prebuilt native helpers. The package's desktop entry passed validation |
| Nix checks | Verified | `nix flake check` passed the package build, 17 tests with 50 assertions, and shellcheck |
| Home Manager module | Verified for focused evaluation | A real Home Manager evaluation produced the service and desktop entry with no failed assertions. The generated artifacts were built. A full machine activation was not run |
| Vicinae registration and Open | Verified | Vicinae listed Hearthstone Deck Tracker. Launching the registered app focused the existing HDT window without a duplicate. The game process stayed unchanged |
| Packaged tracker startup | Verified | The generated service started the Nix-packaged watcher and launcher. HDT loaded native CLR and exported fresh controls with matching game and overlay origins |
| Restart action command | Verified | `hdt-proton restart`, the desktop action's target, restarted the isolated tracker service. HDT's UI updates advanced afterward. Hearthstone's process stayed unchanged. The action was not clicked through Vicinae's menu |

The [Nix installation guide](NIX.md) covers the package, module, and desktop actions. These results do not extend the earlier visual tests to every control or monitor configuration.

## Evidence limits

### Isolated UI replay

Two fresh replay runs on 2026-11-15 passed synthetic lobby → first offer → second offer → menu transitions under Proton Experimental. Each run used the actual HDT main window and Arena control, verified changed card models and displayed scores, and accepted native Settings clicks in each phase. Resize, generic popup state, and the actual New deck-type dialog also passed. A deliberate assertion failure returned status 1 and retained evidence. No `ReplayHost.exe` or Xvfb process remained after that failure.

Those initial runs covered level-zero badges: HDT preferred the fixture's default dynamic-score object. The corrected fixture asserts actual plaque levels and particle visibility. With native Direct3D 9 selected and the HDT-only DXVK shader-model setting applied, level-five particles reproduced `88980406` without the plugin. Version 0.5.0 passed 12 decisions with three level-five badges and zero active emitters. Direct3D selection and the shader setting were confirmed in DXVK's log. The private Xvfb environment used Vulkan llvmpipe, so this does not identify a hardware-driver defect.

The replay bypasses normal HDT startup, HearthMirror, watcher dispatch, and ArenaSmith requests. Plugin coverage requires `--with-plugin`. It uses Xvfb with Openbox rather than Hyprland. Separate live acceptance followed: the installed plugin suppressed Arena emitters, and the operator confirmed offer updates, responsive main-window controls, and successful completion of the current draft. A fresh draft from initial hero selection and long-session reliability remain untested. See [Replay HDT UI transitions in isolation](UI-REPLAY.md) for the procedure.

Raw logs and saved game records remain private because they contain account or game data. The public record contains selected facts rather than the raw files.

The latest read-only inspection found native CLR, fresh control snapshots, matching game and overlay origins, and fresh `Power.log` output. HDT history contained the two Arena wins and the three Standard results.

The [test procedures](TESTING.md) cover the remaining comparisons, including exact offer identities, a fresh initial draft, more controls, automatic startup, and a complete Stable Proton retest. Longer-session reliability remains unresolved on Experimental too.
