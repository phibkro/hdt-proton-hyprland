using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hdt.Avalonia;
using HearthMirror.Enums;
using HearthMirror.Objects;
using HearthWatcher;
using HearthWatcher.Providers;

// Optional local acceptance against actual vendor assemblies, never shipped with native UI.
internal static class NativeWatcherChecks
{
    public static int Run(string directory)
    {
        MainWindow? window = null;
        SourceDispatcher? source = null;
        var checks = 0;
        var status = 1;
        try
        {
            var evidence = Path.GetFullPath(directory); Directory.CreateDirectory(evidence);
            foreach (var assembly in new[] { typeof(ArenaStateWatcher).Assembly, typeof(ArenaState).Assembly })
                Console.WriteLine($"ACTUAL ASSEMBLY {assembly.FullName} MVID={assembly.ManifestModule.ModuleVersionId} SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location)))}");
            Console.WriteLine($"RUNTIME {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} PID={Environment.ProcessId}");
            AppBuilder.Configure<App>().UseHeadless(new() { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
            AvaloniaSynchronizationContext.InstallIfNeeded();
            void Check(bool condition, string why)
            { if (!condition) throw new InvalidOperationException(why); checks++; Console.WriteLine("PASS " + why); }
            void Until(Func<bool> condition, string why, double seconds = 8)
            {
                var clock = Stopwatch.StartNew();
                while (!condition())
                {
                    if (clock.Elapsed.TotalSeconds > seconds) throw new TimeoutException(why);
                    source?.ThrowFailure(); Dispatcher.UIThread.RunJobs();
                    using var turn = new CancellationTokenSource(10); Dispatcher.UIThread.MainLoop(turn.Token);
                }
                Dispatcher.UIThread.RunJobs();
                Console.WriteLine("OBSERVED " + why);
            }
            void Wait(Task task) { Until(() => task.IsCompleted, "owned async operation"); task.GetAwaiter().GetResult(); }
            var file = Path.Combine(evidence, "watcher-observation.json");
            source = new(file);
            source.Change(["FIXTURE_HERO_A", "FIXTURE_HERO_B", "FIXTURE_HERO_C"]);
            Until(() => source.Latest?.Arena?.ViewSelection is ArenaHeroSelection, "actual watcher hero delivery");
            window = new(new(null, Path.Combine(evidence, "settings.json"), ClientData: Path.Combine(evidence, "library.json"), NativeState: file));
            window.Show(); Wait(window.Initialization);
            var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
            void Click(Control control)
            {
                control.BringIntoView(); Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame();
                var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window) ?? throw new InvalidOperationException("Missing pointer target");
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            }
            bool HasText(string value) => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains(value, StringComparison.OrdinalIgnoreCase) == true);
            void Capture(string name)
            {
                foreach (var width in new[] { 1120, 800 })
                {
                    window.Width = width; window.Height = 760; Dispatcher.UIThread.RunJobs();
                    window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, $"watcher-{name}-{width}.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
                var observed = source.Latest ?? throw new InvalidOperationException("No source publication");
                OverlayRendering.Render(observed, new TrackerSettings(), new(1, 640, 720, "bgra8888Opaque"), Path.Combine(evidence, $"watcher-{name}-overlay.png"));
                File.WriteAllText(Path.Combine(evidence, $"watcher-{name}.json"), JsonSerializer.Serialize(observed, ReplaySource.JsonOptions));
            }
            Click((TabItem)tabs.Items[1]!);
            Until(() => window.OfferCount == 3 && window.Summary.Contains("Choose a hero"), "actual watcher identities reach native Main through file and Rust");
            Check(HasText("Fixture hero A") && HasText("memory freshness unknown") && !window.NextButton.IsEnabled, "rendered hero identity and delivery-only evidence without selection input");
            Capture("heroes");
            var hero = source.Latest!;
            var receipt = hero.Arena!.ChoiceEvidence!;
            var sequence = hero.Sequence;
            Until(() => source.Latest!.Sequence > sequence + 1, "periodic publication advances");
            Check(source.Latest!.Arena!.ChoiceEvidence == receipt, "periodic publication never restamps memory/event receipt");
            source.MutateLastPayload();
            Wait(window.RefreshNativeAsync());
            Check(HasText("Fixture hero A") && !HasText("MUTATED") && ((ArenaHeroSelection)source.Latest!.Arena!.ViewSelection).Choices[0].SourceIndex == 7,
                "vendor event payload mutation cannot alter immutable copied identities or opaque source indices");
            source.Change(["FIXTURE_CARD_A", "FIXTURE_CARD_B", "FIXTURE_CARD_C"]);
            Until(() => window.Summary.Contains("Card draft") && HasText("Fixture card A"), "actual watcher hero-to-card transition");
            Check(!HasText("Fixture hero A") && HasText("Ratings and deck synergies unavailable") && !HasText("plaque 0"), "card identities replace heroes without fabricated unknown advice");
            Check(window.PickedCount == 0 && HasText("choice correlation unknown") && HasText("Separate cached picked card") && window.Summary.Contains("pick progress unavailable"),
                "separate cached deck never derives pick progress or choice recommendations");
            Capture("cards");
            source.Change(["FIXTURE_CARD_D", "FIXTURE_CARD_E", "FIXTURE_CARD_F"]);
            Until(() => HasText("Fixture card D") && !HasText("Fixture card A"), "actual watcher replacement event removes obsolete offered cards");
            Capture("replacement");
            // Explicit negative file controls exercise reader ordering, separate from provider acceptance.
            var orderFile = Path.Combine(evidence, "ordering-control.json");
            using (var orderWriter = new NativeSnapshotWriter(orderFile, value => JsonSerializer.SerializeToUtf8Bytes(value, ReplaySource.JsonOptions),
                () => throw new IOException("Ordering control writer failed")))
            {
                var reader = new NativeObservationReader(orderFile);
                var baseline = source.Latest!; var baselineEvidence = baseline.Arena!.ChoiceEvidence!;
                var orderSequence = baseline.Sequence;
                NativeObservation Reframe(ArenaChoiceEvidence item)
                {
                    var now = DateTimeOffset.UtcNow;
                    return baseline with
                    {
                        Sequence = ++orderSequence,
                        ObservedAt = now,
                        Arena = baseline.Arena! with { Sequence = orderSequence, ObservedAt = now, ChoiceEvidence = item }
                    };
                }
                void PublishControl(NativeObservation value)
                {
                    orderWriter.Offer(value);
                    Until(() => File.Exists(orderFile) && JsonSerializer.Deserialize<NativeObservation>(File.ReadAllBytes(orderFile), ReplaySource.JsonOptions)?.Sequence == value.Sequence,
                        "owned atomic ordering control published");
                }
                PublishControl(Reframe(baselineEvidence));
                Wait(reader.ReadAsync());
                foreach (var invalid in new[] { baselineEvidence with { Revision = baselineEvidence.Revision - 1 },
                    baselineEvidence with { Generation = baselineEvidence.Generation - 1 },
                    baselineEvidence with { ReceivedAt = baselineEvidence.ReceivedAt.AddSeconds(-1) } })
                {
                    PublishControl(Reframe(invalid));
                    try { Wait(reader.ReadAsync()); throw new InvalidOperationException("Delivered-event rollback accepted"); }
                    catch (InvalidDataException) { Check(true, "real Rust-backed reader rejects delivered-event rollback despite increasing envelope sequence"); }
                }
                var changedPayload = Reframe(baselineEvidence);
                changedPayload = changedPayload with
                {
                    Arena = changedPayload.Arena! with
                    {
                        Selection = new ArenaCardDraft(((ArenaCardDraft)changedPayload.Arena!.ViewSelection).Choices
                    .Select(offer => offer with { Card = offer.Card with { Name = "Changed under the same receipt" } }).ToArray())
                    }
                };
                PublishControl(changedPayload);
                try { Wait(reader.ReadAsync()); throw new InvalidOperationException("Immutable delivered-event identity changed under one revision"); }
                catch (InvalidDataException) { Check(true, "one delivered-event revision cannot change its immutable choice payload"); }
                PublishControl(Reframe(baselineEvidence with { Revision = baselineEvidence.Revision + 1, ReceivedAt = DateTimeOffset.UtcNow }));
                Wait(reader.ReadAsync());
                Check(true, "delivered-event ordering recovers on a fresh valid generation");
            }
            source.Change(["FIXTURE_HERO_A", "FIXTURE_CARD_A", "FIXTURE_CARD_B"]);
            Until(() => window.OfferCount == 0 && window.Summary.Contains("unavailable"), "mixed explicit metadata becomes unavailable");
            source.Change(["UNKNOWN", "FIXTURE_CARD_A", "FIXTURE_CARD_B"]);
            Until(() => source.Latest!.Arena!.ChoiceEvidence is null, "unknown metadata has no delivery fact evidence");
            Check(window.OfferCount == 0, "unknown choices cannot retain prior rendered advice");
            source.Change(["FIXTURE_CARD_A", "FIXTURE_CARD_B", "FIXTURE_CARD_C"], duplicateIndices: true);
            Until(() => source.Latest!.Arena!.ChoiceEvidence is null, "duplicate source indices become unavailable");
            source.Change(["FIXTURE_CARD_A", "FIXTURE_CARD_B", "FIXTURE_CARD_C"]);
            Until(() => window.OfferCount == 3, "real watcher recovers from unsupported choices");
            source.HeroContextChange();
            Until(() => window.OfferCount == 0 && source.Latest!.Arena!.ChoiceEvidence is null, "real watcher hero-context event invalidates choices without a replacement choices event");
            source.Change(["FIXTURE_CARD_A", "FIXTURE_CARD_B", "FIXTURE_CARD_C"]);
            Until(() => window.OfferCount == 3, "real watcher choices event restores facts after explicit context invalidation");
            source.PackageContextChange();
            Until(() => source.Latest!.Arena!.ViewSelection is ArenaUnavailable { Kind: ArenaUnavailableKind.Package } && window.OfferCount == 0,
                "real watcher package event clears supported advice");
            source.ObserveModeOnly(menu: true);
            Until(() => window.GameView.ModeText.StartsWith("Menu"), "separate observed mode leaves Arena before another watcher event");
            source.ObserveModeOnly(menu: false);
            Until(() => source.Latest!.Mode == NativeMode.Arena && source.Latest.Arena!.ViewSelection is ArenaUnavailable { Kind: ArenaUnavailableKind.Unknown },
                "observed leave resets package context instead of retaining a sticky unsupported variant");
            Check(window.OfferCount == 0, "reentry without fresh watcher context remains unavailable and cannot resurrect old choices");
            source.Menu(); source.Change(["FIXTURE_CARD_A", "FIXTURE_CARD_B", "FIXTURE_CARD_C"]);
            Until(() => window.OfferCount == 3, "fresh actual watcher context recovers after package and mode transitions");
            source.Pause();
            var frozen = File.GetLastWriteTimeUtc(file);
            source.Change(["FIXTURE_HERO_A", "FIXTURE_HERO_B", "FIXTURE_HERO_C"]);
            Until(() => window.GameView.ModeText.Contains("unavailable") && window.OfferCount == 0, "stopped source dispatcher cannot keep native facts live", 10);
            Check(File.GetLastWriteTimeUtc(file) == frozen, "negative control: provider calls do not publish while delivery dispatcher is stopped");
            Click((TabItem)tabs.Items[3]!);
            Check(window.SelectedPage == 3, "native pointer navigation remains responsive with stopped delivery dispatcher");
            window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, "watcher-source-stopped.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            source.Resume(); Click((TabItem)tabs.Items[1]!);
            Until(() => window.OfferCount == 3 && window.Summary.Contains("Choose a hero"), "source dispatcher recovery delivers queued real watcher event");
            Capture("recovered");
            source.Menu();
            Until(() => window.OfferCount == 0 && window.GameView.ModeText.StartsWith("Menu"), "actual watcher landing/menu transition clears obsolete controls");
            Capture("menu");
            source.Detach();
            var detached = source.EventsCopied;
            source.Change(["FIXTURE_CARD_A", "FIXTURE_CARD_B", "FIXTURE_CARD_C"]);
            Until(() => source.Latest!.Sequence > source.DetachSequence + 1, "detached fixture continues bounded publication");
            Check(source.EventsCopied == detached && source.Latest!.Arena!.ChoiceEvidence is null, "exactly-once repeated detach ignores subsequent real watcher events");
            Console.WriteLine($"WATCHER ACCEPTANCE PASS {checks} assertions; synthetic provider, real installed watcher/adapter/IO/rendering; no memory/account calls.");
            status = 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            try { window?.CaptureRenderedFrame()?.Save(Path.Combine(Path.GetFullPath(directory), "watcher-failure.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
            catch (Exception captureError) { Console.Error.WriteLine(captureError); }
        }
        finally
        {
            try { window?.Close(); } catch (Exception error) { Console.Error.WriteLine(error); status = 1; }
            try { source?.Dispose(); } catch (Exception error) { Console.Error.WriteLine(error); status = 1; }
            Dispatcher.UIThread.RunJobs();
        }
        return status;
    }

    internal sealed class SourceDispatcher : SynchronizationContext, IArenaStateProvider, IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new(128);
        private readonly ManualResetEventSlim _started = new(), _paused = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Thread _thread;
        private readonly NativeSnapshotWriter _writer;
        private readonly string _file;
        private readonly string _session = Guid.NewGuid().ToString();
        private ArenaState? _state;
        private NativeObservation? _latest;
        private NativeArenaEventBridge? _bridge;
        private ArenaStateWatcher? _watcher;
        private Exception? _failure;
        private int _version, _sequence;
        private volatile bool _pause, _menu;
        public int EventsCopied { get; private set; }
        public int DetachSequence { get; private set; }
        public NativeObservation? Latest => Volatile.Read(ref _latest);
        public SourceDispatcher(string file)
        {
            _file = file;
            _writer = new(file, value => JsonSerializer.SerializeToUtf8Bytes(value, ReplaySource.JsonOptions), () => _failure = new IOException("Fixture publisher failed"));
            _thread = new(Loop) { IsBackground = true, Name = "Owned fixture event dispatcher" };
            try
            {
                _thread.Start();
                if (!_started.Wait(3000)) throw new TimeoutException("Fixture dispatcher startup");
                ThrowFailure();
            }
            catch
            {
                _stop.Cancel();
                if (_thread.IsAlive) _thread.Join(3000);
                _writer.Dispose(); _writer.Completion.Wait(TimeSpan.FromSeconds(3));
                throw;
            }
        }
        public override void Post(SendOrPostCallback action, object? state)
        { if (!_queue.TryAdd(() => action(state))) throw new InvalidOperationException("Fixture event queue exceeded bound"); }
        public ArenaState GetState(int? deck, int? redraft, ArenaState.ScryCache? cache) => Volatile.Read(ref _state)!;
        public void Change(string[] ids, bool duplicateIndices = false)
        {
            var state = new ArenaState
            {
                ChoicesVersion = Interlocked.Increment(ref _version),
                Choices = ids.Select((id, index) => new ArenaState.DraftChoice { Actor = new ArenaState.ActorInfo { Index = duplicateIndices ? 7 : index * 10 + 7, CardId = id } }).ToList(),
                ClientState = ArenaClientStateType.Normal_Draft,
                SessionState = ArenaSessionState.DRAFTING,
                Cache = new ArenaState.ScryCache()
            };
            _menu = false; Volatile.Write(ref _state, state);
            Task.Run(() => _watcher!.Update()).GetAwaiter().GetResult();
        }
        public void Menu()
        {
            _menu = true;
            Volatile.Write(ref _state, new ArenaState { ClientState = ArenaClientStateType.Normal_Landing, SessionState = ArenaSessionState.DRAFTING, Choices = [], Cache = new ArenaState.ScryCache() });
            Task.Run(() => _watcher!.Update()).GetAwaiter().GetResult();
        }
        public void MutateLastPayload() { _state!.Choices[0].Actor.CardId = "MUTATED"; _state.Choices[0].Actor.Index = 254; }
        public void HeroContextChange()
        {
            _state!.ChosenHero = "FIXTURE_HERO_B";
            Task.Run(() => _watcher!.Update()).GetAwaiter().GetResult();
        }
        public void PackageContextChange()
        {
            _state!.IsPackageSelectOpen = true;
            Task.Run(() => _watcher!.Update()).GetAwaiter().GetResult();
        }
        // The optional producer also observes a separate public game mode, not only watcher events.
        public void ObserveModeOnly(bool menu) { _menu = menu; }
        public void Pause()
        {
            _pause = true;
            if (!_paused.Wait(3000)) throw new TimeoutException("Fixture dispatcher freeze acknowledgement");
            // A paused dispatcher alone does not acknowledge an in-flight atomic write.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (true)
            {
                var file = SnapshotFiles.ReadJsonAsync<NativeObservation>(_file, 2 * 1024 * 1024, deadline.Token).GetAwaiter().GetResult();
                if (file.Sequence == Latest!.Sequence) return;
                deadline.Token.ThrowIfCancellationRequested();
                Thread.Sleep(10);
            }
        }
        public void Resume() { _pause = false; _paused.Reset(); }
        public void Detach()
        { Post(_ => { _bridge!.Dispose(); _bridge.Dispose(); DetachSequence = _sequence; }, null); }
        public void ThrowFailure() { if (_failure is { } error) throw new InvalidOperationException("Owned source fixture failed", error); }
        private void Loop()
        {
            try
            {
                SetSynchronizationContext(this); PollingWatcher.SetEventContext(this);
                _watcher = new(this, 15);
                _bridge = new(_watcher, id =>
                {
                    EventsCopied++;
                    if (id is "FIXTURE_HERO_A" or "FIXTURE_HERO_B" or "FIXTURE_HERO_C") return new(id, "Fixture hero " + id[^1], "HERO", 0);
                    if (id is "FIXTURE_CARD_A" or "FIXTURE_CARD_B" or "FIXTURE_CARD_C" or "FIXTURE_CARD_D" or "FIXTURE_CARD_E" or "FIXTURE_CARD_F") return new(id, "Fixture card " + id[^1], "MINION", 2);
                    return null!;
                });
                _started.Set();
                while (!_stop.IsCancellationRequested)
                {
                    if (_pause) { _paused.Set(); _stop.Token.WaitHandle.WaitOne(10); continue; }
                    while (_queue.TryTake(out var action)) action();
                    var now = DateTime.UtcNow; var sequence = ++_sequence;
                    var raw = _bridge.Capture(!_menu, false, false, false).Observation(_session, sequence, now,
                        [new { id = "CACHED_UNCORRELATED", name = "Separate cached picked card", mana = 3 }]);
                    var arena = JsonSerializer.Deserialize<ArenaObservation>(JsonSerializer.Serialize(raw, ReplaySource.JsonOptions), ReplaySource.JsonOptions)!;
                    var value = new NativeObservation(1, _session, sequence, now, NativeAvailability.Available, null, _menu ? NativeMode.Menu : NativeMode.Arena,
                        _menu ? "HUB" : "DRAFT", "Fixture only", null, _menu, true, null, null, null, null, null, null, [], [], [], [], arena, [], 256);
                    NativeObservationReader.Validate(value, now);
                    _writer.Offer(value); Volatile.Write(ref _latest, value);
                    _stop.Token.WaitHandle.WaitOne(100);
                }
            }
            catch (Exception error) { _failure = error; _started.Set(); }
            finally { _bridge?.Dispose(); PollingWatcher.SetEventContext(null); }
        }
        public void Dispose()
        {
            _stop.Cancel();
            if (!_thread.Join(3000)) throw new TimeoutException("Owned source dispatcher did not stop");
            _writer.Dispose();
            if (!_writer.Completion.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException("Owned fixture atomic writer did not stop");
            _queue.Dispose(); _started.Dispose(); _paused.Dispose(); _stop.Dispose();
        }
    }
}
