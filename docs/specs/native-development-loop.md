# Reproducible native development loop

## Goal and journey

A developer runs the documented F# watch command, edits a view and sees a rebuilt
native window. Verify a real source edit, process replacement and changed pixels,
not just watch-file discovery. Keep the ordinary app free of diagnostics.

## Constraints

Reuse the pinned SDK, existing dev.sh entrypoint and private X11/bubblewrap
patterns. Archive committed source into an owned disposable working copy; never
edit an operator's source to trigger a test. No host display, network, account,
license activation or game activity. Preserve evidence and bound/reap every child.
F# uses full restart, not hot reload. Licensed inspector/MCP and editor attach are
separate prerequisites, not a consequence of a successful watch test.

## Acceptance

- Locked development build and normal shell checks pass.
- On a private X11 display, the real documented watch command opens the replay.
- Change an F# palette value in that run's private source; observe a new native
  window PID, changed assembly bytes and corresponding rendered palette pixels.
- Previous window disappears and cleanup terminates watcher/X server and children.
- Save source identity, commands, logs, screenshots and expected failure evidence.
- Missing licensed/editor prerequisites remain explicit. No invented attachment.

NuGet/MSBuild require user resolution even in an offline namespace. Supply a
synthetic run-owned passwd/group identity; do not bind the host account database.

## Revisions

- 2026-11-16: Initial bounded workflow acceptance after operator requested all
  outstanding lanes in parallel. This lane owns developer-loop checks only.

- 2026-11-16: Initial isolated attempt stalled before GenerateWatchList completed.
  Owned-process syscall evidence showed repeated missing passwd lookups; add the
  synthetic identity prerequisite rather than changing SDK or allowing network.
