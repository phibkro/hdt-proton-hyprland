# Manage a deck library

Users organize saved decks, choose a deck for tracking, and preserve changes to
cards and deck metadata.

## Sub-features

- `deck-browser`: browse, search, filter, and select saved decks.
- `deck-editor`: create a deck, edit cards and sideboards, and save or cancel.
- `deck-metadata`: rename a deck and edit notes, tags, and its web address.
- `deck-versions`: inspect versions and clone a deck or version.
- `deck-archive`: archive, restore, and delete an entry with distinct effects.
- `deck-selection`: choose tracked or no-deck mode and change deck category.
- `deck-missing`: inspect missing cards against the available collection.

## How to get to it (user POV)

Open the deck browser, the **New** menu, or **Deck** with a saved deck selected.
Use the selected deck's context menu for available deck actions.

## Driving it with native checks and original WPF replay

Preconditions: use an owned library with known decks. Collection-dependent
checks additionally require an observed collection, not an invented empty one.

- Run the [native library workflow](../native-avalonia.md#native-library-workflow).
  Create, edit, export, import, archive, restore, and reopen a deck. Require the
  rendered identities and a second read of persisted state to agree.
- Run the [catalog workflow](../native-avalonia.md#local-catalog-and-archive-restore).
  Search an owned catalog, add and remove copies, select a hero, and save.
  Malformed catalog recovery must preserve the saved deck.
- Run the [WPF transition sequence](../overlay-ui.md#driving-it-with-xvfb-and-wpf-replay).
  **New** must open the original deck-type dialog. This does not verify editor
  save, tags, versions, cloning, missing cards, or automatic selection.
- Those remaining original actions need dedicated owned-instance journeys.
  For version and deletion checks, retain old identities and history associations
  before and after the action. No executable acceptance currently covers them.

## Gotchas

Native tracking selection labels newly observed history. It does not select the
Windows HDT deck, change Hearthstone, or recalculate remaining cards. Archive is
not deletion. An imported deckstring does not certify format legality or ownership.
