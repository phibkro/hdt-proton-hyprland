using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hdt.Avalonia;

if (args.Length != 2) throw new ArgumentException("Usage: Hdt.Checks FIXTURE EVIDENCE_DIRECTORY");
var fixture = Path.GetFullPath(args[0]);
var evidence = Path.GetFullPath(args[1]);
Directory.CreateDirectory(evidence);
AppBuilder.Configure<App>().UseHeadless(new() { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
AvaloniaSynchronizationContext.InstallIfNeeded();
var checks = 0;
void Wait(Task task)
{
    PumpUntil(() => task.IsCompleted, "asynchronous operation completes", TimeSpan.FromSeconds(8));
    task.GetAwaiter().GetResult();
}
T Result<T>(Task<T> task) { Wait(task); return task.GetAwaiter().GetResult(); }
void PumpUntil(Func<bool> predicate, string reason, TimeSpan? timeout = null)
{
    var clock = System.Diagnostics.Stopwatch.StartNew();
    while (!predicate())
    {
        if (clock.Elapsed > (timeout ?? TimeSpan.FromSeconds(5))) throw new TimeoutException(reason);
        Dispatcher.UIThread.RunJobs();
        using var turn = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));
        Dispatcher.UIThread.MainLoop(turn.Token);
    }
    Dispatcher.UIThread.RunJobs();
}
void Check(bool condition, string reason)
{
    if (!condition) throw new InvalidOperationException(reason);
    checks++; Console.WriteLine($"PASS {reason}");
}
var frames = Result(new ReplaySource(fixture).ReadAsync());
Check(frames.Count == 3, "replay loads all observations");
Check(frames[0].Offers[0].DisplayScore == "—", "missing rating stays unknown");
Check(frames[0].Offers[1].DisplayScore == "8", "whole score omits decimal");
Check(frames[1].Offers[0].DisplayScore == "10", "dynamic rating overrides base and truncates above ten");
Check(frames[1].Offers[0].SynergyCount(frames[1].PickedDeck) == 1, "synergy counts actual picked-card multiplicity");
var duplicate = frames[1] with { PickedDeck = [frames[1].PickedDeck[0], frames[1].PickedDeck[0]] };
Check(duplicate.Offers[0].SynergyCount(duplicate.PickedDeck) == 2, "duplicate picks contribute twice to enhancement");
var both = frames[1].Offers[0] with { Enabled = new(["DEMO_A"], []) };
Check(both.SynergyCount(duplicate.PickedDeck) == 1, "enabled relation excludes enhancement double counting");
var settings = Path.Combine(evidence, "settings.json");
if (File.Exists(settings)) File.Delete(settings);
var window = new MainWindow(new(fixture, settings));
window.Show(); Wait(window.Initialization); Dispatcher.UIThread.RunJobs();
Check(window.OfferCount == 3 && window.PickedCount == 0, "native controls show initial offers and empty picked deck");
Click(window.NextButton);
Dispatcher.UIThread.RunJobs();
Check(window.OfferCount == 2 && window.PickedCount == 1 && window.Scores[0] == "10", "next button replaces offers and reflects observed pick");
var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
void Click(Control control)
{
    Dispatcher.UIThread.RunJobs();
    window.CaptureRenderedFrame();
    var position = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
        ?? throw new InvalidOperationException("Control has no window position.");
    window.MouseDown(position, MouseButton.Left);
    window.MouseUp(position, MouseButton.Left);
    Dispatcher.UIThread.RunJobs();
}
Click((TabItem)tabs.Items[3]!);
Check(window.SelectedPage == 3, "pointer navigation opens native Options panel");
Click(window.ScoresToggle);
Wait(window.PendingPreferences);
Check(window.Scores.Count == 0 && !Result(new SettingsStore(settings).LoadAsync()).ShowArenasmithScore, "native checkbox pointer input persists score preference");
var persisted = new MainWindow(new(fixture, settings));
Wait(persisted.Initialization);
Check(persisted.ScoresToggle.IsChecked == false, "fresh window restores the nondefault saved preference");
persisted.Close();
window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, "options.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
Click((TabItem)tabs.Items[1]!);
Check(!window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("Arenasmith ") == true), "disabled scores are removed from rendered control tree");
Click((TabItem)tabs.Items[3]!);
Click(window.ScoresToggle);
Wait(window.PendingPreferences);
Click((TabItem)tabs.Items[1]!);
window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, "arena.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
Check(File.Exists(Path.Combine(evidence, "arena.png")), "real Avalonia renderer emits screenshot");
Click(window.NextButton);
Check(window.OfferCount == 0 && window.PickedCount == 2 && !window.NextButton.IsEnabled && window.Summary.Contains("Underground"), "terminal observation updates mode, deck and navigation boundary");
Click(window.ResetButton);
Check(window.OfferCount == 3 && window.PickedCount == 0, "rewind restores initial observation");
var invalid = Path.Combine(evidence, "invalid.json");
File.WriteAllText(invalid, "[{\"Sequence\":1}]");
try { Wait(new ReplaySource(invalid).ReadAsync()); throw new Exception("Invalid replay accepted"); }
catch (InvalidDataException) { checks++; Console.WriteLine("PASS incomplete replay fails explicitly"); }
var plugin = Path.Combine(evidence, "plugin-state.json");
var now = DateTimeOffset.UtcNow;
File.WriteAllText(plugin, System.Text.Json.JsonSerializer.Serialize(new { observedMS = now.ToUnixTimeMilliseconds(), state = new { mode = "DRAFT", arenaClientState = "DRAFTING", arenaSessionState = "PICKING", arenaDrafting = true, arenaAvailable = true } }));
Check(Result(new ProtonStateReader(plugin).ReadAsync(now)).Contains("connected"), "read-only adapter consumes actual MIT plugin field contract");
try { Wait(new ProtonStateReader(plugin).ReadAsync(now.AddSeconds(10))); throw new Exception("Stale state accepted"); }
catch (InvalidDataException) { checks++; Console.WriteLine("PASS old acquisition state is rejected as stale"); }
window.Close();
var restored = new MainWindow(new(fixture, settings));
Wait(restored.Initialization);
Check(restored.ScoresToggle.IsChecked == true, "settings survive window reconstruction");
restored.Close();


void ExpectInputFailure(Func<Task> action, string reason)
{
    try { Wait(action()); throw new Exception("Invalid input was accepted: " + reason); }
    catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidDataException)
    { Check(true, reason); }
}
var oversized = Path.Combine(evidence, "oversized.json");
using (var file = File.Create(oversized)) file.SetLength(SnapshotFiles.ReplayBytes + 1);
ExpectInputFailure(() => new ReplaySource(oversized).ReadAsync(), "oversized replay fails at real Rust byte boundary");
ExpectInputFailure(() => new ProtonStateReader(oversized).ReadAsync(), "oversized status fails at its smaller byte bound");
File.WriteAllText(invalid, "{broken");
ExpectInputFailure(() => new ProtonStateReader(invalid).ReadAsync(), "malformed status JSON fails explicitly");
ExpectInputFailure(() => new ReplaySource(evidence).ReadAsync(), "directory is not accepted as a regular file");
ExpectInputFailure(() => new ReplaySource("/dev/zero").ReadAsync(), "character device cannot bypass byte/regular-file contract");
var fifo = Path.Combine(evidence, "input.fifo");
if (File.Exists(fifo)) File.Delete(fifo);
var fifoStart = new System.Diagnostics.ProcessStartInfo("mkfifo") { UseShellExecute = false };
fifoStart.ArgumentList.Add(fifo);
using (var maker = System.Diagnostics.Process.Start(fifoStart)!) { maker.WaitForExit(); Check(maker.ExitCode == 0, "owned FIFO fixture created"); }
ExpectInputFailure(() => new ReplaySource(fifo).ReadAsync(), "FIFO with no writer is rejected without hanging");
File.Delete(fifo);
var link = Path.Combine(evidence, "input-link");
if (File.Exists(link)) File.Delete(link);
File.CreateSymbolicLink(link, fixture);
ExpectInputFailure(() => new ReplaySource(link).ReadAsync(), "final symlink is rejected at open");
File.Delete(link);

void PublishStatus(string mode, int secondsAgo = 0)
{
    File.WriteAllText(plugin, System.Text.Json.JsonSerializer.Serialize(new
    {
        observedMS = DateTimeOffset.UtcNow.AddSeconds(-secondsAgo).ToUnixTimeMilliseconds(),
        state = new { mode, arenaClientState = "DRAFTING", arenaSessionState = "PICKING", arenaDrafting = true, arenaAvailable = true }
    }));
}
PublishStatus("DRAFT");
window = new MainWindow(new(fixture, settings, plugin));
window.Show(); Wait(window.Initialization);
Check(window.ConnectionStatus.Contains("connected"), "rendered window attaches to read-only status file");
PublishStatus("DRAFT", 10);
PumpUntil(() => window.ConnectionStatus.Contains("STALE"), "polling must expose stale producer");
window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, "status-stale.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
Check(window.ConnectionStatus.Contains("unavailable"), "real dispatcher timer clears stale status");
PublishStatus("GAMEPLAY");
PumpUntil(() => window.ConnectionStatus.Contains("GAMEPLAY"), "polling must recover after a fresh file arrives");
window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, "status-recovered.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
Check(window.ConnectionStatus.Contains("connected"), "real timer recovers and preserves mode transition");

var realHost = Environment.GetEnvironmentVariable("HDT_HOST_BINARY");
var slowHost = Path.Combine(evidence, $"slow-reader-{Guid.NewGuid():N}.sh");
File.WriteAllText(slowHost, "#!/bin/sh\nprintf '%s' \"$$\" > \"$0.pid\"\nexec sleep 30\n");
if (OperatingSystem.IsLinux()) File.SetUnixFileMode(slowHost, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
try
{
    Environment.SetEnvironmentVariable("HDT_HOST_BINARY", slowHost);
    var pending = window.RefreshProtonAsync();
    PumpUntil(() => File.Exists(slowHost + ".pid"), "owned slow child starts");
    Click(window.NextButton);
    Check(!pending.IsCompleted && window.PickedCount == 1, "pointer journey remains responsive while external child stalls");
    Wait(pending);
    Check(window.ConnectionStatus.Contains("deadline"), "slow reader reaches visible bounded deadline");
    var pid = int.Parse(File.ReadAllText(slowHost + ".pid"));
    PumpUntil(() => { try { using var child = System.Diagnostics.Process.GetProcessById(pid); return child.HasExited; } catch (ArgumentException) { return true; } }, "owned slow child is reaped");
    Check(true, "deadline kills and reaps only the owned slow child");
}
finally { Environment.SetEnvironmentVariable("HDT_HOST_BINARY", realHost); }
PublishStatus("RECOVERED");
PumpUntil(() => window.ConnectionStatus.Contains("RECOVERED"), "actor releases custody and recovers after timeout");
Check(window.ConnectionStatus.Contains("connected"), "status recovers after timeout with no stuck actor slot");
window.Close();

var blocker = Path.Combine(evidence, $"blocked-storage-{Guid.NewGuid():N}");
File.WriteAllText(blocker, "owned failure fixture");
window = new MainWindow(new(fixture, Path.Combine(blocker, "settings.json")));
window.Show(); Wait(window.Initialization);
var failureTabs = window.GetVisualDescendants().OfType<TabControl>().Single();
Click((TabItem)failureTabs.Items[3]!);
Click(window.ScoresToggle);
var failedSave = window.PendingPreferences;
Wait(failedSave);
Check(window.ScoresToggle.IsChecked == true && window.ScoresToggle.IsEnabled && window.PreferencesStatus.Contains("could not be saved"), "unavailable storage rolls checkbox back and visibly fails");
Dispatcher.UIThread.RunJobs();
Check(ReferenceEquals(failedSave, window.PendingPreferences), "guarded rollback does not enqueue a recursive preference write");
window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, "settings-failure.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
File.Delete(blocker); Directory.CreateDirectory(blocker);
Click(window.ScoresToggle); Wait(window.PendingPreferences);
Check(window.ScoresToggle.IsChecked == false && window.PreferencesStatus.Contains("saved"), "preference controls recover after storage becomes available");
Check(!Result(new SettingsStore(Path.Combine(blocker, "settings.json")).LoadAsync()).ShowArenasmithScore, "recovered preference write is durable at real IO boundary");
window.Close();
Console.WriteLine($"{checks} checks passed; renderer evidence: {evidence}");
