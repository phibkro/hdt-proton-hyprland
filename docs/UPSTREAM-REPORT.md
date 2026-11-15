# Arena score particles fail under Proton

This is a report draft. It has not been submitted upstream.

## Expected behavior

ArenaSmith scores update after each draft pick. HDT's main window remains responsive while the draft overlay is visible.

## Actual behavior

Level-five Arena score badges can trigger `COMException 0x88980406` under Proton. The overlay retains the previous offer's ratings, and main-window controls stop responding. Main and crash-report windows can become white. The crash report may remain unreadable.

The failure can appear after several successful decisions. In the isolated reproduction, card models continued to change while the displayed offer stayed stale. A fresh UI heartbeat alone did not establish that rendering worked.

## Environment

- HDT 1.60.1, tracker assembly 1.60.1.9028, targeting .NET Framework 4.7.2.
- Native .NET Framework 4.8 in the same Steam prefix as Hearthstone.
- Proton Experimental build `experimental-11.0-20261112-x86_64`.
- NixOS with Hyprland and XWayland for the live session.
- Native Direct3D 9 through DXVK, with the HDT-only `d3d9.shaderModel = 1` setting.
- Separate replay environment with Xvfb, Openbox, and Vulkan llvmpipe.

## Reproduction

Enable both **Enable Arenasmith** and **Show Arenasmith Scores**. Enter an Arena draft with an eligible Premium account. Continue making picks until a level-five badge appears. Observe offer updates and try opening HDT Settings.

The [UI replay procedure](UI-REPLAY.md) also reproduces the failure using the original HDT assembly and synthetic card models. It requires a local HDT installation. The repository does not distribute HDT or Microsoft binaries.

Run the negative comparison in the isolated environment:

```sh
devenv shell -- hdt-replay run --plaque-level 5
```

Run the same scenario with the compatibility plugin:

```sh
devenv shell -- hdt-replay run --with-plugin --plaque-level 5
```

The unpatched comparison failed with `0x88980406`. The patched comparison passed 12 decisions, Settings interactions, resize, popup cleanup, and the New deck dialog. The replay bypasses HearthMirror and ArenaSmith network requests. It tests HDT's actual WPF controls and rendering.

## Isolated component and workaround

The controlled comparison isolates initialization and rendering of `Arena.ParticleEmitter`, used by level-five score badges. The emitter creates WPF 3D particles and rendered textures.

[Compatibility plugin 0.5.0](https://github.com/phibkro/hdt-proton-hyprland/pull/5) intercepts the emitter's Loaded event before its initialization handler runs. It suppresses the cosmetic particles while preserving ratings, scores, and badge artwork.

After installation in the live session, the operator confirmed repeated offer updates, responsive main-window controls, and successful completion of the current draft. A fresh draft from initial hero selection and longer-session reliability remain untested.

This evidence does not identify the failing Wine, DXVK, or graphics-driver function. The WPF render exception is an observation point. The workaround does not repair every possible blank crash-report window.

An upstream option to disable these cosmetic particles would provide a compatibility path without changing ArenaSmith data. Native Windows behavior has not been compared in this investigation.

## Supporting evidence

The [compatibility record](COMPATIBILITY.md) separates replay results from live observations. The [rendering explanation](EXPLANATION.md) describes the Linux compatibility changes. Raw logs remain private because they contain account and game data.
