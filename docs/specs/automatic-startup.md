# Automatic HDT startup

2026-11-15. Operator requested HDT start automatically when Hearthstone opens.

Contract: in this Hyprland session, detect the exact configured Steam-class Hearthstone window and start the existing compatibility launcher. Reuse an already-open HDT. Recover failed owned launches with the bounded [retry policy](../reference/commands.md#automatic-startup). A successful launcher exit pauses recovery. Never launch, stop, focus, or change Hearthstone. Keep installer, prefix, and Proton selection in the existing launcher. One session-owned monitor is guarded by a runtime lock. Disable through the user service.

Implementation: existing Bash/jq/flock tools in devenv, a lightweight two-second client query, and a Home Manager user-service declaration in the canonical homelab source. No new TypeScript runtime or Effect exception. The watcher is desktop integration, not a scheduled agent automation. The generated live unit must derive from that same declaration; a full dirty homelab rebuild is unnecessary for activating this one service.

Acceptance: real current game and tracker identity checks; existing HDT is not duplicated; close only HDT and let the real monitor start the unchanged compatibility launcher; verify native CLR, fresh control bridge, overlay alignment and updater success. Repeated monitor startup cannot produce a second owner. New game launch after logout remains an operator-controlled future test. Preserve the running game throughout.

2026-11-16 revision: two recorded nonzero XInput exits exposed the watcher's lack
of recovery within one game process. The watcher now owns its launch child's exit
status and retries failures under the referenced policy. Existing trackers remain
under their original launcher. Offline checks run the actual watcher with launcher
stand-ins, real process identities, and simulated compositor state and time.
Those checks do not establish live Wine crash recovery or resolve the XInput error.
