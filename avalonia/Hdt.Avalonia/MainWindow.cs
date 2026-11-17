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
    private readonly NativeObservationReader? _nativeReader;
    private readonly global::Avalonia.Threading.DispatcherTimer? _nativeTimer;
    private bool _refreshingNative;
    private DateTimeOffset _nativeObservedAt;
    private readonly Dictionary<string, string?> _matchDecks = new();
    public NativeGameView GameView { get; } = new();
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
    public NativeOverlayControls NativeOverlay { get; }
    public SettingsView SettingsPanel { get; }
    public Button NextButton { get; } = new() { Content = "Next observation", IsEnabled = false };
    public Button ResetButton { get; } = new() { Content = "Rewind", IsEnabled = false };
    public CheckBox ScoresToggle { get; } = new() { Content = "Show Arenasmith scores" };
    public CheckBox SynergiesToggle { get; } = new() { Content = "Show deck synergies" };
    public int OfferCount => _offers.Children.OfType<Border>().Count();
    public int PickedCount => _frames.Count == 0 ? 0 : _frames[_index].PickedDeck.Length;
    public string Summary => _summary.Text ?? "";
    public string Status => _status.Text ?? "";
    public IReadOnlyList<string> Scores => _frames.Count == 0 || !_settings.ShowArenasmithScore || _frames[_index].SchemaVersion == 3
        ? [] : _frames[_index].CardOffers.Select(o => o.DisplayScore).ToArray();
    public int SelectedPage => _tabs.SelectedIndex;

    public MainWindow(LaunchOptions options)
    {
        Title = "Hearthstone Deck Tracker • Avalonia migration preview";
        Width = 1120; Height = 760; MinWidth = 800; MinHeight = 540;
        Background = new SolidColorBrush(Color.Parse("#151B21"));
        FontSize = 13;
        _store = new(options.Settings);
        Library = new(options.ClientData ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(options.Settings))!, "library.json"), _lifetime.Token);
        DeckLibrary = new(Library, _lifetime.Token);
        NativeOverlay = new(options.NativeState is not null && options.SessionRole is null, _lifetime.Token, options.SessionRole is null ? null : "Independent overlay lifetime belongs to the native session owner. This main window does not render or control it.");
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
        var deck = new StackPanel { Spacing = 12, Margin = new(20, 0, 0, 0), Children = { Text("PICKED CARDS", 16), _deck } };
        Grid.SetColumn(deck, 1); content.Children.Add(new ScrollViewer { Content = deck, [Grid.ColumnProperty] = 1 });
        var optionsPane = new StackPanel
        {
            Margin = new(4),
            Spacing = 16,
            Children = {
            Text("Arena draft assistance", 18), ScoresToggle, SynergiesToggle, _preferencesStatus,
            Text("These preferences affect the native draft view and are saved locally.", 15),
            Text("Hero identities require an explicit observation source. Hero statistics, related-card tooltips and redraft suggestions are unavailable.", 13) }
        };
        SettingsPanel = new(optionsPane, NativeOverlay, new SettingsPaths(options.Settings,
            options.ClientData ?? "library.json beside the preferences file", options.NativeState ?? "No source configured."));
        _tabs = new TabControl
        {
            ItemsSource = new TabItem[] {
            new() { Header = "My Decks", Content = DeckLibrary },
            new() { Header = "Arena Draft", Content = content },
            new() { Header = "Stats", Content = new HistoryView(Library, new HistoryScope.All()) },
            new() { Header = "Options", Content = SettingsPanel },
            new() { Header = "Help", Content = Notice("Migration preview", "Native deck library and Arena rendering. Optional read-only Proton observations provide known cards and supplied history; native acquisition, account login and combat odds are not implemented. Native X11 overlays require explicit opt-in. No actions are sent to Hearthstone.") },
            new() { Header = "Live Game", Content = GameView }
        },
            FontSize = 14,
            SelectedIndex = options.Replay is null ? 0 : 1
        };
        foreach (var tab in _tabs.Items.Cast<TabItem>())
            tab.Header = new TextBlock { Text = (string)tab.Header!, FontSize = 14 };
        var menu = new Menu
        {
            ItemsSource = new MenuItem[] {
            LibraryAction("New", () => { _tabs.SelectedIndex = 0; DeckLibrary.NewDeck(); }),
            LibraryAction("Deck", () => { _tabs.SelectedIndex = 0; DeckLibrary.EditSelected(); }),
            LibraryAction("Import", () => { _tabs.SelectedIndex = 0; DeckLibrary.FocusImport(); }),
            LibraryAction("Export", () => { _tabs.SelectedIndex = 0; DeckLibrary.ExportSelected(); }), Navigate("Stats", 2),
            Missing("HSReplay.net", "Account login and authenticated acquisition are not migrated."), Navigate("Options", 3), Navigate("Help", 4), Navigate("Live Game", 5)
        }
        };
        var root = new DockPanel();
        var brand = new Grid { ColumnDefinitions = new("*,Auto"), Background = TrackerLayout.Accent };
        brand.Children.Add(new TextBlock { Text = "HEARTHSTONE DECK TRACKER", Margin = new(12, 7), FontSize = 15, FontWeight = FontWeight.SemiBold });
        brand.Children.Add(new TextBlock { Text = "NATIVE PREVIEW", Margin = new(12, 7), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, [Grid.ColumnProperty] = 1 });
        DockPanel.SetDock(brand, Dock.Top); root.Children.Add(brand);
        menu.Background = TrackerLayout.Surface;
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
        if (options.NativeState is not null)
        {
            if (options.Replay is not null) throw new ArgumentException("Choose one Arena acquisition source explicitly.");
            _nativeReader = new NativeObservationReader(options.NativeState);
            _nativeTimer = new() { Interval = TimeSpan.FromSeconds(1) };
            _nativeTimer.Tick += async (_, _) =>
            {
                if (_nativeObservedAt != default && (DateTimeOffset.UtcNow - _nativeObservedAt).TotalSeconds > 5)
                    ClearNativeFacts("Last observation is stale.");
                await RefreshNativeAsync();
            };
            _tabs.SelectedIndex = 5;
        }
        Closed += (_, _) => { _lifetime.Cancel(); _protonTimer?.Stop(); _nativeTimer?.Stop(); NativeOverlay.Stop(); };
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
        ScoresToggle.IsEnabled = SynergiesToggle.IsEnabled = true;
        _openButton.IsEnabled = _nativeReader is null;
        try { await Library.ReloadAsync(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
        catch (Exception error) when (InputFailure(error)) { /* The library exposes its explicit failure and reload action. */ }
        if (options.Replay is not null) await TryLoadAsync(options.Replay);
        if (_lifetime.IsCancellationRequested) return;
        await RefreshProtonAsync();
        await RefreshNativeAsync();
        if (!_lifetime.IsCancellationRequested) { _protonTimer?.Start(); _nativeTimer?.Start(); }
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

    public async Task RefreshNativeAsync()
    {
        if (_nativeReader is null || _refreshingNative || _lifetime.IsCancellationRequested) return;
        _refreshingNative = true;
        try
        {
            var observation = await _nativeReader.ReadAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            _nativeObservedAt = observation.ObservedAt;
            GameView.Apply(NativeGamePresentation.Project(observation));
            NativeOverlay.Publish(observation, _settings);
            _frames = [observation.Arena!]; _index = 0; Render();
            _status.Text = $"Game observation file • {observation.Mode} • updated {observation.ObservedAt.ToLocalTime():T}";
            if (observation.MatchId is not null && !observation.InMenu && observation.IsRunning
                && !observation.CompletedMatches!.Any(match => match.Id == observation.MatchId)
                && !_matchDecks.ContainsKey(observation.MatchId))
            {
                if (_matchDecks.Count >= 256) _matchDecks.Clear();
                _matchDecks.Add(observation.MatchId, Library.State.SelectedDeckId);
            }
            var existingById = Library.State.Matches.ToDictionary(match => match.Id);
            foreach (var supplied in observation.CompletedMatches!)
            {
                existingById.TryGetValue(supplied.Id, out var existing);
                var deck = existing?.DeckId ?? (_matchDecks.TryGetValue(supplied.Id, out var selected) ? selected : null);
                var record = supplied with { DeckId = deck };
                if (record == existing) continue;
                if (Library.Busy || !Library.Ready) { _status.Text += " • history pending: reload/finish library action"; break; }
                await Library.RecordAsync(record);
                _status.Text += " • history record synchronized";
                break; // Bound history work per poll; keep current game observations responsive.
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (InputFailure(error))
        {
            if (_lifetime.IsCancellationRequested) return;
            ClearNativeFacts(error.Message);
        }
        finally { _refreshingNative = false; }
    }

    private void ClearNativeFacts(string reason)
    {
        NativeOverlay.Publish(null, _settings);
        GameView.Unavailable(reason); _frames = []; _index = 0; Render();
        _summary.Text = "Arena observations unavailable"; _observation.Text = "Previous offers and picks cleared.";
        _status.Text = $"Native observation/history unavailable: {reason}";
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
            NativeOverlay.UpdateSettings(_settings);
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
        if (_frames.Count == 0) { NextButton.IsEnabled = ResetButton.IsEnabled = false; return; }
        var frame = _frames[_index];
        _summary.Text = ArenaPresentation.Heading(frame);
        _observation.Text = $"Observation {_index + 1}/{_frames.Count} • {frame.Description}";
        NextButton.IsEnabled = _index + 1 < _frames.Count; ResetButton.IsEnabled = _index > 0;
        foreach (var choice in ArenaPresentation.Choices(frame, _settings, 21)) _offers.Children.Add(choice);
        var picked = frame.SchemaVersion == 3 ? frame.CachedDeck!.Cards : frame.PickedDeck;
        if (frame.SchemaVersion == 3) _deck.Children.Add(Text("Cached HDT tile model • choice correlation unknown. Not draft pick progress.", 13));
        foreach (var group in picked.GroupBy(card => card.Id).OrderBy(g => g.First().Mana).ThenBy(g => g.First().Name))
            _deck.Children.Add(Text($"{group.First().Mana}   {group.First().Name} ×{group.Count()}", 16));
        if (picked.Length == 0) _deck.Children.Add(Text(frame.SchemaVersion == 3 ? "No cached picked cards supplied." : "No picks observed yet.", 15));
    }
}
