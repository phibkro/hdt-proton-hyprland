using Avalonia;
using Avalonia.Threading;

namespace Hdt.Avalonia;

// No MainWindow, shared library writer or main-process event dependency exists here.
public static class IndependentOverlay
{
    public static int Run(LaunchOptions options)
    {
        using var lifetime = new CancellationTokenSource();
        var signals = SessionProtocol.StopSignals(lifetime);
        AppBuilder.Configure<App>().UsePlatformDetect().UseSkia().SetupWithoutStarting();
        AvaloniaSynchronizationContext.InstallIfNeeded();
        var actor = NativeOverlayActor.ForIdentity(options.Game ?? throw new ArgumentException("Explicit overlay game required."), _ => { }, lifetime.Token);
        var work = StartAsync(options, actor, lifetime);
        try { Dispatcher.UIThread.MainLoop(lifetime.Token); }
        finally
        {
            lifetime.Cancel(); actor.Stop();
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (!work.IsCompleted || !actor.Completion.IsCompleted)
            {
                if (deadline.Elapsed > TimeSpan.FromSeconds(5)) throw new IOException("Independent renderer cleanup exceeded deadline.");
                Dispatcher.UIThread.RunJobs(); using var turn = new CancellationTokenSource(TimeSpan.FromMilliseconds(10)); Dispatcher.UIThread.MainLoop(turn.Token);
            }
            actor.Completion.GetAwaiter().GetResult();
            foreach (var signal in signals) signal.Dispose();
        }
        try { work.GetAwaiter().GetResult(); return 0; }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static async Task StartAsync(LaunchOptions options, NativeOverlayActor actor, CancellationTokenSource lifetime)
    {
        Task? protocol = null;
        try
        {
            var spec = await actor.Ready.WaitAsync(TimeSpan.FromSeconds(3), lifetime.Token);
            var rendererSession = Guid.NewGuid().ToString(); var noticeSequence = 0;
            var source = new NativeObservationReader(options.NativeState!); var preferences = new SettingsStore(options.Settings);
            SessionProjection state = new(SessionProjectionKind.Starting);
            actor.Offer(OverlayRendering.Notice(rendererSession, ++noticeSequence, spec, "Observations unavailable", "Reading the independent source."));
            protocol = Task.Run(() => new SessionProtocol(options, actor.Window, () => Volatile.Read(ref state), lifetime.Token).RunAsync());
            while (!lifetime.IsCancellationRequested)
            {
                if (protocol.IsCompleted) { await protocol; throw new IOException("Independent control transport exited."); }
                if (actor.Completion.IsCompleted) { await actor.Completion; throw new IOException("Independent native window owner exited."); }
                try
                {
                    // Each process owns its own bounded reader and sequence high-water.
                    var observation = await source.ReadAsync(lifetime.Token);
                    var settings = await preferences.LoadAsync(lifetime.Token);
                    if (lifetime.IsCancellationRequested) break;
                    if (observation.Mode is NativeMode.Menu or NativeMode.Offline or NativeMode.Unknown || !observation.IsRunning
                        || observation.Mode == NativeMode.Arena && observation.Arena?.Mode != ArenaMode.Drafting)
                    { actor.Offer(null); Volatile.Write(ref state, new(SessionProjectionKind.Inactive, observation.Sequence, settings.ShowArenasmithScore, settings.ShowArenaDeckSynergies)); }
                    else
                    { actor.Offer(OverlayRendering.Render(observation, settings, spec)); Volatile.Write(ref state, new(SessionProjectionKind.Facts, observation.Sequence, settings.ShowArenasmithScore, settings.ShowArenaDeckSynergies)); }
                }
                catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidDataException or UnauthorizedAccessException)
                {
                    if (lifetime.IsCancellationRequested) break;
                    actor.Offer(OverlayRendering.Notice(rendererSession, checked(++noticeSequence), spec, "Observations unavailable", "Source or preferences are stale, invalid or unavailable. No game advice is displayed."));
                    Volatile.Write(ref state, new(SessionProjectionKind.SourceUnavailable));
                }
                await Task.Delay(250, lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { lifetime.Cancel(); if (protocol is not null) { try { await protocol; } catch (OperationCanceledException) { } } }
    }
}
