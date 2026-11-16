# HDT Proton verification map

Read the parent skill first. The map covers the native integration, not every
feature of upstream HDT. Source review and runtime evidence are separate columns
in private run notes. A passed isolated path never upgrades an unavailable live
path to passed.

- [Launch and setup](launch-setup.md): configuration, packaging, same-session launch, plugin/settings installation, desktop/service entry points.
- [Overlay and UI](overlay-ui.md): transient relationships, pixel sampling, isolated WPF transitions and live input/geometry.
- [Native Avalonia preview](native-avalonia.md): bounded Rust snapshot IO, native replay controls, status freshness/recovery and local preferences; separate from installed HDT acceptance.
- [Freeze evidence](freeze-evidence.md): independent byte capture, recorder continuity and trace export.

Initialize a fresh evidence directory and doctor each CLI session. Only the
coordinator drives apps. Full native X11/WPF runs need measured resource headroom.
A live Hearthstone session is optional for isolated tests and mandatory for the
live game routes. Never enter Arena, pick cards or start a match on the user's
behalf. Cleanup retains proof and touches only run-owned processes.
