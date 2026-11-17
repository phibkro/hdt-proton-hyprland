namespace Hdt.Avalonia

open Avalonia.Controls
open Avalonia.Markup.Xaml

type DeckMetadataDisplay = { Tags: string list; Website: string }

/// Owns layout and temporary input only; persistence is supplied by LibraryView.
[<Sealed>]
type DeckMetadataView() as this =
    inherit UserControl()
    do AvaloniaXamlLoader.Load(this)
    let tags = TrackerLayout.Required<TextBox>(this, "TagsInput")
    let website = TrackerLayout.Required<TextBox>(this, "WebsiteInput")
    let editor = TrackerLayout.Required<Border>(this, "MetadataEditor")
    let savedTags = TrackerLayout.Required<TextBlock>(this, "SavedTags")
    let savedWebsite = TrackerLayout.Required<TextBlock>(this, "SavedWebsite")
    let openButton = TrackerLayout.Required<Button>(this, "OpenMetadata")
    let saveButton = TrackerLayout.Required<Button>(this, "SaveMetadata")
    let cancelButton = TrackerLayout.Required<Button>(this, "CancelMetadata")

    member _.TagsBox = tags
    member _.WebsiteBox = website
    member _.OpenButton = openButton
    member _.SaveButton = saveButton
    member _.CancelButton = cancelButton
    member _.Editor = editor
    member _.SavedTags = savedTags
    member _.SavedWebsite = savedWebsite

    member _.ShowSaved(display: DeckMetadataDisplay) =
        savedTags.Text <-
            if display.Tags.IsEmpty then
                "No tags"
            else
                System.String.Join(" • ", display.Tags)

        savedWebsite.Text <-
            if display.Website.Length = 0 then
                "No website"
            else
                display.Website

    member _.Open(display: DeckMetadataDisplay) =
        if not editor.IsVisible then
            tags.Text <- System.String.Join("\n", display.Tags)
            website.Text <- display.Website
            editor.IsVisible <- true
            openButton.IsEnabled <- false
            tags.BringIntoView()

    member _.Close() =
        editor.IsVisible <- false
        tags.Text <- ""
        website.Text <- ""
        openButton.IsEnabled <- saveButton.IsEnabled

    member _.ApplyAvailability(canEdit: bool, busy: bool) =
        let editable = canEdit && not busy
        openButton.IsEnabled <- editable && not editor.IsVisible
        saveButton.IsEnabled <- editable
        cancelButton.IsEnabled <- not busy
        tags.IsEnabled <- editable
        website.IsEnabled <- editable
