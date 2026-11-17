# Import and export decks

Users bring decks into the library and share a selected deck through supported
formats and destinations.

## Sub-features

- `share-deckstring`: import and export deckstrings, including sideboards.
- `share-web`: import from a URL and update a linked deck.
- `share-game`: import observed constructed, Brawl, and Arena decks.
- `share-cardnames`: copy a selected deck's card names to the clipboard.
- `share-destinations`: export an image or use the Hearthstone code-copy instructions.

## How to get to it (user POV)

Use **Import** or **Export** in the main menu. Some entries require a selected
deck, an active game view, or a source URL.

## Driving it with native checks and original reference capture

Preconditions: owned source files and library. External destinations need their
actual application or service and explicit authorization.

- Run the [native library workflow](../native-avalonia.md#native-library-workflow).
  Import a known commented deckstring and export it again. Compare hero, format,
  card counts, and sideboards. Invalid input must leave saved bytes unchanged.
- Use the [reference capture route](../../../../../docs/guides/ui-replay.md#capture-original-views)
  to inspect available original menus. Capture establishes the entry point only.
- Web, game-import, card-name, image, and Hearthstone instruction paths need separate executable
  journeys. Record the selected route and inspect the actual destination or saved
  file. No current native deckstring test proves those paths.

## Gotchas

Equal deck names do not prove equal card identities. A generated clipboard value
must be read back. A mocked URL or supplied draft observation does not establish
original website import or game deck extraction.
Original **Export > Hearthstone** opens a code-copy and instructions flyout.
Native code copying overlaps that behavior, but the complete instruction journey
is not implemented. A local card catalog is a native extension, not evidence of
an original deck-file import feature.
Legacy Dungeon and PVPDR import preferences exist in the reference settings.
Their current availability and native behavior remain unaudited.
