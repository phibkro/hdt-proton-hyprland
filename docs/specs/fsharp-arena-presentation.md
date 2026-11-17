# F# Arena presentation and WPF migration audit

## Goal

Move the shared hero/card/unsupported-choice visuals used by native Main and
overlay into F#, preserving their observable layouts and fact boundaries. Make
the WPF-to-Avalonia review explicit and source-linked for subsequent slices.

## Design and limits

Keep the validated C# wire contracts and acquisition unchanged. Project those
facts into an immutable F# display model; its discriminated union distinguishes
unavailable advice from hidden or supplied scores/synergies. Hero choices cannot
contain card advice. Both Main and overlay use one F# renderer through the
existing ArenaPresentation projection. Delete the superseded C# control builders.
Use existing Avalonia controls directly, without copied WPF code or assets.
The original hierarchy/provenance reference and topic-specific Avalonia sources
are linked from the [WPF migration review](../reference/wpf-migration.md).

The audit maps current source/evidence to Avalonia's WPF migration guide. It must
distinguish reviewed mappings, incomplete WPF fidelity, and untested platform
behavior. Documentation is not proof of popup, DPI or compositor parity.

## Acceptance

- Locked build and formatting pass, warnings as errors.
- Actual rendered journeys preserve hero/card replacement, unsupported/menu
  clearing, preference toggles and representative-size geometry.
- Main and overlay use the F# choice implementation; raw watcher cards never
  acquire unsupported ratings or synergies through this projection.
- Original watcher/scripted-provider acceptance drives actual event delivery
  through the adapter, file, Rust and native pixels; no direct cache setter
  substitutes for that route.
- Private X11 and independent-main/renderer failure/restart journeys pass.
- Live Hearthstone, account activity and installed runtime changes are excluded.

## Revisions

- 2026-11-16: Initial contract following operator request to audit against the
  official WPF migration guidance while continuing the F# port.
- 2026-11-16: Operator additionally requested current Avalonia development tools
  and an XPF comparison. Add an isolated Debug diagnostics host, project-local
  tool/editor/MCP setup and a sourced decision record. Ordinary app/test bundles
  must exclude diagnostics dependencies. Licensed activation remains an explicit
  prerequisite, not a skipped pass. Keep the selected native migration; do not
  start an XPF trial or buy a license as part of tooling setup.
