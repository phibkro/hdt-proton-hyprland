namespace Hdt.Avalonia

open System.Runtime.InteropServices
open Avalonia
open Avalonia.Controls
open Avalonia.Media

/// Shared native section geometry; no game or application state belongs here.
[<AbstractClass; Sealed>]
type TrackerLayout private () =
    static member val Surface: IBrush = SolidColorBrush(Color.Parse("#20262D"))
    static member val Line: IBrush = SolidColorBrush(Color.Parse("#394653"))
    static member val Accent: IBrush = SolidColorBrush(Color.Parse("#2489BD"))

    static member Text(text: string, [<Optional; DefaultParameterValue(13.0)>] size: float) =
        TextBlock(Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap)

    static member Section(title: string, body: Control, [<Optional; DefaultParameterValue(false)>] scroll: bool) =
        let dock = DockPanel()

        let header =
            Border(
                Background = TrackerLayout.Accent,
                Padding = Thickness(10.0, 6.0),
                Child = TrackerLayout.Text(title.ToUpperInvariant(), 13.0)
            )

        DockPanel.SetDock(header, Dock.Top)
        dock.Children.Add(header)

        let content: Control = if scroll then ScrollViewer(Content = body) else body

        dock.Children.Add(Border(Padding = Thickness(10.0), Child = content))

        Border(
            Background = TrackerLayout.Surface,
            BorderBrush = TrackerLayout.Line,
            BorderThickness = Thickness(1.0),
            Child = dock
        )
