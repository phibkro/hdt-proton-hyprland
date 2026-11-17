# Original HDT and native client parity

This reference maps original HDT user journeys to the native Avalonia preview.
It is the authoritative migration-status ledger. The
[application feature map](../../.cursor/skills/verify-hdt-proton/features/application/README.md)
owns user entry points and verification expectations. The
[native verification recipe](../../.cursor/skills/verify-hdt-proton/features/native-avalonia.md)
owns executable checks.

## Baseline and evidence

Original source baseline: `2698623dc0e91491671a614ae176f9db68831aa7`.
Native implementation reviewed: PR38 head `2318d86b67e5b00951360c900f5b60880bfe1563`.
The selected-deck history increment is `cfd096a8443277186d466913fabd4c7c2d39ab57`.
Its product and test files match tested commit `0510615` byte for byte after
rebasing onto the documentation map. The locked build passed with zero warnings
and errors. Its rendered suite passed 280 checks and cleaned up 12 windows,
one actor, and four writers. This remains headless control and real persistence
evidence, not live-game acceptance.
The original checkout contains 226 application XAML files. These include controls
and resources, so that count is not a feature denominator. The installed HDT
1.60.1 binary's correspondence to this source is unverified.

The map covers eight user-goal groups derived from original menus, windows,
settings, and tracking handlers. It does not certify every business rule,
seasonal control, remote flag, plugin-generated command, or account state.
No migration percentage is defined.

Implementation and verification have separate meanings:

| Label | Meaning |
|---|---|
| Subset | The named bounded native journey exists. The listed original behavior remains broader. |
| Partial | Some required behavior exists, but the named journey has important missing steps. |
| Absent | No corresponding native behavior was found in the reviewed source. |
| Rendered | Actual Avalonia controls, dispatcher, pointer events, and Skia rendering with supplied observations. |
| File IO | Actual Rust process and local persistence boundary. Supplied data does not prove game acquisition. |
| Private X11 | Owned fixture windows, real X11 input or pixels, and process cleanup. |
| Scripted watcher | Actual original watcher and adapter with a scripted provider, not game memory or service calls. |
| Unverified live | No native live-game acceptance establishes this journey. |

At this baseline, all native game and service journeys remain unverified live.
Original patched-WPF successes do not change that status. The rendered suite
passed 266 checks in the coordinator's 2026-11-17 rerun of the existing candidate
binaries. It closed 11 windows and completed one actor and four writers. That
result covers the executable subset, not all original journeys listed below.
Private-X11 and scripted-watcher evidence belong to their separately recorded
candidate runs and acceptance recipes.

## Deck management

User journeys: [deck management](../../.cursor/skills/verify-hdt-proton/features/application/decks.md).
Original entry points: [deck menu][menu], [main window][main], and [deck picker][picker].
Native implementation: [library controller](../../avalonia/Hdt.Avalonia/LibraryView.cs),
[F# library views](../../avalonia/Hdt.Views/DeckLibraryView.fs), and
[Rust transactions](../../src/host/client.rs).

| Feature IDs | Native status | Existing evidence and remaining behavior |
|---|---|---|
| `deck-browser` | Subset | Rendered search, class filtering, selection, and selected-deck layout. Original sort, tags, and grouping are incomplete. |
| `deck-editor` | Subset | Rendered and File IO create/edit/save, notes, explicit hero/format, card counts, and sideboards. No legality or collection checks. |
| `deck-metadata` | Partial | Name and notes exist. Tags and linked website operations are absent. |
| `deck-versions` | Absent | No native version history or clone-version journey. |
| `deck-archive` | Partial | Rendered archive and restore preserve identity and notes. Original permanent-delete workflow is absent. |
| `deck-selection` | Partial | Native selection labels future observed history. No original automatic deck selection, game selection, or remaining-deck reconstruction. |
| `deck-missing` | Absent | No collection-backed missing-card calculation. |

## Import and export

User journeys: [import and export](../../.cursor/skills/verify-hdt-proton/features/application/sharing.md).
Original entry points: [menu][menu], [import handlers][imports], and [export handlers][exports].
Native implementation: [library controller](../../avalonia/Hdt.Avalonia/LibraryView.cs)
and [deckstring codec](../../src/host/deckcode.rs).

| Feature IDs | Native status | Existing evidence and remaining behavior |
|---|---|---|
| `share-deckstring` | Subset | Rendered import/export and clipboard readback, Rust known-code and sideboard tests, invalid-input persistence checks. |
| `share-web` | Absent | No URL import, website update, or native network service. |
| `share-game` | Absent | No native constructed, Brawl, Arena, or last-game deck extraction. |
| `share-cardnames` | Absent | No equivalent card-name clipboard export. Local catalog input is a native extension, not an original deck-file import route. |
| `share-destinations` | Partial | Native code copy overlaps original Export > Hearthstone. The original code-copy instructions flyout and screenshot export are not migrated. |

## Matches and history

User journeys: [matches and history](../../.cursor/skills/verify-hdt-proton/features/application/matches.md).
Original entry points: [game event handlers][events] and [statistics controls][stats].
Native implementation: [observation model](../../avalonia/Hdt.Avalonia/NativeObservation.cs),
[library controller](../../avalonia/Hdt.Avalonia/LibraryView.cs), and
[F# game views](../../avalonia/Hdt.Views/NativeGameView.fs).

| Feature IDs | Native status | Existing evidence and remaining behavior |
|---|---|---|
| `match-detection` | Partial | Reads supplied mode, session, and completed observations. No independent native log or memory acquisition. |
| `match-live` | Partial | Rendered known cards, public boards, counts, health, zero-versus-unknown, and mode clearing. Draw/origin, secret, and full turn-state reconstruction are absent. |
| `match-history` | Partial | File IO stores supplied records and deduplicates identities. Rendered selected-deck history filters rows and totals by the displayed deck, while global Stats retains all records. No Windows database import or independent match acquisition. |
| `match-statistics` | Partial | Global record rows and total/win/loss summary. No original date/mode/matchup analysis, charts, or full per-deck analytics. |
| `match-correction` | Absent | No native add/edit/move/discard-result dialogs or replay navigation. |
| `match-arena-run` | Partial | Individual supplied Arena results can persist. No run/reward aggregate workflow. |

## Arena

User journeys: [Arena](../../.cursor/skills/verify-hdt-proton/features/application/arena.md).
Original entry points: [Arena controls][arena], [Arena preferences][arena-options],
and [original watcher][watcher]. Native implementation:
[F# choices](../../avalonia/Hdt.Views/ArenaChoices.fs),
[Arena display model](../../avalonia/Hdt.Avalonia/Arena.cs), and
[optional event bridge](../../src/plugin/NativeArenaEventBridge.cs).

| Feature IDs | Native status | Existing evidence and remaining behavior |
|---|---|---|
| `arena-phases` | Partial | Rendered typed hero/card/replacement/menu and explicit unsupported states. Scripted watcher seam exists. Live hero IDs, dual-class, hero-power, and package acquisition are incomplete. |
| `arena-picks` | Subset | Rendered multiple offer replacements, picked groups, count changes, stale/malformed clearing, and recovery. Independent acquisition is absent. |
| `arena-ratings` | Partial | Supplied base/dynamic scores, unknown values, and generation matching. No native authenticated rating request or entitlement discovery. |
| `arena-relations` | Partial | Supplied relation and synergy counts render. Full related-card visual and tooltip behavior is absent. |
| `arena-redraft` | Absent | Original discard suggestions and package flows are not implemented as native user journeys. |
| `arena-preferences` | Subset | Rendered persistence and score/synergy display combinations. Original account-backed enablement and complete settings semantics remain broader. |

## Battlegrounds

User journeys: [Battlegrounds](../../.cursor/skills/verify-hdt-proton/features/application/battlegrounds.md).
Original entry points: [Battlegrounds controls][bg] and [overlay hierarchy][overlay].
Native implementation: [F# game views](../../avalonia/Hdt.Views/NativeGameView.fs).

| Feature IDs | Native status | Existing evidence and remaining behavior |
|---|---|---|
| `bg-selection` | Absent | No native hero, quest, trinket, or hero-power advice workflow. |
| `bg-minions` | Absent | No native minion-pool filters, pinning, or shop panel. |
| `bg-guides` | Absent | No native guides, compositions, or inspiration menus. |
| `bg-combat` | Partial | Supplied public board and known-hand facts render. No Bob's Buddy, opponent history, or combat calculation. |
| `bg-session` | Absent | No full session, tribe, placement, or mechanic UI. |
| `bg-transitions` | Partial | Rendered mode changes clear obsolete constructed deck rows. Full phase-specific panel lifecycle is absent. |

## Overlay and auxiliary windows

User journeys: [tracking windows](../../.cursor/skills/verify-hdt-proton/features/application/windows.md).
Original entry points: [overlay hierarchy][overlay] and [window definitions][windows].
Native implementation: [raster renderer](../../avalonia/Hdt.Avalonia/OverlayRaster.cs),
[overlay actor](../../avalonia/Hdt.Avalonia/NativeOverlayActor.cs), and
[Rust session owner](../../src/host/native_session.rs).

| Feature IDs | Native status | Existing evidence and remaining behavior |
|---|---|---|
| `window-follow` | Partial | Private X11 geometry, focus, stale-frame hiding, and recovery. Native live compositor, mixed-DPI, and monitor transitions are unverified. |
| `window-input` | Partial | Empty X11 input shape and owned-window selection. No transparent selective-interaction parity. |
| `window-preview` | Absent | Native original-style artwork tooltips and guidance popups are absent. WPF pixel tests are separate. |
| `window-match` | Partial | Bounded supplied deck/board facts render. Full timers, secrets, resources, effects, counters, and board-order UI are absent. |
| `window-guidance` | Absent | No native mulligan, Outfinder, or full related-card guidance. |
| `window-auxiliary` | Absent | No equivalent independent player/opponent deck, timer, or stats windows. |
| `window-mercenaries` | Absent | No native ability-preview or task workflow. |
| `window-errors` | Subset | Optional sibling UI processes, stale-data notices, deadlines, and explicit restart have private fault tests. The original observation producer still depends on WPF. |

## Preferences and lifecycle

User journeys: [preferences and lifecycle](../../.cursor/skills/verify-hdt-proton/features/application/preferences.md).
Original entry points: [settings tree][options], [tray][tray], and [window definitions][windows].
Native implementation: [F# Options](../../avalonia/Hdt.Views/SettingsView.fs),
[preference store](../../avalonia/Hdt.Avalonia/Settings.cs), and
[main window](../../avalonia/Hdt.Avalonia/MainWindow.cs).

| Feature IDs | Native status | Existing evidence and remaining behavior |
|---|---|---|
| `preferences-navigation` | Partial | Three native categories render. Original tree has 25 named leaves, with six Advanced gates. Original search/tray routing is absent. |
| `preferences-persistence` | Subset | Rendered restart, guarded write-failure rollback, recovery, and atomic file storage. Only native preferences are covered. |
| `preferences-customization` | Absent | No full native localization, theme, hotkey, or notification workflow. |
| `lifecycle` | Partial | Native launch/navigation/close and owned-process cleanup. Original tray and start-game actions are absent. |
| `maintenance` | Absent | No original-compatible update, backup/restore, or system-management workflow. |
| `plugins` | Absent | No native equivalent of original plugin discovery and menu integration. |
| `errors` | Partial | Native source/storage failures have explicit status and cleanup. Original crash-report and update-error UI are not migrated. |

## Online services

User journeys: [online services](../../.cursor/skills/verify-hdt-proton/features/application/services.md).
Original entry points: [HSReplay settings][hsreplay-options], [OAuth][oauth], and
[streaming options][streaming]. Native **HSReplay.net** is explicitly disabled in
[MainWindow](../../avalonia/Hdt.Avalonia/MainWindow.cs).

| Feature IDs | Native status | Existing evidence and remaining behavior |
|---|---|---|
| `service-account` | Absent | No native account login, logout, or authenticated session. |
| `service-collection` | Absent | No collection synchronization. |
| `service-replays` | Absent | No native upload, replay library, or player. |
| `service-advice` | Partial | Supplied advice display and expiry exist. Authenticated retrieval and entitlement are absent. |
| `service-streaming` | Absent | No capturable-overlay or Twitch-extension parity. |

[menu]: https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/Windows/MainWindowControls/MainWindowMenuView.xaml
[main]: https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/Windows/MainWindow.xaml
[picker]: https://github.com/HearthSim/Hearthstone-Deck-Tracker/tree/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/Controls/DeckPicker
[imports]: https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/Windows/MainWindow.Import.cs
[exports]: https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/Windows/MainWindow.Export.cs
[events]: https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/GameEventHandler.cs
[stats]: https://github.com/HearthSim/Hearthstone-Deck-Tracker/tree/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/Controls/Stats
[arena]: https://github.com/HearthSim/Hearthstone-Deck-Tracker/tree/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/Controls/Overlay/Arena
[arena-options]: https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/FlyoutControls/Options/Overlay/OverlayArena.xaml
[watcher]: https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/2698623dc0e91491671a614ae176f9db68831aa7/HearthWatcher/ArenaStateWatcher.cs
[bg]: https://github.com/HearthSim/Hearthstone-Deck-Tracker/tree/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/Controls/Overlay/Battlegrounds
[overlay]: https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/Windows/OverlayWindow.xaml
[windows]: https://github.com/HearthSim/Hearthstone-Deck-Tracker/tree/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/Windows
[options]: https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/FlyoutControls/Options/OptionsMain.xaml
[tray]: https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/TrayIcon.cs
[hsreplay-options]: https://github.com/HearthSim/Hearthstone-Deck-Tracker/tree/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/FlyoutControls/Options/HSReplay
[oauth]: https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/HsReplay/HSReplayNetOAuth.cs
[streaming]: https://github.com/HearthSim/Hearthstone-Deck-Tracker/tree/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/FlyoutControls/Options/Streaming
