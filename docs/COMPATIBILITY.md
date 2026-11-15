# Observed compatibility

Desktop session tested on 2026-11-15: NixOS, Hyprland 0.56.2 (`efb50993780079460b0cbed1363e2166a2de1d9f`), one 3440x1440 monitor at scale 1, XWayland windows, Steam non-store Battle.net shortcut, Proton Experimental / Wine 11.0, HDT 1.60.1.9028 with native .NET Framework 4.8. This does not establish native-Wayland, fractional scaling, mixed-DPI, multi-monitor or other compositor support.

## Acceptance results

| Path | Result | Evidence and limits |
|---|---|---|
| HDT startup | Verified | Native Microsoft CLR mapped in the real tracker process; tracker stayed open after earlier Wine Mono crashes |
| Game detection | Verified | Correct log directory, fresh Power.log consumption and automatic import of an existing complete Arena deck |
| Standard match recording | Verified | Three saved Ranked/Standard results at cleanup; one earlier match was observed through an HDT-only restart |
| Arena match recording | Verified for two later saved results; historical failures retained | During the structure/setup work, read-only DeckStats inspection found two Arena wins, each 10 turns, with distinct start/end times. Earlier two completions were not saved during a UI/export stall; no isolated cause or general stability fix is claimed |
| Live Arena redraft recording | Verified for saved events | After the operator's redraft, HDT saved five picks with three choices each. Individual displayed card identities were not compared; a fresh full draft remains untested |
| ArenaSmith ratings | Verified for recorded redraft | Operator upgraded to Premium. All five recorded picks report ArenaSmith available and overlay visible, with 15 nonzero rating entries. Earlier unregistered-trial observations predate the upgrade |
| Transparent overlay | Verified with workaround | Software-rendering profile plus 1/255 black background; faint tint is intentional |
| Hover preview clearing | Verified | Operator confirmed overlay and main-window previews disappear. WPF requested hide while the X11 surface retained old content before the alpha-floor test; the exact defective graphics function was not identified |
| Movement/resizing/Alt-Tab | Verified for preceding visual profile | Operator confirmed deck follows and game input works. Final control gate was not separately subjected to the full transition sequence |
| Selective control input | Verified for one control and ordinary pass-through | Operator closed the blue Mulligan Guide announcement and confirmed transparent space still interacts with Hearthstone. WPF mouse-event counters also advanced; every HDT control was not individually tested |
| Stacking/flicker | Verified in transient-link test | Linking only the overlay's X11 transient parent to the game lets Hyprland raise them together; repeated raising disabled after readback. Operator confirmed no flicker, clearing and control input, including after relaunch |
| Automatic startup | Verified for current session | Enabled Hyprland-session user service detects the real game, reuses an existing tracker and launches the compatibility profile without duplicates. Fresh-login/new-game transition remains untested |
| Residual Arena analysis / main-window input | Not reproduced after recovery | User confirmed normal operation after restart and leaving Arena. WPF reported the Arena panel Collapsed outside Arena. Wrong deck selection explained the stale list; the earlier residual visual and apparent freeze have no isolated root-cause fix |
| HDT update check | Verified after full installation | Same verified 1.60.1 installer, complete Velopack layout, same prefix/Proton. Update check reports “No new update available”; previous NotInstalledException absent. Future version download/application remains untested |
| Long-session UI liveness | Failed later observation | After successful visual/control tests, both UI heartbeat and control export timestamps stopped progressing while the process stayed alive. Current log also stopped; no matching HearthMirror RPC exception was found. Cause unisolated; stale regions expired as designed |

“Verified” combines named observations: operator visual/click acceptance, process maps, selected logs, saved results, or geometry. It does not mean automated end-to-end coverage of every combination. Raw local evidence includes private session/game material and is deliberately not published.

## Setup integration checks

| Path | Result | Evidence and limits |
|---|---|---|
| Steam Launch Options wrapper | Verified subprocess behavior; Steam client untested | Controlled service success/failure tests preserve arguments and game exit status. A real request to the existing active desktop service succeeded. The operator has not yet launched Battle.net through the new option |
| Structured setup command | Verified composition and real plugin build; application untested | Tests cover ordered build/install/configuration and fail-fast behavior. The moved plugin compiles against the real running prefix. The new aggregate command has not closed/reconfigured the accepted live tracker |
| Stable Proton | Untested | Only Experimental installed locally; no compatibility selection or prefix was changed |

## Why these mechanisms were chosen

The ordinary protontricks path used the right prefix but a different temporary-files namespace on this NixOS setup, producing another wineserver and a stale fsync mapping. The launcher instead joins the actual server namespace and retains the game's environment in memory. It does not log that environment. Both game and tracker were independently observed with WINEFSYNC=1.

Native .NET resolved the observed tracker startup failure with Wine Mono/mixed C++/CLI dependencies. A full stopped-prefix recovery copy preceded that installation. A broken local automount drive mapping also delayed Wine startup; its one-off repair is not generalized or shipped here.

The plugin refreshes geometry through HDT's own API rather than guessing monitor coordinates. Exact-title compositor rules prevent tiling and manage stacking without moving the game. HDT background hiding is disabled so the local compositor integration owns foreground stacking.

Repeated game clicks hid the overlay with only the active-window event handler, so removing the 100ms raise fallback alone failed. The pinned [Hyprland WindowState implementation](https://github.com/hyprwm/Hyprland/blob/efb50993780079460b0cbed1363e2166a2de1d9f/src/desktop/state/WindowState.cpp) instead raises X11 transient children with their parent. `overlay-link.c` checks exact titles/class/PIDs, sets only HDT's `WM_TRANSIENT_FOR` to the current game window and verifies readback. The host then disables polling raises, leaving the focus-transition handler for Alt-Tab. The native helper supports guarded restoration; no game property, position, focus or input event is changed. The new helper is built by the compatibility launch command. If control integration is disabled or linkage fails, the original polling fallback remains; absence of flicker is not claimed for that path.

For preview persistence, WPF showed a live UI heartbeat, hide requests and zero visible CardTooltip visuals while the last preview rectangle still contained opaque content in the overlay backing pixmap. Other overlay content prevents treating those pixel counts alone as a perfectly blank baseline. Applying a 1/255-opacity background was followed by near-background-only pixels and operator-confirmed clearing. This supports a practical clearing workaround, not a proven patch for a specific Wine/DXVK function. Restart/combined-profile effects on main-window previews were not isolated independently.

Blanket no_focus prevented compositor selection of HDT controls. Enabling focus globally intercepted empty-space interaction. The final integration enables input only inside HDT's own marked clickable bounds while the game/overlay is active. Its data is nonce-scoped, identity checked and expires if updates stop. Hover-only game-card regions are deliberately excluded. The original XShape-only approach failed and was retired.

## Remaining real tests

1. Repeat Arena match recording across a longer session; two later saved Arena results now establish successful recording after the earlier failures.
2. When an Arena offer is available, pause before selecting. Compare the displayed options with HDT's automatic detection, then let the operator pick and verify the next offer/pick state.
3. Extend the successful redraft/rating test to a fresh initial draft and compare displayed individual offers with detected cards.
4. Exercise more overlay controls, drag/resize/Alt-Tab under the final input gate, and additional monitor/scale configurations.
5. Isolate the later UI/export stall and complete a sustained session with fresh heartbeats. No claim that matched WINEFSYNC or the visual fixes resolve this stall.

Arena entry, picks and gameplay remain operator actions. No game patch, injected rendering, automatic picks or account bypass is implemented.

At cleanup the operator reported that everything seemed to work. Live read-only checks still found native CLR, advancing UI/input snapshots, enabled automatic startup and the Arena panel Collapsed during gameplay. A later read-only check during repository organization found two distinct saved Arena wins as well as the three Standard results, fresh Power.log output, native CLR and fresh control heartbeats. This establishes those Arena saves, not a general cure for the earlier long-session stall.
