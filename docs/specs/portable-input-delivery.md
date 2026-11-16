# Deliver pointer input to portable overlay controls

## Goal

The private X11/Wine journey must deliver real pointer input to an HDT overlay
button, while empty space continues to reach the game fixture. Native focus is
not proof of WPF hover or Click delivery.

## Boundaries

Use the existing isolated replay, real HDT assemblies and Rust controller.
Keep game/account activity and the working installed Hyprland setup untouched.
Retain failing evidence. Do not retry CI into a pass, inject managed Click events,
or relax the six actual-click and empty-space assertions.

## Work and acceptance

First record the X11, Win32 and WPF pointer/target observations at the first
failed transition. Identify the cause before changing product behavior. A fix
must pass the real pointer journey with the original and corrected libXi, retain
the original failing artifact and pass existing input ownership/fault tests.
Missing prerequisites and unresolved intermittent failures block acceptance.

## Revisions

2026-11-16: Start from runtime integration b77883d. Earlier evidence includes
both complete passing runs and failures before WPF hover, with a failed
Mouse.Synchronize experiment removed. No library-selection causality is assumed.

2026-11-16: Read-only X11 and Win32 observations found core EnterNotify at the
real control position without WM_MOUSEMOVE or WPF hover. A later real one-pixel
movement delivered the missing WPF update. The next trace found repeated
WS_EX_TRANSPARENT changes (786440 ↔ 786472) in empty space: the compatibility
adapter and HDT's original interactivity loop were competing over click-through.

The candidate now leases the existing CanvasInfo entry in HDT's clickable list
without marking it as an exported control. This holds the Win32 surface receptive;
the native owner alone restricts input to exported control rectangles. The lease
and cached click-through setting are restored when ownership ends. Explicit
Unprepared/Prepared publication prevents admission before Win32 preparation;
asynchronous WPF activation is not used as an input-readiness gate.

Both the activation gate and an explicit WM_MOUSEMOVE refresh were tried and
removed. The current candidate injects no mouse messages, pointer movements or
clicks. The replay still supplies actual native pointer movements and clicks on
its private display. A read-only style-change counter now rejects competing
click-through owners during empty-space interaction. Removing the lease on clean
f8e09eb makes that assertion fail; original failed runs remain private evidence.

Private acceptance does not establish compositor-specific KDE/GNOME behavior,
Hearthstone state acquisition or long-session crash prevention. The libXi
request-buffer defect remains a separate fix, not the explanation for this
Win32/native ownership conflict.
