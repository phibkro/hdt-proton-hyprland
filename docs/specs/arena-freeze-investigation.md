# Isolate the Arena draft freeze

2026-11-15. Continue from the passing isolated WPF replay at `6a13f9b`.

## Contract

Find the first failing boundary in successive Arena offers and departure from drafting. Preserve the live game and tracker while the operator plays. Keep original assemblies, private fixture data, and ordered evidence under the project. Do not claim a root cause from a passing substitute or static source alone.

## Experiments

1. Compile and load the current compatibility plugin into the isolated host. Exercise its update lifecycle during the existing transition sequence. Compare with the original baseline.
2. Inspect the installed watcher API and event-dispatch implementation. Drive that actual watcher with a synthetic provider where its interface permits. Verify event delivery independently of direct model updates.
3. Add the real Arena callback and controlled rating work where the installed boundaries permit. Observe polling, event delivery, model changes, dispatcher responsiveness, and visible state independently.
4. Extend composition coverage only when the preceding experiments identify the remaining boundary. Use fresh owned namespaces and prefixes for each run.

## Acceptance

A root-cause claim needs a reproducible failure, the initiating operation or violated invariant, and a controlled comparison that removes the failure. A fix needs a regression check that fails without the fix and passes with it, followed by a live test when the operator can pause at a draft. Report untested boundaries explicitly. Do not modify or replace the game, issue account requests, or make picks in automation.

## Narrowed experiment

The operator identified the score display setting, then reported recurrence after about five decisions with both Arenasmith and scores enabled. No-deck mode did not prevent recurrence. The first replay fixture assigned level-zero plaques because HDT preferred its default dynamic-score object. Correcting both score objects produced actual level 1, 3, and 5 badges and reproduced `88980406`, stale first-offer pixels, and white window regions.

Level-four badges passed. Level-five badges failed without the compatibility plugin. Suppressing the particle emitter's instance Loaded handler passed with level-five artwork and scores retained. This isolates the particle initialization and rendering path in the tested environment, not a specific Wine or graphics-driver function. Implement suppression in the compatibility plugin before emitter initialization. Keep the executable, scoring logic, and ratings unchanged.
