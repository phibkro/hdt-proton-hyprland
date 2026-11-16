# Native Avalonia or XPF?

Problem: preserve recognizable HDT behavior on Linux while keeping its UI
responsive, inspectable and isolated from game acquisition failures. Existing
Wine compatibility remains useful while native feature parity grows.

| Approach | What it preserves | Principal cost or unresolved condition |
|---|---|---|
| Patched WPF under Proton | Existing application features and integrations | Wine/rendering/input boundary remains; platform regressions still need diagnosis |
| XPF rendering replacement | Existing WPF views and controls after a supported application retarget | Commercial application license, original application build/retarget and platform integration validation |
| Native Avalonia/F# | Explicit native view/state boundaries and existing Rust process/window ownership | Remaining original views, services and behavior must be migrated and tested |

Continue the already selected native Avalonia migration. This is a recommendation
for this project, not a claim that XPF is generally inferior. XPF is attractive
when preserving the original WPF code is the primary goal and the owner can
build and maintain that application on supported .NET with a suitable license.

The inspected HDT build targets .NET Framework 4.7.2. The
[current XPF 2 setup guide](https://docs.avaloniaui.net/xpf/getting-started)
requires an SDK-style project and .NET 10 or newer; its older 1.x line requires
at least .NET 6. It is not an installer option that makes this Framework binary
native on Linux. The [XPF overview](https://docs.avaloniaui.net/xpf) describes
commercial application licensing and a hybrid path that retains WPF controls
while introducing Avalonia views. Tooling licenses and XPF licenses are distinct.

For HDT specifically, rendering replacement is only one part of the problem.
HearthMirror, Win32 game identity, Proton memory/process access and overlay input
ownership are separate integration boundaries. The [XPF FAQ](https://docs.avaloniaui.net/xpf/faq)
documents Win32 shims, but we have not tested that they preserve these HDT/game
integrations. Do not infer that XPF alone fixes them. Retargeting original HDT
also requires checking the applicable source/dependency reuse terms; our current
native UI is independently implemented rather than copied upstream XAML.

Reconsider XPF after an isolated proof demonstrates original HDT startup,
Arena choices across picks, authenticated ratings, match-end transitions and
overlay focus/input with the real Proton game, plus acceptable deployment terms.
No XPF account, trial, package feed or license is configured by this decision.
