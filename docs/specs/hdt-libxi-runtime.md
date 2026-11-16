# Select the corrected libXi for HDT

## Goal

Run HDT with the corrected `XOpenDevice` implementation while preserving the
running game and shared Steam installation. Keep selection explicit and reversible.

## Constraints

- Build from the existing pinned libXi source and patch, retaining its notices.
- Select a single absolute x86-64 shared library only for the HDT launch.
- Preserve unrelated inherited preloads; reject conflicting libXi selection.
- Do not replace shared libraries, edit the game prefix, or restart Hearthstone.
- Keep replay prefixes, account data and process captures outside published source.

## Acceptance

1. Invalid library selections fail before the HDT launch environment changes.
2. A clean committed private Wine/WPF replay loads the selected library, saves
   its process mappings and exercises real HDT window and Arena transitions.
3. The packaged launcher passes its build checks.
4. A live HDT-only trial records the selected library in HDT's mappings and
   preserves Hearthstone's process identity and library mappings.
5. Document rollback and distinguish successful loading from evidence that
   intermittent crashes have stopped.

## Revisions

2026-11-16: The existing libXi regression establishes the library defect. This
extension tests runtime selection separately. The Steam bundle removes its Nix
RUNPATH and lets the existing process environment supply dependencies. The
private replay confirms actual loading; it does not establish long-session
desktop stability or protect against genuine device disappearance.

2026-11-16: The additional portable-X11 journey initially failed before WPF
received pointer hover. The subsequent [input-ownership investigation](portable-input-delivery.md)
identified competing Win32 click-through decisions and Wine's stale cursor
cache outside its windows. [PR21](https://github.com/phibkro/hdt-proton-hyprland/pull/21)
fixes those boundaries and retains the negative
control and explicit hover/Click assertions. Both original and corrected libXi
passed that corrected private journey. This supersedes the earlier unexplained
portable-hover blocker; it does not establish compositor or long-session desktop
acceptance, nor make libXi the cause of those pointer-delivery failures.
