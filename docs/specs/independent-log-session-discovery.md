# Independent log-session discovery

## Outcome and limits

Extend the existing native byte journal so a user can select a Hearthstone log
root once and keep collecting explicitly selected logs as session directories
appear. This does not interpret game events, acquire memory, identify the active
game, obtain Arena choices or request account/statistics services. The existing
[independent observer contract](independent-observer.md) remains authoritative.

CLI:

```sh
hdt-host observe --output /owned/new-capture --from start --max-bytes 8388608 \
  --game-log-root /explicit/Hearthstone/Logs \
  --game-log Power.log --game-log LoadingScreen.log
```

`--game-log-root` occurs once and requires an absolute path without `.` or `..`
components. At least one `--game-log` is required; duplicates fail. The closed
allowlist is `Power.log`, `LoadingScreen.log`, `Arena.log`, `Achievements.log`,
the streams configured by the inspected HDT log manager. No prefix discovery,
Steam inspection, logging configuration or installed application activity occurs.
For a flat legacy directory use existing explicit `--source name=/absolute/file`.
The Linux collector requires mounted procfs for descriptor-relative enumeration;
it does not dereference source-supplied procfs paths or symlinks.

## Discovery and ownership

Recorder and game roots share one sequential bounded directory-discovery pass
inside the existing journal poll, followed by the existing sequential cursors.
Each root keeps its own discovered-session set and unavailable/recovery state.
Recorder names and journal records preserve their existing contract. Game sessions
are immediate child directories with 1..128 ASCII letters/digits/underscore/hyphen
in their names; no date, creation time, modification time or directory ordering
identifies an active session. Other ordinary entries are ignored. A candidate
directory symlink fails loudly. Every selected filename gets its own cursor even
if the file has not yet appeared. Late files are read from byte zero.

Each root admits at most 128 session names over one capture; its directory scan
reads at most 4096 entries per poll. Each game session admits only the explicitly
selected allowlisted filenames (maximum four). Capacity overflow fails the
capture with a retained stop reason. Root/source disappearance is observable and
recoverable; permissions, symlinks, nonregular files and other I/O faults fail.
Nothing removes captured sessions or silently evicts their cursors. Source names
encode the session name deterministically plus the log kind; collisions with
explicit or recorder sources are rejected. All generations retain their distinct
source identity/offsets. Existing startup sources obey `--from`; new sessions and
files begin at zero. A restarted collector requires a new output directory and
the caller's declared offset policy; no unverified resume checkpoint is inferred.

No source/root path component may be a symlink. Resolve directory components with
owned directory descriptors and `O_NOFOLLOW`; enumerate and open descendants
relative to those descriptors. Holding an already open source descriptor permits
draining a replaced file without opening an unintended new path. Missing roots
can appear later. An ancestor becoming a link is an explicit error, even if the
link points inside the selected root. Never rename, truncate, configure or delete
producer files; no read modifies their bytes.

The journal start record declares selected root/logs. New `game.root.unavailable`,
`game.root.available` and `game.discovered` records identify root availability and
session/source membership. They are acquisition metadata, not semantic game
events. `source.*`, heartbeat and stop records retain the existing representation.
Evidence is private and bounded by the same byte limit, including JSON/base64.

## Acceptance and falsifiers

Use owned temporary directories, original synthetic bytes and real collector CLI
processes. Independently decode source bytes by source/generation/offset; compare
exact content and check private output permissions and unmodified source bytes.

- Startup/append, partial binary input and explicitly selected filenames work.
- A new session is captured without dropping unread bytes from the previous one.
  Late selected files and root disappearance/recovery are observable.
- Truncation and replacement retain explicit generation/continuity records.
- Missing/recreated roots and restarted producers/collector are exercised; prior
  capture output cannot be reused or silently adopted.
- Root/session/file links, ancestor links, malformed/duplicate/unpaired CLI
  selection and forbidden log names fail with evidence and no outside bytes.
- Both session-count, scan-entry and byte capacity fail explicitly with a final
  stop reason; bounded constructor/property cases cover closed-set/path policy.
- Existing explicit-source and recorder discovery regressions still pass,
  including producer stop/death/restart and byte reconstruction.

Use the pinned development shell, measured resource admission and at most two
build/test workers. Record exact tested commit and separate synthetic CLI/file
evidence from unrun live Hearthstone and canonical semantic acceptance.

## Provenance

Independently authored extension of this project's MIT collector. The source
selection allowlist is grounded in the pinned
[HDT LogWatcherManager](https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/2698623dc0e91491671a614ae176f9db68831aa7/Hearthstone%20Deck%20Tracker/LogReader/LogWatcherManager.cs).
That reserved implementation is reference evidence only; no code/assets/binaries
are copied. Session-name/path policy here is our explicit acquisition contract,
not a claim that every Hearthstone release uses exactly this layout.

## Revisions

- 2026-11-16: Startup discovery initializes each selected `--from end` cursor
  through the production source-open path before `start` returns. Existing files
  retain their observed end offset and descriptor; absent files record that
  absence immediately. A file appearing between startup discovery and the first
  regular poll is late input and begins at zero. This closes a startup interval
  that could otherwise discard its bytes; explicit-source initialization is
  unchanged.
