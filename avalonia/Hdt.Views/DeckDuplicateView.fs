namespace Hdt.Avalonia

open Avalonia.Controls
open Avalonia.Markup.Xaml

type DeckDuplicateDisplay = { Id: string; Name: string }

type private DuplicatePhase =
    | Closed
    | Editing
    | Completed of DeckDuplicateDisplay

/// Temporary presentation state only; the controller supplies persisted results.
[<Sealed>]
type DeckDuplicateView() as this =
    inherit UserControl()
    do AvaloniaXamlLoader.Load(this)
    let openButton = TrackerLayout.Required<Button>(this, "OpenDuplicate")
    let name = TrackerLayout.Required<TextBox>(this, "DuplicateName")
    let saveButton = TrackerLayout.Required<Button>(this, "SaveDuplicate")
    let cancelButton = TrackerLayout.Required<Button>(this, "CancelDuplicate")
    let editor = TrackerLayout.Required<Border>(this, "DuplicateEditor")
    let resultText = TrackerLayout.Required<TextBlock>(this, "DuplicateResult")
    let openResult = TrackerLayout.Required<Button>(this, "OpenDuplicateResult")
    let mutable phase = Closed
    let mutable available = false
    let mutable busy = false

    let render () =
        let editing = phase = Editing
        let editable = available && not busy
        editor.IsVisible <- editing
        openButton.IsEnabled <- editable && not editing
        name.IsEnabled <- editable
        saveButton.IsEnabled <- editable
        cancelButton.IsEnabled <- not busy

        match phase with
        | Completed result ->
            resultText.Text <- $"Created {result.Name}. Game history was not copied."
            resultText.IsVisible <- true
            openResult.IsVisible <- true
            openResult.IsEnabled <- editable
        | _ ->
            resultText.Text <- ""
            resultText.IsVisible <- false
            openResult.IsVisible <- false

    do render ()

    member _.OpenButton = openButton
    member _.NameBox = name
    member _.SaveButton = saveButton
    member _.CancelButton = cancelButton
    member _.Editor = editor
    member _.ResultText = resultText
    member _.OpenResultButton = openResult

    member _.ResultId: string | null =
        match phase with
        | Completed result -> result.Id
        | _ -> null

    member _.Open() =
        if phase <> Editing then
            name.Text <- ""
            phase <- Editing
            render ()
            name.BringIntoView()

    member _.Close() =
        name.Text <- ""
        phase <- Closed
        render ()

    member _.ShowResult(result: DeckDuplicateDisplay) =
        name.Text <- ""
        phase <- Completed result
        render ()
        openResult.BringIntoView()

    member _.ApplyAvailability(canDuplicate: bool, working: bool) =
        available <- canDuplicate
        busy <- working
        render ()
