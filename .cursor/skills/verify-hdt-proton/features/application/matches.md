# Track matches and review history

Users see changing game facts and retain completed results associated with the
correct deck and mode.

## Sub-features

- `match-detection`: detect game start, mode, and end.
- `match-live`: update remaining cards, counts, and other observed game facts.
- `match-history`: retain results with deck associations and avoid duplicates.
- `match-statistics`: filter games and inspect deck and global statistics.
- `match-correction`: add or edit a game, add notes, and move or delete one or several results.
- `match-arena-run`: retain Arena results and run rewards.

## How to get to it (user POV)

Launch the tracker with Hearthstone. During a game, read the overlay. Afterward,
open **Stats**, **My Games**, or the selected deck's statistics.

## Driving it with native checks and live observation

Preconditions: an owned observation source and native library for isolated tests.
Original live tracking requires the user's game and a fresh game log.

- Run the [native observation bridge](../native-avalonia.md#optional-native-observation-bridge).
  Observe changing supplied constructed and Arena facts, then a completed result.
  Require one persisted record, deduplication, and correct native association.
- Repeat publication of the same result and inspect persisted history. Existing
  records and unsaved deck edits must remain intact.
- In the native rendered journey, browse two saved decks with distinct results.
  **My Games** rows and totals must match only the displayed deck. Switch to
  **Stats** and require all records, including an unassociated result. Browsing
  must leave stored associations and revisions unchanged. Archive, no-selection,
  and empty-history states must not retain the previous deck's rows.
- Follow [live state checks](../overlay-ui.md#driving-it-with-xvfb-and-wpf-replay)
  while the user plays. Compare start, end, fresh log output, and the saved match.
  A responsive window alone does not verify tracking.
- Original filtering, aggregate analytics, corrections, and rewards need dedicated
  owned-data journeys. Those paths have no complete native acceptance route.

## Gotchas

The optional producer retains a bounded current-session result list. It is not a
Windows history import or an exactly-once event journal. Supplied snapshots do
not prove independent game-log parsing or draw and card-origin tracking.
