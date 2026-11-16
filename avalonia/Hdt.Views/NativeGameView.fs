namespace Hdt.Avalonia

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Media

[<RequireQualifiedAccess>]
type GameValue =
    | Unknown
    | Known of int

    override this.ToString() =
        match this with
        | GameValue.Unknown -> "unknown"
        | GameValue.Known value -> string value

type GameCardDisplay =
    {
        Name: string
        Mana: int
        Count: int
        Health: GameValue
    }

    override this.ToString() =
        let health =
            match this.Health with
            | GameValue.Unknown -> ""
            | GameValue.Known value -> $" • {value} health"

        $"{this.Mana}   {this.Name} ×{this.Count}{health}"

type GameSideDisplay =
    {
        Health: GameValue
        HandCount: GameValue
        DeckCount: GameValue
    }

[<RequireQualifiedAccess>]
type GameDeckDisplay =
    | Hidden
    | Cards of GameCardDisplay list

type GameFactsDisplay =
    {
        ModeLabel: string
        Player: GameSideDisplay
        Opponent: GameSideDisplay
        Deck: GameDeckDisplay
        KnownHand: GameCardDisplay list
        PlayerBoard: GameCardDisplay list
        OpponentBoard: GameCardDisplay list
    }

[<RequireQualifiedAccess>]
type GameDisplay =
    | Unavailable of reason: string
    | Observed of GameFactsDisplay

/// A display-only view: no source polling, mode inference or account access.
[<Sealed>]
type NativeGameView() as this =
    inherit UserControl()

    let mode = TextBlock(FontSize = 25.0, TextWrapping = TextWrapping.Wrap)
    let counts = TextBlock(TextWrapping = TextWrapping.Wrap)

    let cards height =
        ListBox(
            MaxHeight = height,
            ItemTemplate =
                FuncDataTemplate<GameCardDisplay>(fun card _ ->
                    // Avalonia clears recycled containers with a null item.
                    if isNull (box card) then
                        null
                    else
                        TrackerLayout.Text(card.ToString()))
        )

    let remaining = cards 450.0
    let hand = cards 180.0
    let player = cards 220.0
    let opponent = cards 220.0
    let deck = TrackerLayout.Section("Observed remaining deck", remaining)

    let columns =
        Grid(ColumnDefinitions = ColumnDefinitions("*,*,*"), ColumnSpacing = 16.0)

    let assign (target: ListBox) (cards: GameCardDisplay list) = target.ItemsSource <- cards

    do
        let yourCards = StackPanel(Spacing = 12.0)
        yourCards.Children.Add(TrackerLayout.Section("Your known hand", hand))
        yourCards.Children.Add(TrackerLayout.Section("Your public board", player))
        let theirCards = StackPanel(Spacing = 12.0)
        theirCards.Children.Add(TrackerLayout.Section("Opponent public board", opponent))

        theirCards.Children.Add(
            TrackerLayout.Text(
                "Opponent hand and secret card IDs are never exported. Combat odds, recommendations and authenticated services are unavailable."
            )
        )

        columns.Children.Add(deck)
        columns.Children.Add(yourCards)
        columns.Children.Add(theirCards)
        Grid.SetColumn(yourCards, 1)
        Grid.SetColumn(theirCards, 2)
        let body = StackPanel(Margin = Thickness(24.0), Spacing = 18.0)
        body.Children.Add(mode)
        body.Children.Add(counts)
        body.Children.Add(columns)
        this.Content <- ScrollViewer(Content = body)
        this.Unavailable("No native observation source configured.")

    [<System.Diagnostics.CodeAnalysis.NotNull>]
    member _.ModeText: string =
        match mode.Text with
        | null -> ""
        | text -> text

    member _.CardCount =
        remaining.ItemCount + hand.ItemCount + player.ItemCount + opponent.ItemCount

    member _.Apply(display: GameDisplay) =
        match display with
        | GameDisplay.Unavailable reason ->
            mode.Text <- "Game observations unavailable"
            counts.Text <- reason
            columns.IsVisible <- false
            deck.IsVisible <- false

            for list in [ remaining; hand; player; opponent ] do
                assign list []
        | GameDisplay.Observed facts ->
            let side label (value: GameSideDisplay) =
                $"{label} health {value.Health} • hand {value.HandCount} • deck {value.DeckCount}"

            mode.Text <- facts.ModeLabel
            counts.Text <- side "Your" facts.Player + "\n" + side "Opponent" facts.Opponent
            columns.IsVisible <- true

            match facts.Deck with
            | GameDeckDisplay.Hidden ->
                deck.IsVisible <- false
                assign remaining []
            | GameDeckDisplay.Cards cards ->
                deck.IsVisible <- true
                assign remaining cards

            assign hand facts.KnownHand
            assign player facts.PlayerBoard
            assign opponent facts.OpponentBoard

    member this.Unavailable(reason: string) =
        this.Apply(GameDisplay.Unavailable reason)
