# Draft and manage an Arena run

Users see advice for the current Arena decision, update the picked deck after
each pick, and leave draft mode without retaining old advice.

## Sub-features

- `arena-phases`: distinguish lobby, hero, dual-class, hero-power, card, and exit states.
- `arena-picks`: replace offers and picked cards after a decision.
- `arena-ratings`: display available account ratings for the matching offer only.
- `arena-relations`: show related cards and synergies with the picked deck.
- `arena-redraft`: show redraft discard suggestions and supported package choices.
- `arena-preferences`: respect Arenasmith enablement and dependent score controls.

## How to get to it (user POV)

The user opens Arena and enters or resumes a draft. Configure advice through
**Settings > Overlay > Arena**. The user makes every entry and pick decision.

## Driving it with WPF replay and native checks

Preconditions: the private original seed for WPF; owned observation files for
native tests. Real offers and account ratings require a live user-controlled
session and the relevant entitlement.

- Run the [WPF sequence](../overlay-ui.md#driving-it-with-xvfb-and-wpf-replay).
  Require changing synthetic offers, score visibility, menu clearing, and a
  responsive Settings command. Keep the known particle workaround enabled.
- Run the [native rendered journey](../native-avalonia.md#rendered-native-journey-and-faults).
  Step hero, card, replacement, unsupported, and menu observations. Old choices,
  counts, and ratings must disappear or be replaced together.
- Run the [original-watcher recipe](../native-avalonia.md#delivered-raw-arena-events-optional-original-assemblies).
  Check delivered events, publication, and rendered results separately. Late or
  mismatched advice must not attach to the next offer.
- For live acceptance, compare the user's displayed offer with the detected
  identities, then observe the user's next pick and following offer. Record
  ratings separately. No account-free fixture passes this route.
- Dual-class, hero-power, packages, and discard suggestions need additional
  native acquisition and presentation journeys. Unsupported input must clear
  advice rather than reuse the previous valid draft.

## Gotchas

Match tracking reads a different path from draft detection. Scores in supplied
JSON do not prove an HSReplay request. A successful particle workaround does not
prove native acquisition. An Arena lobby and an active draft are distinct states.
