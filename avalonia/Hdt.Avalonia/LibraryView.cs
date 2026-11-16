using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Controls.Templates;

namespace Hdt.Avalonia;

public sealed class LibraryView : DeckLibrarySurface
{
    private readonly ClientLibrary _library;
    private string? _editing;
    private bool _working, _creating, _updatingDeckList;
    private ulong _shownRevision = ulong.MaxValue;
    public CardCatalogView Catalog => (CardCatalogView)CatalogControl;
    public Task PendingAction { get; private set; } = Task.CompletedTask;
    public string Status => StatusText.Text ?? "";
    public LibraryView(ClientLibrary library, CancellationToken lifetime = default) : base(new CardCatalogView(lifetime), new HistoryView(library, compact: true))
    {
        _library = library;
        Catalog.ChangeCard += (card, change) =>
        {
            if (_working || !_library.Ready) return;
            try
            {
                var cards = Lines(CardsBox.Text).Select(parts => parts.Length == 2 ? new CardCount(uint.Parse(parts[0]), uint.Parse(parts[1])) : throw new FormatException("Repair the existing card list before adding a catalog card.")).ToList();
                var existing = cards.FindIndex(entry => entry.DbfId == card.DbfId);
                if (change == CardEdit.AddCopy)
                {
                    if (existing < 0) cards.Add(new(card.DbfId, 1));
                    else cards[existing] = cards[existing] with { Count = checked(cards[existing].Count + 1) };
                }
                else if (existing >= 0)
                {
                    if (cards[existing].Count <= 1) cards.RemoveAt(existing);
                    else cards[existing] = cards[existing] with { Count = cards[existing].Count - 1 };
                }
                else { StatusText.Text = $"{card.Name} is not in the unsaved deck."; return; }
                CardsBox.Text = string.Join('\n', cards.Select(entry => $"{entry.DbfId} {entry.Count}"));
                StatusText.Text = $"Updated {card.Name} in the unsaved deck. Save deck to persist.";
            }
            catch (Exception error) when (error is FormatException or OverflowException) { StatusText.Text = error.Message; }
        };
        Catalog.UseHero += card => { if (!_working && _library.Ready) { HeroBox.Text = card.DbfId.ToString(); ClassBox.Text = card.Classes.Length > 0 ? string.Join("/", card.Classes) : card.CardClass ?? ""; } };
        NewButton.Click += (_, _) => NewDeck();
        SaveButton.Click += (_, _) => Start(async () =>
        {
            var content = new DeckContent(uint.Parse(HeroBox.Text ?? ""), uint.Parse(FormatBox.Text ?? ""),
                Lines(CardsBox.Text).Select(parts => parts.Length == 2 ? new CardCount(uint.Parse(parts[0]), uint.Parse(parts[1])) : throw new FormatException("Cards require DBF ID and count.")).ToArray(),
                Lines(SideboardsBox.Text).Select(parts => parts.Length == 3 ? new SideboardCard(uint.Parse(parts[0]), uint.Parse(parts[1]), uint.Parse(parts[2])) : throw new FormatException("Sideboards require owner, card and count.")).ToArray());
            var id = _editing;
            await _library.SaveAsync(id, NameBox.Text ?? "", ClassBox.Text ?? "", content, NotesBox.Text ?? "");
            Edit(_library.State.Decks.Single(deck => deck.Id == (id ?? _library.State.SelectedDeckId)));
            Decks.SelectedItem = _library.State.Decks.Single(deck => deck.Id == _editing);
        });
        ImportButton.Click += (_, _) => Start(async () =>
        {
            await _library.ImportAsync(string.IsNullOrWhiteSpace(NameBox.Text) ? null : NameBox.Text, CodeBox.Text ?? "");
            Decks.SelectedItem = _library.State.Decks.FirstOrDefault(deck => deck.Id == _library.State.SelectedDeckId);
        });
        ExportButton.Click += (_, _) => ExportSelected();
        CopyButton.Click += (_, _) => Start(async () =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard ?? throw new IOException("Clipboard is unavailable.");
            if (string.IsNullOrWhiteSpace(CodeBox.Text)) throw new IOException("Export or paste a deck code before copying.");
            await clipboard.SetTextAsync(CodeBox.Text);
        }, "Deck code copied.");
        UseButton.Click += (_, _) => { if (_editing is not null) Start(() => _library.SelectAsync(_editing)); };
        ArchiveButton.Click += (_, _) =>
        {
            if (_editing is not null)
            {
                var id = _editing;
                var archived = _library.State.Decks.Single(deck => deck.Id == id).Archived;
                Start(async () => { await _library.SetArchivedAsync(id, !archived); ClearEditor(); });
            }
        };
        ShowArchived.IsCheckedChanged += (_, _) => { _shownRevision = ulong.MaxValue; ClearEditor(); Refresh(); };
        ReloadButton.Click += (_, _) => Start(_library.ReloadAsync);
        Decks.SelectionChanged += (_, _) => { if (!_updatingDeckList && Decks.SelectedItem is DeckEntry deck) Edit(deck); };
        _library.Changed += Refresh;
        Decks.ItemTemplate = new FuncDataTemplate<DeckEntry>((entry, _) => entry is null ? null :
            DeckRow(entry.Name, entry.HeroClass, entry.Content.Cards.Sum(card => (long)card.Count)));
        SearchBox.TextChanged += (_, _) => { _shownRevision = ulong.MaxValue; Refresh(); };
        EditButton.Click += (_, _) => EditSelected();
        Refresh();
    }
    private static IEnumerable<string[]> Lines(string? text) => (text ?? "").Split('\n').Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Where(parts => parts.Length > 0);
    public void NewDeck()
    {
        if (_working || _library.Busy) return;
        if (ShowArchived.IsChecked == true) ShowArchived.IsChecked = false;
        SearchBox.Text = "";
        ClearEditor(); OpenEditor(NameBox);
    }
    public void EditSelected() => OpenEditor(NameBox);
    private void ClearEditor()
    {
        _creating = true; _editing = null; ClearInputs();
        RefreshButtons(); RefreshSelected();
    }
    public void FocusImport() => OpenEditor(CodeBox);
    public void ExportSelected()
    {
        OpenEditor(CodeBox);
        if (_editing is null) { StatusText.Text = "Select a saved deck before exporting."; return; }
        Start(async () => { var export = await _library.ExportAsync(_editing); CodeBox.Text = $"### {export.Name}\n{export.Code}"; });
    }
    private void Edit(DeckEntry deck)
    {
        LoadEditor(Project(deck), _editing != deck.Id);
        _creating = false; _editing = deck.Id;
        RefreshButtons(); RefreshSelected();
    }

    private void Start(Func<Task> action, string? success = null)
    {
        if (_working || _library.Busy) return;
        PendingAction = RunAsync(action, success);
    }
    private async Task RunAsync(Func<Task> action, string? success)
    {
        _working = true; RefreshButtons();
        try { await action(); StatusText.Text = success ?? _library.Status; }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidDataException or FormatException or OverflowException or NotSupportedException)
        { StatusText.Text = error.Message; }
        finally { _working = false; RefreshButtons(); }
    }
    private void Refresh()
    {
        StatusText.Text = _library.Status;
        var historyDeck = _library.State.Decks.FirstOrDefault(deck => deck.Id == _library.State.SelectedDeckId)?.Name;
        ShowHistoryDeck(historyDeck);
        if (_shownRevision != _library.State.Revision && _library.Ready)
        {
            _shownRevision = _library.State.Revision;
            var selected = _editing ?? _library.State.SelectedDeckId;
            bool Visible(DeckEntry deck) => deck.Archived == (ShowArchived.IsChecked == true)
                && (string.IsNullOrWhiteSpace(SearchBox.Text) || deck.Name.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase)
                    || deck.HeroClass.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase));
            var next = _creating ? null : _library.State.Decks.FirstOrDefault(deck => deck.Id == selected && Visible(deck))
                ?? _library.State.Decks.FirstOrDefault(deck => deck.Id == _library.State.SelectedDeckId && Visible(deck));
            _updatingDeckList = true;
            try { Decks.ItemsSource = _library.State.Decks.Where(Visible).ToArray(); Decks.SelectedItem = next; }
            finally { _updatingDeckList = false; }
            if (next is not null && next.Id != _editing) Edit(next);
        }
        RefreshButtons(); RefreshSelected();
    }
    private void RefreshSelected()
    {
        var deck = _library.State.Decks.FirstOrDefault(entry => entry.Id == _editing);
        ShowSelected(deck is null ? LibrarySelection.Empty : LibrarySelection.NewSaved(Project(deck)));
    }
    private static LibraryDeckDisplay Project(DeckEntry deck) => new(
        deck.Name, deck.HeroClass, deck.Content.HeroDbfId, deck.Content.Format, deck.Notes,
        Microsoft.FSharp.Collections.ListModule.OfSeq(deck.Content.Cards.Select(card => new LibraryCardDisplay(card.DbfId, card.Count))),
        Microsoft.FSharp.Collections.ListModule.OfSeq(deck.Content.Sideboards.Select(card => new LibrarySideboardDisplay(card.OwnerDbfId, card.DbfId, card.Count))));
    private void RefreshButtons()
    {
        var deck = _library.State.Decks.FirstOrDefault(deck => deck.Id == _editing);
        var selection = deck is null ? LibraryEditorSelection.NoneSelected : deck.Archived ? LibraryEditorSelection.Archived : LibraryEditorSelection.Active;
        ApplyAvailability(_working, _library.Busy, _library.Ready, ShowArchived.IsChecked == true, selection);
    }
}

public sealed class HistoryView : HistorySurface
{
    public HistoryView(ClientLibrary library, bool compact = false) : base(compact)
    {
        void Refresh()
        {
            ShowSummary(library.State.Matches.Length, library.State.Matches.Count(match => match.Result == "Win"), library.State.Matches.Count(match => match.Result == "Loss"));
            Matches.ItemsSource = library.State.Matches.Reverse().ToArray();
        }
        library.Changed += Refresh;
        Matches.ItemTemplate = new FuncDataTemplate<MatchRecord>((match, _) => match is null ? null :
            Row(match.Result, match.Mode, match.PlayerHero, match.OpponentHero, match.Turns, match.ObservedAt));
        Refresh();
    }
}
