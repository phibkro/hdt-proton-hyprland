# HDT view trees and reference captures

Use a view tree within each window. Shared tracking state supplies the windows;
individual controls do not form a second application-wide message graph.

This is a reference inventory, not a claim of complete migration or capture.
The source reference is HDT commit
[`2698623`](https://github.com/HearthSim/Hearthstone-Deck-Tracker/tree/2698623dc0e91491671a614ae176f9db68831aa7).
It contains 226 XAML files under the application directory: these include shared
controls and resources, not 226 separate screens. Exact correspondence with the
installed 1.60.1 binary remains unverified. Runtime capture enumerates that
binary's actual settings tree instead of assuming the source list matches.

## Main window

```text
Main window
├── Navigation: New / Deck / Import / Export / Stats / HSReplay
├── Deck overview
│   ├── Deck browser: search, filters, selection, context menu
│   ├── My Games / global deck statistics
│   └── Selected deck: cards, sideboards, mana curve
├── Flyouts
│   ├── Deck editor / history
│   ├── Statistics
│   └── Settings
│       ├── HSReplay: account, collection, replays
│       ├── Overlay: general, mulligan, Outfinder, counters, related cards,
│       │            deck windows, player, opponent, Battlegrounds, Arena,
│       │            Mercenaries
│       ├── Tracker: main window, stats, importing, notifications, plugins,
│       │            hotkeys, backups, appearance, system
│       └── Streaming: capturable overlay, Twitch extension
└── Modal dialogs
    ├── New deck type / deck selection / import choices
    ├── Notes / move game / discard game / Arena reward
    └── Language / crash reporting
```

The containment reference is `Windows/MainWindow.xaml`; settings categories and
selection handlers are in `FlyoutControls/Options/OptionsMain.xaml` and its
code-behind. Dialog definitions are under `Windows/`. A dialog's existence in
source does not establish that its command is available in every game/account
state. Shared controls and MahApps dialogs may live within the main window's
visual tree rather than own operating-system windows.

## Overlay and auxiliary windows

```text
Overlay window
├── Menu views: configured static panels and notices
├── Constructed / Arena match: deck, hand counts, counters, timers
├── Arena draft: offers, ratings, picked deck, relations / suggestions
└── Battlegrounds: top bar, minions / guides, combat-specific views

Auxiliary windows
├── Player / opponent deck windows
├── Timer / statistics
├── Capturable overlay
└── Battlegrounds session / mechanic windows
```

`Windows/OverlayWindow.xaml` owns the overlay's control hierarchy. The separate
`feat/avalonia-ui` preview branch maintains its capability inventory in
`avalonia/README.md`; that implementation is not part of this main-based
reference branch. Source existence, model-fixture rendering and live game
observation are separate evidence.

| Interaction | State owner and effect |
|---|---|
| Choose a saved deck | Main-window selection; source HDT may use it for tracking. Native preview selection currently labels history only. |
| Open Settings | Main-window flyout; selecting a category replaces its content. |
| Change an overlay preference | Shared configuration changes; relevant overlay controls rerender. |
| Game mode changes | Acquisition updates tracker state; overlay selects a mode-specific subtree. |
| Arena pick | Acquisition supplies a new offer/deck snapshot; old choices and ratings must be replaced. |
| Open a guide or tooltip | Local transient UI; closing or changing mode must clear that subtree. |
| Source becomes unavailable | Native client clears expired facts; absence is not a zero rating or empty valid draft. |

## Process boundary target

```text
Independent supervisor
├── Tracker / shared-state publisher
├── Main UI process       ← bounded state snapshots / commands
└── Overlay UI process    ← bounded state snapshots / commands
```

Each UI owns a view tree and receives shared state independently. A frozen main
UI must not be the relay for overlay data. The publisher owns authoritative
tracking state and persisted preferences; views own temporary selection,
scroll position and popups. Commands need explicit outcomes; snapshot identity,
sequence and freshness prevent old advice from remaining current after failure.
The supervisor owns each process lifecycle, so one UI's exit need not terminate
the other. A responsive overlay can clear stale advice and display a small
unavailable/reconnecting status independently of its main-window sibling.

**This split is not implemented yet.** Original HDT hosts its WPF windows in one
application. The current Avalonia preview generates overlay rasters in the main
process and owns a Rust display child whose lifetime is tied to that parent.
That child separates display operations, not the UI failure domain. Independent
overlay rendering, supervision, command routing and recovery require separate
implementation and fault-injection acceptance.

UI isolation also differs from acquisition isolation. The optional native-client
bridge currently captures cached HDT models on HDT's UI dispatcher before its
separate writer publishes them. A frozen original HDT dispatcher can therefore
stop fresh observations even if both native UI processes remain healthy.
Independent acquisition is a separate migration step; the immediate UI contract
is responsiveness, stale-data clearing and an honest source status.

## Capture coverage

Run [the reference capture route](../guides/ui-replay.md#capture-original-views)
against the real HDT assemblies in the private replay environment. It records:

- Every leaf in the runtime settings tree, with advanced pages revealed
  temporarily and overlapping scroll tiles for long content.
- The main window, New deck-type dialog, and existing menu/Arena/Battlegrounds
  fixture views.
- The actual page type, selection, rendered dimensions, scroll position, input
  scope and source identity alongside each image.

These are original WPF controls with private synthetic game models and no
account session. They do not establish live authentication, collection,
Premium eligibility, real combat, error dialogs or every popup variant. Those
remain uncaptured until their real prerequisites or bounded fixtures are
available. Screenshots stay in ignored private evidence; no upstream artwork,
XAML or binaries are added to the repository.
