# Native Avalonia migration — first executable slice

An independently authored .NET/Avalonia application. This is a migration preview,
not a replacement for HDT. It opens a native window with a working Arena replay
view and local Arena preferences. It never reads game memory or sends game input.

```
Arena observation replay ──validated v1 contract──▶ native draft view
MIT Proton plugin export ──read-only status──────▶ connection/status text
                                                    (no cards in export)
```

## Run

From the repository root:

```sh
nix-shell avalonia/shell.nix
cd avalonia
dotnet restore Hdt.Checks/Hdt.Checks.csproj --locked-mode
dotnet build Hdt.Checks/Hdt.Checks.csproj --no-restore -m:2 -p:UseSharedCompilation=false
dotnet run --no-build --project Hdt.Avalonia -- --replay fixtures/draft.json
```

Use **Open replay**, **Next observation**, **Rewind**, and **Options**. The fixture
is clearly illustrative; its card names and scores are invented test data, not
current Arena advice. Actual captured observations can use the same JSON contract.
There is no account login and no network request in the application.

Optional arguments:

```sh
dotnet run --no-build --project Hdt.Avalonia -- \
  --replay /absolute/path/observations.json \
  --settings /absolute/path/native-settings.json \
  --proton-state /absolute/path/input-regions.json
```

`--proton-state` reads the existing `HDT_INPUT_REGION_FILE` export once per second.
It accepts only the timestamp and `state` projection defined in
[the MIT plugin](../src/plugin/ProtonCompatibility.cs). Missing, invalid, future
(>5 seconds) and stale (>5 seconds) status is visibly unavailable. It never
converts visibility flags into offers, picks or ratings. The reader owns no writer
and does not modify the file, Windows configuration or installed plugin.

Settings default to the platform's LocalApplicationData `hdt-avalonia/settings.json`.
They are separate from Windows HDT settings and use an atomic same-directory
replacement. Pass `--settings` to isolate each session.

Toolchain: .NET SDK **10.0.401** (roll-forward disabled), Avalonia **12.1.6**,
NuGet transitive versions and content hashes in each `packages.lock.json`.
`shell.nix` reuses the repository's `devenv.lock` as the one nixpkgs pin, including
native graphics/font/X11 libraries. No extra nixpkgs pin or downloaded SDK script.
C# is used because this is a WPF-to-Avalonia migration; the repository's preferred
TypeScript stack cannot supply native Avalonia controls.

## Acceptance

After the build:

```sh
dotnet run --no-build --project Hdt.Checks -- fixtures/draft.json ../local/avalonia-evidence
```

The first assertions exercise JSON loading, unknown/base/dynamic scores,
formatting, and synergy multiplicity. They are model tests, not game validation.

The rendered acceptance journey creates the real `MainWindow` with Avalonia's
headless Skia backend, injects pointer events into **Next observation**, tab
headers, checkboxes and **Rewind**, checks the resulting control tree, saves a
nondefault preference, reconstructs another window from disk, and writes
`arena.png` / `options.png`. It verifies replacement of three offers by two,
picked-deck changes, removal of scores, and removal of all offers when leaving
the draft. This verifies Avalonia rendering and application input handling; it
does not prove a display-server, Proton or Hearthstone integration journey.

A separate `Avalonia migration` CI job restores with locked mode, builds this
slice and runs the same rendered journey. CI is defined here; no remote CI run
is claimed by a local acceptance result. A private Xvfb smoke run also opened the
ordinary X11 window at 1120×760 and saved `native-x11.png`; both owned processes
were then stopped. That smoke proves a native display-server launch, not game
integration.

## Parity inventory

| Capability | This slice | Evidence / remaining work |
|---|---|---|
| Native Linux window and navigation | Native | Real Avalonia menu/tabs; Options, Stats and Help navigate |
| New/import/export/account menus | Absent, disabled | No false successful commands; deck manager not migrated |
| Arena scores and deck-synergy toggles | Native, persisted | Pointer journey and nondefault window reconstruction |
| Offered cards and picked deck | Native rendering, replay acquisition | Validated snapshot replacement, duplicate-pick grouping |
| Dynamic score preference / base fallback | Native | Derived from observed upstream behavior; nullable ratings stay unknown |
| Synergy calculation | Native | Enabled relations plus picked enhancer multiplicity, excluding overlap |
| Underground color / inactive transition | Native rendering, replay acquisition | Observed mode drives border; inactive has no offers |
| Existing Proton acquisition state | Read-only bridge, status only | Existing MIT export fields; no live card/pick/score export exists |
| Live card offers / selections / scores | Absent | Requires a separately authorized producer of the v1 observation contract |
| HSReplay login, entitlement and rating requests | Absent | No claim that upstream acquisition is a public/free API |
| HearthMirror / game memory / log watcher | Absent | Separate future acquisition adapter, no native port yet |
| Hero/hero-power/package selection, redraft, related tooltips | Absent | Separate observation variants and views required |
| In-game overlay placement/click-through | Absent | Separate compositor/window integration; ordinary window only |
| Deck editing/import/history/statistics | Absent | Data adapters and workflows not implemented |
| Upstream assets, localization, exact styling | Absent | No licensed artwork or fonts copied; native Fluent controls |

## Observation boundary

`ArenaObservation` in `Hdt.Avalonia/Arena.cs` is the authoritative v1 schema;
`fixtures/draft.json` is a runnable complete example. A replay is an ordered array
of complete observations, not a sequence of UI commands. Each observation declares
schema version, source session, observed UTC time, increasing sequence, mode,
pick index, most recently selected card ID (nullable), hero, Underground flag,
picked deck, offered cards, ratings and related-card ID sets. `PickIndex` must
match the complete picked-deck length; a selected card must exist in that deck;
`Inactive` must contain no offers. Unknown ratings are null, never fabricated zero.
The snapshot is fully validated before the previous view is replaced. Replay time
is historical and intentionally does not use the live status freshness gate.

`IArenaObservationSource` is the acquisition seam. Only `ReplaySource` currently
implements card acquisition. `ProtonStateReader` is a separate status contract,
because the existing producer has different semantics and does not have the
fields needed to implement `IArenaObservationSource`.

The next slice is a versioned, account-free observation exporter owned by our MIT
plugin, with upstream type access independently checked against the installed
assembly. It must carry session/time/mode, offer IDs, picked IDs and nullable score
availability; atomic snapshot publication and stale/session-change handling need
real Proton-to-native acceptance. No guessed reflection bridge is included here.

## Provenance and licensing

Inspected upstream: [HearthSim/Hearthstone-Deck-Tracker](https://github.com/HearthSim/Hearthstone-Deck-Tracker/tree/2698623dc0e91491671a614ae176f9db68831aa7),
commit **2698623dc0e91491671a614ae176f9db68831aa7**. Its README lines 59–61 state
`Copyright © HearthSim. All Rights Reserved.` There is no root license granting
redistribution in that checkout. A bounded check of tag v1.30.0 found the same
notice. Therefore no upstream C#, XAML, assets, binaries, fonts or license text
was copied. This original implementation falls under the parent MIT license;
that does not grant rights to HDT or imply endorsement by HearthSim.

Behavioral reference inventory at that exact revision:

- `Windows/MainWindowControls/MainWindowMenuView.xaml`: menu destinations.
- `FlyoutControls/Options/Overlay/OverlayArena.xaml` and `Config.cs`: Arena setting names/defaults.
- `Controls/Overlay/Arena/ArenaPickSingleCardOptionViewModel.cs`: score display preference, border colors, relation/deck-count semantics.
- `Controls/Overlay/Arena/ArenaPickHelperViewModel.cs`: offered/picked separation and dynamic/base score preference.
- `Controls/Overlay/Arena/ArenaPickApi.cs`: observed upstream response field meanings; no acquisition rights inferred.

Evaluated the official [Avalonia template workflow](https://docs.avaloniaui.net/docs/get-started/install-avalonia)
and [Microsoft SDK pinning](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json).
Used Avalonia's supported Desktop, Fluent, Skia and Headless packages directly.
A separate template engine install and MVVM framework were unnecessary for this
bounded control/view slice. Existing repository Nix dependency pin and MIT plugin
status format were reused. Proprietary current upstream source was rejected as a
copying source because its notice supplies no redistribution license.
