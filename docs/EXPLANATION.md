# HDT compatibility design

HDT reads game logs, inspects live state through HearthMirror, and draws a separate WPF overlay. Each path has different dependencies. The fixes in this project address process discovery, rendering, geometry, and input separately.

## The current execution path

HDT remains a Windows .NET Framework WPF application. The compatibility plugin
does not replace HDT with a Linux UI. Native Microsoft .NET Framework 4.8 runs
inside Proton's Wine environment, which supplies the Windows API implementation.
HDT's windows reach the Wayland desktop through XWayland:

```text
HDT main window, overlay, and popups
                |
       Microsoft .NET and WPF
                |
       Proton Experimental / Wine
                |
           X11 windows
                |
            XWayland
                |
      Hyprland Wayland compositor
```

Rendering also crosses WPF's composition and Direct3D paths. The current launcher
applies an HDT-only DXVK shader-model setting, while the plugin requests WPF
software rendering. These settings do not force Hearthstone to use software
rendering. The faint overlay background and Arena particle workaround address
separate rendering failures, described below.

The launcher joins the running game's Steam namespace and Wine prefix, and uses
that session's Proton executable. The plugin refreshes overlay geometry and
exports clickable control bounds. Hyprland rules and the X11 transient link keep
the overlay aligned, stacked with the game, and interactive only over exported
controls. This compositor integration is specific to the tested Hyprland setup.

Ordinary match tracking still consumes `Power.log`. Live Arena offers and state
still come from HearthMirror. ArenaSmith ratings use HDT's HSReplay integration
and require the relevant account entitlement. Working overlay rendering does
not prove that any of these data paths is advancing.

The current baseline is Experimental after a desktop comparison with Stable
11.0-2. Stable showed submenu performance and responsiveness problems in that
comparison. Experimental also retains unresolved freezes and fatal XInput
errors. The [observed version comparison](COMPATIBILITY.md#proton-version-comparison)
owns the test results and their limits.

## The Steam process namespace

### Steam's overlay stays with the game

The tracker inherits the game's Wine session so it can discover Hearthstone.
The launcher removes `gameoverlayrenderer.so` from the tracker's `LD_PRELOAD`
list. Other preloaded tools remain. The inherited game environment is unchanged.

A captured HDT freeze stopped its UI thread in `GetAsyncKeyState` while X11
waited on a mutex owned by that same thread. Steam's `XCheckIfEvent` hook appeared
on the native call path. Excluding the hook removes an unnecessary component
from that path. Repeated Settings animations on Stable Proton still caused
temporary stalls without the library. This change does not fix every UI stall.
[Valve's related overlay investigation](https://github.com/ValveSoftware/steam-for-linux/issues/11729)
describes another input-event problem involving the same hook.

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

## Arena score particles

The level-five Arena score badge contains a WPF `ParticleEmitter`. Its Loaded handler creates particle systems with `RenderTargetBitmap` textures and 3D geometry, then starts a timer. Scores and the surrounding badge artwork are separate controls.

The isolated replay reproduced `88980406` with level-five particles, including without the compatibility plugin. Internal card models advanced while the displayed scores remained on the first offer. Level-four badges passed. Level-five badges also passed when the emitter's Loaded handler was prevented from running.

Plugin version 0.5.0 registers a class handler before future emitters load. It handles that event and collapses the emitter, preventing its particle initialization and timer. WPF invokes class handlers before instance handlers; normally registered instance handlers skip handled events. See [WPF class handling](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/events/marking-routed-events-as-handled-and-class-handling). Ratings, badge artwork, and scoring requests remain in HDT's own implementation. Existing emitters require an HDT restart to remove.

This comparison isolates the particle path under the tested Proton configuration. It does not identify a specific Wine, Direct3D, or driver function. The workaround removes cosmetic particles. It does not disable Arenasmith or hide scores.

## Window position and stacking

The plugin calls HDT's own geometry update when the game rectangle changes. Hyprland rules match the exact window title and Steam class so the rules do not affect every window in the prefix.

The first stacking workaround raised the overlay every 100 milliseconds. That restored visibility after clicks but caused flicker. Removing the timer left the overlay behind Hearthstone again.

The pinned [Hyprland WindowState implementation](https://github.com/hyprwm/Hyprland/blob/efb50993780079460b0cbed1363e2166a2de1d9f/src/desktop/state/WindowState.cpp) raises X11 transient children with their parent. `src/host/x11.rs` uses that behavior by setting the overlay's `WM_TRANSIENT_FOR` to the game window.

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

HDT's public plugin interface requires C#. Rust now owns the Linux host, X11 protocol helpers, journal, and trace export. Bash retains service and Steam command boundaries; Hyprland executes Lua. Bun remains a development/test dependency. See [the Rust host](RUST.md) for tooling, migrated coverage, and the outstanding live acceptance boundary. The [engineering record](engineering-exception.md) preserves earlier language decisions.

The investigation evaluated these sources:

- The [same-prefix HDT launcher](https://github.com/alexzfe/HDT-linux) supplied the process-sharing approach. This desktop also required the running Steam namespace.
- The [HDT Linux instructions](https://github.com/borisbabic/hearthstone_hdt_linux) and [Linux Proton installation guide](https://github.com/Bakedpotato-99/Hearthstone-Deck-Tracker-linux-proton-install) supplied installation leads.
- The [WPF and DXVK rendering issue](https://github.com/doitsujin/dxvk/issues/4883) supplied a rendering lead.
- The [Hyprland input-region discussion](https://github.com/hyprwm/Hyprland/discussions/11834) supplied an input lead.
- [Steam's wrapper convention](https://help.steampowered.com/en/faqs/view/145A-FE54-F37B-278A) supports a host wrapper before `%command%`.

No third-party implementation was copied into this repository. The inspected upstream HDT commit was `2698623dc0e91491671a614ae176f9db68831aa7`. Its exact correspondence to the installed build remains unverified, so plugin compilation uses the installed assemblies.

## Immutable package and mutable data

The Nix package stores source and native helpers in the Nix store. The launcher keeps the configuration, HDT installation, backups, and evidence in a separate mutable directory. The checkout workflow keeps its original directory defaults.

Nix builds the Rust host and its X11 helpers before installation. Plugin compilation still uses the actual HDT assemblies and native .NET in the game prefix. The package therefore contains no HDT or Microsoft binaries.

The package and Home Manager module derive their application entry from one template. The service still owns the tracker lifecycle. The desktop Open action focuses the exact HDT window instead of matching the Steam class shared by Battle.net and Hearthstone.
