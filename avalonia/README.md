# Native Avalonia migration — executable client preview

An independently authored .NET/Avalonia application with a Linux-first Rust IO boundary. This is a migration preview,
not a replacement for HDT. It opens a native window with deck management, deckstring import/export, a persisted
match-history view, an Arena replay view and local Arena preferences. It never reads game memory or sends game input.

```
Arena observation replay ──validated v1 contract──▶ native draft view
Native deck editor ────────Rust transactions─────▶ private native library
MIT Proton status export ──read-only status──────▶ connection/status text
Optional observation export ──validated v1───────▶ native game / Arena / history
                       (source-only; live installation not changed)
Native rendered pixels ──bounded stream─────────▶ Rust-owned input-empty X11 panel
```

## Run

From the repository root:

```sh
nix-shell avalonia/shell.nix
cargo build --locked -j2
cargo test --locked -j2 --test rust-snapshot --test rust-client
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
  --proton-state /absolute/path/input-regions.json \
  --client-data /absolute/path/native-library.json
```

`--proton-state` reads the existing `HDT_INPUT_REGION_FILE` export once per second.
It accepts only the timestamp and `state` projection defined in
[the MIT plugin](../src/plugin/ProtonCompatibility.cs). Missing, invalid, future
(>5 seconds) and stale (>5 seconds) status is visibly unavailable. It never
converts visibility flags into offers, picks or ratings. The reader owns no writer
and does not modify the file, Windows configuration or installed plugin.

**My Decks** supports new decks, edits and notes, commented deckstring import,
export/copy, native tracking selection, archiving and explicit restore.
The editor supports numeric DBF IDs and optional card-name search from a local
catalog you supply; no catalog or artwork is bundled. A decoded
code is structurally checked, not certified legal for a Hearthstone format.
Sideboards are preserved. Archiving retains the deck and notes in the library;
**Show archived decks** opens preserved entries; **Restore** retains their stable
identity and historical associations without silently changing tracking selection.
Archived entries cannot be saved or selected for tracking. Tracking selection affects
only this native client; it does not select a deck inside the game or Windows HDT.
**Use for game history** labels new games observed after that native selection.
The live remaining-deck list comes from the observation source's tracker;
native selection does not recalculate its counts. Independent native draw/origin
tracking is not implemented, and no subtraction is guessed from a hand snapshot.

`--client-data` selects a separate native JSON library; by default it is
`library.json` beside native settings. The existing Rust host owns versioned,
revision-checked transactions under a regular-file lock, atomic same-directory
replacement, file/directory synchronization and private newly created files.
Requests travel through bounded stdin rather than process arguments. A conflict
or uncertain failure disables mutations until **Reload library** verifies disk.
**Stats** displays persisted supplied match records. The optional observation
source can ingest completed games observed in its current session; Windows HDT
database import is absent. It does not invent game history.

Settings default to the platform's LocalApplicationData `hdt-avalonia/settings.json`.
They are separate from Windows HDT settings and use an atomic same-directory
replacement. Settings initialization, replay reads, status reads, parsing and
settings writes run off the UI dispatcher. Preference controls have one pending
write owner, and guarded rollback never triggers another save. Pass `--settings` to isolate each session.

Toolchain: .NET SDK **10.0.401** (roll-forward disabled), Avalonia **12.1.6**,
NuGet transitive versions and content hashes in each `packages.lock.json`.
`shell.nix` reuses the repository's `devenv.lock` as the one nixpkgs pin, including
native graphics/font/X11 libraries. No extra nixpkgs pin or downloaded SDK script. The shell selects the built
Rust host through explicit `HDT_HOST_BINARY`; an older/missing host is a visible
input failure. This slice currently requires Linux for that boundary.
C# is used because this is a WPF-to-Avalonia migration; the repository's preferred
TypeScript stack cannot supply native Avalonia controls.

## Acceptance

After the build:

```sh
dotnet run --no-build --project Hdt.Checks -- fixtures/draft.json ../local/avalonia-evidence
```

The native-library rendered journey additionally uses pointer and keyboard input
to create a deck, export a known deckstring, import it as another deck, select it,
edit notes, archive and restore it. It also searches an owned catalog by card
name/class, adds/removes exact copies, explicitly selects a hero, saves that deck,
and proves malformed-source clearing/recovery without saved-library mutation.
It verifies the Rust-backed persisted history view,
invalid-import failure and reload recovery. `native-card-catalog.png`, `native-library.png` and
`native-history.png` retain rendered evidence. Rust CLI tests separately assert
revision conflict leaves exact stored bytes unchanged, private file permissions,
history identity deduplication and sideboard codec roundtrip. A supplied anonymous
match proves the storage/view seam only, not automatic game acquisition.

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

A separate `Avalonia migration` CI job uses the existing complete-change scope
classifier, skips builds for documentation-only changes, restores with locked
mode, builds the Rust host and native UI, and runs the same rendered journey. CI is defined here; no remote CI run
is claimed by a local acceptance result. A private Xvfb smoke run also opened the
ordinary X11 window at 1120×760 and saved `native-x11.png`; both owned processes
were then stopped. That smoke proves a native display-server launch, not game
integration.

The fault journey additionally invokes the real Rust process on oversized,
malformed, directory, symlink, character-device and FIFO inputs. Real dispatcher
polling proves stale → fresh recovery and mode changes. A slow owned helper
proves the watchdog while pointer navigation stays responsive, then proves child
reaping and recovery. `status-stale.png`, `status-recovered.png` and
`settings-failure.png` retain rendered evidence. The slow helper exercises process
supervision; it supplies no pretend acquisition facts. Unavailable preferences
storage must visibly fail, roll controls back without reentrant writes, and save
successfully after storage recovers.

## Bounded external-file contract

All JSON input uses `SnapshotFiles` and the existing Rust host's explicit
`read-snapshot MAX_BYTES PATH` command. That command opens read-only with
`O_NONBLOCK | O_NOFOLLOW`, checks the same opened handle's `is_file()` and size,
then reads at most the caller's bound plus one byte before emitting any stdout.
It rejects final symlinks, FIFO/socket/device/directory inputs and files that grow
past the bound. Missing files have a distinct exit code; only missing local
settings select defaults. Other settings read failures remain visible.

The managed process actor owns at most one child, bounds stdout/stderr, and
applies a two-second deadline. It kills only that child on cancellation/timeout;
the slot remains owned until it is reaped, so a kernel-stalled reader cannot
spawn an unbounded replacement chain. No external file open, process start or
JSON parsing happens on the dispatcher. Replay input is limited to 8 MiB, status
to 64 KiB and settings to 16 KiB. Validated text/collection limits also bound the
work needed to render one observation. These are explicit preview limits;
inputs outside them fail rather than truncate or masquerade as complete data.

The Linux metadata and descriptor handling reuse the repository's existing Rust
`nix` dependency and standard-library file APIs; no platform struct layouts or
new native C shim were introduced. The IO watchdog fake is separate from real
regular-file/fault acceptance.

## Parity inventory

| Capability | This slice | Evidence / remaining work |
|---|---|---|
| Native Linux window and navigation | Native | Real Avalonia menu/tabs; Options, Stats and Help navigate |
| New/import/export menus | Native | Pointer/keyboard journey through real Rust transactions; account menu remains unavailable |
| Arena scores and deck-synergy toggles | Native, persisted | Pointer journey and nondefault window reconstruction |
| Offered cards and picked deck | Native rendering, replay or optional observation source | Validated snapshot replacement, duplicate-pick grouping; live producer acceptance pending |
| Dynamic score preference / base fallback | Native | Derived from observed upstream behavior; nullable ratings stay unknown |
| Synergy calculation | Native | Enabled relations plus picked enhancer multiplicity, excluding overlap |
| Underground color / inactive transition | Native rendering, replay or optional observation source | Observed mode drives border; inactive has no offers |
| Existing Proton acquisition state | Read-only bridge, status only | Existing MIT status export fields contain no card/pick/score observations |
| Live card offers / picked deck / scores | Native consumer + optional source-only Proton producer | Real public-model reference compile and owned publication/polling journey; actual game acquisition remains unverified |
| HSReplay login, entitlement and rating requests | Absent | No claim that upstream acquisition is a public/free API |
| HearthMirror / game memory / log watcher | Retained Proton acquisition when optional producer is used | No native memory/log acquisition port; bridge reads public observed models only |
| Hero/hero-power/package selection, redraft, related tooltips | Absent | Separate observation variants and views required |
| Native overlay placement/input pass-through | Native X11 preview | Rust-owned opaque panel; private-display pixels/shape/focus/stale checks pass; real-game/compositor acceptance pending |
| Deck editing / notes / archive / restore / selection | Native | Real private library, revision conflicts, stable IDs, atomic persistence; no game write |
| Card-name search / deck building | Native, optional local catalog | Read-only localized HearthstoneJSON projection, exact DBF identities, add/remove copies and explicit hero choice; no ownership/legality inference |
| Hearthstone deckstring import / export | Native, structural validation | Golden version-1 bytes, comments, sideboards; no current catalog / legality check |
| Match history / basic win-loss counts | Native storage/view + optional completed-observation ingestion | Stable source IDs deduplicate; native deck association only after observing match start; Windows database import absent |
| Menu / constructed / Battlegrounds state and known cards | Native consumer + optional source-only Proton producer | Owned worker → Rust file reader → rendered timer journey; no combat odds or recommendations |
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

## Optional native observation bridge

The original [NativeClientExport](../src/plugin/NativeClientExport.cs) is an
opt-in source-only extension to our MIT plugin. `HDT_NATIVE_CLIENT_FILE` enables
it; absence preserves the existing plugin behavior. It reads public cached HDT
models on their UI owner, copies only bounded primitive/string/card observations,
and sends the immutable projection to one latest-only worker. Serialization,
file creation, flush and atomic replacement run on that worker. It makes no
HSReplay request, acquires no game memory, reads no account identifiers and sends
no game input. Unknown base/dynamic ratings stay null. The producer's last-selected
card ID is explicitly unavailable; picked tiles and counts are observed facts.

The Rust launcher's optional `HDT_NATIVE_CLIENT_FILE` setting requires an absolute
host file in an existing directory owned by the current user with no group/other
access. It rejects a final symlink/nonregular target and converts the path for
Wine. This only configures a future opted-in launch; no installation/restart or
live-prefix change was performed for this preview. Native launch uses the same
host path, explicitly selecting acquisition instead of replay:

```sh
dotnet run --no-build --project Hdt.Avalonia -- \
  --native-state /absolute/private/directory/native-observation.json \
  --settings /absolute/private/directory/native-settings.json \
  --client-data /absolute/private/directory/native-library.json
```

`--replay` and `--native-state` are mutually exclusive. The older `--proton-state`
visibility/status projection remains separate and cannot supply card data.
`NativeObservation` and its validator in
[NativeObservation.cs](Hdt.Avalonia/NativeObservation.cs) define the native v1
consumer boundary: own session/sequence/time, explicit availability and mode,
known cards/counts/health, an identity-matched Arena observation and anonymous
completed matches. The source never exports opponent hand or secret card IDs.
Offline/menu snapshots clear previous game facts. Missing, invalid, stale,
future, backwards and explicitly unavailable inputs clear all native cards,
picks and offers; fresh complete sessions recover. A dispatcher freshness guard
also expires already rendered facts while another asynchronous operation waits.

Completed games use new random IDs owned by this source, never upstream game,
account or player IDs. The producer retains the latest **256** completed records
in memory for its current process session. Polling can miss games that finish
between samples or before the source starts; this is not a historical database
import or an exactly-once durable game event journal. Native ingestion writes at
most one new/changed history record per poll and deduplicates stable identities.
A native deck association is made only if the client observed that match in
progress with a native selected deck. History arriving after startup otherwise
has no inferred deck association. Automatic history transactions preserve unsaved
deck edits.

Verification is deliberately split. The producer compiles as C# 5/net472 against
real HDT reference binaries (HDT exe SHA256
`65d66314dddd8a36588a83b5d60deebea8ee2e758c6b91751838d663d0ae7586`), with no
stubs and no copied binaries. The native acceptance driver links the **same**
`NativeSnapshotWriter`, publishes anonymous illustrative observations through its
actual worker/atomic file boundary, reads through the Rust child, and drives
rendered constructed/Arena/Battlegrounds/menu/stale/recovery/history views. It
also checks publisher failure/recovery, byte bounds and latest-only coalescing.
Screenshots are `native-constructed.png`, `native-observed-arena.png`,
`native-battlegrounds.png` and `native-observation-stale.png`. These checks do
**not** execute cached HDT model capture inside a real game. Live Proton-to-native
acquisition acceptance remains outstanding; no installed plugin was modified.

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


Deckstring prior art: [HearthSim/HearthDb DeckSerializer.cs](https://github.com/HearthSim/HearthDb/blob/4f91601ac3a47fa06eede3eac5c4cf84db52545e/HearthDb/Deckstrings/DeckSerializer.cs),
commit **4f91601ac3a47fa06eede3eac5c4cf84db52545e**, MIT copyright 2016 HearthSim.
The independently authored Rust codec follows the version-1 serializer's wire
layout, including sideboard owner/count order. The reference's multi-copy
sideboard deserializer uses a different order; this codec follows the serializer
and rejects trailing or unsupported data. The original copyright/permission
notice is retained in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
The existing `base64`, `serde`, `uuid`, `chrono`, `nix` and already-locked
`tempfile` dependencies are reused. A stale HearthDb NuGet package was evaluated
and rejected as a current card catalog; no card definitions or copyrighted
artwork are bundled.

## Optional native X11 overlay

With `--native-state FILE`, open **Options**, **Find Hearthstone windows**, choose
an explicit window and enable **Show native tracking overlay**. The opt-in is
session-only. A separate Rust child creates its own opaque 320×640 window with
an empty input shape **before mapping**. It reads the selected game's window
identity, position, size and active-window state; it never changes the game's
window, input shape or focus. The panel follows its right edge and hides when
the game is inactive, too small, not mapped, observations are unavailable, or
its last frame is older than five seconds. Closing the native client kills and
reaps only its owned child. The Rust child also exits on parent death or stdin
closure. A failed owner must be reaped before another can start.

Avalonia supplies the actual card/score/synergy rendering through a bounded,
latest-only raster channel. The Rust boundary validates explicit version,
dimensions, opaque BGRA pixels, session, monotonic sequence and observation time.
No game discovery or external process IO runs on the UI dispatcher. The child
has bounded status/diagnostic pipes and preparation/write deadlines.

This first panel is opaque, X11/XWayland-only, fixed scale, and shows up to three
Arena offers or 22 tracked deck/board entries. It has no artwork, tooltips,
combat odds, full Battlegrounds advice, native Wayland layer integration or
transparent compositor style. Window discovery requires the expected public
Hearthstone title/class/PID and same-user process. Other window naming and
compositor behavior require separate validation. Real-game overlay acceptance
has **not** been run; no installed plugin or game was changed.

After the normal rendered acceptance, run from the repository root:

```sh
nix-shell avalonia/shell.nix --run 'bash scripts/check-rust-x11.sh --native-frame local/avalonia-evidence/native-overlay-frame.json'
```

This uses the actual Avalonia-produced raster on a private Bubblewrap/Xvfb
fake-game window. It compares every displayed RGB pixel, verifies the empty
input shape before mapping and after moves, checks focus/stale/backward/hide
transitions and recovery, and observes exact-child exit/window destruction.
Evidence includes `native-overlay.ppm`. Without `--native-frame`, the same Rust
route explicitly reports protocol-only solid pixels; that is not managed-renderer
acceptance. The managed driver separately proves changing rendered offers and a
stalled helper's deadline/reaping while pointer navigation remains responsive.
The complete Options-enable → streaming-child flow is not yet an automated
rendered end-to-end route.

## Optional local card catalog

In **My Decks**, expand **Find cards by name**, choose a local JSON file (or enter
its path), type a name/class/type/card ID/DBF ID and press **Search local catalog**.
Select a result and add or remove copies in the unsaved editor; **Save deck**
persists the result. **Include noncollectible cards** exposes additional
entries, and **Use selected hero** explicitly sets that hero's DBF ID and
class. No default hero mapping is guessed. Full and collectible-only localized
[HearthstoneJSON arrays](https://hearthstonejson.com/docs/cards.html) are supported;
hero choices depend on the supplied catalog's entries. Multi-class metadata is
preserved and searched alongside a card's primary class. The
catalog path is session-only; an older imported deck still opens without one.
No catalog metadata is written into the source file or copied into this repository.

The Rust reader owns regular-file/no-final-symlink checks, validation and search.
Its shared read primitive has a 32 MiB hard ceiling to accommodate full catalogs;
existing replay/observation/settings callers retain their smaller explicit limits.
Catalogs require at most 50,000 unique positive DBF/card identities with bounded
plain metadata. Type, class and cost can be absent and remain visibly unknown.
Search requests are bounded to 4 KiB and a 256-byte query, and return at most 100
results plus the total match count. Missing/malformed/oversized catalogs clear
previous results and require a new search after repair. The UI owns no download,
account call, collection-ownership claim, format legality claim or licensed art.

This is independently authored format consumption. No HearthstoneJSON client
source was copied. Developer acceptance additionally reads current public full
and collectible JSON files in ignored private evidence; no data file is bundled.
The rendered automated journey enters the file path and drives the remaining
controls. The native operating-system file chooser itself has not been automated.

The format provider explicitly identifies its JSON content as Blizzard
copyright with all rights reserved; its website's CC0 notice does not license
that data. This client reads supplied runtime files and distributes none of
those files, card text or artwork. See the provider's
[copyright notice](https://hearthstonejson.com/docs/cards.html).
