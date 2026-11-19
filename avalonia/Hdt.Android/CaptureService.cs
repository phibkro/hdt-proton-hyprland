using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Hardware.Display;
using Android.Media;
using Android.Media.Projection;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Avalonia.Threading;
using Xamarin.Google.MLKit.Vision.Common;
using Xamarin.Google.MLKit.Vision.Text;
using Xamarin.Google.MLKit.Vision.Text.Latin;
using GmsTask = Android.Gms.Tasks.Task;

namespace Hdt.Mobile;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaProjection)]
public sealed class CaptureService : Service
{
    internal const string ConsentExtra = "capture-consent";
    private const string StopAction = "org.phibkro.hdt.android.STOP";
    private const string Channel = "tracking";
    private const int NotificationId = 1;
    private const long SampleIntervalMs = 750;
    private const long MaxObservationAgeMs = 3_000;

    private readonly Handler main = new(Looper.MainLooper!);
    private readonly CancellationTokenSource lifetime = new();
    private Java.Lang.Runnable? sample;
    private MediaProjection? projection;
    private ProjectionEvents? projectionEvents;
    private VirtualDisplay? display;
    private ImageReader? reader;
    private ITextRecognizer? recognizer;
    private ArenaTitles? titles;
    private Context? windowContext;
    private IWindowManager? windows;
    private TextView? panel;
    private int density;
    private int width;
    private int height;
    private int viewportRevision;
    private bool visible;
    private bool reading;
    private long lastReadAt;
    private long lastFrameTimestamp = -1;
    private string[] previous = [];
#if HDT_ANDROID_FIXTURE
    private int fixtureDiagnosticReads;
#endif

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == StopAction)
        {
            End("Tracking is off.");
            return StartCommandResult.NotSticky;
        }
        if (projection is not null)
            return StartCommandResult.NotSticky;

        using var consent = intent?.GetParcelableExtra(ConsentExtra, Java.Lang.Class.FromType(typeof(Intent))) as Intent;
        if (consent is null)
        {
            End("Screen sharing permission is required.");
            return StartCommandResult.NotSticky;
        }

        try
        {
            ShowNotification();
            using var manager = GetSystemService(MediaProjectionService) as MediaProjectionManager;
            projection = manager?.GetMediaProjection((int)Result.Ok, consent)
                ?? throw new InvalidOperationException("Android did not grant a capture session.");

            projectionEvents = new(this);
            projection.RegisterCallback(projectionEvents, main);
            using var displays = GetSystemService(DisplayService) as DisplayManager;
            using var primary = displays?.GetDisplay(0)
                ?? throw new InvalidOperationException("Android's primary display is unavailable.");
            windowContext = CreateWindowContext(primary, (int)WindowManagerTypes.ApplicationOverlay, null);
            windows = windowContext.GetSystemService(WindowService)?.JavaCast<IWindowManager>()
                ?? throw new InvalidOperationException("Android window manager is unavailable.");
            density = (int)(windowContext.Resources?.DisplayMetrics?.DensityDpi
                ?? global::Android.Util.DisplayMetricsDensity.Default);
            using var bounds = windows.MaximumWindowMetrics.Bounds;
            width = bounds.Width();
            height = bounds.Height();
            reader = NewReader(width, height);
            display = projection.CreateVirtualDisplay("HDT capture", width, height, density,
                (DisplayFlags)VirtualDisplayFlags.AutoMirror, reader.Surface, null, main)
                ?? throw new InvalidOperationException("Android could not start the capture display.");

            Update("Loading the English card catalog…");
            _ = LoadCatalog(lifetime.Token);
        }
        catch (Exception error)
        {
            Fail("Could not start tracking.", error);
        }

        return StartCommandResult.NotSticky;
    }

    private void ShowNotification()
    {
        using var notifications = GetSystemService(NotificationService) as NotificationManager;
        using var channel = new NotificationChannel(Channel, "Hearthstone tracking", NotificationImportance.Low);
        notifications?.CreateNotificationChannel(channel);

        using var stopIntent = new Intent(this, typeof(CaptureService)).SetAction(StopAction);
        using var stop = PendingIntent.GetService(this, 0, stopIntent, PendingIntentFlags.Immutable);
        using var openIntent = new Intent(this, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop);
        using var open = PendingIntent.GetActivity(this, 0, openIntent, PendingIntentFlags.Immutable);
        using var builder = new Notification.Builder(this, Channel)
            .SetContentTitle("HDT Android")
            .SetContentText("Screen sharing is active")
            .SetSmallIcon(global::Android.Resource.Drawable.IcMenuView)
            .SetContentIntent(open)
            .SetOngoing(true);
        using var actionBuilder = new Notification.Action.Builder(null, "Stop tracking", stop);
        using var action = actionBuilder.Build();
        using var notification = builder.AddAction(action).Build();
        StartForeground(NotificationId, notification, ForegroundService.TypeMediaProjection);
    }

    private async Task LoadCatalog(CancellationToken cancellationToken)
    {
        try
        {
#if HDT_ANDROID_FIXTURE
            var catalog = ArenaTitles.FixtureCatalog();
#else
            var catalog = await ArenaTitles.Load(cancellationToken).ConfigureAwait(false);
#endif
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return;
                titles = catalog;
                recognizer = TextRecognition.GetClient(TextRecognizerOptions.DefaultOptions);
                Update("Waiting for three readable Arena card titles…");
                sample = new Java.Lang.Runnable(Sample);
                main.Post(sample);
            });
        }
        catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                    Fail("Could not prepare card recognition. Check your connection and restart tracking.", error);
            });
        }
    }

    private void Sample()
    {
        if (sample is null || lifetime.IsCancellationRequested)
            return;
        main.PostDelayed(sample, SampleIntervalMs);

        if (!visible || width <= height || reader is null)
            return;
        var now = SystemClock.ElapsedRealtime();
        if (now - lastReadAt > MaxObservationAgeMs && previous.Length > 0)
            ClearOffers("Waiting for a fresh Arena draft view…");
        if (reading)
            return;

        try
        {
            var bitmap = CaptureFrame(reader);
            if (bitmap is null)
                return;
            reading = true;
            _ = ReadOffers(bitmap, width, height, viewportRevision, now);
        }
        catch (Exception error)
        {
            Fail("Screen capture stopped.", error);
        }
    }

    private Bitmap? CaptureFrame(ImageReader source)
    {
        using var frame = source.AcquireLatestImage();
        if (frame is null)
            return null;

        try
        {
            if (frame.Timestamp <= lastFrameTimestamp)
                return null;
            lastFrameTimestamp = frame.Timestamp;
            if (frame.GetPlanes() is not { Length: 1 } planes)
                throw new InvalidOperationException("Android did not return an RGBA image.");
            using var plane = planes[0];
            using var buffer = plane.Buffer
                ?? throw new InvalidOperationException("Android returned an empty pixel buffer.");
            var bitmapWidth = plane.RowStride / plane.PixelStride;
            var bitmap = Bitmap.CreateBitmap(bitmapWidth, frame.Height, Bitmap.Config.Argb8888!);
            try
            {
                if (buffer.Remaining() >= bitmap.ByteCount)
                {
                    bitmap.CopyPixelsFromBuffer(buffer);
                }
                else
                {
                    // The source can omit stride padding after the last row.
                    using var padded = Java.Nio.ByteBuffer.AllocateDirect(bitmap.ByteCount)!;
                    padded.Put(buffer);
                    padded.Rewind();
                    bitmap.CopyPixelsFromBuffer(padded);
                }

                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }
        finally
        {
            frame.Close();
        }
    }

    private async Task ReadOffers(Bitmap bitmap, int frameWidth, int frameHeight, int revision, long capturedAt)
    {
        string[] offers = [];
        try
        {
            using (bitmap)
            using (var input = InputImage.FromBitmap(bitmap, 0))
            using (var task = recognizer!.Process(input))
            {
                using var completion = new RecognitionCompletion();
                task.AddOnCompleteListener(completion);
                using var result = await completion.Result.ConfigureAwait(false);
                var lines = result.TextBlocks.SelectMany(block => block.Lines)
                    .Select(line =>
                    {
                        using var box = line.BoundingBox;
                        return box is null ? default : new ScreenLine(line.Text, box.CenterX(), box.CenterY());
                    })
                    .Where(line => line.Text is not null)
                    .ToArray();
                offers = titles!.ReadOffers(lines, frameWidth, frameHeight);
#if HDT_ANDROID_FIXTURE
                if (Interlocked.Increment(ref fixtureDiagnosticReads) <= 8)
                {
                    var elapsed = SystemClock.ElapsedRealtime() - capturedAt;
                    var recognized = string.Join(" | ", lines.Select(line => $"{line.Text} at {line.X},{line.Y}"));
                    global::Android.Util.Log.Info("HdtAndroid",
                        $"Fixture OCR {elapsed} ms, {frameWidth}x{frameHeight}: {recognized}; matched {offers.Length}");
                }
#endif
            }
        }
        catch (Exception error)
        {
            global::Android.Util.Log.Warn("HdtAndroid", $"Card recognition: {error.GetType().Name}");
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            reading = false;
            if (lifetime.IsCancellationRequested || !visible || revision != viewportRevision
                || SystemClock.ElapsedRealtime() - capturedAt > MaxObservationAgeMs)
                return;

            lastReadAt = capturedAt;
            if (offers.Length == 0 || !offers.SequenceEqual(previous))
            {
                ClearOffers("Waiting for three readable Arena card titles…");
                previous = offers;
                return;
            }

            try
            {
                ShowOffers(offers);
                Update("Arena card offers", offers);
            }
            catch (Exception error)
            {
                Fail("Could not display the Android overlay.", error);
            }
        });
    }

    private void ShowOffers(string[] offers)
    {
        if (panel is null)
        {
            if (!Settings.CanDrawOverlays(this))
                throw new InvalidOperationException("Overlay permission was removed.");

            var text = new TextView(windowContext!) { TextSize = 12 };
            text.SetTextColor(Color.White);
            text.SetBackgroundColor(Color.Rgb(24, 31, 42));
            text.SetPadding(Dp(8), Dp(6), Dp(8), Dp(6));
            using var layout = new WindowManagerLayoutParams(Dp(140), ViewGroup.LayoutParams.WrapContent,
                WindowManagerTypes.ApplicationOverlay, WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchable,
                Format.Translucent)
            {
                Gravity = GravityFlags.Top | GravityFlags.Right,
                X = Dp(12),
                Y = Dp(24),
                Alpha = 0.75f
            };
            layout.Title = "HDT Arena overlay";
            try
            {
                windows!.AddView(text, layout);
                panel = text;
            }
            catch
            {
                text.Dispose();
                throw;
            }
        }

        panel.Text = string.Join("\n", offers);
    }

    private int Dp(int value) => (int)Math.Round(value * density / 160d);

    private static ImageReader NewReader(int width, int height) =>
        ImageReader.NewInstance(width, height, (ImageFormatType)Format.Rgba8888, 3);

    private void Resize(int newWidth, int newHeight)
    {
        if (display is null || (newWidth == width && newHeight == height))
            return;
        viewportRevision++;
        ClearOffers("Waiting for the resized Hearthstone view…");
        try
        {
            var replacement = NewReader(newWidth, newHeight);
            var oldReader = reader;
            reader = replacement;
            try
            {
                display.Resize(newWidth, newHeight, density);
                display.Surface = replacement.Surface;
                width = newWidth;
                height = newHeight;
                lastFrameTimestamp = -1;
            }
            finally
            {
                oldReader?.Close();
                oldReader?.Dispose();
            }
        }
        catch (Exception error)
        {
            Fail("Could not resize screen capture.", error);
        }
    }

    private void SetVisibility(bool isVisible)
    {
        if (visible == isVisible)
            return;
        visible = isVisible;
        viewportRevision++;
        ClearOffers(visible ? "Waiting for three readable Arena card titles…" : "Open Hearthstone to resume tracking.");
    }

    private void ClearOffers(string status)
    {
        previous = [];
        RemovePanel();
        Update(status);
    }

    private void RemovePanel()
    {
        if (panel is not { } view)
            return;
        panel = null;
        try
        {
            windows?.RemoveViewImmediate(view);
        }
        catch (Java.Lang.IllegalArgumentException)
        {
            // Android can already have removed the window when permission changed.
        }
        finally
        {
            view.Dispose();
        }
    }

    private static void Update(string status, string[]? offers = null)
    {
        var current = TrackingSession.Current;
        if (current.Status != status || !(current.Offers ?? []).SequenceEqual(offers ?? []))
            TrackingSession.Publish(new(status, Active: true, offers));
    }

    private void Fail(string message, Exception error)
    {
        global::Android.Util.Log.Warn("HdtAndroid", $"{message} {error.GetType().Name}");
        End(message);
    }

    private void End(string status)
    {
        Cleanup();
        TrackingSession.Publish(new(status));
        StopSelf();
    }

    public override void OnDestroy()
    {
        Cleanup();
        if (TrackingSession.Current.Active)
            TrackingSession.Publish(new("Tracking is off."));
        base.OnDestroy();
    }

    private void Cleanup()
    {
        if (lifetime.IsCancellationRequested)
            return;
        lifetime.Cancel();
        if (sample is not null)
        {
            main.RemoveCallbacks(sample);
            sample.Dispose();
            sample = null;
        }
        RemovePanel();
        windowContext?.Dispose();
        if (projectionEvents is not null)
        {
            projection?.UnregisterCallback(projectionEvents);
            projectionEvents.Dispose();
        }
        display?.Release();
        display?.Dispose();
        reader?.Close();
        reader?.Dispose();
        projection?.Stop();
        projection?.Dispose();
        recognizer?.Close();
        StopForeground(StopForegroundFlags.Remove);
    }

    private sealed class ProjectionEvents(CaptureService service) : MediaProjection.Callback
    {
        public override void OnStop() => service.End("Screen sharing ended.");
        public override void OnCapturedContentResize(int width, int height) => service.Resize(width, height);
        public override void OnCapturedContentVisibilityChanged(bool isVisible) => service.SetVisibility(isVisible);
    }

    private sealed class RecognitionCompletion : Java.Lang.Object, global::Android.Gms.Tasks.IOnCompleteListener
    {
        private readonly TaskCompletionSource<Text> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task<Text> Result => completion.Task;

        public void OnComplete(GmsTask task)
        {
            if (task.IsSuccessful && task.Result is { } result)
                completion.TrySetResult(result.JavaCast<Text>());
            else
                completion.TrySetException(new InvalidOperationException("ML Kit could not read the captured frame."));
        }
    }
}
