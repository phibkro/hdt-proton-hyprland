# Position, render and interact with the overlay

The overlay follows Hearthstone, clears hidden content, stays above the game,
and accepts marked control input while transparent space leaves game input alone.

## Sub-features

- `x11`: exact window identity, transient apply/readback/restore and backing pixels.
- `wpf`: Settings, synthetic Arena offers, score badges, Battlegrounds tabs and popups.
- `desktop-overlay`: game move/resize/Alt-Tab, transparency, stacking and selective input.
- `live-state`: current draft offers/picks and account ratings, separately from match tracking.

## How to get to it (user POV)

Launch `hdt-overlay-launch`, then use Hearthstone and HDT Settings/overlay controls.
Development entry points are `bash scripts/check-rust-x11.sh`, `hdt-replay run`,
`hdt-launch --window-probe` and `hdt-proton surface-check`.

## Driving it with Xvfb and WPF replay

Preconditions: parent doctor; measured headroom; original HDT/native CLR seed for
WPF. A live game plus user-controlled draft/match is required for the live routes.

- **X11 relationship and pixels.** Run `bash scripts/check-rust-x11.sh` and retain the printed evidence directory. Require the actual ignored acceptance test to run, pass and compare C/Rust ARGB output. This private Xvfb display is separate from the host desktop.
- **WPF transition sequence.** From an owned prepared seed, run `hdt-replay run --with-plugin --without-particles --battlegrounds-tabs --diagnostics`. Require readiness, exit zero, a clean source revision/hash, successive `transitions.jsonl` models, Settings state, actual deck-type dialog, and state/screenshots. Retain the isolation record. Native mouse input drives Settings; synthetic setters drive Arena states, so this proves no live draft subscription.
- **Live geometry/input.** Run `hdt-status` and the Win32 probe first. With the user operating the real game, compare move/resize bounds, Alt-Tab visibility, cleared previews, transparent click-through and an actual overlay control. Record observations and relevant rectangles. Never move the overlay separately to hide a geometry defect.
- **Live state paths.** The user pauses on offers and makes picks; compare detected choices and the next offer. Check ratings/entitlement separately. A fresh Power.log and saved result prove only match tracking. If the game/draft is absent, retain the status/route attempted and record those prerequisites rather than starting a match.

## Gotchas

Ordinary Cargo tests skip the isolated X11 test; run its dedicated script.
The WPF seed contains proprietary files and stays private. `88980406` with
particles enabled is a known rendering failure; keep suppression enabled for the
normal acceptance path. Proton 11.0 replay success does not negate live Stable
submenu stalls. A static screenshot or responsive diagnostic timer is not proof
of changing game state, correct ratings or a responsive WPF command.

Experimental portable route: run `bash scripts/replay.sh run --with-plugin
--without-particles --desktop-input` from the prepared development shell. Require
six real WPF control clicks, empty-space Win32 fixture clicks, invalid readiness
rejection and controller-loss recovery in `desktop-input-result.json`, followed
by the normal replay's successful exit. Keep source hashes and isolation evidence.
This fixture proves the opt-in X11 owner, not KDE/GNOME or live-game support.
