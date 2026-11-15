# Install HDT integration with Nix

2026-11-15. Status: frozen for review.

## Goal

Install the compatibility tools with a Nix flake. Register Hearthstone Deck Tracker in the application launcher opened by Super+Space. Opening the app focuses the existing tracker. A separate action restarts the tracker without restarting Hearthstone.

## Constraints

Keep the existing game prefix, HDT installation, account data, and Experimental selection. The package contains original source and native helpers, not HDT or .NET binaries. Mutable configuration and evidence remain outside the Nix store. The existing checkout remains usable through devenv. Nix packaging derives nixpkgs from the existing devenv lock rather than maintaining a second dependency pin.

Use the existing systemd watcher for tracker process ownership. A desktop action cannot start another unmanaged tracker. Restart refuses if a game process belongs to the tracker service. Launching without Hearthstone starts the watcher and reports that the game must be opened through Steam. No game launch or forced game termination occurs.

## Acceptance

- `nix build` produces runnable tools and a validated desktop entry with a Restart tracker action.
- Source paths and mutable data paths have separate definitions. The package uses an explicit mutable directory. Existing checkout defaults remain compatible.
- Native helpers build through Nix. Installed launches do not need a runtime C compiler or devenv.
- A Home Manager module declares the package, mutable directory and startup service. Account data and the game prefix never enter a derivation. The local existing configuration is reused without being copied into the store.
- Vicinae lists the registered app. Launching the registered app targets HDT's exact window. Existing HDT is reused. The game process remains unchanged.
- Checks cover source/data separation, command behavior, native compilation, desktop validation and Nix evaluation/build. Results distinguish real desktop checks from controlled subprocess tests.
- Documentation uses the pinned technical-writing and unslop skills. Existing verification limits remain explicit.

## Revisions

- 2026-11-15: Initial contract includes both application registration and the operator's subsequent Nix flake request.

- 2026-11-15: The Home Manager module leaves the private JSON configuration under operator control. The package and module share one desktop-entry template. A user-profile installation can require a user-directory desktop symlink before Vicinae refreshes its application list.

## Verification

- Nix built the package and both native helpers. The package and generated Home Manager desktop entries passed desktop-file-validate.
- Nix flake checks passed 17 tests with 50 assertions and shellcheck. Source/data tests reject store-backed state and verify an external configuration. Desktop tests cover an owned game, separate service, absent game, and ambiguous tracker windows.
- The Home Manager module evaluated with no failed assertions. Its generated unit and desktop entry were built and used for the focused live activation. The canonical homelab declaration pins the published source commit. No full machine rebuild was run.
- The installed package appeared in Vicinae. A real Vicinae app launch focused the existing HDT window without a duplicate. The game PID was unchanged.
- The packaged namespace launcher compiled the plugin against the real installed HDT and native .NET assemblies. Steam's inherited preload warnings did not prevent compilation.
- The live unit switched from devenv to the packaged watcher. Its restart command reopened HDT, loaded native CLR, and produced advancing UI/control snapshots. Overlay and game origins matched, and the game process remained unchanged. The desktop action's command was tested; the Vicinae menu item itself was not clicked.
- Current account data, installation, and evidence stay in the existing project. The default installed data path is a local symlink to that directory. Before-switch unit and homelab source copies remain in private backups.
- Fresh login, a fresh game transition, complete HDT/.NET bootstrap, stable Proton, and additional visual configurations remain untested.
