# Native HDT layout pass

Status: implementation. Owner: Avalonia migration worktree. 2026-11-16.

## Goal and user journey

Keep Avalonia and make the native main window recognizably follow HDT's
information hierarchy. Open My Decks: browse/search decks at left, inspect supplied
My Games in the middle, selected deck/cards/notes at right. New/Edit/Import opens
a secondary editor. Options uses a narrow category sidebar and section headers.
Arena and live observations retain real source semantics and clear unknowns.

## Constraints and provenance

Reference only: upstream HDT 2698623dc0e91491671a614ae176f9db68831aa7,
`Windows/MainWindow.xaml` deck picker/MyGames/right deck-list/curve hierarchy;
`FlyoutControls/Options/OptionsMain.xaml` sidebar categories and content. Upstream
README59–61 reserves all rights. Independently author all code/styles/text; copy
no XAML/assets/artwork. Retain existing Avalonia12.1.6/Rust boundaries. No installed
runtime, operator display, game, account or network integration changes.

## Acceptance and falsifiers

- Default library overview does not expose the raw numeric editor. Actual deck
  CRUD, import/export, archive/restore, history and settings journeys still pass.
- Deck search filters real persisted entries; selected facts derive from the
  selected entry, supplied history derives from stored matches. Never invent
  winrates, global statistics or mana costs absent from the model.
- Settings categories expose working controls only. Actual pointer navigation
  enables/disables the existing Rust overlay in the isolated desktop journey.
- At1120x760 and 800x600, library/browser/detail panels and navigation remain
  visible and do not overlap. Long names and paths wrap/trim within their panel.
  Render screenshots for library, settings and Arena at both sizes.
- Existing managed checks, private X11 Options lifecycle, source/format/build
  gates pass on a clean committed head. Root owns publication and independent
  preview acceptance. No claim of pixel-identical WPF or complete migration.

## Revisions

- 2026-11-16: initial bounded layout contract; parent requests recognizable
  three-column hierarchy and representative-size geometry assertions.

- 2026-11-16: managed assertion failures were escaping the test entrypoint and
  triggering SIGABRT/core notifications. Add an explicit full-diagnostic/nonzero
  runner boundary, await owned cleanup, and prove intentional failure exits1
  without a new core before resuming acceptance. Do not suppress native crashes.
