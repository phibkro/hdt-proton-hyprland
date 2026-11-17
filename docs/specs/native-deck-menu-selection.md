# Native Deck action requires a saved selection

## Frozen bounded contract

The top Deck action must share the selected-deck edit precondition with the
existing Edit selected deck control. With no saved deck selected, show an explicit
selection instruction and retain the current overview or unsaved editor contents.
With a saved active or archived deck selected, preserve the existing edit/read-only
behavior and bring the name field into view. This does not select a deck for live
tracking or history, create a new deck, or add a new acquisition path.

Reference: independently read upstream MainWindow.xaml:132–133 (secondary deck
editor) and MainWindow.xaml.cs:553–556 (editor opens with an explicit Deck model)
under the canonical reference/upstream tree. No vendor implementation/assets are
copied. Native authority is LibraryView.EditSelected, F# DeckLibrarySurface and
its existing availability rules. Preserve the public shared-plugin compiler fixes.

## Observable acceptance

- Actual pointer activation of top Deck with an empty library at1120×760 and
  800×600 shows the selection instruction, keeps the overview visible and leaves
  library revision/deck count unchanged; retain rendered screenshots.
- The same action while composing a new unsaved deck preserves its name/content.
- With a saved selection, the actual top Deck action opens the existing name field
  before the input helper can scroll it, including minimum width. At minimum width,
  return with the actual overview control and repeat through Edit selected deck;
  both entrypoints retain independent pointer coverage.
- Existing rendered CRUD/search/import/export/archive/catalog/history and layout
  acceptance passes using the real Rust IO boundary. No new WPF, acquisition or
  desktop parity claim follows from native headless rendering.
- Locked managed build/full analyzers, affected formatting and normal commit hooks
  pass on clean committed content; retain exact source/binary identity and exits.

The change is one controller precondition, not a second presentation or IO layer.
README ownership is derived from the current F# library source and C# controllers.
