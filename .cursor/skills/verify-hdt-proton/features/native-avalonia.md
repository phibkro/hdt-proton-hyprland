# Native Avalonia migration preview

Feature IDs: `native-window`, `native-draft-replay`, `native-arena-phases`, `native-arena-event-bridge`, `native-preferences`,
`bounded-snapshot-io`, `proton-status-bridge`, `native-deck-library`,
`native-deckstring`, `native-history`, `native-observation-bridge`, `native-x11-overlay`. These are preview routes; passing
them establishes no live game offer/rating/overlay parity.

## Preconditions and doctor

Check current machine headroom before each build or rendered journey. Use this
checkout's pinned shell, isolated target, explicit host identity and a new private
evidence directory. Linux is currently required for native snapshot IO.

```sh
nix-shell avalonia/shell.nix
cargo build --locked -j2
cargo test --locked -j2 --test rust-snapshot --test rust-client
cd avalonia
dotnet restore Hdt.Checks/Hdt.Checks.csproj --locked-mode
dotnet build Hdt.Checks/Hdt.Checks.csproj --no-restore -m:2 -p:UseSharedCompilation=false
```

Before driving, record clean/dirty source, `git rev-parse HEAD`,
`sha256sum "$HDT_HOST_BINARY"`, and `"$HDT_HOST_BINARY" --help`. The help must
include `read-snapshot` and `client`; a host built from an older revision is not ready.
Retain the .NET SDK version and lockfiles alongside the source identity.

## Rendered native journey and faults

```sh
dotnet run --no-build --project Hdt.Checks -- fixtures/draft.json "$HDT_VERIFY_ROOT/native-ui"
```

Require zero exit and all checks passing. Inspect `arena.png`, `options.png`,
`status-stale.png`, `status-recovered.png`, `settings-failure.png`,
`native-library.png` and `native-history.png`. The driver
uses actual headless Skia controls and pointer input. It loads a real replay
file, steps observed offers/picks, navigates Options, persists a nondefault
preference, reconstructs a window, removes scores, leaves draft and rewinds.

The same driver creates a private illustrative schema 2 phase replay and uses actual
pointer controls through hero → card draft → replacement → unsupported → menu at
1120×760 and 800×600. Inspect `arena-hero-*`, `arena-cards-*`, `arena-menu-*` and
`native-observed-heroes.png`. Hero controls must contain supplied identities and
unknown statistics, with no card mana/synergy advice. Typed unsupported and menu
transitions remove prior choices. Schema 1 remains explicitly card-only. The actual
publisher/reader polling route must clear malformed/stale hero facts and recover
only on valid fresh observations. This does not prove live hero acquisition: the
optional public-model bridge lacks offered hero IDs and reports that gap.

The IO journey invokes the real Rust child against regular/oversized/malformed
files, a directory, a final symlink, a character device and an owned FIFO with no
writer. FIFO rejection must complete rather than hang. The stale/recovery route
uses the actual dispatcher timer and changes an owned status file from stale to
fresh with a different mode. A deliberately slow owned helper verifies the child
deadline while replay pointer input remains responsive, then verifies recovery
and child cleanup. That helper tests supervision only; it is not evidence of
upstream acquisition. The unavailable-storage route checks guarded checkbox
rollback and subsequent successful durable save after the fault is repaired.

## Ordinary native display

On an owned private X11 display, run:

```sh
dotnet run --no-build --project Hdt.Avalonia -- \
  --replay fixtures/draft.json --settings "$HDT_VERIFY_ROOT/native-settings.json"
```

Retain its exact window identity and screenshot; exercise Next observation,
Rewind, Options and score/synergy checkboxes. Close that window. Headless success
alone does not prove an ordinary display-server launch.

`--proton-state FILE` optionally reads an existing MIT plugin export. Its status
flags contain no offered cards, picks or ratings. Missing/stale/invalid data must
be unavailable, and a subsequent fresh observation must recover. A manually
written status fixture proves format/polling, not a live Proton producer. Never
operate Hearthstone or alter the operator's live prefix for this route.

## Cleanup and limits

Keep every evidence directory and failure screenshot. The driver owns its
snapshot children, FIFO and fault files; timeout termination must target only the
child it started. Confirm no owned slow-reader child remains. The Rust/native UI
runs never write the external input file. Remote CI and live game paths remain
unverified unless separately observed.


## Native library workflow

The same rendered driver verifies selected-deck **My Games** at default and
minimum widths. Two decks and an unassociated record distinguish filtered history
from global **Stats**. Require correct rows and totals after pointer selection,
an explicit no-selection prompt, an empty selected-deck state, and archive
transitions. A fresh window must preserve persisted associations. Navigation
must not change the library revision. Background history updates must preserve
unsaved notes. Inspect `deck-history-1120.png` and `deck-history-800.png`.

The rendered driver must create/edit a deck using real pointer/keyboard input,
export the golden code, import as a second stable identity, change tracking
selection, preserve notes on archive, and show a persisted supplied match.
Invalid import must leave stored state intact, visibly disable mutations and
recover after explicit reload. The Rust CLI journey additionally verifies
revision conflict, exact unchanged stored bytes, sideboard codec behavior and
new file mode 0600. No card catalog, legality certification, automatic Windows
history import or write into Hearthstone is implied by these routes.


## Optional native observation bridge

The default rendered journey links the same `NativeSnapshotWriter` as the
optional source-only MIT plugin exporter. Require worker publication → actual
Rust file reader → real timer/control replacement across constructed, Arena,
Battlegrounds, menu, stale, unavailable and recovered sessions. Inspect
`native-constructed.png`, `native-observed-arena.png`, `native-battlegrounds.png`
and `native-observation-stale.png`. Stable completed-match IDs must deduplicate,
associate native decks only after observing an in-progress match, and preserve
unsaved deck notes during automatic history writes. The publisher must coalesce
a blocked worker's pending snapshots, reject excess bytes, recover from missing
owned storage and exit when disposed. This is producer IO/consumer acceptance
with anonymous fixture observations, **not** proof of HDT model capture or real
game acquisition. The latter requires separately authorized private runtime
acceptance. Never deploy into the operator's prefix or restart the game here.

When complete original reference assemblies are available locally, compile the
full Framework source with `tooling/net/HdtAnalysis.csproj`, explicit
`AnalysisScope=full` and `HdtAppDir`, without stubs. Record binary hashes and zero
warnings/errors. Preserve no proprietary binary in source control. The export
is disabled unless explicitly configured; its output directory must be private.

## Native input-empty X11 overlay

Feature ID: `native-x11-overlay`. After the rendered driver, return to the root
and run the private protocol journey with the actual emitted frame:

```sh
nix-shell avalonia/shell.nix --run 'bash scripts/check-rust-x11.sh --native-frame local/avalonia-evidence/native-overlay-frame.json --native-ui'
```

Require the log to say `exercising actual Avalonia-rendered raster`, all actual
RGB pixels to match, input shape empty before first map and after geometry
changes, inactive/stale/backward/explicit-hide transitions to unmap, subsequent
fresh observations to recover, and owned child exit/window destruction while
the fake game's geometry and input remain intact. Inspect retained
`native-overlay.ppm` and the rendered driver's `native-overlay-render.png`.
The ordinary Rust route without the frame argument reports protocol-only pixels.
The additional desktop route must drive production Options controls with real
X11 pointer input: missing selection fails visibly, discovery finds the owned
fake game, enable starts the real Rust child, actual pixels match and input
shape is empty, focus/stale transitions hide, fresh data recovers, and disable
reaps that exact child and destroys its window. Inspect `desktop-ui/actions.jsonl`,
`desktop.ppm`, `invalid-selection.png`, `stale.png`, `disabled.png` and
`identity.json`. This is isolated X11 acceptance; actual Hearthstone/compositor
acceptance remains unverified. Do not use the operator's game or display to fill
that gap without separate authorization.

## Local catalog and archive restore

Feature IDs: `native-card-catalog`, `native-archive-restore`. The ordinary rendered
driver must search an actual owned illustrative catalog file through the Rust
reader, select/add/remove copies with pointer input, choose an explicit hero,
recover after replacing that source with malformed JSON, and persist the resulting
deck. Inspect `native-card-catalog.png`. No current card data is bundled. A separate
manual CLI probe of a current public catalog can establish format compatibility;
retain URL/time/hash privately and never turn its data into a tracked fixture.
The driver must also browse archived entries and restore one without changing its
stable identity, notes, history association or tracking selection. The native file
chooser remains a separate unverified route.

## Main-window hierarchy and managed failure boundary

Feature ID: `native-window-layout`. After building, run
`nix-shell avalonia/shell.nix --run 'bash scripts/check-native-failure.sh'`.
Require the intentional assertion to preserve its full stack, return1 and reap
its exact helper. A failing assertion must never be confused with a successful
run or escape the entrypoint as an unhandled managed exception.

The ordinary rendered route checks library/browser/MyGames/selected-deck order,
settings sidebar/content separation and every navigation target at 1120×760 and
800×600. Inspect `layout-library-*`, `layout-settings-*`, `layout-arena-*`,
`layout-local-files-*` and `layout-editor-800.png`. Require a collapsed raw editor
on overview, real filtered decks and supplied games, unknown metadata retained,
long names/paths contained, and minimum-width pointer save/export/copy. New from
archived browsing must expose/focus the new name field with one action, before
the input helper can scroll it into view.

The X11 child still depends on main-process raster rendering and parent lifetime;
this acceptance does not establish independent overlay UI/error handling.

## Independent UI lifetimes (explicit opt-in)

Feature ID: `native-ui-isolation`. The coupled single-main preview above remains
available. The opt-in Rust session owner starts sibling Main and overlay-renderer
processes from an explicit configuration; both independently read the existing
observation source. See [the ownership/protocol contract](../../../../docs/specs/native-ui-isolation.md).
The cached-HDT producer still depends on its own dispatcher.

After the rendered driver above produces its fixtures, run from repository root:

```sh
bash scripts/check-native-isolation.sh "$HDT_VERIFY_ROOT/native-ui"
```

Require the actual ignored test to run, exit zero and retain the printed private
evidence directory. It launches the real application and Rust owner, uses actual
X11 pointer input for Main navigation/preferences, freezes/kills/restarts each UI
role, compares independently rendered expected pixels, verifies source failures,
focus/geometry/empty input shape, and observes exact role/group cleanup. Inspect
`main-stopped-overlay.png`, `preferences-overlay.png`, `stale-notice.png`,
`isolated-desktop.png` and `session-actions.jsonl`. No setters or Main-relayed game
facts are used. This is owned fake-game acceptance, not live game acquisition.

## Delivered raw Arena events (optional original assemblies)

[The frozen contract](../../../../docs/specs/native-arena-event-bridge.md) owns the
actual-watcher recipe, evidence distinctions and binary provenance. It requires
an explicit local `HDT_APP_DIR`; no vendor assemblies ship with native UI. Execute
that recipe to observe the real watcher with a scripted provider through bounded
copied events, publication, Rust file reads and rendered native controls. Retain
assembly hashes, receipt-vs-publication evidence, paused-source negative control,
hero/card/replacement/menu screenshots and cleanup. This does not prove live game
memory acquisition, production Wine scheduling, or independent acquisition.
