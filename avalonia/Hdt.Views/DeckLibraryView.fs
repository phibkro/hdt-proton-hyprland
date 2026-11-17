namespace Hdt.Avalonia

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media

/// Display facts only; validated wire records and transactions belong to the caller.
type LibraryCardDisplay = { DbfId: uint32; Count: uint32 }

type LibrarySideboardDisplay =
    {
        OwnerDbfId: uint32
        DbfId: uint32
        Count: uint32
    }

type LibraryDeckDisplay =
    {
        Name: string
        HeroClass: string
        HeroDbfId: uint32
        Format: uint32
        Notes: string
        Cards: LibraryCardDisplay list
        Sideboards: LibrarySideboardDisplay list
    }

[<RequireQualifiedAccess>]
type LibrarySelection =
    | Empty
    | Saved of LibraryDeckDisplay

[<RequireQualifiedAccess>]
type LibraryEditorSelection =
    | NoneSelected
    | Active
    | Archived

[<RequireQualifiedAccess>]
type CatalogSelection =
    | NoneSelected
    | Card
    | Hero

/// Optional local catalog presentation. File selection and searches are injected.
type CatalogSurface() as this =
    inherit UserControl()

    let status =
        TrackerLayout.Text("Optional: supply a localized HearthstoneJSON card array. Nothing is downloaded or bundled.")

    let path =
        TextBox(MaxLength = 4096, PlaceholderText = "Local card catalog JSON path")

    let query =
        TextBox(MaxLength = 256, PlaceholderText = "Card name, class, type, card ID or DBF ID")

    let includeAll = CheckBox(Content = "Include noncollectible cards")
    let results = ListBox(MinHeight = 80.0, MaxHeight = 200.0)
    let search = Button(Content = "Search local catalog")
    let choose = Button(Content = "Choose catalog file…")
    let add = Button(Content = "Add selected card")
    let remove = Button(Content = "Remove one copy")
    let hero = Button(Content = "Use selected hero")

    do
        let buttons = WrapPanel(Orientation = Orientation.Horizontal)
        buttons.Children.AddRange([ add; remove; hero ])
        let body = StackPanel(Spacing = 8.0)
        body.Children.AddRange([ choose; path; query; includeAll; search; results; buttons; status ])
        this.Content <- Expander(Header = "Find cards by name (optional local catalog)", Content = body)

    member _.StatusText = status
    member _.PathBox = path
    member _.QueryBox = query
    member _.IncludeNonCollectible = includeAll
    member _.Results = results
    member _.SearchButton = search
    member _.ChooseButton = choose
    member _.AddButton = add
    member _.RemoveButton = remove
    member _.HeroButton = hero

    member _.ApplyAvailability(working: bool, selection: CatalogSelection) =
        for control in [ path :> Control; query; includeAll; search; choose ] do
            control.IsEnabled <- not working

        add.IsEnabled <- not working && selection <> CatalogSelection.NoneSelected
        remove.IsEnabled <- add.IsEnabled
        hero.IsEnabled <- not working && selection = CatalogSelection.Hero

    static member CardLabel
        (
            dbfId: uint32,
            id: string,
            name: string,
            cardType: string | null,
            cardClass: string | null,
            classes: string array,
            cost: System.Nullable<uint32>
        ) =
        let classLabel =
            if classes.Length > 0 then
                System.String.Join("/", classes)
            else
                match cardClass with
                | null -> "unknown class"
                | value -> value

        let typeLabel =
            match cardType with
            | null -> "unknown type"
            | value -> value

        let mana = if cost.HasValue then string cost.Value else "?"
        $"{mana} mana • {name} • {classLabel} • {typeLabel} • {id} / #{dbfId}"

/// Used both in My Decks and in the full history tab with the same row renderer.
type HistorySurface(compact: bool) as this =
    inherit UserControl()
    let summary = TextBlock(FontSize = if compact then 16.0 else 22.0)

    let empty = TrackerLayout.Text("")

    let matches =
        ListBox(MinHeight = (if compact then 100.0 else 180.0), MaxHeight = (if compact then 250.0 else 500.0))

    do
        let body =
            StackPanel(Margin = Thickness(if compact then 0.0 else 24.0), Spacing = 12.0)

        body.Children.AddRange(
            [
                summary
                empty
                matches
                TrackerLayout.Text(
                    "Only supplied observations are recorded. There is no automatic import from Windows HDT's database."
                )
            ]
        )

        this.Content <- body

    member _.Matches = matches
    member _.Summary = summary.Text
    member _.EmptyMessage = empty.Text

    member _.ShowEmpty(message: string) =
        empty.Text <- message
        empty.IsVisible <- message.Length > 0


    member _.ShowSummary(total: int, wins: int, losses: int) =
        summary.Text <- $"{total} supplied games • {wins} wins / {losses} losses"

    static member Row
        (result: string, mode: string, player: string, opponent: string, turns: uint32, observedAt: string)
        =
        let row = StackPanel(Spacing = 4.0, Margin = Thickness(0.0, 4.0))

        row.Children.AddRange(
            [
                TrackerLayout.Text($"{result} • {mode}", 14.0)
                TrackerLayout.Text($"{player} vs {opponent} • {turns} turns", 12.0)
                TrackerLayout.Text(observedAt, 11.0)
            ]
        )

        row

/// Owns the complete library/editor visual tree; controllers attach existing actions.
type DeckLibrarySurface(catalog: Control, history: Control, metadata: Control) as this =
    inherit UserControl()
    let status = TrackerLayout.Text("")

    let historyDeck =
        TextBlock(TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 2)

    let decks = ListBox(MinHeight = 120.0)
    let search = TextBox(PlaceholderText = "Search decks or class", MaxLength = 256)
    let overview = Button(Content = "Back to deck overview")
    let edit = Button(Content = "Edit selected deck")

    let editor =
        Expander(Header = "Deck editor / import", HorizontalAlignment = HorizontalAlignment.Stretch)

    let title =
        TextBlock(
            Text = "No deck selected",
            FontSize = 18.0,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 3
        )

    let facts = TrackerLayout.Text("Choose a deck in the browser to inspect it.")
    let notes = TrackerLayout.Text("")
    let cards = StackPanel(Spacing = 3.0)
    let name = TextBox(MaxLength = 256, PlaceholderText = "Deck name")
    let heroClass = TextBox(MaxLength = 64, PlaceholderText = "Class label (optional)")
    let hero = TextBox(MaxLength = 10, PlaceholderText = "Hero DBF ID, e.g. 637")

    let format =
        TextBox(MaxLength = 1, Text = "2", PlaceholderText = "Format: 1 Wild, 2 Standard, 3 Classic, 4 Twist")

    let cardInput =
        TextBox(
            MaxLength = 16384,
            AcceptsReturn = true,
            Height = 140.0,
            PlaceholderText = "One DBF ID and count per line, e.g. 77 2"
        )

    let sideboards =
        TextBox(
            MaxLength = 16384,
            AcceptsReturn = true,
            Height = 80.0,
            PlaceholderText = "Sideboard: owner DBF ID, card DBF ID, count"
        )

    let noteInput =
        TextBox(MaxLength = 8192, AcceptsReturn = true, Height = 80.0, PlaceholderText = "Deck notes")

    let code =
        TextBox(
            MaxLength = 65536,
            AcceptsReturn = true,
            Height = 110.0,
            PlaceholderText = "Paste a Hearthstone deckstring or commented deck export"
        )

    let newDeck = Button(Content = "New deck")
    let save = Button(Content = "Save deck")
    let import = Button(Content = "Import code")
    let export = Button(Content = "Export code")
    let useDeck = Button(Content = "Use for game history")
    let archive = Button(Content = "Archive")
    let archived = CheckBox(Content = "Show archived decks")
    let reload = Button(Content = "Reload library")
    let copy = Button(Content = "Copy code")

    let columns =
        Grid(ColumnDefinitions = ColumnDefinitions("210,*,235"), ColumnSpacing = 12.0, Margin = Thickness(12.0))

    let browser = DockPanel(LastChildFill = true)
    let browserPanel = TrackerLayout.Section("My decks", browser)
    let middle = StackPanel(Spacing = 14.0)
    let gamesPanel = TrackerLayout.Section("My games", middle, true)
    let selected = StackPanel(Spacing = 12.0)
    let selectedPanel = TrackerLayout.Section("Selected deck", selected, true)

    let row controls =
        let panel = WrapPanel(Orientation = Orientation.Horizontal)
        panel.Children.AddRange(controls)
        panel

    let cardRow (dbfId: uint32) (count: uint32) =
        Border(
            Background = TrackerLayout.Line,
            Padding = Thickness(7.0, 5.0),
            Child = TrackerLayout.Text($"{count} × DBF {dbfId}")
        )

    do
        let tools = StackPanel(Spacing = 8.0)
        tools.Children.AddRange([ search; newDeck; archived; historyDeck ])
        DockPanel.SetDock(tools, Dock.Top)
        browser.Children.Add(tools)
        let bottom = StackPanel(Spacing = 8.0, Margin = Thickness(0.0, 8.0, 0.0, 0.0))
        bottom.Children.AddRange([ reload; status ])
        DockPanel.SetDock(bottom, Dock.Bottom)
        browser.Children.Add(bottom)
        browser.Children.Add(decks)
        let fields = StackPanel(Margin = Thickness(0.0, 12.0, 0.0, 0.0), Spacing = 10.0)

        fields.Children.AddRange(
            [
                overview
                TrackerLayout.Text("Deck editor", 20.0)
                name
                heroClass
                hero
                format
                catalog
                cardInput
                sideboards
                noteInput
                row [ save; useDeck; archive ]
                TrackerLayout.Text("Hearthstone import / export", 20.0)
                code
                row [ import; export; copy ]
                TrackerLayout.Text(
                    "Choose a deck here to label new game history. Live card counts come from the observation source. Optional catalog search adds cards by name; deckcode validation checks structure, not game legality."
                )
            ]
        )

        editor.Content <- fields
        middle.Children.AddRange([ history; editor ])

        selected.Children.AddRange(
            [
                title
                facts
                edit
                cards
                TrackerLayout.Text("NOTES", 12.0)
                notes
                metadata
                TrackerLayout.Text("MANA CURVE", 12.0)
                TrackerLayout.Text(
                    "Unavailable: saved deck codes contain card identities and counts, not mana costs. No curve is guessed.",
                    12.0
                )
            ]
        )

        columns.Children.AddRange([ browserPanel; gamesPanel; selectedPanel ])
        Grid.SetColumn(gamesPanel, 1)
        Grid.SetColumn(selectedPanel, 2)
        overview.Click.Add(fun _ -> editor.IsExpanded <- false)

        editor.PropertyChanged.Add(fun change ->
            if change.Property = Expander.IsExpandedProperty then
                Grid.SetColumnSpan(gamesPanel, if editor.IsExpanded then 2 else 1)
                selectedPanel.IsVisible <- not editor.IsExpanded)

        this.Content <- columns

    member _.StatusText = status
    member _.HistoryDeckText = historyDeck
    member _.Decks = decks
    member _.SearchBox = search
    member _.OverviewButton = overview
    member _.EditButton = edit
    member _.EditorPane = editor
    member _.BrowserPanel = browserPanel
    member _.GamesPanel = gamesPanel
    member _.SelectedPanel = selectedPanel
    member _.NameBox = name
    member _.ClassBox = heroClass
    member _.HeroBox = hero
    member _.FormatBox = format
    member _.CardsBox = cardInput
    member _.SideboardsBox = sideboards
    member _.NotesBox = noteInput
    member _.CodeBox = code
    member _.NewButton = newDeck
    member _.SaveButton = save
    member _.ImportButton = import
    member _.ExportButton = export
    member _.UseButton = useDeck
    member _.ArchiveButton = archive
    member _.ShowArchived = archived
    member _.ReloadButton = reload
    member _.CopyButton = copy
    member _.MetadataControl = metadata
    member _.HistoryControl = history
    member _.CatalogControl = catalog

    member _.ShowHistoryDeck(deckName: string | null) =
        let label =
            match deckName with
            | null -> "none selected"
            | value -> value

        historyDeck.Text <- $"Game history deck: {label}"
        ToolTip.SetTip(historyDeck, historyDeck.Text)

    member _.OpenEditor(target: Control) =
        editor.IsExpanded <- true
        this.UpdateLayout()
        target.BringIntoView()
        target.Focus() |> ignore

    member _.ClearInputs() =
        decks.SelectedItem <- null

        for field in [ name; heroClass; hero; cardInput; sideboards; noteInput; code ] do
            field.Text <- ""

        format.Text <- "2"

    member _.LoadEditor(deck: LibraryDeckDisplay, changedIdentity: bool) =
        if changedIdentity then
            code.Text <- ""

        name.Text <- deck.Name
        heroClass.Text <- deck.HeroClass
        hero.Text <- string deck.HeroDbfId
        format.Text <- string deck.Format

        cardInput.Text <-
            deck.Cards
            |> List.map (fun card -> $"{card.DbfId} {card.Count}")
            |> String.concat "\n"

        sideboards.Text <-
            deck.Sideboards
            |> List.map (fun card -> $"{card.OwnerDbfId} {card.DbfId} {card.Count}")
            |> String.concat "\n"

        noteInput.Text <- deck.Notes

    member _.ApplyAvailability
        (working: bool, busy: bool, ready: bool, browsingArchived: bool, selection: LibraryEditorSelection)
        =
        let idle = not working && not busy
        let selected = selection <> LibraryEditorSelection.NoneSelected
        save.IsEnabled <- idle && ready && not browsingArchived
        import.IsEnabled <- save.IsEnabled
        useDeck.IsEnabled <- save.IsEnabled && selected
        edit.IsEnabled <- idle && selected
        archive.IsEnabled <- idle && ready && selected
        export.IsEnabled <- archive.IsEnabled

        archive.Content <-
            if selection = LibraryEditorSelection.Archived then
                "Restore"
            else
                "Archive"

        for control in [ newDeck :> Control; reload; copy; decks; archived ] do
            control.IsEnabled <- idle

        catalog.IsEnabled <- not working && ready && not browsingArchived

        for field in [ name; heroClass; hero; format; cardInput; sideboards; noteInput ] do
            field.IsReadOnly <- working || browsingArchived

    member _.ShowSelected(selection: LibrarySelection) =
        cards.Children.Clear()

        match selection with
        | LibrarySelection.Empty ->
            title.Text <- "No deck selected"
            facts.Text <- "Choose a saved deck, or create/import one in the browser."
            notes.Text <- "No saved notes."
        | LibrarySelection.Saved deck ->
            title.Text <- deck.Name

            let classLabel =
                if System.String.IsNullOrWhiteSpace(deck.HeroClass) then
                    "Unknown class"
                else
                    deck.HeroClass

            let count = deck.Cards |> List.sumBy (fun card -> int64 card.Count)
            facts.Text <- $"{classLabel} • {count} cards\nHero DBF {deck.HeroDbfId} • format {deck.Format}"

            notes.Text <-
                if System.String.IsNullOrWhiteSpace(deck.Notes) then
                    "No saved notes."
                else
                    deck.Notes

            for card in deck.Cards do
                cards.Children.Add(cardRow card.DbfId card.Count)

            let sideboards =
                deck.Sideboards |> List.groupBy (fun card -> card.OwnerDbfId) |> List.sortBy fst

            for owner, entries in sideboards do
                let group = StackPanel(Spacing = 3.0)
                group.Children.Add(TrackerLayout.Text($"SIDEBOARD • owner DBF {owner}", 12.0))

                for card in entries |> List.sortBy (fun card -> card.DbfId) do
                    group.Children.Add(cardRow card.DbfId card.Count)

                cards.Children.Add(group)

    static member DeckRow(name: string, heroClass: string, count: int64) =
        let row = StackPanel(Spacing = 4.0, Margin = Thickness(2.0, 5.0))

        let classLabel =
            if System.String.IsNullOrWhiteSpace(heroClass) then
                "Unknown class"
            else
                heroClass

        row.Children.AddRange(
            [
                TextBlock(
                    Text = name,
                    FontSize = 15.0,
                    FontWeight = FontWeight.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 1
                )
                TrackerLayout.Text($"{classLabel} • {count} cards", 12.0)
            ]
        )

        row
