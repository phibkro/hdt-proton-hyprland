# HDT runtime integration on main

## Goal and boundary

Integrate the existing HDT-only corrected libXi selection and Hyprland controller
namespace repair on current main without requiring the deferred KDE/GNOME doctor
or independent debugger-capture commands. Reuse the reviewed runtime source from
PR20 and scoped helper source from PR32. Native Avalonia remains separate.

## Invariants

- Runtime library selection is explicit, HDT-only and reversible. Invalid
  path/header selections and conflicting preloads fail before launch; unrelated
  environment is preserved. Header checks do not establish full loader/ABI validity.
- The host controller keeps Hyprland IPC. Only X11 apply/restore helpers enter
  the selected matching-prefix Steam wineserver namespace, revalidating PID,
  Linux start identity and existing Proton/session guards before every call.
- Display and optional authority paths come from that selected session for those
  helpers only. No cookie reads, shared socket edits, game restart or prefix edits.
- Only an absent input-region file is normal waiting. Missing commands, invalid
  input and dead/replaced sessions remain visible errors.
- The isolated regression and runtime bundle derive from one patched libXi source.
- Preserve current main's replay preparation and deterministic regression fixes.
  Do not import the older debugger capture or generic desktop input changes.

## Acceptance and evidence

Use normal changed-file hooks, full Rust tests/Clippy, C/shell/TS quality checks,
packaged launcher and corrected-library builds. Test namespace construction,
identity rejection, display/auth overrides, library validation and unrelated
preload preservation. The synthetic environment projection does not prove the
original Command's ambient inheritance; report that boundary accurately.

Drive the actual isolated Wine/WPF replay with the corrected library mapped and
real controls/transitions. Historical PR20/PR32 live acceptance is evidence for
those exact source checkpoints; this newly combined main-based source needs its
own final live Settings/controller/input check before merging. Do not claim a
long-session crash-prevention result from a build, fixture or fresh heartbeat.
Hearthstone entry, picks and matches remain operator-controlled.

## Cleanup and rollback

All test prefixes/displays belong to their bounded replay. Preserve evidence,
reap exact owned children and leave installed services untouched during isolated
checks. A future live trial must retain the previous service launcher/library
selection and back up changed files before replacing only HDT's candidate.
