# Capture exceptions before the crash dialog opens

2026-11-15. The operator reports white, unusable HDT main and crash windows after clicking New or changing a window to floating during Arena drafting. The Arena overlay continues to display information. Arena specificity is unknown.

## Contract

Record HDT dispatcher exception metadata before HDT enters its modal crash dialog. Preserve HDT's exception handling and keep Hearthstone untouched. Store diagnostics beside the existing private control snapshot. Include exception types, HRESULTs, method names, and window geometry. Exclude exception messages, account data, card names, and method arguments.

## Verification

Compile against the installed HDT and native .NET assemblies. Load the plugin in the real tracker. On a reproduced failure, verify that the diagnostic file precedes the dialog and contains the exception metadata. A successful compile alone does not verify capture. Do not suppress the exception or claim the renderer is fixed.

## Investigation

The Wine GDI renderer test immediately produced another Crash Report and was reverted. The existing DXVK configuration remains the baseline. Compare focus changes, resizing, and dialog opening independently of Arena when the operator can change the game screen.

## Revisions

2026-11-15. The first live capture recorded COMException 88980406 after repeated Settings clicks during drafting. The capture already included a CrashDialog, so later failures can overwrite the initiating exception. Retain each exception as a separate line in a process-specific file. Add a presentation-source snapshot to distinguish normal windows from popup roots and identify their dispatcher threads and requested render modes. The snapshot observes existing WPF sources and does not add HearthMirror reads. The component map records verified relations separately from upstream-source assumptions.

2026-11-15. The operator confirmed Settings opens and closes at the main menu with the revised diagnostics loaded. One controlled draft entry also stayed normal. Repeat Settings during drafting to capture the initiating exception separately from any later failure in the crash dialog. The existing EnableArenasmithOverlay setting also changes rating work, so disabling it would not isolate drawing alone.
