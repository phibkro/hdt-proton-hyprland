# WPF → Avalonia migration review

The native client is an independently implemented preview, not a mechanically
converted WPF application. Its original-view reference is the pinned upstream
hierarchy described in [the layout spec](../specs/native-layout.md). No upstream
XAML, assets or style implementation is copied. F# and C# use the same Avalonia
API; changing language does not establish UI or platform parity.

Reviewed against the official [WPF migration guide](https://docs.avaloniaui.net/docs/migration/wpf)
on 2026-11-16, for this repository's pinned Avalonia version. This is a source
review map, not a certification that every WPF view has been migrated.

| Concern | Current implementation and evidence | Remaining work |
|---|---|---|
| Layout | `MainWindow`, `LibraryView` and `TrackerLayout` use native panels; Options uses independently authored AXAML with F# UI code-behind. Representative-size rendered tests check bounds and navigation. | Original-view fidelity is incomplete; scrolling and mixed-scale monitors need target-desktop acceptance. |
| Presentation state | Validated C# observations project into F# Arena and live-game display unions. Hero/card/unavailable shapes and missing advice remain distinct. Main and overlay share choice rendering. | Remaining C# views/state; full application behavior parity. |
| Binding/property system | Options paths use compiled AXAML bindings with explicit `SettingsPaths` data type and immutable `OneTime` mode. Other current views construct controls directly. Neither route uses WPF properties or trigger syntax. | Reusable templated controls need deliberate Avalonia property and binding-mode design. |
| Styling/templates | Native Fluent theme plus independently authored palette and sections. | HDT-equivalent templates, theme resources, hover/pressed/focus states and localization have not been faithfully ported. |
| Events/commands | Actual Avalonia control events drive navigation and preferences; rendered and X11 tests send pointer input. | Routed keyboard commands, tooltips and popup close/reopen/focus journeys need explicit coverage when those features migrate. |
| Visibility | Live deck/editor panels use `IsVisible` to remove layout participation. The F# live-game view clears hidden Battlegrounds deck rows and all unavailable card lists. Library zero-count cards use reduced opacity while retaining their rows. | Audit each future WPF Hidden/Collapsed mapping rather than substituting blindly. |
| Threading | IO uses bounded asynchronous workers. Main updates return to its dispatcher; each renderer process owns its dispatcher. Stop/recovery and responsiveness tests exercise these boundaries. | Original HDT acquisition still depends on its own dispatcher and Wine. |
| Window/input behavior | Rust owns an opaque, input-empty X11 panel receiving Avalonia-rendered pixels. Real private X11 tests cover input, focus and geometry. | Transparent selective interaction and other desktop/compositor combinations are not native feature parity. |
| DPI | Overlay specification explicitly describes pixel dimensions; rasterization uses one layout unit per output pixel at 96 DPI. | This contract is not proof of mixed-DPI or fractional-scale behavior. Add scale-transition tests before changing conversions. |
| Lifetime/error UI | Main and renderer have independent process ownership, unavailable notices, deadlines and cleanup tests. | Independent native game acquisition, live acceptance and longer sessions remain outstanding. |

## Applying the upstream guidance

- [Layout](https://docs.avaloniaui.net/docs/migration/wpf/layout): preserve the
  intended hierarchy, then check wrapping, scrolling and device-independent
  bounds on the target. Existing size tests are evidence for those sizes only.
- [Styling](https://docs.avaloniaui.net/docs/migration/wpf/styling): translate
  interaction states to Avalonia selectors/classes and use `ControlTheme` when
  replacing a control template. The current fixed palette is a preview, not a
  migrated WPF theme.
- [Events](https://docs.avaloniaui.net/docs/migration/wpf/events): use pointer
  events and explicit routing strategies for tunnelling. Do not assume WPF
  preview-event names or mouse-only behavior transfer unchanged.
- [Cheat sheet](https://docs.avaloniaui.net/docs/migration/wpf/cheat-sheet): review
  visibility, commands, dispatcher calls, resources and window assumptions per
  feature. It explicitly warns that window transparency does not supply WPF-like
  click-through; that remains a platform boundary here.

Each further migration spec must identify the applicable mappings, an original
view reference, observable behavior tests and unverified platform differences.
Keep wire formats and native-window protocols separate from control conversion.
The executable coverage index is the
[native verification recipe](../../.cursor/skills/verify-hdt-proton/features/native-avalonia.md).
