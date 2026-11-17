# Read and interact with tracking windows

Users keep tracking information aligned with the game and operate controls
without transparent window areas taking game input.

## Sub-features

- `window-follow`: follow game movement, resize, focus, and monitor changes.
- `window-input`: route transparent-area clicks to the game and control clicks to UI.
- `window-preview`: clear card previews, guides, and other transient visuals.
- `window-match`: show decks, sideboards, counts, timers, secrets, and active effects.
- `window-guidance`: show mulligan, related-card, and Outfinder advice.
- `window-auxiliary`: player, opponent, timer, and statistics windows.
- `window-mercenaries`: ability previews and tasks in Mercenaries mode.
- `window-errors`: clear expired facts and keep failures visible without freezing siblings.

## How to get to it (user POV)

Launch the tracker with the game and enable the required panels in **Settings >
Overlay**. Open auxiliary windows through their configured controls. Native
preview overlay selection is under **Options > Native overlay**.

## Driving it with private X11, WPF replay, and live observation

Preconditions: an owned display and window fixture for isolated input tests.
Original transparency and desktop behavior require their actual compositor.

- Follow [overlay verification](../overlay-ui.md). Require measured identities,
  actual pixels, click delivery, and geometry rather than visibility flags alone.
- Run the [native X11 journey](../native-avalonia.md#native-input-empty-x11-overlay).
  Require actual native-rendered pixels, empty input shape, focus and stale-data
  clearing, and exact-child cleanup.
- Run [independent UI lifetimes](../native-avalonia.md#independent-ui-lifetimes-explicit-opt-in).
  Freeze and restart each owned role. Its sibling must remain responsive and
  expired observations must clear.
- Original WPF preview clearing has an executable screen-pixel journey in the
  [replay guide](../../../../../docs/guides/ui-replay.md#check-the-original-card-preview).
  It tests the main-window preview, not every overlay popup.
- The user performs desktop move, resize, and Alt-Tab checks. Auxiliary windows,
  secret logic, timers, mulligan advice, Mercenaries, and mixed-scale behavior
  need separate journeys before native parity can be claimed.

## Gotchas

The native panel is opaque and input-empty. It does not reproduce the patched
WPF overlay's transparent selective interaction. Private X11 is not Hyprland
composition. Separating native UI processes does not remove the original WPF
producer's acquisition dependency.
