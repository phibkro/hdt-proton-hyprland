# Native Avalonia migration preview

Feature IDs: `native-window`, `native-draft-replay`, `native-preferences`,
`bounded-snapshot-io`, `proton-status-bridge`, `native-deck-library`,
`native-deckstring`, `native-history`, `native-observation-bridge`. These are preview routes; passing
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
