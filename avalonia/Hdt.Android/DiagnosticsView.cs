using System.Diagnostics;
#if HDT_ANDROID_DEV
using Android.OS;
using Android.Provider;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Button = Avalonia.Controls.Button;
using Orientation = Avalonia.Layout.Orientation;
#endif

namespace Hdt.Mobile;

internal static class AndroidDiagnostics
{
    [Conditional("HDT_ANDROID_DEV")]
    internal static void Record(string message)
    {
#if HDT_ANDROID_DEV
        if (events.Count == HistoryLimit)
            events.Dequeue();
        events.Enqueue($"[{DateTimeOffset.Now:HH:mm:ss}] {message}");
        Refresh();
#endif
    }

    [Conditional("HDT_ANDROID_DEV")]
    internal static void Sample(string message)
    {
#if HDT_ANDROID_DEV
        latestSample = $"[{DateTimeOffset.Now:HH:mm:ss}] {message}";
        Refresh();
#endif
    }

    [Conditional("HDT_ANDROID_DEV")]
    internal static void Refresh()
    {
#if HDT_ANDROID_DEV
        Changed?.Invoke();
#endif
    }

#if HDT_ANDROID_DEV
    // Capture status and results are published on Android's main thread.
    private const int HistoryLimit = 40;
    private static readonly Queue<string> events = new(HistoryLimit);
    private static string latestSample = "Waiting for capture.";
    internal static event Action? Changed;

    internal static string Report()
    {
        var permission = MainActivity.Current is { } activity
            ? Settings.CanDrawOverlays(activity) ? "allowed" : "denied"
            : "unavailable";
        return string.Join('\n',
            $"HDT Android Dev • Android API {(int)Build.VERSION.SdkInt}",
            $"Overlay permission: {permission}",
            $"Tracking: {TrackingSession.Current.Status}",
            "",
            "Latest sample",
            latestSample,
            "",
            "Recent events",
            string.Join('\n', events.Reverse()));
    }

    internal static void Clear()
    {
        events.Clear();
        latestSample = "Waiting for capture.";
        Refresh();
    }
#endif
}

#if HDT_ANDROID_DEV
internal sealed class DiagnosticsView : UserControl
{
    private readonly TextBlock report = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock feedback = new() { TextWrapping = TextWrapping.Wrap };

    internal DiagnosticsView()
    {
        var copy = new Button { Content = "Copy report" };
        copy.Click += async (_, _) =>
        {
            try
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard
                    ?? throw new InvalidOperationException("Clipboard is unavailable.");
                await clipboard.SetTextAsync(AndroidDiagnostics.Report());
                feedback.Text = "Report copied.";
            }
            catch (Exception)
            {
                feedback.Text = "Could not copy the report.";
            }
        };
        var clear = new Button { Content = "Clear" };
        clear.Click += (_, _) =>
        {
            AndroidDiagnostics.Clear();
            feedback.Text = "Diagnostics cleared.";
        };

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24, 32),
                MaxWidth = 540,
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Capture diagnostics",
                        FontSize = 24,
                        FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = "Reports include capture metrics and errors. Screenshots, OCR text and game logs are excluded.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { copy, clear } },
                    feedback,
                    report
                }
            }
        };
        AttachedToVisualTree += (_, _) =>
        {
            AndroidDiagnostics.Changed += Refresh;
            Refresh();
        };
        DetachedFromVisualTree += (_, _) => AndroidDiagnostics.Changed -= Refresh;
    }

    private void Refresh() => report.Text = AndroidDiagnostics.Report();
}
#endif
