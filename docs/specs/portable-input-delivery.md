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
