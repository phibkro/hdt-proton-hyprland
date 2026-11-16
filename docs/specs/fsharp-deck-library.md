# F# deck-library presentation

## Intent and ownership

Migrate the complete existing My Decks presentation into Hdt.Views: browser,
selected-deck facts, editor/import/export, optional catalog and embedded history.
Retain the existing three-column overview and full-width secondary editor.
The independently authored native C# view at 43e5c056 is the behavior/layout
reference; no vendor implementation or artwork is copied.

F# owns control creation, layout, item rendering and display formatting. C# owns
validated library/catalog models, transaction/event coordination and bounded IO.
Immutable display projections or injected controls cross this boundary; Hdt.Views
must not reference the executable or its wire models. Remove the corresponding
C# presentation instead of maintaining parallel implementations. Rust client and
catalog validation, atomic storage and revision contracts remain authoritative.

## Observable acceptance

- Existing real pointer/keyboard journeys pass: new/save/edit, search/selection,
  import/export/isolated clipboard copy, archive/restore, history association,
  catalog search/add/remove/hero, invalid import and explicit reload recovery.
- Selecting a deck remains a history-only association. Background history updates
  preserve unsaved edits. Archived decks remain read-only until restored.
- New from archived/search opens a blank editable name field in one action.
  Edit/import/export expose their target before helpers can scroll it into view.
- Rendered overview at 1120×760 and 800×600 retains nonoverlapping browser,
  games and selected-deck panels; editor actions wrap at minimum width. Long
  names/notes/code/catalog paths remain confined. Retain screenshots.
- Assert the actual production controls derive from the F# presentation assembly;
  cover empty/selected facts and archive/recovery availability, not only types.
- Locked build, full managed analysis, F# formatting and relevant source checks
  pass. Existing private X11 and independent renderer lifecycle journeys pass.
  Rerun acceptance on committed clean source and preserve identity and exit codes.

## Limits

No new acquisition, account calls, automatic HDT history import, card artwork,
collection ownership, legality validation or guessed mana curves. File-picker
and clipboard availability remain platform-dependent and surface their failures.
No game, installed runtime, operator desktop or external service is touched.

## Revisions

- 2026-11-16: Initial contract after inspecting LibraryView, CardCatalogView,
  ClientLibrary, existing F# views and rendered pointer acceptance.
