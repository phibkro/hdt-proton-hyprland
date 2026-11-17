# Configure and recover the tracker

Users change tracker behavior, preserve preferences across restarts, and recover
from failed updates or application errors.

## Sub-features

- `preferences-navigation`: main and tray Settings entry points, search, and Advanced pages.
- `preferences-persistence`: preserve valid changes and report failed writes.
- `preferences-customization`: appearance, language, hotkeys, and notifications.
- `lifecycle`: start, restore, minimize, quit, and select no-deck mode from the tray.
- `maintenance`: updates, backups, import preferences, and system options.
- `plugins`: discover and configure enabled plugins and plugin menu entries.
- `errors`: show actionable update failures, crash reports, and recovery state.

## How to get to it (user POV)

Open **Settings** from the main window or tray. Search or reveal Advanced pages.
Use tray controls, help, update notices, and plugin menus where available.

## Driving it with reference capture and native checks

Preconditions: isolated app data and original assemblies for original controls.
Do not update an installed tracker, overwrite backups, or send crash reports as
part of a navigation test.

- Run [reference capture](../../../../../docs/guides/ui-replay.md#capture-original-views).
  Enumerate the installed binary's actual Settings leaves and retain page types,
  advanced state, and scroll tiles. This establishes capture, not setting effects.
- Run the [native rendered journey](../native-avalonia.md#rendered-native-journey-and-faults).
  Toggle an Arena preference, reconstruct the window, and verify persistence.
  A failed write must restore the prior value and recover after storage repair.
- Repeatedly open and close original Settings using the WPF sequence. Require
  responsiveness and disappearance in captured pixels.
- Tray entry points, hotkeys, plugins, backup restore, updates, and crash-dialog
  recovery need dedicated acceptance. No native Options navigation proves them.

## Gotchas

Native Options has Arena draft, Native overlay, and Local files categories.
Those do not implement the original Settings tree. A screenshot of a crash
window does not prove that its controls respond or its report saves correctly.
