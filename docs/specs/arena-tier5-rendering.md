# Restore Arena tier-five particles safely

Investigation: [issue 8](https://github.com/phibkro/hdt-proton-hyprland/issues/8).
Branch: `investigate/arena-tier5-rendering`.

## Goal

ArenaSmith scores continue updating, HDT remains responsive, and cosmetic effects
render correctly across draft transitions. Keep the existing particle suppression
enabled until a candidate passes the isolated and live checks below.

## Evidence and limits

The [report draft](../UPSTREAM-REPORT.md) records the original HDT control replay:
tier five failed without suppression, tier four passed, and suppressed tier five
passed 12 offers with UI interactions. A live draft completed with suppression.
The replay bypasses HearthMirror and ArenaSmith network requests.

The inspected C# source at upstream commit
`2698623dc0e91491671a614ae176f9db68831aa7` creates a 32-pixel ellipse texture using
WPF `RenderTargetBitmap`, freezes it, wraps it in a diffuse material, and updates
particle mesh collections on a dispatcher timer. Exact correspondence between
that source commit and the shipped assembly remains unverified.

The observed `COMException 0x88980406` does not identify the faulty Wine, Proton,
DXVK, driver, or application function. There is no native Windows comparison.
This issue is separate from fatal XInput mapping exits and submenu slowdown.

## Experiments

1. Reduce the original effect in an isolated WPF process: texture rendering;
   frozen image brush and material; static 3D mesh; mesh updates; timer lifetime;
   repeated loading and unloading. Retain an original-control negative comparison.
2. Record the first exception, command acknowledgements, surface output, and
   native stacks. A UI heartbeat alone cannot prove that rendering advances.
3. Run the same reproduction on native Windows and pinned Proton environments
   before assigning the defect to a compatibility layer.
4. If a valid WPF operation is implemented incorrectly, build a minimal fix
   against the matching Wine/graphics source and retest the real boundary.
5. If appropriate, test an HDT-side 2D cosmetic replacement or an explicit
   upstream effect setting. Preserve score/data controls and document visual changes.

## Acceptance before enabling an effect

- Repeated high-tier offers and enter/leave-draft cycles render fresh scores
  without exceptions, stale surfaces, or unresponsive main-window controls.
- Settings/New, overlay input, click-through, resizing, and popup cleanup work.
- An affected unpatched build still fails the negative comparison.
- A live draft passes when the operator chooses to play. No automated entry or picks.
- Results identify tested HDT/runtime builds and distinguish isolated tests,
  native Windows results, and live observations.
- No HDT, Microsoft, account, or private log files enter the repository.

## Revisions

- 2026-11-16: Initial investigation contract. No renderer fix is implemented on
  this branch; production suppression remains unchanged.
