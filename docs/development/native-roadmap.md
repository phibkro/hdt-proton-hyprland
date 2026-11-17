# Continue the native HDT migration

Use the [parity ledger](../reference/hdt-feature-parity.md) to choose a user journey
and the [application feature map](../../.cursor/skills/verify-hdt-proton/features/application/README.md)
to define its acceptance. Keep the existing Proton patch usable during migration.

## Choose the next bounded journey

1. Complete account-free library gaps under `deck-metadata` and `deck-versions`.
   Give each operation its own save, cancel, restart, and association tests. Inspect
   original menu and selected-deck behavior before choosing the native layout.
2. Strengthen the acquisition boundary under `match-detection` and `arena-phases`.
   Keep raw event receipt, published observations, rating generation, and rendered
   state separately observable. Add independent acquisition before claiming an
   independently usable tracker.
3. Expand the existing Arena journey under `arena-relations` and `arena-redraft`.
   Define supported payloads first. Old advice must clear when a phase is unsupported.
4. Add Battlegrounds journeys separately under `bg-selection`, `bg-minions`,
   `bg-guides`, and `bg-combat`. Repeated open/close, mode changes, and stale-data
   clearing belong in each interactive panel's acceptance.

Library and history work can proceed without Hearthstone or account access.
Acquisition and Arena presentation depend on explicit observation contracts.
Authenticated advice depends on actual service authorization and entitlement.
Native compositor parity depends on a working isolated compositor route.
Do not block account-free user journeys on unrelated desktop infrastructure.
The selected-deck history increment supplies a reusable account-free acceptance
pattern under `match-history`: explicit global and deck scopes, one projection
for rows and totals, persisted associations, and unsaved-edit protection.

## Make each change reviewable

1. Select feature IDs from the ledger. Freeze the original behavior, native
   behavior, data ownership, unavailable states, and excluded work before coding.
2. Keep one writer per source worktree. Read shared views and callers before
   adding another implementation of the same projection or operation.
3. Implement the full bounded journey, including persistence and failure handling.
   Reuse existing F# views, C# orchestration, Rust transactions, and test drivers.
4. Run meaningful model checks and actual rendered input. For IO, inspect the real
   stored result and a negative case that would catch an incorrect implementation.
   Add private X11 or live-client acceptance only when the changed boundary requires it.
5. Update the parity row in the same change. Name what passed and what remains
   unverified. Keep run-specific logs, screenshots, hashes, and account data private.
6. Review the exact committed tree through the [PR workflow](pr-review.md).
   Passing fixtures do not satisfy an outstanding live acceptance gate.

## Keep the map honest

Add new user entry points to their feature file. Put implementation status only
in the ledger and executable commands in the existing verification recipes.
Link those sources instead of copying their content into summaries.

Use pstack's maintenance skill for a full verification audit. It requires source
readers and coordinator-driven runtime coverage for every mapped feature. A
source-only map expansion is not a completed maintenance pass. Unavailable account,
seasonal, compositor, or live-game routes stay explicitly untested.

## Remaining layout coverage

The native rendered driver's `FitsWindowBounds` helper in
`avalonia/Hdt.Checks/Program.cs` checks window bounds only. Existing callers do not
prove that a control is unclipped by ancestor scroll viewports. The metadata
journey additionally uses `InsideScrollViewport` to require its edit action and
both summaries inside the same actual viewport. Extend that ancestor check to
other scrolling journeys when their layouts change; retain pointer and pixel
evidence rather than interpreting window containment as full visibility.
