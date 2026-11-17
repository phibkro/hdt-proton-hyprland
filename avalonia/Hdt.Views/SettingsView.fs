namespace Hdt.Avalonia

open Avalonia
open Avalonia.Controls
open Avalonia.Layout

/// Display paths are supplied by the composition root, never inferred by a view.
type SettingsPaths =
    {
        Preferences: string
        Library: string
        Observations: string
    }

[<Sealed>]
type SettingsView(arena: Control, overlay: Control, paths: SettingsPaths) as this =
    inherit UserControl()

    let category label =
        Button(Content = label, HorizontalAlignment = HorizontalAlignment.Stretch)

    let arenaCategory = category "Arena draft"
    let overlayCategory = category "Native overlay"
    let storageCategory = category "Local files"
    let detailPanel = ContentControl()
    let categories = [ arenaCategory; overlayCategory; storageCategory ]

    let categoryPanel =
        let items = StackPanel(Spacing = 6.0)

        items.Children.AddRange(
            [
                TrackerLayout.Text("OVERLAY", 12.0)
                arenaCategory
                overlayCategory
                Border(Height = 12.0)
                TrackerLayout.Text("TRACKER", 12.0)
                storageCategory
            ]
        )

        Border(
            Background = TrackerLayout.Surface,
            BorderBrush = TrackerLayout.Line,
            BorderThickness = Thickness(1.0),
            Padding = Thickness(8.0),
            Child = items
        )

    do
        let storage = StackPanel(Spacing = 12.0)

        [
            "These files belong to this native client. Their paths are supplied when it starts; no Windows HDT database is imported."
            "Preferences"
            paths.Preferences
            "Deck library and supplied match history"
            paths.Library
            "Read-only game observations"
            paths.Observations
        ]
        |> List.iter (fun text -> storage.Children.Add(TrackerLayout.Text(text)))

        let arenaPane = TrackerLayout.Section("Overlay / Arena draft", arena, true)
        let overlayPane = TrackerLayout.Section("Overlay / Native tracking", overlay, true)
        let storagePane = TrackerLayout.Section("Tracker / Local files", storage, true)

        let show (selected: Button) (content: Control) =
            for button in categories do
                button.Background <-
                    if button = selected then
                        TrackerLayout.Accent
                    else
                        TrackerLayout.Surface

            detailPanel.Content <- content

        arenaCategory.Click.Add(fun _ -> show arenaCategory arenaPane)
        overlayCategory.Click.Add(fun _ -> show overlayCategory overlayPane)
        storageCategory.Click.Add(fun _ -> show storageCategory storagePane)

        let columns =
            Grid(ColumnDefinitions = ColumnDefinitions("170,*"), ColumnSpacing = 12.0, Margin = Thickness(12.0))

        columns.Children.Add(categoryPanel)
        columns.Children.Add(detailPanel)
        Grid.SetColumn(detailPanel, 1)
        this.Content <- columns
        show arenaCategory arenaPane

    member _.ArenaCategory = arenaCategory
    member _.OverlayCategory = overlayCategory
    member _.StorageCategory = storageCategory
    member _.CategoryPanel = categoryPanel
    member _.DetailPanel = detailPanel
