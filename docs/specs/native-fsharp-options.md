# F# native Options page

## Goal and boundary

Continue the native Avalonia migration in F#, beginning with the complete Options
page and shared section layout. Preserve category navigation, controls, geometry,
text and independent main/overlay ownership. This checkpoint does not convert the
whole client, alter serialization, change live HDT, or add game/account access.

## Design

Use the existing pinned .NET SDK and Avalonia APIs directly. A separate F# views
library depends only on Avalonia; the C# composition root supplies actual Arena
and overlay controls and explicit display paths. It must not reference the C#
application, avoiding a circular dependency. Remove superseded C# views rather
than maintaining parallel implementations. Keep .NET warnings as errors and
lock transitive dependencies, including FSharp.Core.

Prior art: [Avalonia code-only UI](https://docs.avaloniaui.net/docs/fundamentals/coded-ui)
supports direct F# control construction. A separate FuncUI architecture is not
needed to preserve these existing control trees. Reuse the existing SDK project
pattern and rendered acceptance driver rather than adding a second UI harness.

## Acceptance

- A locked restore and mixed-language build succeed with warnings as errors.
- Existing rendered journeys exercise Arena, Native overlay and Local files
  categories at both supported test sizes; controls remain usable.
- Runtime inspection establishes the Options page and layout implementation
  actually come from the F# library, with no duplicate C# implementation.
- Existing private X11 pointer, overlay geometry, focus, stale-state and cleanup
  journey passes against the mixed application. This is isolated UI evidence,
  not live Hearthstone acceptance.

## Revisions

- 2026-11-16: Initial bounded slice; full F# application migration remains later
  work after preserving an executable baseline.
