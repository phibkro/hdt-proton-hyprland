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

2026-11-16: An additional portable-X11 input journey intermittently failed before
WPF received pointer hover: native readiness alone did not establish a usable
control. Retain an explicit control-hover assertion before the existing Click
assertion. One original-library run and one patched run passed the complete
journey, but other patched runs failed. A WPF synchronization experiment also
failed and was removed. The trigger and any relationship to library selection
remain unresolved; hold merge rather than retrying the gate into a pass.
