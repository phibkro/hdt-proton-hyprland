# Android live overlay feature map

The Android preview reads three English Arena offer titles from shared Hearthstone frames and renders them in a system overlay. Its Arena and match demos exercise more presentation states using fixtures. Android match log acquisition is pending. The proposed split is log-first match tracking, screen-first Arena draft presentation, and separate providers for external advice.

## How desktop HDT gets events

| Source | How HDT receives it | What it provides |
|---|---|---|
| Hearthstone logs | `LogFileWatcher` opens the file for shared reading, seeks to the previous offset and buffers incomplete lines. `LogWatcher` checks at a nominal 100 ms interval, sorts the available lines by timestamp and raises `OnNewLines` inside HDT. | `LogWatcherManager` subscribes to that callback and dispatches `Power`, `LoadingScreen`, `Arena` and `Achievements` lines. Power handlers assemble entities, tags and game actions. Loading-screen lines help identify modes, games and reconnects. `API.GameEvents` exposes the resulting HDT events to plugins. |
| HearthMirror | HDT samples game-client state and compares consecutive snapshots. `ArenaWatcher` defaults to a 500 ms delay. `ArenaStateWatcher` defaults to a 15 ms delay. | `Arena.log` signals drafting, redrafting and rewards. The draft watcher gets offer and pick data through `GetArenaDraftChoicesV3()`. State watchers get deeper Arena and match UI state, including choices, hover, tooltips and screen context. |
| Data and advice | The tracker combines the observed state with card definitions, local history and external services. | Card names and metadata, ArenaSmith ratings, mulligan and Battlegrounds guides, and account-dependent features. |

Sources at the desktop revision used by this project are [file tailing](https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/2698623dc0e91491671a614ae176f9db68831aa7/HearthWatcher/LogReader/LogFileWatcher.cs), [batching](https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/2698623dc0e91491671a614ae176f9db68831aa7/HearthWatcher/LogWatcher.cs), [dispatch](https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/LogReader/LogWatcherManager.cs), [watcher wiring](https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/Hearthstone/Watchers.cs), [Arena watcher](https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/2698623dc0e91491671a614ae176f9db68831aa7/HearthWatcher/ArenaWatcher.cs), [Arena state watcher](https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/2698623dc0e91491671a614ae176f9db68831aa7/HearthWatcher/ArenaStateWatcher.cs), and the [overlay inventory](https://github.com/phibkro/hdt-proton-hyprland/blob/docs/hdt-feature-parity/.cursor/skills/verify-hdt-proton/features/application/windows.md).

## Feature map

“Logs” marks a candidate until a recording from current Android Hearthstone confirms the needed events. “Screen” means information visible in the shared Hearthstone window. Each row covers the related desktop overlay behavior, with Android readiness called out independently.

| Overlay capability | Android source and behavior | Android readiness |
|---|---|---|
| Overlay lifecycle, placement and input | Android sharing and overlay permission. Follow the selected app, hide on loss of visibility, rotate and relayout. Use read-only panels for counts and small touch targets for controls or temporary detail views. | Arena panel and visibility lifecycle implemented and fixture-tested. Real game layout, rotation and touch behavior need device verification. |
| Match start, mode, turn, reconnect, result and spectating | Logs for session boundaries, mode, turn changes, results and observed player context. Rebuild from available events on reconnect. | Mode switching and results demoed in-app. Live acquisition pending. |
| Player deck, hand/deck totals, draws and draw odds | Logs plus a selected or imported starting deck. Apply observed draws, generated cards, transformations and shuffles. Derive odds only with enough deck state. | Cards and totals demoed in-app. Live tracking, deck import and odds pending. |
| Ordered deck and auxiliary piles | Logs plus deck metadata for known top/bottom cards, overdraw, sideboards and related-card counts. Maintain unknown values after unobserved changes. | Pending. |
| Opponent revealed cards and hand markers | Logs for public reveals, plays, card counts and observable hand age/origin. Keep unrevealed identities unknown. | Cards and totals demoed in-app. Live tracking and hand markers pending. |
| Secrets, counters, resources and effects | Logs and card rules for public events, inferred secret candidates, active effects, progress, mana, and mode-specific counters. | Pending. |
| Board, attack totals, order and card previews | Public log entities and tags for board and attack state. Catalog for card previews, with tap replacing desktop hover. Match the layout to the Android UI only where needed. | Pending. |
| Turn timer and game notices | Logs for turn boundaries and visible effects. A clock based on those events needs a verified start signal and resync policy. Use tracker state for capture/update/permission notices. | Basic tracker state exists. Match timer and game notices pending. |
| Constructed mulligan and pre-match guides | Logs for starting hand when available, screen for choice visibility if needed, and a separate guide service for advice. | Pending. |
| Discover, choice prompts, shop and contextual tooltips | Probe log choice events and add screen recognition for current visible choices or hover-equivalent states. Log entity creation alone is insufficient evidence that the dialog is on screen. | Pending. |
| Arena offers, hero/power, packages, picks and redrafts | Screen-first recognition for visible choices and confirmed transitions. Probe Arena and Power logs for phase/pick corroboration and run/deck reconstruction. | Three offer titles live with fixture coverage. Draft navigation and selection demoed in-app. Other live phases pending. |
| Arena ratings, synergies, run counts and rewards | Ratings provider, confirmed draft picks and catalog for deck context, observed run outcomes and reward screen or events when available. | Sample ratings and synergies in-app. Live scoring and run/reward data pending. |
| Battlegrounds board, opponents and last seen | Logs for public shop/combat entities, visible opponent board snapshots and placements where logged. Screen for leaderboard selection and other UI-only state. | Basic observations and placements demoed in-app. Live tracking pending. |
| Battlegrounds combat odds | Complete observed pre-combat state plus a simulator and current card rules. Support a missing-state result. | Pending. |
| Battlegrounds hero, hero power, quest, trinket, minion pool and guides | Logs or visible offers to identify options, plus catalog and a separate guide/rating source. Keep inferred minion types provisional. | Pending. |
| Mercenaries abilities and tasks | Establish what the current Android client exposes before choosing logs or screen. Requires mode-specific card and task data. | Pending. |

The desktop [parity ledger](https://github.com/phibkro/hdt-proton-hyprland/blob/docs/hdt-feature-parity/docs/reference/hdt-feature-parity.md) covers the full desktop application. Deck-library editing, account setup and replay uploads are separate app workflows.

## Android acquisition

**Authorized ADB log access.** Modern Android restricts an ordinary app's access to another app's external app-specific storage. [Nomi's Android beta](https://nomi.gg/android/) documents a working design for their tracker using user-authorized Wireless debugging and Hearthstone logging. Start with a workstation using ADB over USB or Wi-Fi to test which files our target client exposes. Evaluate an optional on-device, user-authorized reader once the Pixel probe succeeds. Observed paths, logging configuration and setup behavior may vary by build. See [Android storage](https://developer.android.com/about/versions/11/privacy/storage), [ADB](https://developer.android.com/tools/adb) and [Arcane Tracker's archived implementation](https://github.com/HearthSim/Arcane-Tracker).

**MediaProjection.** Keep the existing Android app-sharing flow. The current `CaptureService` samples about every 750 ms, uses stable title matches and expires stale offers. Extend it around visible transitions where logs lack evidence. Process screen snapshots and ordered log events independently, and gate the overlay on the active Hearthstone session. See [Android app screen sharing](https://developer.android.com/media/grow/media-projection) and [Android overlay touch behavior](https://developer.android.com/about/versions/12/behavior-changes-all#untrusted-touch-events).

**Shared state.** Keep Android readers independent from Avalonia presentation. A log reader should retain the session, file identity, offset and partial line, reset on truncation or rotation, and replay from a known boundary on reconnect. Reduce ordered events before publishing a snapshot through `NativeObservation` or `ArenaObservation`. Carry per-field freshness and uncertainty when combining screen evidence with parsed state. A hidden identity remains unknown even if a local log happens to disclose it. The [Power.log format notes](https://hearthsim.info/docs/gamestate-protocol/) and [python-hslog](https://github.com/HearthSim/python-hslog) provide independent protocol references.

## Verification order

1. On a real device, use the read-only probe below to look for `Power.log`, `LoadingScreen.log` and `Arena.log` under Hearthstone's app-specific external files. Check whether files grow during a game and repeat across a Hearthstone restart to look for rotation. Use the Pixel 8a on GrapheneOS for the first pass.
2. In a Constructed game, check events for mulligan, turn, draw, opponent reveal, disconnect/reconnect and result. In Arena, check hero or card offers and confirmed picks against logs and visible screens. In Battlegrounds, check shop and combat entities, last-seen boards and placement. Retain only the anonymized events needed for reproducible tests.
3. Validate the parser in CI with replay fixtures for partial lines, restarts, duplicate delivery and missing observations. Feed the same snapshots to the Android emulator overlay, covering app switch, orientation, revocation and stale state. Keep a physical test for file accessibility, actual Hearthstone payloads, layout and input.

### Read-only phone probe

From a workstation with Python 3.9+ and `adb`, authorize the phone over USB or wireless ADB. Open Hearthstone and play while the two snapshots are taken. Run from the repository root.

```sh
adb devices -l
python3 scripts/probe-android-hearthstone-logs.py --interval 30
```

Pass `--serial` if more than one device is attached or `--adb` if the executable is outside `PATH`. The probe reports relative paths, whether ADB can open each file for reading, and file size changes. It reads no log contents and leaves game settings untouched. A successful scan with no matches means the expected files were absent from that directory at the time of the scan. A failed scan reports the device, package or filesystem error so it can be diagnosed separately. File growth confirms that the client is writing the file. Checking event coverage requires a second pass.
