# Local freeze diagnostics

Status: implemented and tested locally. Not installed or published.

## Goal and scope

The operator approved a bounded local flight recorder, an independent dispatcher watchdog, and observations around the Battlegrounds card-back control. A developer must be able to retain the sequence before a UI stall and inspect it as a timeline without interrupting Hearthstone.

Keep the running game and tracker unchanged during development. Use the isolated replay for acceptance. Exclude account data and automatic network uploads. Do not infer successful rendering from a UI heartbeat. Do not expand this change into a runtime migration or a remote telemetry service.

## Acceptance

| Requirement | Falsifier |
| --- | --- |
| Capture a real dispatcher delay | A 3.5-second block in the isolated WPF dispatcher does not produce a stall event, capture, delayed acknowledgment, and recovery |
| Bound storage and producer work | Pressure exceeds two rolling 1 MiB files, the queue grows without a limit, or dropped events are not reported |
| Observe the actual BG control binding | Synthetic WPF input at the bound card-back control and six command-driven view changes do not produce their separate observations |
| Preserve diagnostic evidence | Export overwrites an existing file, accepts overlapping duplicate events, or changes its inputs |
| Make test results reproducible | The fixture reads mutable source after capture, or its result omits the source manifest hash |
| Fail a deliberately false expectation | The replay's existing impossible-card assertion returns success |
| Preserve existing replay behavior | Arena offers, Settings input, resize, popup, or New dialog regress |

Native BG input delivery, live combat reproduction, actual GPU presentation, HearthMirror tracing, ArenaSmith request spans, and live installation are outside this acceptance claim. Existing portable desktop experiments remain separate.

## Evidence

Before separation on 2026-11-15, the prototype positive and deliberately failing replay used the same source manifest SHA-256, `24d5fdb5bcc24319365f8f9adc3ea30d3a42d6c92b0e46ac17fd685dcbbc1885`. The positive run passed. The negative run returned 1 at its intentional impossible-card assertion. The private result records retain the original dirty-checkout flag.

The separated candidate passed 21 tests and 68 assertions, shellcheck, TypeScript bundles, and native builds. The CLI rejected existing output and duplicate inputs while preserving the original trace. These checks do not establish live BG reliability or full portable-desktop compatibility. Clean-commit replay and Nix package verification are required before publication. Their results will be recorded with the review.

## Revisions

- 2026-11-15: Recorded the approved scope and implemented acceptance as a durable spec after the initial code work. This document was not written before implementation.
- 2026-11-15: Replaced the initial standard-button assumption with observations of HDT's custom `OverlayButton` binding. REA inspection of the exact installed assembly identifies its mouse-up command path. The test distinguishes synthetic WPF input from native input.
- 2026-11-15: Captured source and scripts per replay after a test read a script while the checkout was being edited. Added a source manifest and read-only namespace mount.
- 2026-11-15: Gave each plugin load its own directory to prevent overlapping writers during reload. Added a long-timestamp conversion check.

Usage, event meanings, retention limits, and remaining gaps are maintained in [freeze diagnostics](../DIAGNOSTICS.md).

- 2026-11-15: Separated diagnostics into plugin 0.5.1 on the released Hyprland baseline. Removed experimental portable input code. Added writer-failure status and a real CLI file-preservation test.
