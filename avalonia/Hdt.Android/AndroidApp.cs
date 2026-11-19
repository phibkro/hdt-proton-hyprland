using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Media.Projection;
using Android.Provider;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Button = Avalonia.Controls.Button;
using Orientation = Avalonia.Layout.Orientation;

[assembly: UsesPermission("android.permission.SYSTEM_ALERT_WINDOW")]
[assembly: UsesPermission("android.permission.FOREGROUND_SERVICE")]
[assembly: UsesPermission("android.permission.FOREGROUND_SERVICE_MEDIA_PROJECTION")]
[assembly: UsesPermission("android.permission.INTERNET")]

namespace Hdt.Mobile;

[Application]
public sealed class AndroidApp(nint javaReference, JniHandleOwnership transfer)
    : AvaloniaAndroidApplication<TrackerApp>(javaReference, transfer)
{
}

public sealed class TrackerApp : global::Avalonia.Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IActivityApplicationLifetime mobile)
            mobile.MainViewFactory = () => new TrackerView();
        base.OnFrameworkInitializationCompleted();
    }
}

[Activity(Label = "HDT Android", MainLauncher = true,
    Theme = "@style/Theme.AppCompat.NoActionBar",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
    private const int CaptureRequest = 1;
    internal static MainActivity? Current { get; private set; }

    protected override void OnResume()
    {
        base.OnResume();
        Current = this;
        TrackingSession.Refresh();
    }

    protected override void OnPause()
    {
        if (ReferenceEquals(Current, this))
            Current = null;
        base.OnPause();
    }

    internal void StartTracking()
    {
        if (!Settings.CanDrawOverlays(this))
        {
            using var intent = new Intent(Settings.ActionManageOverlayPermission,
                global::Android.Net.Uri.Parse($"package:{PackageName}"));
            StartActivity(intent);
            TrackingSession.Publish(new("Allow HDT Android to display over other apps, then tap Start tracking again."));
            return;
        }

        using var manager = GetSystemService(MediaProjectionService) as MediaProjectionManager;
        if (manager is null)
        {
            TrackingSession.Publish(new("Screen sharing is unavailable."));
            return;
        }

        using var config = MediaProjectionConfig.CreateConfigForUserChoice();
        using var request = manager.CreateScreenCaptureIntent(config);
        StartActivityForResult(request, CaptureRequest);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != CaptureRequest)
            return;

        if (resultCode != Result.Ok || data is null)
        {
            TrackingSession.Publish(new("Screen sharing was canceled."));
            return;
        }

        using var intent = new Intent(this, typeof(CaptureService));
        intent.PutExtra(CaptureService.ConsentExtra, data);
        try
        {
            StartForegroundService(intent);
            TrackingSession.Publish(new("Starting screen capture…", Active: true));
        }
        catch (Java.Lang.IllegalStateException)
        {
            TrackingSession.Publish(new("Android could not start screen capture. Open the app and try again."));
        }
    }

    internal void StopTracking()
    {
        using var intent = new Intent(this, typeof(CaptureService)).SetAction(CaptureService.StopAction);
        StartService(intent);
    }
}

internal sealed class TrackerView : UserControl
{
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button start = new() { Content = "Start tracking" };
    private readonly Button stop = new() { Content = "Stop tracking" };

    public TrackerView()
    {
        start.Click += (_, _) => MainActivity.Current?.StartTracking();
        stop.Click += (_, _) => MainActivity.Current?.StopTracking();

        var tracking = new ScrollViewer
        {
            Content = new StackPanel
            {
                MaxWidth = 540,
                Margin = new Thickness(24, 32),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = "HDT Android", FontSize = 30, FontWeight = FontWeight.SemiBold },
                    new TextBlock
                    {
                        Text = "Open an English-language Arena draft. Tap Start tracking, select A single app, and choose Hearthstone.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = "Card text is read on this device. Starting a session downloads the card catalog from HearthstoneJSON.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { start, stop } },
                    status
                }
            }
        };

        Content = new TabControl
        {
            SelectedIndex = 0,
            Items =
            {
                new TabItem { Header = "Live tracker", Content = tracking },
                new TabItem { Header = "Arena demo", Content = new ArenaDemoView() },
                new TabItem { Header = "Match demo", Content = new MatchDemoView() },
            },
        };

        AttachedToVisualTree += (_, _) =>
        {
            TrackingSession.Changed += Update;
            Update(TrackingSession.Current);
        };
        DetachedFromVisualTree += (_, _) => TrackingSession.Changed -= Update;
    }

    private void Update(TrackingState state)
    {
        start.IsEnabled = !state.Active;
        stop.IsEnabled = state.Active;
        status.Text = state.Offers is { Length: > 0 } offers
            ? $"{state.Status}\n{string.Join("\n", offers)}"
            : state.Status;
    }
}

internal sealed record TrackingState(string Status, bool Active = false, string[]? Offers = null);

// The activity, capture callbacks and overlay all publish or read on Android's main thread.
internal static class TrackingSession
{
    public static TrackingState Current { get; private set; } = new("Tracking is off.");
    public static event Action<TrackingState>? Changed;

    public static void Publish(TrackingState state)
    {
        Current = state;
        Refresh();
    }

    public static void Refresh() => Changed?.Invoke(Current);
}
