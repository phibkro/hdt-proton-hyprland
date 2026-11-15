# HDT compatibility design

Why does HDT need more than the same Wine prefix to work with Hearthstone on this desktop?

HDT reads game logs, inspects live state through HearthMirror, and draws a separate WPF overlay. Each path has different dependencies. The fixes in this project address process discovery, rendering, geometry, and input separately.

## The Steam process namespace

A prefix identifies Wine's files and configuration. Steam also starts Wine inside its runtime namespaces.

In this investigation, protontricks selected the correct prefix but used a different temporary-files namespace. HDT started another wineserver and encountered a stale fsync mapping. The launcher now joins the running server's user and mount namespaces, then derives that server's Proton path.

The launcher keeps the inherited environment in memory because the environment can contain credentials. It rejects ambiguous servers and deleted fsync mappings instead of starting another server.

Both Hearthstone and HDT had `WINEFSYNC=1` during the observed session. That observation does not explain or resolve the later UI stalls.

## Native .NET and the HDT updater

Wine Mono crashed with the tracker's mixed C++ and CLI dependencies. Native Microsoft .NET Framework 4.8 resolved the observed startup failure. The installation followed a stopped-prefix recovery copy.

A broken local drive mapping also delayed startup. That machine-specific repair is not part of this repository.

The first HDT layout contained the extracted application files but lacked Velopack's installation structure. HDT opened, but its updater threw `Velopack.Exceptions.NotInstalledException`.

A full installation supplied the missing updater files. Resolving the executable's physical path also let HDT find `Update.exe` when `app` was a symlink. The next update check reported no available update. Applying a future update remains untested.

## Preview clearing

The black overlay cleared after the HDT-only DXVK workaround and WPF software rendering. Hidden previews still left pixels behind.

WPF continued to report UI updates and hide requests, with zero visible `CardTooltip` visuals. The overlay's XComposite backing pixmap still contained opaque pixels in the previous preview rectangle.

Other overlay content can occupy that rectangle, so the pixel counts alone do not prove that the entire rectangle should be blank. The observation does separate WPF's visibility state from the displayed result.

A black overlay background at 1/255 opacity was followed by near-background pixels and user-confirmed preview clearing. That faint tint is an intentional workaround. The investigation did not identify a defective Wine or DXVK function.

The user also confirmed that main-window previews cleared. The effect of the combined settings and restart on that result was not isolated.

## Window position and stacking

The plugin calls HDT's own geometry update when the game rectangle changes. Hyprland rules match the exact window title and Steam class so the rules do not affect every window in the prefix.

The first stacking workaround raised the overlay every 100 milliseconds. That restored visibility after clicks but caused flicker. Removing the timer left the overlay behind Hearthstone again.

The pinned [Hyprland WindowState implementation](https://github.com/hyprwm/Hyprland/blob/efb50993780079460b0cbed1363e2166a2de1d9f/src/desktop/state/WindowState.cpp) raises X11 transient children with their parent. `src/hyprland/overlay-link.c` uses that behavior by setting the overlay's `WM_TRANSIENT_FOR` to the game window.

The helper checks both windows' titles, classes, and process IDs before the change. It changes only the overlay property and verifies the result. The input bridge then disables repeated raising. The focus-change handler remains for Alt-Tab.

If the link fails or control integration is disabled, the polling fallback remains. The no-flicker result applies to the verified transient-link configuration. The helper supports restoration while the window identities and current parent still match.

## Overlay input

A blanket `no_focus` rule let game input pass through but prevented HDT control clicks. Allowing focus everywhere made transparent overlay space intercept game interaction.

The plugin exports rectangles for HDT's marked clickable controls. `hdtProtonControlRule` allows input while the pointer is inside those rectangles and the game or overlay is active. Hover-only game-card regions are excluded.

The bridge checks the session nonce, window identities, rectangle bounds, and data age. The compositor expires the rectangles if updates stop. The integration sends no synthetic clicks and writes no game memory.

The earlier XShape experiment did not establish working control input and was retired.

## Tracking and account access

Ordinary match tracking reads `Power.log`. Live draft choices and Arena state use HearthMirror. ArenaSmith ratings require a separate HSReplay integration and account entitlement.

A saved match therefore does not establish live draft detection. A detected offer does not establish access to ratings. The [compatibility record](COMPATIBILITY.md) reports those results separately.

HDT's selected deck is also separate from Hearthstone's menu selection. Choosing the intended deck in HDT resolved the reported stale list. No speculative deck-selection patch was added.

## Desktop service ownership

The Steam wrapper starts the existing systemd user service and executes Steam's original command. The service owns the watcher independently of Battle.net's initial launcher process.

That ownership avoids a second watcher implementation. It also means that stopping the service can close HDT. The wrapper retains Steam's selected Proton rather than choosing a version for the game.

## Language choices and prior art

HDT's public plugin interface requires C#. The Xlib helpers use C. Bash composes host commands, and the existing launcher uses Bun. The [engineering exception](engineering-exception.md) records why the current implementation retains Bun instead of migrating to Effect during a working desktop session.

The investigation evaluated these sources:

- The [same-prefix HDT launcher](https://github.com/alexzfe/HDT-linux) supplied the process-sharing approach. This desktop also required the running Steam namespace.
- The [HDT Linux instructions](https://github.com/borisbabic/hearthstone_hdt_linux) and [Linux Proton installation guide](https://github.com/Bakedpotato-99/Hearthstone-Deck-Tracker-linux-proton-install) supplied installation leads.
- The [WPF and DXVK rendering issue](https://github.com/doitsujin/dxvk/issues/4883) supplied a rendering lead.
- The [Hyprland input-region discussion](https://github.com/hyprwm/Hyprland/discussions/11834) supplied an input lead.
- [Steam's wrapper convention](https://help.steampowered.com/en/faqs/view/145A-FE54-F37B-278A) supports a host wrapper before `%command%`.

No third-party implementation was copied into this repository. The inspected upstream HDT commit was `2698623dc0e91491671a614ae176f9db68831aa7`. Its exact correspondence to the installed build remains unverified, so plugin compilation uses the installed assemblies.
