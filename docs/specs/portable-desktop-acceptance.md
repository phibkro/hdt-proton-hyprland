# Private compositor acceptance

## Goal and ownership

Extend the existing portable input journey beyond Openbox using an actual current
compositor from the repository's one nixpkgs pin. Keep the existing Hyprland
desktop, Steam installation, prefixes, services and game untouched. This branch
owns only its private replay descendants and evidence. No synthetic product-side
input, account access, device removal or library replacement is permitted.

## Selected route and prior art

Initially evaluate KWin 6.8.1's X11 windowed backend on a private Xvfb parent,
then its virtual backend with KWin-owned Xwayland. KWin's upstream
[command-line source](https://github.com/KDE/kwin/blob/v6.8.1/src/main_wayland.cpp)
defines `--x11-display`, `--xwayland`, window dimensions and session ownership.
Its [contributor guide](https://github.com/KDE/kwin/blob/v6.8.1/CONTRIBUTING.md)
uses an isolated D-Bus session for nested development. Reuse the existing replay,
native controller and six-click acceptance instead of a parallel fixture.

The pinned `kdePackages.kwin-x11` explicitly rejects evaluation because upstream
ended that component. Do not restore an obsolete pin to obtain an easier pass.
GNOME's [Mutter developer guide](https://github.com/GNOME/mutter/blob/main/doc/building-and-running.md)
also supports nested/headless testing; that is a separate route, not a fallback
that may silently replace KWin. No upstream implementation is copied.

## Frozen acceptance and stopping boundary

1. Start with empty private X/Wayland sockets, a private D-Bus session, an owned
   HOME/runtime directory, no host desktop variables and no host session bus.
   No physical input or graphics device is needed; use software rendering.
2. Record KWin version, selected package identity, parent and child display
   identities, compositor process and X11 window-manager identity. Prove clients
   connect to KWin's Xwayland, not the parent Xvfb or Openbox.
   If that prerequisite fails, retain the nonzero doctor result and stop before
   HDT/WPF execution. Do not weaken software/device isolation to obtain a pass.
3. Run the existing real Wine/WPF portable journey unchanged: six actual control
   clicks, empty-space game fixture clicks, outside-overlay style stability,
   invalid acknowledgement rejection and controller-loss restoration. Retain
   failures without retrying them into acceptance.
4. Re-run the original/patched libXi request-ID regression separately. Library
   identity preservation and compositor input delivery are different results.
5. Run affected CLI/static checks and commit reviewable source. Final acceptance
   uses committed clean source; retain hashes, statuses and cleanup evidence.
6. Timeout/failure must reap only the owned namespace and retain its artifacts.
   A missing prerequisite or failed route is explicit, never an implicit skip.

## Evidence limits

Any later pass is KWin virtual/Xwayland software-rendering fixture evidence. It does not
establish a complete Plasma or GNOME session, native Wayland HDT, device hotplug,
mixed DPI, real-game state acquisition, or long-session crash prevention. PR21's
surface-ownership fix supersedes the earlier unexplained portable-hover blocker;
it does not supersede these remaining live/platform requirements.

## Observed prerequisite failure

The X11 backend cannot use the private Xvfb route: the pinned
[backend implementation](https://github.com/KDE/kwin/blob/v6.8.1/src/backends/x11/x11_windowed_backend.cpp)
requires DRI3 1.2. The initial stripped namespace also lacked `/bin/sh` for Xvfb's
keymap compiler; retaining the corrected attempt distinguishes harness setup
from this compositor prerequisite. `KWIN_COMPOSE=Q` is not supported by this
version's OpenGL renderer and is not used by the final doctor.

The device-free virtual backend reached its private Wayland socket but reported
`Found no render device!` and quit because compositing was unavailable. No usable
Xwayland client or HDT input journey followed. The reusable prerequisite doctor
is `bash scripts/check-portable-desktop.sh`, with dependencies selected by
`nix/desktop-acceptance.nix` from the existing pin. It exposes neither host DRI
devices nor session sockets, retains stderr and exact source identity, and exits
nonzero when no real KWin client can connect. It is a doctor, never a green KDE
acceptance substitute. A functioning rendering prerequisite inside these
isolation constraints remains required before extending the Wine/WPF journey.

The first exact-head doctor retained the renderer failure and timed out without
a usable client. Its missing explicit Xwayland executable dependency was then
declared in the optional shell; it must not be confused with a successful render
device prerequisite. All owned processes remained bounded by the PID namespace.

The separate libXi regression rerun satisfied all six race assertions but failed
Nix output reproducibility: the original library's failing iteration changed
from one to zero. The check now streams raw evidence into its build log and
retains only stable result/executable artifacts in its output. The semantic
original-failure/patched-success assertions are unchanged.
