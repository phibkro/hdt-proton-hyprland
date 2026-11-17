namespace Hdt.Avalonia

open Avalonia.Controls
open Avalonia.Markup.Xaml

/// Display paths are supplied by the composition root, never inferred by a view.
type SettingsPaths =
    {
        Preferences: string
        Library: string
        Observations: string
    }

/// Parameterless XAML root permits tooling to load the layout without inventing IO inputs.
type SettingsLayout() as this =
    inherit UserControl()
    do AvaloniaXamlLoader.Load(this)

    member internal this.Required<'T when 'T :> Control>(name: string) =
        match this.FindControl<'T>(name) with
        | null -> invalidOp $"Settings XAML is missing the required control {name}."
        | control -> control

[<Sealed>]
type SettingsView(arena: Control, overlay: Control, paths: SettingsPaths) as this =
    inherit SettingsLayout()

    let arenaCategory = this.Required<Button>("ArenaCategory")
    let overlayCategory = this.Required<Button>("OverlayCategory")
    let storageCategory = this.Required<Button>("StorageCategory")
    let categoryPanel = this.Required<Border>("CategoryPanel")
    let detailPanel = this.Required<ContentControl>("DetailPanel")
    let arenaPane = this.Required<Border>("ArenaPane")
    let overlayPane = this.Required<Border>("OverlayPane")
    let storagePane = this.Required<Border>("StoragePane")
    let categories = [ arenaCategory; overlayCategory; storageCategory ]
    let panes = [ arenaPane; overlayPane; storagePane ]

    do
        this.DataContext <- paths
        this.Required<ContentControl>("ArenaSlot").Content <- arena
        this.Required<ContentControl>("OverlaySlot").Content <- overlay

        let show (selected: Button) (content: Border) =
            for button in categories do
                button.Background <-
                    if button = selected then
                        TrackerLayout.Accent
                    else
                        TrackerLayout.Surface

            for pane in panes do
                pane.IsVisible <- (pane = content)

        arenaCategory.Click.Add(fun _ -> show arenaCategory arenaPane)
        overlayCategory.Click.Add(fun _ -> show overlayCategory overlayPane)
        storageCategory.Click.Add(fun _ -> show storageCategory storagePane)
        show arenaCategory arenaPane

    member _.ArenaCategory = arenaCategory
    member _.OverlayCategory = overlayCategory
    member _.StorageCategory = storageCategory
    member _.CategoryPanel = categoryPanel
    member _.DetailPanel = detailPanel
