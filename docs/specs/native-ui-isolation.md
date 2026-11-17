# Independent native UI processes

Status: opt-in implementation with owned private-process acceptance. Owner: `feat/native-ui-isolation` worktree.
Date:2026-11-16. No installed runtime or acquisition changes.

## Goal and view tree

A stopped/crashed/restarted Main UI cannot freeze or terminate overlay rendering;
a stopped/crashed/restarted overlay renderer cannot freeze Main UI. Existing
baseline preview stays separately selected; isolation never silently falls back.

```text
Rust session owner (explicit opt-in, private session lease)
├─ Main UI process
│  ├─ existing hierarchical native pages
│  └─ sole preferences/library writer
└─ independent Avalonia overlay renderer process
   └─ existing Rust input-empty X11 window owner
```

Main and renderer each read the producer's version1 observation file directly.
Neither UI relays canonical observations to the other. The existing cached-HDT
producer still depends on its WPF dispatcher: this does not isolate acquisition.

## Semantic inputs and ownership

| Boundary | Meaning / authority | Rejection and bounds |
|---|---|---|
| Explicit session configuration | Launch intent: absolute runtime/application/observation paths, private session directory, explicit fake/real game identity | version1; bounded regular file; no default attached game or implicit baseline |
| Producer observation | Account-free observed game facts, not commands | existing native DTO validation: byte/count limits, freshness5s, GUID/session/order/mode constraints; clear prior advice on rejection |
| Preferences file | Main-owned atomic preferences; renderer read-only | owner lease + reap prior Main before replacement guarantees one writer; reuse existing preferences contract, no added CAS/revision scheme |
| Owner stdin | version1 lifecycle commands: status, restart(role), shutdown | typed closed roles; bounded8KiB lines; one serialized command at a time; no automatic restarts |
| Child stdout | version1 readiness/control observations, not game authority | PID/role/instance identity; bounded8KiB lines; readiness deadline; UI health requires dispatcher acknowledgement |
| Raster stream | Derived presentation with provenance | existing bounded exact opaque BGRA frame; explicit source-unavailable notice distinct from game facts |

The session owner holds the exclusive private lease and child/process-group
custody. Main alone owns preferences and library writes. Overlay preference
acknowledgements report observed values; they are not storage revisions/conflict
checks. Explicit repeated restart remains possible, but the old exact group must
be terminated and reaped before its replacement exists. No restart quota or storm.

## Lifecycle and liveness

The owner is outside both UI event loops. It keeps minimal status available if a
child exits or stops acknowledging its UI dispatcher. Child IPC readers/writers
run off UI; heartbeat requests have at most one pending dispatcher callback.
Background process activity alone never labels a frozen UI responsive. Readiness
requires the real window/input-empty owner to be prepared and the UI dispatcher
acknowledged, within a bounded deadline.

Closing/crashing one child leaves its sibling running. The explicit owner shutdown
or owner input EOF ends the session. Cleanup first continues any stopped owned
group, requests termination, then kills on bounded timeout and reaps only owned
children/groups. Owner death must not orphan UI descendants. No game input,
focus change, installed service or global process-name cleanup is permitted.

Role trampolines compare the exact expected owner PID before and after installing
parent-death SIGKILL. Managed-owned Rust helpers similarly receive an explicit
`HDT_OWNED_PARENT_PID`; standalone CLI commands without it keep their ordinary
lifetime. One shared managed launcher keeps each child's creating thread alive
through its exact wait/reap. This matters because Linux
[parent-death signals follow the creating thread](https://man7.org/linux/man-pages/man2/PR_SET_PDEATHSIG.2const.html),
and the Rust owner's [subreaper](https://man7.org/linux/man-pages/man2/PR_SET_CHILD_SUBREAPER.2const.html)
collects adopted role descendants. The managed launcher is used only by existing
background actors; dispatcher code never waits on its thread teardown. Native
raster parent-death also uses SIGKILL, including when that descendant is stopped.

The renderer independently polls observations/preferences and renders on its own
Avalonia dispatcher. Invalid/stale/unavailable observations clear advice and
produce a visibly labelled source-unavailable notice, with transport freshness
separate from tracker observation time. Notices do not advance/reset accepted
fact high-water. Fresh valid facts recover; game focus/geometry/input-empty rules
apply to both facts and notices. A dead/stopped renderer is reported by the owner;
this first slice does not promise a second renderer capable of drawing through
its own stopped dispatcher.

## Acceptance / falsifiers

Use owned fake game and real processes in the existing Bubblewrap/private-Xvfb
namespace, with source/binary identities and screenshot/pixel evidence.

- Source changes continue producing different actual overlay pixels while Main
  is SIGSTOP'd; Main kill/restart also leaves the overlay alive and current.
- Overlay renderer STOP/kill leaves Main UI dispatcher heartbeat and actual
  navigation responsive. Explicit role restart recovers without duplicate owner.
- Stale, malformed, unavailable and backwards source states remove prior advice
  and show notice pixels independent of Main. Fresh valid state recovers.
- Production Main preference controls change independently read overlay output.
- Observe identities, empty input shape, focus hide, geometry follow, and final
  exact-child/group exit; no game input and no host display socket activity.
- Reject a concurrent owner lease before spawning another writer. Kill the owner
  with Main, renderer and raster all stopped; observe every exact PID/group gone.
- Retain original baseline UI journeys, managed negative-control failure boundary,
  relevant Rust/managed/format gates and exact committed-head acceptance.

A stub-only lifecycle test, main-relayed data, hidden stale cards, unmanaged
children, a background-only health signal, or silent baseline fallback falsifies
completion. State remaining integration gaps explicitly.

Run the actual fixture with `bash scripts/check-native-isolation.sh MANAGED_EVIDENCE_DIRECTORY`
inside `nix-shell avalonia/shell.nix`, after generating the managed evidence using
the existing `Hdt.Checks` driver and `avalonia/fixtures/draft.json`. The script
captures source status/hash, binaries, control transcript and actual window PNGs
under a fresh ignored `evidence/native-isolation-*` directory. It uses Bubblewrap
private PID/mount/network namespaces and an empty private `/tmp/.X11-unix`.
Expected raster bytes are rendered in a separate test child from the exact
published snapshot/preferences, including its observation timestamp. The test
uses real pointer navigation/preferences and production owner commands; it does
not ask Main to calculate or relay overlay pixels.

Limits: the owner exposes JSON control status rather than an independently drawn
supervisor window. A stopped/dead renderer cannot draw its own error; its raster
window clears after five seconds, and explicit restart is required. No automatic
restart, native acquisition, Wayland compositor integration or installed/live
Hearthstone acceptance is claimed. Runtime evidence observes tested cases;
parent-death guarantees still depend on Linux and non-privilege-changing exec.

## Revisions

- 2026-11-16: initial contract from inspected NativeOverlayActor/Controls,
  NativeObservationReader, LaunchOptions, native_overlay.rs and launch.rs.
- 2026-11-16: lead review removes arbitrary restart quota and unnecessary settings
  CAS/revisions; ownership lease + serialized reap/restart supplies single writer.
  Heartbeat must be acknowledged on UI dispatcher. Heavy runs held for incident
  preservation until explicitly released by lead.
