# Use Battlegrounds guidance

Users receive selection advice and inspect changing shop, combat, guide, and
session information without blocking other tracker controls.

## Sub-features

- `bg-selection`: hero, quest, trinket, and hero-power advice.
- `bg-minions`: minion pool filters, pinning, and shop information.
- `bg-guides`: guides, compositions, and inspiration.
- `bg-combat`: combat predictions, opponent information, and history.
- `bg-session`: placement, tribes, session summary, and mechanic notices.
- `bg-transitions`: clear obsolete panels when the phase or mode changes.

## How to get to it (user POV)

The user opens Battlegrounds. During the user's game, use the overlay top bar and
its available minion, guide, and other controls. Session information also has
auxiliary-window entry points.

## Driving it with original replay and native checks

Preconditions: the original seed and owned synthetic models for isolated WPF.
Full seasonal or service advice requires its actual data and account conditions.

- Run the [WPF Battlegrounds-tab sequence](../overlay-ui.md#driving-it-with-xvfb-and-wpf-replay).
  Repeatedly open and close its tested tabs. Require responsive commands and
  clearing. This covers only the specific controls exercised by that sequence.
- Run the [native observation bridge](../native-avalonia.md#optional-native-observation-bridge).
  Switch a supplied constructed observation to Battlegrounds. Obsolete deck rows
  must clear while supplied public board and hand facts remain accurate.
- Selection guides, compositions, combat calculations, and session analytics need
  dedicated native implementations and rendered journeys. The current basic
  game view cannot pass those checks.
- For live regression, record the actual top-bar control, phase, and resulting
  latency while the user plays. Do not start a match for this test automatically.

## Gotchas

A Battlegrounds mode label is not migrated guidance. Seasonal controls may lack
current data or regional availability. The reported card-back and compositions
stalls need their exact control and timing, not a generic successful tab click.
