# Duplicate a saved deck

Feature ID: `deck-duplicate`. This is an independent copy of saved content, not
version history. The [parity ledger](../reference/hdt-feature-parity.md#deck-management)
owns migration status; the [verification recipe](../../.cursor/skills/verify-hdt-proton/features/native-avalonia.md#duplicate-a-saved-deck)
owns the executable journey.

## Original behavior and native boundary

Original HDT's [clone dialog](https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/Windows/MainWindow.Edit.cs#L92)
offers a choice about copying game history, assigns a fresh deck identity and
unarchives the result. Its separate selected-version clone resets versions.
The native journey implements only the independent, no-history copy. No upstream
UI assets or implementation were copied for this change.

The selected-deck panel has a **Duplicate saved deck** form with a required name.
The source may be active or archived. The copy is active. It receives the saved
cards, sideboards, hero, class, notes, tags and website. Unsaved input stays with
the source. Cancel performs no transaction.

Success keeps the source displayed and shows **Open duplicate**. This explicit
action clears search and selects the result's current archive view. Opening another deck
replaces editor drafts, just as selecting another browser row does. Neither
duplication nor opening the result changes **Use for game history** selection.

## Ownership and invariants

The AXAML view owns layout; its F# controller owns temporary name and result
state. C# sends one transaction and resolves the single new identity from the
returned state. It never locates the result by name: duplicate names are valid.

Rust's `duplicateDeck` request requires `expectedRevision`, `sourceId` and `name`.
It uses the existing exclusive transaction, saved-state validator and atomic
replacement. There is no history-copy option, schema migration or second writer.
Only the new deck and revision are added. The new deck receives a fresh UUID and
timestamps. Every prior deck, match and tracked selection remains unchanged.

Revision conflicts, invalid names, missing sources, unknown fields and store
capacity failures leave saved bytes unchanged. The UI retains failed input and
requires reload before retrying an uncertain operation. A background history
update preserves the open form and unrelated unsaved source input.

An in-progress match keeps the deck captured when first observed. Only explicit
tracking selection can associate a later match with the duplicate. Existing
games are never copied or reassigned, and global statistics do not gain cloned
records.

## Acceptance boundary

The recipe drives real CLI/file IO and rendered pointer input, including
background supplied-observation updates. It does not establish independent game
acquisition, live Steam behavior or physical compositor compatibility. These
remain separate gates. True versions, optional copied history and restoring old
content as a new version are excluded from this journey.
