using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hdt.Avalonia;

// Test-only protocol. Production MainWindow/Options are driven by actual X11
// pointer events from the private Rust fixture; no control setters invoke actions.
public sealed class OverlayDesktopAcceptance : Application
{
    private static string _root = "";
    public static int Run(string directory)
    {
        if (Environment.GetEnvironmentVariable("HDT_TEST_X11_ISOLATED") != "1")
            throw new InvalidOperationException("Desktop acceptance requires the private namespace driver.");
        _root = Path.GetFullPath(directory); Directory.CreateDirectory(_root);
        return AppBuilder.Configure<OverlayDesktopAcceptance>().UsePlatformDetect().StartWithClassicDesktopLifetime([]);
    }
    public override void Initialize()
    {
        RequestedThemeVariant = global::Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow(new(null, Path.Combine(_root, "settings.json"), ClientData: Path.Combine(_root, "library.json"), NativeState: Path.Combine(_root, "observation.json")))
            { Title = "Owned native overlay UI acceptance" };
            desktop.MainWindow = window;
            window.Opened += async (_, _) =>
            {
                try { await window.Initialization; _ = Task.Run(() => DriveAsync(window)); }
                catch (Exception error) { Console.Error.WriteLine(error.Message); desktop.Shutdown(1); }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
    private static async Task DriveAsync(MainWindow window)
    {
        try
        {
            await ReplyAsync(new { kind = "ready", pid = Environment.ProcessId });
            var stream = Console.OpenStandardInput();
            while (true)
            {
                var line = new List<byte>(); var one = new byte[1];
                while (line.Count <= 8192)
                {
                    if (await stream.ReadAsync(one) == 0) throw new IOException("Desktop fixture command stream closed.");
                    if (one[0] == (byte)'\n') break;
                    line.Add(one[0]);
                }
                if (line.Count > 8192) throw new IOException("Desktop fixture command exceeds bound.");
                using var json = JsonDocument.Parse(line.ToArray());
                var action = json.RootElement.GetProperty("action").GetString();
                if (action == "point")
                {
                    var target = json.RootElement.GetProperty("target").GetString();
                    Control Locate() => target switch
                    {
                        "options" => (TabItem)window.GetVisualDescendants().OfType<TabControl>().Single().Items[3]!,
                        "overlay-settings" => window.SettingsPanel.OverlayCategory,
                        "find" => window.NativeOverlay.RefreshButton,
                        "enabled" => window.NativeOverlay.EnabledToggle,
                        _ => throw new IOException("Unknown private control target.")
                    };
                    await Dispatcher.UIThread.InvokeAsync(() => { Locate().BringIntoView(); window.UpdateLayout(); });
                    await Task.Delay(100);
                    var point = await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var control = Locate();
                        if (!control.IsEffectivelyVisible || !control.IsEffectivelyEnabled) throw new IOException("Control is unavailable.");
                        var value = control.PointToScreen(new(control.Bounds.Width / 2, control.Bounds.Height / 2));
                        return new { kind = "point", x = value.X, y = value.Y };
                    });
                    await ReplyAsync(point);
                }
                else if (action == "inspect")
                {
                    var state = await Dispatcher.UIThread.InvokeAsync(() => new
                    {
                        kind = "state",
                        page = window.SelectedPage,
                        status = window.NativeOverlay.Status,
                        enabled = window.NativeOverlay.EnabledToggle.IsChecked == true,
                        idle = window.NativeOverlay.PendingChange.IsCompleted,
                        ownerCompleted = window.NativeOverlay.OwnerCompletion.IsCompleted,
                        choices = window.NativeOverlay.GameWindows.ItemCount,
                        source = window.GameView.ModeText,
                        cards = window.GameView.CardCount
                    });
                    await ReplyAsync(state);
                }
                else if (action == "expected")
                {
                    var value = await SnapshotFiles.ReadJsonAsync<NativeObservation>(Path.Combine(_root, "observation.json"), 2 * 1024 * 1024);
                    var spec = await SnapshotFiles.InvokeJsonAsync<OverlaySpec>(["native-overlay", "info"], SnapshotFiles.StatusBytes);
                    var raster = await Dispatcher.UIThread.InvokeAsync(() => OverlayRendering.Render(value, new TrackerSettings(), spec, Path.Combine(_root, "expected-overlay.png")));
                    await File.WriteAllBytesAsync(Path.Combine(_root, "expected.bgra"), raster.Pixels);
                    await ReplyAsync(new { kind = "expected", width = spec.Width, height = spec.Height });
                }
                else if (action == "capture")
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        using var bitmap = new RenderTargetBitmap(new((int)window.Bounds.Width, (int)window.Bounds.Height));
                        bitmap.Render(window); bitmap.Save(Path.Combine(_root, "options.png"), PngBitmapEncoderOptions.Default);
                    });
                    await ReplyAsync(new { kind = "capture" });
                }
                else if (action == "close")
                {
                    await window.NativeOverlay.OwnerCompletion.WaitAsync(TimeSpan.FromSeconds(3));
                    await ReplyAsync(new { kind = "closed" });
                    await Dispatcher.UIThread.InvokeAsync(window.Close);
                    return;
                }
                else throw new IOException("Unknown private desktop action.");
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            await Dispatcher.UIThread.InvokeAsync(() => ((IClassicDesktopStyleApplicationLifetime)Current!.ApplicationLifetime!).Shutdown(1));
        }
    }
    private static async Task ReplyAsync(object value)
    { await Console.Out.WriteLineAsync(JsonSerializer.Serialize(value)); await Console.Out.FlushAsync(); }
}
