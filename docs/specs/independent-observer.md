# Independent HDT event observation

Status: prototype. Exact input collection is implemented. Canonical replay has
limited mode-transition coverage; automatic live event comparison is pending.

## Goal

Keep collecting evidence when HDT freezes or exits. Preserve HDT's canonical
logging and event semantics so a comparison can identify where HDT stopped
progressing. Do not substitute a heuristic Power.log parser for HDT's parser.

## User journey

Start the observer independently of HDT. Play normally. When HDT stops responding,
the observer continues recording input and its own health. Inspect the last input
received, last handler entered/completed, and last UI acknowledgement. Reproduce
from retained input using the same HDT parser version where that input is sufficient.

## Contract

- The collector runs outside HDT and its Wine process. Closing or freezing HDT
  cannot stop the collector. Its lifecycle does not depend on HDT's exit status.
- Existing HDT diagnostic records retain their original content and order within
  their stream; acquisition metadata is an envelope, not a replacement schema.
- Game-event names and payload semantics come from HDT's event definitions. A
  separately maintained interpretation is not accepted as equivalent.
- Independent expected-event production reuses the canonical parser implementation
  and records its version/hash. No claim of equivalence without replay comparison.
- Input arrival, handler entry, handler completion, event publication, and UI
  acknowledgement are distinct observations. Missing observations mean unknown;
  they do not prove that an event never occurred.
- Capture file identity/offsets and stream/session identity for correlation. Do not
  infer causality from adjacent wall-clock timestamps alone.
- A frozen producer cannot emit future internal events. The collector must show
  that gap explicitly while retaining independent input capture.
- Power.log is not a complete input for Arena/HearthMirror or remote ratings.
  Missing independent inputs must be reported rather than simulated as truth.
- Keep raw evidence local and private, bounded by explicit retention. Exports must
  distinguish original private records from sanitized records.
- Do not restart or alter the live game, tracker, or prefix to develop this feature.

## Source findings

REA selected-type metadata confirms `Utility.Logging.Log`, its `OnLogLine`
subscription methods, `API.GameEvents`, and `LogReader.LogWatcherManager` in the
installed assembly SHA-256
`65d66314dddd8a36588a83b5d60deebea8ee2e758c6b91751838d663d0ae7586`.
Evidence: `ev_224a4f9229ab91570070f94cfbc25dd58c416818e71f4d86645d9e07dfe423be`.
This confirms interfaces, not complete source/build equivalence.

Upstream source at commit `2698623dc0e91491671a614ae176f9db68831aa7`:

- [Log.cs](https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/Utility/Logging/Log.cs)
  writes through `System.Diagnostics.Trace` and invokes `OnLogLine` synchronously.
- [LogWatcherManager.cs](https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/LogReader/LogWatcherManager.cs)
  invokes Power/Arena log callbacks after their handlers. Those callbacks alone
  cannot establish handler entry or expose a handler stuck before publication.
  It constructs `HsGameState`, `GameV2`, and `GameEventHandler`; these dependencies
  require inspection before treating it as a headless parser.
- [GameEvents.cs](https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/API/GameEvents.cs)
  defines canonical game-event ActionLists. Subscribing observes publication, not
  all work before or after publication.

## Implementation boundary still to resolve

First establish the smallest reusable canonical parser boundary and compare it
against the installed build. An external file collector can preserve exact log
bytes immediately, but is not by itself a semantic shadow of HDT. The existing
in-process flight recorder is also not whole-process failure isolation.

## Prototype boundary

`hdt-proton observe` captures original bytes from explicitly selected files in a
native process. The journal does not interpret events. It records source file
identity, offset, generation, acquisition time, its own heartbeat, and termination.
Capacity is explicit. Exhaustion stops with a failure record rather than deleting
evidence. Detected replacement or rewrite marks continuity unknown. A rewrite
between polls that preserves the checked byte range can escape detection; this
is not a kernel-level audit of every file write.

The optional recorder root adds session discovery to the existing collector.
Every matching HDT session owns a separate cursor, so a restart does not replace
an older session that still has unread bytes. New sessions start at byte zero.
Startup sessions follow the requested offset policy. Discovery has a limit of
128 sessions and shares the journal's byte limit. Both limits fail explicitly.
Hearthstone log-session discovery remains outside this implementation.

`hdt-replay run --canonical-events --with-plugin` loads the installed HDT and
HearthWatcher assemblies in the existing isolated WPF host. It invokes
`LogWatcherManager.OnNewLines`, subscribes to the actual `GameEvents` fields, and
records original `Log.OnLogLine` strings. It does not start HDT's file watcher,
which can move or delete files. Canonical event names and argument types come
from the installed assembly. Arguments use Newtonsoft's public JSON projection;
that representation is not a complete clone of arbitrary CLR object state.
Serialization failures are explicit and fail the replay acceptance check.

The exercised fixture covers Arena/Battlegrounds/menu mode changes. It does not
reproduce an Arena battle or prove full parser equivalence across game states.
Parser call entry/return is observed, not each internal handler's completion.
An asynchronous event retains its input context through `AsyncLocal`; input zero
means no originating replay input is known. A returned parser call does not prove
that asynchronous handlers or rendering have completed.

Remaining work includes canonical replay of complete retained match input,
per-handler observations in HDT, live correlation, and independent acquisition of
non-log inputs. The reported intermittent freeze after Arena battles is unresolved.

## Acceptance gates

1. The real independent collector continues receiving recorded game-log input
   after the isolated HDT test process is stalled, killed, and restarted.
2. Exact diagnostic-log collection passes byte/order comparison, including
   partial writes, rotation, truncation, and process restart.
3. Shared-parser replay produces the same canonical event stream for identical
   complete input and initial state. Version and unsupported inputs are explicit.
4. A deliberate stuck handler is distinguished from stalled input and a stalled
   UI, using correlated entry/completion observations rather than guessed markers.
5. Transport overflow, collection failure, and retention loss are visible. An
   unavailable event never appears as successful processing.
6. Exercise the installed runtime seam in isolation before activation. Synthetic
   tests do not establish live Arena, HearthMirror, or match-end coverage.

## Revisions

- 2026-11-15: User clarified that the logger must be isomorphic to HDT's and
  survive HDT failure. Supersedes the private heuristic observer sketch; no such
  observer was installed or started. This contract requires canonical semantics
  and separate process ownership.
- 2026-11-15: Dependency inspection found that the parser calls the game model
  and WPF overlay. The prototype reuses the installed binaries in the existing
  isolated WPF host instead of replacing their semantics with a headless mock.
  A native journal owns byte retention independently. Added the explicit pilot
  coverage boundary; the original full-comparison acceptance gates remain open.
