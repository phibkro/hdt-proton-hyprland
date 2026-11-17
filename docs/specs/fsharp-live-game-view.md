# F# live-game presentation

## Goal and journey

Continue the native F# migration with the complete Live Game view. Display only
supplied remaining cards, known player hand and public boards. Switching game
modes replaces the previous display; stale or failed acquisition clears every
card and shows the supplied failure reason. Recovery restores the new facts.

## Design and limits

Keep validated C# wire contracts and bounded IO unchanged. Project into immutable
F# display facts and explicit unavailable/observed and hidden-deck/card-list
unions. F# owns the controls and their formatting. No executable dependency from
the F# library to the C# application. Reuse existing Avalonia panels and lists;
do not copy vendor UI code/assets. The reference hierarchy and applicable WPF
layout, visibility and dispatcher mappings are in the
[migration review](../reference/wpf-migration.md).

Unavailable state hides the game sections instead of inheriting visibility from
the previous mode. Battlegrounds hides and clears the remaining-deck section;
returning to a supplied constructed game restores it. Unknown values remain
unknown, including health; opponent hand/secret identities remain unavailable.

## Acceptance

- Locked build, C#/F# format and fast checks pass on committed clean source.
- Real rendered controls show supplied card/count/health text at 1120×760 and
  800×600, without overlapping columns or text escaping the available width.
- The actual publisher → Rust reader → dispatcher path covers constructed,
  Arena, Battlegrounds, stale, menu, recovery and backward-sequence rejection.
  Hidden/unavailable sections retain no previous card items.
- Private X11 Options/input/focus/geometry and independent Main/renderer
  lifecycle journeys remain passing. Preserve exact source and evidence paths.
- No game launch, account activity, installed runtime change or live parity
  claim. Remaining HDT modes/features and WPF styling are not implemented here.

The existing raster overlay uses the same F# card text projection. Its production
content tree must retain supplied names/counts/health rather than wire-record
diagnostic fields; render and check that non-Arena frame through private X11.

## Revisions

- 2026-11-16: Initial bounded contract after the shared Arena presentation slice.
