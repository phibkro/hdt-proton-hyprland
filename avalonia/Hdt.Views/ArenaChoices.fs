namespace Hdt.Avalonia

open Avalonia
open Avalonia.Controls
open Avalonia.Media

/// Source indices are opaque identities, never inferred screen positions.
[<RequireQualifiedAccess>]
type ArenaSource =
    | Unspecified
    | ActorIndex of int

/// Unavailable advice cannot carry a stale score or synergy count.
[<RequireQualifiedAccess>]
type ArenaAdvice =
    | Unavailable
    | Hidden
    | ScoreOnly of score: string * plaque: int
    | SynergiesOnly of count: int
    | ScoreAndSynergies of score: string * plaque: int * count: int

type ArenaHeroDisplay =
    {
        Id: string
        Name: string
        ClassLabel: string
        Source: ArenaSource
    }

type ArenaCardDisplay =
    {
        Id: string
        Name: string
        Mana: int
        Source: ArenaSource
        Advice: ArenaAdvice
        Underground: bool
    }

[<RequireQualifiedAccess>]
type ArenaChoiceDisplay =
    | Hero of ArenaHeroDisplay
    | Card of ArenaCardDisplay
    | Unavailable of kind: string * reason: string

/// Main and the raster overlay share this renderer; neither view acquires facts.
[<AbstractClass; Sealed>]
type ArenaChoiceViews private () =
    static member Message(value: string, size: float) =
        TextBlock(Text = value, FontSize = size, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap)

    static member private SourceLabel(source: ArenaSource) =
        match source with
        | ArenaSource.Unspecified -> "Source position not supplied"
        | ArenaSource.ActorIndex index -> $"Source actor index {index} • position unknown"

    static member private Card(card: ArenaCardDisplay, size: float) =
        let lines = StackPanel(Spacing = 6.0)

        let add text fontSize =
            lines.Children.Add(ArenaChoiceViews.Message(text, fontSize))

        add $"{card.Mana} mana   {card.Name}" size
        add card.Id 12.0

        match card.Source with
        | ArenaSource.Unspecified -> ()
        | ArenaSource.ActorIndex _ -> add (ArenaChoiceViews.SourceLabel(card.Source)) 11.0

        let scoreLine score plaque =
            add $"Arenasmith {score} • plaque {plaque}" (size - 3.0)

        let synergyLine count = add $"Deck synergies: {count}" 14.0

        match card.Advice with
        | ArenaAdvice.Unavailable -> add "Ratings and deck synergies unavailable • no matching source generation" 13.0
        | ArenaAdvice.Hidden -> ()
        | ArenaAdvice.ScoreOnly(score, plaque) -> scoreLine score plaque
        | ArenaAdvice.SynergiesOnly count -> synergyLine count
        | ArenaAdvice.ScoreAndSynergies(score, plaque, count) ->
            scoreLine score plaque
            synergyLine count

        Border(
            Padding = Thickness(if size < 20.0 then 10.0 else 16.0),
            CornerRadius = CornerRadius(6.0),
            BorderThickness = Thickness(2.0),
            BorderBrush = SolidColorBrush(Color.Parse(if card.Underground then "#932020" else "#067F93")),
            Child = lines
        )

    static member Render(choice: ArenaChoiceDisplay, size: float) : Control =
        match choice with
        | ArenaChoiceDisplay.Hero hero ->
            let lines = StackPanel(Spacing = 6.0)
            lines.Children.Add(ArenaChoiceViews.Message(hero.ClassLabel, size - 3.0))
            lines.Children.Add(ArenaChoiceViews.Message(hero.Id, 12.0))
            lines.Children.Add(ArenaChoiceViews.Message(ArenaChoiceViews.SourceLabel(hero.Source), 11.0))
            lines.Children.Add(ArenaChoiceViews.Message("Hero statistics unavailable", 13.0))
            TrackerLayout.Section(hero.Name, lines)
        | ArenaChoiceDisplay.Card card -> ArenaChoiceViews.Card(card, size)
        | ArenaChoiceDisplay.Unavailable(kind, reason) ->
            ArenaChoiceViews.Message($"{kind} choices unavailable • {reason}", 14.0)
