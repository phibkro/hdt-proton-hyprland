using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Hdt.Avalonia;

public sealed class MainWindow : Window
{
    private readonly SettingsStore _store;
    private TrackerSettings _settings;
    private IReadOnlyList<ArenaObservation> _frames = [];
    private int _index;
    private readonly StackPanel _offers = new() { Spacing = 12 };
    private readonly StackPanel _deck = new() { Spacing = 6 };
    private readonly TextBlock _summary = Text("Open an Arena observation replay to begin.", 22);
    private readonly TextBlock _status = Text("No source attached • native interface • migration preview", 13);
    private readonly TextBlock _observation = Text("No observations", 14);
    private readonly TabControl _tabs;
    private readonly global::Avalonia.Threading.DispatcherTimer? _protonTimer;
    public Button NextButton { get; } = new() { Content = "Next observation", IsEnabled = false };
    public Button ResetButton { get; } = new() { Content = "Rewind", IsEnabled = false };
    public CheckBox ScoresToggle { get; } = new() { Content = "Show Arenasmith scores" };
    public CheckBox SynergiesToggle { get; } = new() { Content = "Show deck synergies" };
    public int OfferCount => _offers.Children.Count;
    public int PickedCount => _frames.Count == 0 ? 0 : _frames[_index].PickedDeck.Length;
    public string Summary => _summary.Text ?? "";
    public string Status => _status.Text ?? "";
    public IReadOnlyList<string> Scores => _frames.Count == 0 || !_settings.ShowArenasmithScore
        ? [] : _frames[_index].Offers.Select(o => o.DisplayScore).ToArray();
    public int SelectedPage => _tabs.SelectedIndex;

    public MainWindow(LaunchOptions options)
    {
        Title = "Hearthstone Deck Tracker • Avalonia migration preview";
        Width = 1120; Height = 760; MinWidth = 800; MinHeight = 540;
        _store = new(options.Settings);
        _settings = _store.Load();
        ScoresToggle.IsChecked = _settings.ShowArenasmithScore;
        SynergiesToggle.IsChecked = _settings.ShowArenaDeckSynergies;
        ScoresToggle.IsCheckedChanged += (_, _) => SaveSettings();
        SynergiesToggle.IsCheckedChanged += (_, _) => SaveSettings();
        NextButton.Click += (_, _) => { if (_index + 1 < _frames.Count) { _index++; Render(); } };
        ResetButton.Click += (_, _) => { _index = 0; Render(); };
        var open = new Button { Content = "Open replay…" };
        open.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Open Arena observation replay", AllowMultiple = false });
            if (files.Count == 0) return;
            var path = files[0].TryGetLocalPath();
            if (path is null) { _status.Text = "Only local replay files are supported."; return; }
            TryLoad(path);
        };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { open, ResetButton, NextButton } };
        var content = new Grid { ColumnDefinitions = new("*,280"), Margin = new(16) };
        var left = new StackPanel { Spacing = 16, Children = { _summary, _observation, controls, _offers } };
        content.Children.Add(new ScrollViewer { Content = left });
        var deck = new StackPanel { Spacing = 12, Margin = new(20, 0, 0, 0), Children = { Text("PICKED DECK", 16), _deck } };
        Grid.SetColumn(deck, 1); content.Children.Add(new ScrollViewer { Content = deck, [Grid.ColumnProperty] = 1 });
        var optionsPane = new StackPanel { Margin = new(24), Spacing = 20, Children = {
            Text("Options › Overlay › Arena", 24), ScoresToggle, SynergiesToggle,
            Text("These preferences affect the native draft view and are saved locally.", 15),
            Text("Hero selection, related-card tooltips and redraft-discard suggestions are not migrated yet.", 15) } };
        _tabs = new TabControl { ItemsSource = new TabItem[] {
            new() { Header = "My Decks", Content = Notice("My Decks", "Deck storage, editing and import are not migrated. Your Windows HDT database is never opened.") },
            new() { Header = "Arena Draft", Content = content },
            new() { Header = "Stats", Content = Notice("Statistics", "Arena runs and constructed statistics require a history adapter; no history source is attached.") },
            new() { Header = "Options", Content = optionsPane },
            new() { Header = "Help", Content = Notice("Migration preview", "Arena observations → native model → Avalonia controls. Replay only; game memory, login, HSReplay acquisition and overlays are absent. No actions are sent to Hearthstone.") }
        }, SelectedIndex = 1 };
        var menu = new Menu { ItemsSource = new MenuItem[] {
            Missing("New", "Deck creation is not migrated."), Missing("Import", "Deck importing is not migrated."),
            Missing("Export", "Deck exporting is not migrated."), Navigate("Stats", 2),
            Missing("HSReplay.net", "Account login and authenticated acquisition are not migrated."), Navigate("Options", 3), Navigate("Help", 4)
        } };
        var root = new DockPanel();
        DockPanel.SetDock(menu, Dock.Top); root.Children.Add(menu);
        var footer = new Border { Padding = new(16, 10), Background = new SolidColorBrush(Color.Parse("#182831")), Child = _status };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(_tabs); Content = root;
        if (options.Replay is not null) TryLoad(options.Replay);
        if (options.ProtonState is not null)
        {
            var reader = new ProtonStateReader(options.ProtonState);
            var connection = Text("Reading Proton status…", 13);
            left.Children.Insert(2, connection);
            void Refresh()
            {
                try { connection.Text = reader.Read(DateTimeOffset.UtcNow); }
                catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidDataException or UnauthorizedAccessException)
                { connection.Text = $"Proton status unavailable: {error.Message}"; }
            }
            _protonTimer = new() { Interval = TimeSpan.FromSeconds(1) };
            _protonTimer.Tick += (_, _) => Refresh();
            Closed += (_, _) => _protonTimer.Stop();
            Refresh(); _protonTimer.Start();
        }
    }
    private MenuItem Navigate(string label, int page)
    {
        var item = new MenuItem { Header = label }; item.Click += (_, _) => _tabs.SelectedIndex = page; return item;
    }
    private MenuItem Missing(string label, string reason) => new() { Header = label, IsEnabled = false, [ToolTip.TipProperty] = reason };
    private static StackPanel Notice(string heading, string body) => new() { Margin = new(24), Spacing = 16, Children = { Text(heading, 26), Text(body, 16) } };
    private static TextBlock Text(string text, double size) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
    public void Load(IArenaObservationSource source)
    {
        var frames = source.Read(); // Validate completely before replacing the current view.
        if (frames.Count == 0) throw new InvalidDataException("Source returned no observations.");
        _frames = frames; _index = 0; _status.Text = source.Description; Render();
    }
    private void TryLoad(string path)
    {
        try { Load(new ReplaySource(path)); }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidDataException or UnauthorizedAccessException)
        { _status.Text = $"Replay could not be loaded: {error.Message}"; }
    }
    private void SaveSettings()
    {
        var updated = new TrackerSettings(ScoresToggle.IsChecked == true, SynergiesToggle.IsChecked == true);
        try { _store.Save(updated); _settings = updated; Render(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _status.Text = $"Preferences could not be saved: {error.Message}";
            ScoresToggle.IsChecked = _settings.ShowArenasmithScore;
            SynergiesToggle.IsChecked = _settings.ShowArenaDeckSynergies;
        }
    }
    private void Render()
    {
        _offers.Children.Clear(); _deck.Children.Clear();
        if (_frames.Count == 0) return;
        var frame = _frames[_index];
        _summary.Text = $"{frame.Hero} • {(frame.Underground ? "Underground Arena" : "Arena")} • {frame.Mode} • {frame.PickedDeck.Length} cards picked";
        _observation.Text = $"Observation {_index + 1}/{_frames.Count} • {frame.Description}";
        NextButton.IsEnabled = _index + 1 < _frames.Count; ResetButton.IsEnabled = _index > 0;
        foreach (var offer in frame.Offers)
        {
            var lines = new StackPanel { Spacing = 6, Children = { Text($"{offer.Card.Mana} mana   {offer.Card.Name}", 21), Text(offer.Card.Id, 12) } };
            if (_settings.ShowArenasmithScore) lines.Children.Add(Text($"Arenasmith {offer.DisplayScore} • plaque {offer.Plaque}", 18));
            if (_settings.ShowArenaDeckSynergies) lines.Children.Add(Text($"Deck synergies: {offer.SynergyCount(frame.PickedDeck)}", 14));
            _offers.Children.Add(new Border { Padding = new(16), CornerRadius = new(6), BorderThickness = new(2), BorderBrush = new SolidColorBrush(Color.Parse(frame.Underground ? "#932020" : "#067F93")), Child = lines });
        }
        foreach (var group in frame.PickedDeck.GroupBy(card => card.Id).OrderBy(g => g.First().Mana).ThenBy(g => g.First().Name))
            _deck.Children.Add(Text($"{group.First().Mana}   {group.First().Name} ×{group.Count()}", 16));
        if (frame.PickedDeck.Length == 0) _deck.Children.Add(Text("No picks observed yet.", 15));
    }
}
