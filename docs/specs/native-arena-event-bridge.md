# Delivered Arena choice identity bridge

This increment transports choices from the existing public
`ArenaStateWatcher.OnChoicesChanged` event. HDT still owns memory sampling,
watcher lifetime and the WPF delivery context. Native Main and overlay consume
the independently published snapshot; acquisition is not independent.

## Evidence and rights

Reference only: HDT source commit `2698623dc0e91491671a614ae176f9db68831aa7`,
`HearthWatcher/ArenaStateWatcher.cs:39–56,208–223` and
`Hearthstone Deck Tracker/Hearthstone/Watchers.cs:510–521`. Its README.md lines 59–61
reserves rights; no implementation or assets are copied. This adapter is
independently authored under this repository's license. Installed public ABI
was inspected and compile-tested, without executing vendor code:

| Assembly | SHA256 |
|---|---|
| HearthWatcher.dll | `9741b2caab6ecd71201b67e533de477e4020cc17814bcf2d03243e1ec2ee1990` |
| HearthMirror.dll | `c0c15ef8ede01c3a5b9b170533a7fdc35c0c81ed5a1936eb75b773281eff2ee7` |

## Boundary and ownership

The plugin subscribes to the existing watcher exactly once and detaches on
unload. It never starts/stops the watcher or changes its event context. Context,
hero, hero-power, deck and package changes invalidate copied choices. Account
deck identities are discarded. A late subscription waits for delivered context;
it cannot infer context from the choice count or card name.

Only bounded copied ID/name/type/mana and opaque `Actor.Index` values survive a callback; mutable vendor
objects do not. Explicit local metadata types distinguish hero/card choices;
unknown/mixed/duplicate identities or actor indices become unavailable. Source indices
are displayed as opaque source values; no left/middle/right or physical ordering
is inferred from index or list order. Existing public
package/redraft/dual-class state vetoes support. No new memory or account calls
are introduced. Cached rating models lack a provable shared generation, so this
path publishes no joined ratings or synergies.

## Versioned observations

Arena schema 3 retains the schema 2 tagged selection union and empty legacy offers.
Supported nonempty choices require `choiceEvidence` with exact origin
`HdtWatcherEvent`, UTC `receivedAt`, positive local `revision` and nonnegative
local `generation`. These are event-delivery evidence, never upstream sampling
versions. Receipt must not exceed envelope publication time. Unsupported/inactive
states clear evidence. Schemas 1 (card-only) and 2 remain explicit legacy adapters.
Schema 3 choices require unique bounded `sourceIndex` values. Separate cached
`Player.Hero` is not associated with choices: the draft hero is explicitly
uncorrelated. Cached picked cards use a separate `cachedDeck` with exact origin
`HdtPublicTileModel`, visible correlation-unknown labeling, and no generation
claim. They never populate `pickedDeck`, `pickIndex`, scores or recommendations;
schema 3 pick progress stays unavailable. Hero/menu transitions clear that cache
projection.
Periodic publication does not restamp event receipt. The UI must display delivery
origin and unknown memory freshness, and clear facts when transport becomes stale.

## Acceptance contract

Use a separately owned real installed watcher and scripted `IArenaStateProvider`
in an optional .NET 10 native rendered harness, with a separate owned
`SynchronizationContext` dispatcher. This revises the initial Wine proposal:
source inspection shows the public watcher depends on Task/Threading/context,
not WPF. The fixture therefore avoids full HDT startup (and its unrelated
account/asset work), while executing the actual installed watcher binary.
Record runtime/assembly identities and prove a stopped dispatcher halts delivery
and publication. This does not establish Wine/WPF scheduling compatibility; the
Framework build against original assemblies is a separate API compile check.
If actual binary instantiation fails, stop and report that runtime boundary.
Real event callbacks must pass through
this adapter, atomic writer, Rust bounded reader and ordinary Main/overlay pixels:
hero→cards→replacement→menu, invalidation, mixed/unknown metadata, source failure
and recovery. Freeze the owned source dispatcher and observe independent native stale/error UI.
Check exactly-once detach and immutable copies. No direct adapter-cache setters
can establish that acceptance. This demonstrates API/transport/rendering, not live
memory acquisition. Preserve existing regression journeys and clean-head evidence.

Remaining gaps: native memory/log acquisition, live validation, statistics and
entitlement, hero-power/package/redraft/dual-class assistance, and any event
delivery/sample failures hidden inside the original watcher/provider.

## Reproduce the optional local boundary check

Admission-control the lane first. `HdtAppDir` must identify an existing local set
of original assemblies; this command performs no full HDT bootstrap. The optional
references are excluded from ordinary CI/native distribution. No vendor binary is
committed. In the pinned `nix-shell avalonia/shell.nix` environment:

```sh
dotnet build avalonia/Hdt.Checks/Hdt.Checks.csproj -m:2 \
  -p:RestoreLockedMode=true -p:UseSharedCompilation=false \
  -p:WatcherAcceptance=true -p:HdtAppDir="$HDT_APP_DIR"
watcher_checks=$(dotnet msbuild avalonia/Hdt.Checks/Hdt.Checks.csproj \
  -p:WatcherAcceptance=true -getProperty:TargetPath)
timeout --kill-after=3s 65 dotnet "$watcher_checks" --watcher-events local/watcher-evidence
```

The actual DLL SHA256/MVIDs and runtime are printed. Output includes native Main
screenshots at 1120/800, production overlay raster captures, copied immutable-event
mutation control, and paused-source dispatcher/file/stale/native-navigation/recovery
checks. These headless renders are distinct from private-X11 acceptance. With the optional
watcher build still active, run the existing real pointer/Options/Rust child driver
with `--watcher-events` in addition to `--native-frame FILE --native-ui`. That
opt-in executes the actual watcher on its owned source dispatcher and is the
sole file publisher during its hero/card/replacement/menu/stale/recovery journey.
It compares actual X11 overlay pixels against the production renderer and checks
empty input shape; it never injects input into the fake game. The usual independent
Main/overlay failure-domain suite remains a separate regression. The optional watcher variant has separate intermediate/output directories; switching
build variants must not reuse dependency manifests or copy vendor references into
the ordinary output. The private X11 driver resolves the chosen target directory
from MSBuild. Before publishing, verify the ordinary output excludes optional
vendor references. Run ordinary rendered regression and
failure cleanup checks separately. The Framework full-scope compilation against
original assemblies is API evidence, not a WPF runtime scheduling check.


## Review follow-up: repeatable build variants

A repeated ordinary→watcher build reproduced a stale ordinary dependency manifest
beside copied watcher DLLs, causing FileNotFoundException before the test began.
The optional variant now has separate compiler/dependency outputs. Acceptance
must exercise ordinary→watcher→ordinary without cleaning, resolve the requested
variant through MSBuild, run the actual watcher and private X11 path, and verify
that ordinary dependencies exclude the optional assemblies. A full Roslyn check
also replaces unnecessary empty-array allocations and names null constructor
parameters; API-only compilation is not a substitute for that analysis.
