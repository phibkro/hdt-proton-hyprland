# HDT with Steam Proton on NixOS / Hyprland

Compatibility plugin and launcher for **HSReplay's Hearthstone Deck Tracker (HDT)** alongside Hearthstone launched through a Battle.net Steam shortcut. Tested with **HDT 1.60.1, Proton Experimental / Wine 11.0, Hyprland 0.56.2 and XWayland**. This is an unofficial compatibility project.

The current desktop profile passed operator checks for transparent rendering, preview clearing, move/resize, stacking, Alt-Tab and clickable overlay controls with empty-space click-through. Two later Arena match results, automatic redraft picks and ArenaSmith ratings were recorded. The [verification matrix](docs/COMPATIBILITY.md) preserves the remaining gaps, including historical session stalls and incomplete initial-draft coverage; general Linux stability is not established.

## Setup and launch

With the authentic HDT installation and native .NET 4.8 already in the game's prefix, configure `hdt.local.json` from `hdt.local.example.json`, start Battle.net through Steam, and initialize HDT once. Then:

```sh
devenv shell -- hdt-setup
devenv shell -- hdt-overlay-launch
```

Setup builds the compatibility plugin, normally closes HDT if necessary, backs up and applies its plugin/settings changes. It preserves the game and existing prefix. A full HDT/.NET bootstrap is not supplied. See [installation and recovery](docs/INSTALL.md) for prerequisites and the complete installed updater layout.

For the existing desktop startup service, generate a line to paste into the Battle.net shortcut's **Steam Launch Options**:

```sh
devenv shell -- hdt-steam-options
```

It starts the existing desktop watcher service and preserves Steam's original command, prefix and Proton selection. The machine-specific service is in [homelab PR #92](https://github.com/phibkro/homelab/pull/92); the wrapper requires that service separately. Actual Steam-client launch acceptance remains pending. [Installation details](docs/INSTALL.md#start-alongside-the-steam-shortcut) explain ownership, rollback and stable Proton comparison.

## Compatibility changes

| Symptom | Working mechanism |
|---|---|
| Same prefix but separate Wine server | Join the running Steam server's user/mount namespace and derive its actual Proton |
| Black WPF overlay | HDT-only software rendering and DXVK `d3d9.shaderModel = 1` |
| Hidden previews leave pixels behind | Overlay background at 1/255 black opacity; deliberate faint tint |
| Overlay does not follow the game | Plugin invokes HDT's geometry update when bounds differ |
| Overlay hides behind the game or flickers | Identity-checked X11 transient relationship; repeated raising disabled after verification |
| Controls fail or empty space blocks game input | Export marked clickable rectangles and gate compositor input over those controls only |

The plugin uses HDT's public extension interface; it does not patch the tracker executable or modify Hearthstone memory. Ordinary Power.log tracking, live HearthMirror draft detection and ArenaSmith account access are separate paths. Premium is required where HSReplay gates its ratings. Automatic deck detection does not mirror every menu deck click; choosing the intended deck in HDT resolved the reported stale list.

## Repository layout

```text
src/
  runtime/      configuration, launch, installation and status
  plugin/       HDT C# compatibility plugin
  hyprland/     compositor rules, input bridge and X11 stack helper
  diagnostics/  read-only Win32 and overlay-surface probes
scripts/        setup, Steam integration, watcher and checks
tests/          bounded tests
docs/           installation, development, verification and specs
```

Private app files, configuration, evidence and recovery data remain outside the published source tree. Root `autostart.sh` retains compatibility with existing service declarations. Public commands and dependencies are declared in `devenv.nix` and pinned by its lockfiles.

```sh
devenv shell -- hdt-check
devenv shell -- hdt-status
```

See [development, diagnostics and prior art](docs/DEVELOPMENT.md). MIT covers original repository source only; HDT, Hearthstone, .NET and their assets retain their respective terms. No third-party binaries or account data are distributed.
