using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Hdt.Avalonia;

public enum NativeUiRole { Main, Overlay }
public sealed record GameIdentity(uint Window, uint Pid, string Class);
public enum SessionProjectionKind { Starting, Main, Facts, Inactive, SourceUnavailable }
public enum SessionReportKind { Ready, Heartbeat }
public sealed record SessionProjection(SessionProjectionKind Kind, int? SourceSequence = null, bool? Scores = null, bool? Synergies = null, int? MainPage = null);

// Control observations have their own identity; they never become tracker facts.
public sealed class SessionProtocol(LaunchOptions options, uint window, Func<SessionProjection> projection, CancellationToken lifetime)
{
    private long _acknowledged;
    public async Task RunAsync()
    {
        TaskCompletionSource? pending = null;
        var sequence = 0L;
        var ready = true;
        SessionProjection observedProjection = new(SessionProjectionKind.Starting);
        while (!lifetime.IsCancellationRequested)
        {
            if (pending is null || pending.Task.IsCompleted)
            {
                pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var acknowledgement = pending;
                var requested = ++sequence;
                Dispatcher.UIThread.Post(() => { Volatile.Write(ref observedProjection, projection()); Interlocked.Exchange(ref _acknowledged, requested); acknowledgement.TrySetResult(); });
            }
            var responsive = false;
            try { await pending.Task.WaitAsync(TimeSpan.FromMilliseconds(750), lifetime); responsive = true; }
            catch (TimeoutException) { /* One pending dispatcher callback; a frozen UI cannot grow this queue. */ }
            if (lifetime.IsCancellationRequested) break;
            var state = Volatile.Read(ref observedProjection);
            var packet = new
            {
                schemaVersion = 1,
                kind = ready && responsive ? SessionReportKind.Ready : SessionReportKind.Heartbeat,
                role = options.SessionRole == NativeUiRole.Main ? "main" : "overlay",
                instance = options.SessionInstance,
                pid = Environment.ProcessId,
                uiSequence = Interlocked.Read(ref _acknowledged),
                uiResponsive = responsive,
                window,
                projection = state.Kind,
                mainPage = state.MainPage,
                sourceSequence = state.SourceSequence,
                scores = state.Scores,
                synergies = state.Synergies
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(packet, new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) } });
            if (bytes.Length > 8191) throw new IOException("Native session report exceeds bound.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime); deadline.CancelAfter(TimeSpan.FromSeconds(2));
            var output = Console.OpenStandardOutput();
            await output.WriteAsync(bytes, deadline.Token); await output.WriteAsync(new byte[] { (byte)'\n' }, deadline.Token); await output.FlushAsync(deadline.Token);
            if (responsive) ready = false;
            await Task.Delay(250, lifetime);
        }
    }
    public static void AttachMain(MainWindow window, LaunchOptions options, global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
    {
        var lifetime = new CancellationTokenSource();
        var signals = StopSignals(lifetime);
        window.Closed += (_, _) => { lifetime.Cancel(); foreach (var signal in signals) signal.Dispose(); };
        window.Opened += async (_, _) =>
        {
            try
            {
                await window.Initialization;
                var identity = window.TryGetPlatformHandle() ?? throw new IOException("Main native window identity unavailable.");
                var protocol = new SessionProtocol(options, checked((uint)identity.Handle.ToInt64()), () => new(SessionProjectionKind.Main, MainPage: window.SelectedPage, Scores: window.ScoresToggle.IsChecked == true, Synergies: window.SynergiesToggle.IsChecked == true), lifetime.Token);
                _ = Task.Run(async () =>
                {
                    var exit = 0;
                    try { await protocol.RunAsync(); }
                    catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                    catch (Exception error) { Console.Error.WriteLine(error); exit = 1; lifetime.Cancel(); }
                    finally { Dispatcher.UIThread.Post(() => desktop.Shutdown(exit)); }
                });
            }
            catch (Exception error) { Console.Error.WriteLine(error); desktop.Shutdown(1); }
        };
    }
    public static IDisposable[] StopSignals(CancellationTokenSource lifetime)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Independent native sessions are Linux-first.");
        return new[] { System.Runtime.InteropServices.PosixSignal.SIGTERM, System.Runtime.InteropServices.PosixSignal.SIGINT }
            .Select(signal => (IDisposable)System.Runtime.InteropServices.PosixSignalRegistration.Create(signal, context => { context.Cancel = true; lifetime.Cancel(); })).ToArray();
    }
}
