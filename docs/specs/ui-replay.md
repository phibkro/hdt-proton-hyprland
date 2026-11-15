# Replay HDT window transitions in isolation

2026-11-15. The operator selected an isolated HDT UI replay harness before a separate real-client environment.

## Goal

Repeat the reported sequence without operating the live game: main menu, Arena lobby, first offer, next offer, then departure from drafting. Exercise Settings, New, popups, focus, and resize around those transitions. Use installed HDT WPF components and the selected Proton version. Keep synthetic draft fixtures distinct from HearthMirror integration evidence.

## Constraints

- All persistent runtime files, copied Windows components, screenshots, and logs stay under the project and remain private.
- Use a separate Wine prefix, display, home directory, and process group. Block network access. Do not import HDT account data or run Battle.net or Hearthstone.
- Automated input targets only the owned display. Reject the live display and existing prefixes.
- Preserve the real game and tracker. Never terminate their wineserver or operate their windows.
- Declare host dependencies in devenv. Reuse HDT assemblies and WPF components rather than drawing a lookalike.
- Respect installed component licenses. Do not commit Microsoft, HDT, or game binaries.

## First implementation

Evaluate the existing REA native UI workflow first. Its native desktop scenario workflow requires macOS, so it cannot drive this Linux environment. Reuse Xvfb and a window manager for a separate X11 desktop, X11 input tools for owned-window interaction, and Bubblewrap for network and desktop isolation. This first renderer test does not reproduce Hyprland composition. A nested Hyprland backend remains a distinct extension if the initial replay cannot reproduce the problem.

Use the real HDT assembly for the main window and Arena control where initialization permits. Fail explicitly if a required component cannot initialize. Do not count a substitute WPF window as coverage of the HDT main window.

## Acceptance

1. One prepare command creates private owned runtime data without modifying the source installation or prefix.
2. One bounded run starts the isolated desktop and real WPF components, applies the fixture transitions, and checks the resulting state.
3. Native input activates at least one real HDT control. Capture before and after state and screenshots.
4. A failed assertion produces a nonzero exit code and retained evidence. A known injected assertion failure verifies this path.
5. Run cleanup terminates only owned processes. Live game and tracker process identities remain unchanged.
6. A second run starts from clean fixture state and produces the same semantic results.

## Evidence limits

Fixture injection verifies the boundary after the injected state. It does not verify live HearthMirror reads, ArenaSmith requests, account entitlement, picks in Hearthstone, or Hyprland-specific behavior. Those remain separate live acceptance tests.

## Implemented boundary

The host loads HDT in a secondary AppDomain with HDT's configuration and WPF resource assembly. It removes HDT's normal startup handler and runs the normal WPF message pump. Synthetic scene and card-model changes exercise the real Arena visibility and binding paths. Native X11 input tests Settings in each phase. The New check executes HDT's command directly and observes its actual dialog after return to the menu. The popup check uses a generic WPF popup.

The seed preserves Wine's initialization marker so a fresh clone does not rerun prefix initialization against the stripped profile. The harness uses a private copy of the selected Steam runtime because its launcher needs writable runtime bookkeeping. Generic `steam-run` alone did not supply the Proton library layout in the first experiment.

The live comparison distinguishes menus, Standard matches, Battlegrounds matches, Arena matches, and successive Arena draft decisions. A passing synthetic draft transition cannot establish progress through the live watcher and ratings paths.
