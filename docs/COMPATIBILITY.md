# Observed compatibility

This record describes the desktop tests from 2026-11-15. It distinguishes user observations, process inspection, log inspection, and saved HDT records. A verified result covers the stated observation, not every combination of settings.

## Tested environment

| Component | Observed configuration |
|---|---|
| Operating system | NixOS |
| Compositor | Hyprland 0.56.2 at commit `efb50993780079460b0cbed1363e2166a2de1d9f` |
| Display | One 3440 × 1440 monitor at scale 1 |
| Window system | XWayland |
| Game launcher | Battle.net through a non-store Steam shortcut |
| Compatibility tool | Proton Experimental with Wine 11.0 |
| HDT | Release 1.60.1, assembly version 1.60.1.9028 |
| .NET | Native Microsoft .NET Framework 4.8 |

Native Wayland, fractional scaling, mixed DPI, multiple monitors, other compositors, and stable Proton remain untested.

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
| Stable Proton | Untested | Only Experimental was installed during the inspection. No version selection or prefix copy was changed |

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

Raw logs and saved game records remain private because they contain account or game data. The public record contains selected facts rather than the raw files.

The latest read-only inspection found native CLR, fresh control snapshots, matching game and overlay origins, and fresh `Power.log` output. HDT history contained the two Arena wins and the three Standard results.

The [test procedures](TESTING.md) cover the remaining comparisons, including exact offer identities, a fresh initial draft, more controls, automatic startup, and stable Proton. A longer session is still needed to investigate the earlier UI stall.
