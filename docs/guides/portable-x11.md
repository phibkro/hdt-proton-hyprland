# Experimental portable X11 input

The installed launcher uses the verified Hyprland backend. The native host also
provides explicit `x11-desktop` and `x11-control` commands for isolated development
of other desktops. No automatic fallback selects them. KDE, GNOME, native
Wayland, mixed DPI and multiple monitors are not accepted configurations yet.

## Why a second adapter is needed

HDT is still the original Windows WPF application under Proton. XWayland exposes
its windows to Linux; the compatibility plugin exports WPF control rectangles.
The portable owner applies X Shape input regions, so controls can receive clicks
while surrounding space reaches the underlying window.

Input shaping alone did not reliably deliver Windows mouse events. The isolated
investigation found a circular dependency: Wine retained an old pointer position,
HDT retained its cached click-through state, and the transparent popup could not
receive the messages needed to update that state. Native focus alone did not fix
it. The experimental adapter combines popup management (`WS_EX_APPWINDOW`) with
a call to HDT's own `SetClickthrough(false)` when a fresh native acknowledgement
reports the pointer over a control. It retains explicit Win32 activation because
X11 focus alone did not establish Wine foreground state in the fixture.

The inspected HDT assembly was 1.60.1.9028, SHA-256
`65d66314dddd8a36588a83b5d60deebea8ee2e758c6b91751838d663d0ae7586`.
These observations do not establish compatibility with another HDT build.

## Ownership and failure behavior

One X selection per overlay excludes competing native owners. One file lock
excludes controllers sharing an evidence directory. Identity checks bind game,
main window and overlay to the expected PID, title and Steam class. Input
rectangles must fit the actual window; expired, malformed or mismatched snapshots
clear input. Acknowledgements bind the session, Windows handle and timestamp.

Cleanup clears the input shape, returns focus only when owned, and restores
parent, stacking and style state only when it still matches the last owned write.
The plugin changes activation styles only after native readiness. Wine may retain
managed-window status until HDT restarts, even after style restoration. Taskbar
and switcher presence on target desktops remain untested.

## Reproduce without a game

Use the prepared account-free environment described in [UI replay](ui-replay.md).
From the development shell, build the native host and run:

```sh
CARGO_BUILD_JOBS=2 cargo build --locked
bash scripts/check-rust-x11.sh
bash scripts/replay.sh run --with-plugin --without-particles --desktop-input
```

The portable journey uses the actual HDT WPF overlay, a registered test button,
and a separate window-only Win32 game stand-in. Native clicks exercise the Rust
controller, plugin export, X Shape and Wine input translation. It checks six WPF
Click events, empty-space game clicks, invalid startup acknowledgements, and
controller-shutdown focus/style recovery. It then continues the ordinary replay.
It does not launch Hearthstone or access an account. Results and source hashes
remain in the printed private evidence directory.

The replay is an Xvfb/Openbox test. Passing it is a gate for this experimental
implementation, not proof of KDE/GNOME usability, live game state, Premium access
or long-session reliability. The existing Hyprland route remains the working
installation while real desktop acceptance is outstanding.

The optional [private compositor doctor](../specs/portable-desktop-acceptance.md)
uses actual pinned KWin virtual/Xwayland components without host devices or
session sockets:

```sh
bash scripts/check-portable-desktop.sh
```

It returns nonzero if compositor prerequisites fail and retains the reason in
its printed evidence directory. The current device-free route reports no render
device; no KWin HDT input acceptance has run. A successful doctor would still
require the full Wine/WPF journey before claiming compositor fixture support.
