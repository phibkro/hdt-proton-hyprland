# Observed compatibility

Desktop session tested on 2026-11-15: NixOS, Hyprland 0.56.2 (`efb50993780079460b0cbed1363e2166a2de1d9f`), one 3440x1440 monitor at scale 1, XWayland windows, Steam non-store Battle.net shortcut, Proton Experimental / Wine 11.0, HDT 1.60.1.9028 with native .NET Framework 4.8. This does not establish native-Wayland, fractional scaling, mixed-DPI, multi-monitor or other compositor support.

## Acceptance results

| Path | Result | Evidence and limits |
|---|---|---|
| HDT startup | Verified | Native Microsoft CLR mapped in the real tracker process; tracker stayed open after earlier Wine Mono crashes |
| Game detection | Verified | Correct log directory, fresh Power.log consumption and automatic import of an existing complete Arena deck |
| Standard match recording | Verified | Two saved Ranked/Standard results; one was observed through an HDT-only restart during the match |
| Arena match recording | Failed earlier test | Game log completed, but HDT stopped progressing mid-match and saved no result. No later completed Arena retest |
| Live Arena draft offers and picks | Untested | A completed-deck import does not prove live offer/pick reads. Requires an operator-controlled offer screen and pick |
| ArenaSmith ratings | Untested | Separate HSReplay/account feature; no entitlement or displayed-rating test |
| Transparent overlay | Verified with workaround | Software-rendering profile plus 1/255 black background; faint tint is intentional |
| Hover preview clearing | Verified | Operator confirmed overlay and main-window previews disappear. WPF requested hide while the X11 surface retained old content before the alpha-floor test; the exact defective graphics function was not identified |
| Movement/resizing/Alt-Tab | Verified for preceding visual profile | Operator confirmed deck follows and game input works. Final control gate was not separately subjected to the full transition sequence |
| Selective control input | Verified for one control and ordinary pass-through | Operator closed the blue Mulligan Guide announcement and confirmed transparent space still interacts with Hearthstone. WPF mouse-event counters also advanced; every HDT control was not individually tested |
| Stacking/flicker | Usable in tested session | Overlay remains above the active game; slight flicker accepted, not proven eliminated |
| Self-updating HDT | Untested | Manually extracted build reports a caught Velopack NotInstalledException. No claim that Squirrel/msdelta instructions fix this build |
| Long-session UI liveness | Failed later observation | After successful visual/control tests, both UI heartbeat and control export timestamps stopped progressing while the process stayed alive. Current log also stopped; no matching HearthMirror RPC exception was found. Cause unisolated; stale regions expired as designed |

“Verified” combines named observations: operator visual/click acceptance, process maps, selected logs, saved results, or geometry. It does not mean automated end-to-end coverage of every combination. Raw local evidence includes private session/game material and is deliberately not published.

## Why these mechanisms were chosen

The ordinary protontricks path used the right prefix but a different temporary-files namespace on this NixOS setup, producing another wineserver and a stale fsync mapping. The launcher instead joins the actual server namespace and retains the game's environment in memory. It does not log that environment. Both game and tracker were independently observed with WINEFSYNC=1.

Native .NET resolved the observed tracker startup failure with Wine Mono/mixed C++/CLI dependencies. A full stopped-prefix recovery copy preceded that installation. A broken local automount drive mapping also delayed Wine startup; its one-off repair is not generalized or shipped here.

The plugin refreshes geometry through HDT's own API rather than guessing monitor coordinates. Exact-title compositor rules prevent tiling and manage stacking without moving the game. HDT background hiding is disabled so the local compositor integration owns foreground stacking.

For preview persistence, WPF showed a live UI heartbeat, hide requests and zero visible CardTooltip visuals while the last preview rectangle still contained opaque content in the overlay backing pixmap. Other overlay content prevents treating those pixel counts alone as a perfectly blank baseline. Applying a 1/255-opacity background was followed by near-background-only pixels and operator-confirmed clearing. This supports a practical clearing workaround, not a proven patch for a specific Wine/DXVK function. Restart/combined-profile effects on main-window previews were not isolated independently.

Blanket no_focus prevented compositor selection of HDT controls. Enabling focus globally intercepted empty-space interaction. The final integration enables input only inside HDT's own marked clickable bounds while the game/overlay is active. Its data is nonce-scoped, identity checked and expires if updates stop. Hover-only game-card regions are deliberately excluded. The original XShape-only approach failed and was retired.

## Remaining real tests

1. Play and finish an existing Arena match; confirm a saved Arena result independently of Power.log completion.
2. When an Arena offer is available, pause before selecting. Compare the displayed options with HDT's automatic detection, then let the operator pick and verify the next offer/pick state.
3. Check ArenaSmith ratings separately from detection, including account availability and HSReplay errors.
4. Exercise more overlay controls, drag/resize/Alt-Tab under the final input gate, and additional monitor/scale configurations.
5. Isolate the later UI/export stall and complete a sustained session with fresh heartbeats. No claim that matched WINEFSYNC or the visual fixes resolve this stall.

Arena entry, picks and gameplay remain operator actions. No game patch, injected rendering, automatic picks or account bypass is implemented.
