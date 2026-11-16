using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input.Platform;

namespace Hdt.Avalonia;

public sealed class LibraryView : UserControl
{
    private readonly ClientLibrary _library;
    private string? _editing;
    private bool _working, _creating, _updatingDeckList;
    private ulong _shownRevision = ulong.MaxValue;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    public ListBox Decks { get; } = new() { MinHeight = 120, MaxHeight = 400 };
    public TextBox NameBox { get; } = new() { MaxLength = 256, PlaceholderText = "Deck name" };
    public TextBox ClassBox { get; } = new() { MaxLength = 64, PlaceholderText = "Class label (optional)" };
    public TextBox HeroBox { get; } = new() { MaxLength = 10, PlaceholderText = "Hero DBF ID, e.g. 637" };
    public TextBox FormatBox { get; } = new() { MaxLength = 1, Text = "2", PlaceholderText = "Format: 1 Wild, 2 Standard, 3 Classic, 4 Twist" };
    public TextBox CardsBox { get; } = new() { MaxLength = 16384, AcceptsReturn = true, MinHeight = 120, PlaceholderText = "One DBF ID and count per line, e.g. 77 2" };
    public TextBox SideboardsBox { get; } = new() { MaxLength = 16384, AcceptsReturn = true, MinHeight = 65, PlaceholderText = "Sideboard: owner DBF ID, card DBF ID, count" };
    public TextBox NotesBox { get; } = new() { MaxLength = 8192, AcceptsReturn = true, MinHeight = 60, PlaceholderText = "Deck notes" };
    public TextBox CodeBox { get; } = new() { MaxLength = 65536, AcceptsReturn = true, MinHeight = 70, PlaceholderText = "Paste a Hearthstone deckstring or commented deck export" };
    public Button NewButton { get; } = new() { Content = "New deck" };
    public Button SaveButton { get; } = new() { Content = "Save deck" };
    public Button ImportButton { get; } = new() { Content = "Import code" };
    public Button ExportButton { get; } = new() { Content = "Export code" };
    public Button UseButton { get; } = new() { Content = "Use for tracking" };
    public Button ArchiveButton { get; } = new() { Content = "Archive" };
    public CheckBox ShowArchived { get; } = new() { Content = "Show archived decks" };
    public Button ReloadButton { get; } = new() { Content = "Reload library" };
    public Button CopyButton { get; } = new() { Content = "Copy code" };
    public CardCatalogView Catalog { get; }
    public Task PendingAction { get; private set; } = Task.CompletedTask;
    public string Status => _status.Text ?? "";
    public LibraryView(ClientLibrary library, CancellationToken lifetime = default)
    {
        _library = library;
        Catalog = new(lifetime);
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
                else { _status.Text = $"{card.Name} is not in the unsaved deck."; return; }
                CardsBox.Text = string.Join('\n', cards.Select(entry => $"{entry.DbfId} {entry.Count}"));
                _status.Text = $"Updated {card.Name} in the unsaved deck. Save deck to persist.";
            }
            catch (Exception error) when (error is FormatException or OverflowException) { _status.Text = error.Message; }
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
        var columns = new Grid { ColumnDefinitions = new("300,*"), Margin = new(16) };
        var list = new StackPanel { Spacing = 12, Children = { Label("MY DECKS"), NewButton, ReloadButton, ShowArchived, Decks, _status } };
        columns.Children.Add(list);
        var editor = new StackPanel
        {
            Margin = new(20, 0, 0, 0),
            Spacing = 10,
            Children = {
            Label("Deck editor"), NameBox, ClassBox, HeroBox, FormatBox, Catalog, CardsBox, SideboardsBox, NotesBox,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { SaveButton, UseButton, ArchiveButton } },
            Label("Hearthstone import / export"), CodeBox,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ImportButton, ExportButton, CopyButton } },
            new TextBlock { Text = "Numeric DBF IDs are exact card identifiers. Optional local catalog search adds cards by name. Structural deckcode validation does not prove game legality. Tracking selection stays inside this native client.", TextWrapping = TextWrapping.Wrap }
        }
        };
        columns.Children.Add(new ScrollViewer { Content = editor, [Grid.ColumnProperty] = 1 });
        Content = columns; Refresh();
    }
    private static TextBlock Label(string text) => new() { Text = text, FontSize = 20 };
    private static IEnumerable<string[]> Lines(string? text) => (text ?? "").Split('\n').Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Where(parts => parts.Length > 0);
    public void NewDeck()
    {
        if (_working || _library.Busy) return;
        if (ShowArchived.IsChecked == true) { ShowArchived.IsChecked = false; return; }
        ClearEditor();
    }
    private void ClearEditor()
    {
        _creating = true; _editing = null; Decks.SelectedItem = null;
        NameBox.Text = ClassBox.Text = HeroBox.Text = CardsBox.Text = SideboardsBox.Text = NotesBox.Text = CodeBox.Text = "";
        FormatBox.Text = "2"; RefreshButtons(); NameBox.Focus();
    }
    public void FocusImport() => CodeBox.Focus();
    public void ExportSelected()
    {
        if (_editing is null) { _status.Text = "Select a saved deck before exporting."; return; }
        Start(async () => { var export = await _library.ExportAsync(_editing); CodeBox.Text = $"### {export.Name}\n{export.Code}"; });
    }
    private void Edit(DeckEntry deck)
    {
        if (_editing != deck.Id) CodeBox.Text = "";
        _creating = false; _editing = deck.Id; NameBox.Text = deck.Name; ClassBox.Text = deck.HeroClass;
        HeroBox.Text = deck.Content.HeroDbfId.ToString(); FormatBox.Text = deck.Content.Format.ToString();
        CardsBox.Text = string.Join('\n', deck.Content.Cards.Select(card => $"{card.DbfId} {card.Count}"));
        SideboardsBox.Text = string.Join('\n', deck.Content.Sideboards.Select(card => $"{card.OwnerDbfId} {card.DbfId} {card.Count}"));
        NotesBox.Text = deck.Notes; RefreshButtons();
    }
    private void Start(Func<Task> action, string? success = null)
    {
        if (_working || _library.Busy) return;
        PendingAction = RunAsync(action, success);
    }
    private async Task RunAsync(Func<Task> action, string? success)
    {
        _working = true; RefreshButtons();
        try { await action(); _status.Text = success ?? _library.Status; }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidDataException or FormatException or OverflowException or NotSupportedException)
        { _status.Text = error.Message; }
        finally { _working = false; RefreshButtons(); }
    }
    private void Refresh()
    {
        _status.Text = _library.Status;
        if (_shownRevision != _library.State.Revision && _library.Ready)
        {
            _shownRevision = _library.State.Revision;
            var selected = _editing ?? _library.State.SelectedDeckId;
            bool Visible(DeckEntry deck) => deck.Archived == (ShowArchived.IsChecked == true);
            var next = _creating ? null : _library.State.Decks.FirstOrDefault(deck => deck.Id == selected && Visible(deck))
                ?? _library.State.Decks.FirstOrDefault(deck => deck.Id == _library.State.SelectedDeckId && Visible(deck));
            _updatingDeckList = true;
            try { Decks.ItemsSource = _library.State.Decks.Where(Visible).ToArray(); Decks.SelectedItem = next; }
            finally { _updatingDeckList = false; }
            if (next is not null && next.Id != _editing) Edit(next);
        }
        RefreshButtons();
    }
    private void RefreshButtons()
    {
        var archived = _library.State.Decks.FirstOrDefault(deck => deck.Id == _editing)?.Archived == true;
        var browsingArchived = ShowArchived.IsChecked == true;
        SaveButton.IsEnabled = !_working && !_library.Busy && _library.Ready && !browsingArchived;
        ImportButton.IsEnabled = SaveButton.IsEnabled;
        UseButton.IsEnabled = SaveButton.IsEnabled && _editing is not null;
        ArchiveButton.IsEnabled = ExportButton.IsEnabled = !_working && !_library.Busy && _library.Ready && _editing is not null;
        ArchiveButton.Content = archived ? "Restore" : "Archive";
        NewButton.IsEnabled = ReloadButton.IsEnabled = CopyButton.IsEnabled = !_working && !_library.Busy;
        Decks.IsEnabled = !_working && !_library.Busy;
        ShowArchived.IsEnabled = !_working && !_library.Busy;
        Catalog.IsEnabled = !_working && _library.Ready && !browsingArchived;
        foreach (var field in new[] { NameBox, ClassBox, HeroBox, FormatBox, CardsBox, SideboardsBox, NotesBox })
            field.IsReadOnly = _working || browsingArchived;
    }
}

public sealed class HistoryView : UserControl
{
    private readonly TextBlock _summary = new() { FontSize = 22 };
    private readonly ListBox _matches = new() { MaxHeight = 500 };
    public HistoryView(ClientLibrary library)
    {
        void Refresh()
        {
            _summary.Text = $"Match history • {library.State.Matches.Length} recorded • {library.State.Matches.Count(match => match.Result == "Win")} wins / {library.State.Matches.Count(match => match.Result == "Loss")} losses";
            _matches.ItemsSource = library.State.Matches.Reverse().ToArray();
        }
        library.Changed += Refresh;
        Content = new StackPanel
        {
            Margin = new(24),
            Spacing = 16,
            Children = { _summary, _matches,
            new TextBlock { Text = "Only supplied observations are recorded. There is no automatic import from Windows HDT's database.", TextWrapping = TextWrapping.Wrap } }
        };
        Refresh();
    }
}
