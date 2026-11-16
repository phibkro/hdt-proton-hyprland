using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Hdt.Avalonia;

public sealed class NativeOverlayControls : UserControl
{
    private readonly bool _configured;
    private readonly CancellationToken _lifetime;
    private NativeOverlayActor? _actor;
    private OverlaySpec? _spec;
    private NativeObservation? _observation;
    private TrackerSettings _settings = new();
    private bool _changing, _restoring, _refreshing;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    public CheckBox EnabledToggle { get; } = new() { Content = "Show native tracking overlay" };
    public ComboBox GameWindows { get; } = new() { MinWidth = 340, PlaceholderText = "Choose a Hearthstone window" };
    public Button RefreshButton { get; } = new() { Content = "Find Hearthstone windows" };
    public Task PendingChange { get; private set; } = Task.CompletedTask;
    public Task OwnerCompletion => _actor?.Completion ?? Task.CompletedTask;
    public string Status => _status.Text ?? "";
    public NativeOverlayControls(bool configured, CancellationToken lifetime)
    {
        _configured = configured; _lifetime = lifetime;
        _status.Text = configured ? "Find and choose a game window to enable the native overlay." : "Attach a native observation source to enable the overlay.";
        EnabledToggle.IsCheckedChanged += (_, _) =>
        {
            if (!_restoring && !_changing) PendingChange = ChangeAsync();
        };
        RefreshButton.Click += (_, _) => { if (!_refreshing) PendingChange = RefreshAsync(); };
        Content = new StackPanel
        {
            Spacing = 12,
            Children = {
            new TextBlock { Text = "Native overlay preview", FontSize = 22 },
            new TextBlock { Text = "An opaque tracking panel follows the selected X11 game window. All game input passes through. It hides when the game is inactive or observations are stale.", TextWrapping = TextWrapping.Wrap },
            RefreshButton, GameWindows, EnabledToggle, _status }
        };
        Availability();
    }
    private async Task RefreshAsync()
    {
        _refreshing = true; Availability();
        try
        {
            var choices = await SnapshotFiles.InvokeJsonAsync<GameWindowChoice[]>(["native-overlay", "list"], SnapshotFiles.StatusBytes, _lifetime);
            if (choices.Length > 2048 || choices.Any(game => game is null || game.Window == 0 || game.Pid == 0 || string.IsNullOrEmpty(game.Class) || game.Class.Length > 128))
                throw new InvalidDataException("Invalid game window choices.");
            GameWindows.ItemsSource = choices;
            GameWindows.SelectedItem = choices.Length == 1 ? choices[0] : null;
            _status.Text = choices.Length == 0 ? "No X11 Hearthstone window found." : "Choose the game window, enable the overlay, then return to Hearthstone.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { _status.Text = $"Game window discovery unavailable: {error.Message}"; }
        finally { _refreshing = false; Availability(); }
    }
    private async Task ChangeAsync()
    {
        _changing = true; Availability();
        try
        {
            if (EnabledToggle.IsChecked == true)
            {
                if (!_configured || GameWindows.SelectedItem is not GameWindowChoice game)
                    throw new IOException("Find and choose a Hearthstone window first.");
                _status.Text = "Preparing input-empty native overlay…";
                _actor = new NativeOverlayActor(game, message => Dispatcher.UIThread.Post(() => { if (!_lifetime.IsCancellationRequested) _status.Text = message; }), _lifetime);
                _spec = await _actor.Ready.WaitAsync(TimeSpan.FromSeconds(3), _lifetime);
                Publish(_observation, _settings);
            }
            else
            {
                if (_actor is not null) await _actor.StopAsync();
                _actor = null; _spec = null; _status.Text = "Native overlay stopped.";
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            _status.Text = $"Native overlay unavailable: {error.Message}";
            // Retain the owner and disable restart until its exact child exits.
            if (_actor is not null) await _actor.StopAsync();
            _actor = null; _spec = null;
            _restoring = true; try { EnabledToggle.IsChecked = false; } finally { _restoring = false; }
        }
        finally { _changing = false; Availability(); }
    }
    public void Publish(NativeObservation? value, TrackerSettings settings)
    {
        _observation = value; _settings = settings;
        if (_actor is null || _spec is null || _actor.Completion.IsCompleted) return;
        if (value is null || !value.IsRunning || value.Mode is NativeMode.Offline or NativeMode.Menu or NativeMode.Unknown
            || value.Mode == NativeMode.Arena && value.Arena?.Mode != ArenaMode.Drafting)
        { _actor.Offer(null); return; }
        try { _actor.Offer(OverlayRendering.Render(value, settings, _spec)); }
        catch (Exception error) { _actor.Offer(null); _status.Text = $"Native overlay rendering unavailable: {error.Message}"; }
    }
    public void UpdateSettings(TrackerSettings settings) => Publish(_observation, settings);
    public void Stop() => _actor?.Stop();
    private void Availability()
    {
        EnabledToggle.IsEnabled = _configured && !_changing && !_refreshing;
        RefreshButton.IsEnabled = GameWindows.IsEnabled = _configured && !_changing && !_refreshing && _actor is null;
    }
}
