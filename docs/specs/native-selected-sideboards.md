# Selected-deck sideboard overview

## Intent and warranted facts

Show saved sideboards beneath main-deck cards in My Decks' selected-deck overview.
Reuse the existing validated SideboardCard → LibrarySideboardDisplay projection.
Group by supplied owner DBF ID and render exact card DBF IDs/counts. No catalog,
card-name, mana, collection, legality, account or acquisition facts are inferred.
Main-deck card count excludes sideboard copies. F# owns presentation; existing C#
coordination and Rust validation/storage remain authoritative and unchanged.

WPF hierarchy reference: pinned upstream 2698623dc0e91491671a614ae176f9db68831aa7,
Windows/MainWindow.xaml:260–261 and MainWindow.xaml.cs:515–521; see
[native layout provenance](native-layout.md). Only hierarchy/behavior is examined;
no upstream implementation, artwork or data is copied.

## Frozen observable acceptance

- Actual pointer/keyboard save or import through Rust, select deck, return to
  overview: exact sideboard owners, identities and multiplicities are rendered.
  Identical card IDs under different owners remain distinct groups.
- Empty sideboards/no saved selection show no sideboard rows or stale headers.
  Switching decks and New clear obsolete groups; unsaved edits do not rewrite
  saved overview facts. Existing history-only selection and archive/read-only
  semantics remain intact.
- At 1120×760 and 800×600 groups fit the existing scrollable selected panel. Long
  owner/card IDs and large valid counts wrap; long lists scroll with actual wheel
  input while the final row remains reachable. Navigation/editor geometry and both
  minimum-width edit entrypoints retain pointer coverage. Preserve screenshots.
- Existing real rendered library/catalog/history/recovery journeys pass, with
  semantic assertions on actual selected-panel controls. Removing rendering must
  fail the new pointer-journey assertion. No direct setter-only acceptance.
- Locked managed build with analyzers/warnings-as-errors, affected formatting and
  normal hooks pass. Rerun on committed clean content; record exact identities,
  exits and orderly cleanup. Separate existing X11/lifetime/live gates are not
  newly established by this display-only change.

## Limits and custody

One bounded presentation slice, no new dependencies/schema/IO/acquisition. No
installed runtime/prefix/service, operator display, game or account actions. Keep
shared-plugin compiler fixes and public ancestry; root owns publication/review.
