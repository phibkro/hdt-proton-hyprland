using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Hdt.Avalonia;

public sealed class MainWindow : Window
{
    private readonly SettingsStore _store;
    private TrackerSettings _settings = new();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _updatingSettings, _savingSettings, _refreshingProton, _loadingReplay;
    private readonly TextBlock _preferencesStatus = Text("Loading local preferences…", 13);
    private readonly TextBlock _connection = Text("Reading Proton status…", 13);
    private readonly ProtonStateReader? _protonReader;
    private readonly Button _openButton = new() { Content = "Open replay…", IsEnabled = false };
    public Task Initialization { get; }
    public Task PendingPreferences { get; private set; } = Task.CompletedTask;
    public string PreferencesStatus => _preferencesStatus.Text ?? "";
    public string ConnectionStatus => _connection.Text ?? "";
    private IReadOnlyList<ArenaObservation> _frames = [];
    private int _index;
    private readonly StackPanel _offers = new() { Spacing = 12 };
    private readonly StackPanel _deck = new() { Spacing = 6 };
    private readonly TextBlock _summary = Text("Open an Arena observation replay to begin.", 22);
    private readonly TextBlock _status = Text("No source attached • native interface • migration preview", 13);
    private readonly TextBlock _observation = Text("No observations", 14);
    private readonly TabControl _tabs;
    private readonly global::Avalonia.Threading.DispatcherTimer? _protonTimer;
    public ClientLibrary Library { get; }
    public LibraryView DeckLibrary { get; }
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
        Library = new(options.ClientData ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(options.Settings))!, "library.json"), _lifetime.Token);
        DeckLibrary = new(Library);
        ScoresToggle.IsEnabled = SynergiesToggle.IsEnabled = false;
        ScoresToggle.IsCheckedChanged += (_, _) => StartPreferencesSave();
        SynergiesToggle.IsCheckedChanged += (_, _) => StartPreferencesSave();
        NextButton.Click += (_, _) => { if (_index + 1 < _frames.Count) { _index++; Render(); } };
        ResetButton.Click += (_, _) => { _index = 0; Render(); };
        _openButton.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Open Arena observation replay", AllowMultiple = false });
            if (files.Count == 0) return;
            var path = files[0].TryGetLocalPath();
            if (path is null) { _status.Text = "Only local replay files are supported."; return; }
            await TryLoadAsync(path);
        };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { _openButton, ResetButton, NextButton } };
        var content = new Grid { ColumnDefinitions = new("*,280"), Margin = new(16) };
        var left = new StackPanel { Spacing = 16, Children = { _summary, _observation, controls, _offers } };
        content.Children.Add(new ScrollViewer { Content = left });
        var deck = new StackPanel { Spacing = 12, Margin = new(20, 0, 0, 0), Children = { Text("PICKED DECK", 16), _deck } };
        Grid.SetColumn(deck, 1); content.Children.Add(new ScrollViewer { Content = deck, [Grid.ColumnProperty] = 1 });
        var optionsPane = new StackPanel
        {
            Margin = new(24),
            Spacing = 20,
            Children = {
            Text("Options › Overlay › Arena", 24), ScoresToggle, SynergiesToggle, _preferencesStatus,
            Text("These preferences affect the native draft view and are saved locally.", 15),
            Text("Hero selection, related-card tooltips and redraft-discard suggestions are not migrated yet.", 15) }
        };
        _tabs = new TabControl
        {
            ItemsSource = new TabItem[] {
            new() { Header = "My Decks", Content = DeckLibrary },
            new() { Header = "Arena Draft", Content = content },
            new() { Header = "Stats", Content = new HistoryView(Library) },
            new() { Header = "Options", Content = optionsPane },
            new() { Header = "Help", Content = Notice("Migration preview", "Arena observations → native model → Avalonia controls. Replay only; game memory, login, HSReplay acquisition and overlays are absent. No actions are sent to Hearthstone.") }
        },
            SelectedIndex = 1
        };
        var menu = new Menu
        {
            ItemsSource = new MenuItem[] {
            LibraryAction("New", () => { _tabs.SelectedIndex = 0; DeckLibrary.NewDeck(); }),
            LibraryAction("Import", () => { _tabs.SelectedIndex = 0; DeckLibrary.FocusImport(); }),
            LibraryAction("Export", () => { _tabs.SelectedIndex = 0; DeckLibrary.ExportSelected(); }), Navigate("Stats", 2),
            Missing("HSReplay.net", "Account login and authenticated acquisition are not migrated."), Navigate("Options", 3), Navigate("Help", 4)
        }
        };
        var root = new DockPanel();
        DockPanel.SetDock(menu, Dock.Top); root.Children.Add(menu);
        var footer = new Border { Padding = new(16, 10), Background = new SolidColorBrush(Color.Parse("#182831")), Child = _status };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(_tabs); Content = root;
        if (options.ProtonState is not null)
        {
            _protonReader = new ProtonStateReader(options.ProtonState);
            left.Children.Insert(2, _connection);
            _protonTimer = new() { Interval = TimeSpan.FromSeconds(1) };
            _protonTimer.Tick += async (_, _) => await RefreshProtonAsync();
        }
        Closed += (_, _) => { _lifetime.Cancel(); _protonTimer?.Stop(); };
        Initialization = InitializeAsync(options);
    }
    private async Task InitializeAsync(LaunchOptions options)
    {
        try
        {
            _settings = await _store.LoadAsync(_lifetime.Token);
            _preferencesStatus.Text = "Local preferences loaded.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
        catch (Exception error) when (InputFailure(error))
        { _preferencesStatus.Text = $"Preferences unavailable; defaults shown: {error.Message}"; }
        if (_lifetime.IsCancellationRequested) return;
        RestoreToggles();
        ScoresToggle.IsEnabled = SynergiesToggle.IsEnabled = _openButton.IsEnabled = true;
        try { await Library.ReloadAsync(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
        catch (Exception error) when (InputFailure(error)) { /* The library exposes its explicit failure and reload action. */ }
        if (options.Replay is not null) await TryLoadAsync(options.Replay);
        if (_lifetime.IsCancellationRequested) return;
        await RefreshProtonAsync();
        if (!_lifetime.IsCancellationRequested) _protonTimer?.Start();
    }
    private static bool InputFailure(Exception error) => error is IOException or System.Text.Json.JsonException
        or InvalidDataException or UnauthorizedAccessException;
    public async Task RefreshProtonAsync()
    {
        if (_protonReader is null || _refreshingProton || _lifetime.IsCancellationRequested) return;
        _refreshingProton = true;
        try
        {
            var text = await _protonReader.ReadAsync(cancellation: _lifetime.Token);
            if (!_lifetime.IsCancellationRequested) _connection.Text = text;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (InputFailure(error))
        { if (!_lifetime.IsCancellationRequested) _connection.Text = $"Proton status unavailable: {error.Message}"; }
        finally { _refreshingProton = false; }
    }

    private static MenuItem LibraryAction(string label, Action action)
    {
        var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); return item;
    }
    private MenuItem Navigate(string label, int page)
    {
        var item = new MenuItem { Header = label }; item.Click += (_, _) => _tabs.SelectedIndex = page; return item;
    }
    private MenuItem Missing(string label, string reason) => new() { Header = label, IsEnabled = false, [ToolTip.TipProperty] = reason };
    private static StackPanel Notice(string heading, string body) => new() { Margin = new(24), Spacing = 16, Children = { Text(heading, 26), Text(body, 16) } };
    private static TextBlock Text(string text, double size) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
    public async Task LoadAsync(IArenaObservationSource source)
    {
        var frames = await source.ReadAsync(_lifetime.Token);
        _lifetime.Token.ThrowIfCancellationRequested();
        if (frames.Count == 0) throw new InvalidDataException("Source returned no observations.");
        _frames = frames; _index = 0; _status.Text = source.Description; Render();
    }
    private async Task TryLoadAsync(string path)
    {
        if (_loadingReplay) return;
        _loadingReplay = true; _openButton.IsEnabled = false;
        try { await LoadAsync(new ReplaySource(path)); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (InputFailure(error))
        { if (!_lifetime.IsCancellationRequested) _status.Text = $"Replay could not be loaded: {error.Message}"; }
        finally { _loadingReplay = false; if (!_lifetime.IsCancellationRequested) _openButton.IsEnabled = true; }
    }
    private void StartPreferencesSave()
    {
        if (_updatingSettings || _savingSettings || _lifetime.IsCancellationRequested) return;
        PendingPreferences = SavePreferencesAsync();
    }
    private void RestoreToggles()
    {
        _updatingSettings = true;
        try
        {
            ScoresToggle.IsChecked = _settings.ShowArenasmithScore;
            SynergiesToggle.IsChecked = _settings.ShowArenaDeckSynergies;
        }
        finally { _updatingSettings = false; }
    }
    private async Task SavePreferencesAsync()
    {
        _savingSettings = true;
        ScoresToggle.IsEnabled = SynergiesToggle.IsEnabled = false;
        _preferencesStatus.Text = "Saving local preferences…";
        var updated = new TrackerSettings(ScoresToggle.IsChecked == true, SynergiesToggle.IsChecked == true);
        try
        {
            await _store.SaveAsync(updated);
            if (_lifetime.IsCancellationRequested) return;
            _settings = updated; _preferencesStatus.Text = "Local preferences saved."; Render();
        }
        catch (Exception error) when (InputFailure(error))
        {
            if (_lifetime.IsCancellationRequested) return;
            _preferencesStatus.Text = $"Preferences could not be saved: {error.Message}";
            RestoreToggles(); // Guarded rollback must never enqueue another write.
        }
        finally
        {
            _savingSettings = false;
            if (!_lifetime.IsCancellationRequested) ScoresToggle.IsEnabled = SynergiesToggle.IsEnabled = true;
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
