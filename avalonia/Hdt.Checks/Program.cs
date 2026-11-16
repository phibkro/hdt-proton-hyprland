using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
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
    control.BringIntoView();
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

window = new MainWindow(new(fixture, Path.Combine(evidence, "client-settings.json"), ClientData: Path.Combine(evidence, $"library-{Guid.NewGuid():N}.json")));
window.Show(); Wait(window.Initialization);
var clientTabs = window.GetVisualDescendants().OfType<TabControl>().Single();
Click((TabItem)clientTabs.Items[0]!);
var editor = window.DeckLibrary;
void Fill(TextBox box, string text)
{
    Click(box);
    window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
    window.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
    window.KeyTextInput(text);
    Dispatcher.UIThread.RunJobs();
}
Click(editor.NewButton);
Fill(editor.NameBox, "Native fixture deck");
Fill(editor.ClassBox, "Mage");
Fill(editor.HeroBox, "637");
Fill(editor.CardsBox, "77 1\n100 2");
Fill(editor.NotesBox, "Native pointer and keyboard acceptance");
Click(editor.SaveButton); Wait(editor.PendingAction);
Check(window.Library.State.Decks.Length == 1 && window.Library.State.Decks[0].Name == "Native fixture deck", "native deck editor saves through real Rust transaction boundary");
Click(editor.ExportButton); Wait(editor.PendingAction);
Check(editor.CodeBox.Text?.Contains("AAECAf0EAU0BZAAA") == true, "native export produces independently specified Hearthstone deckstring");
Click(editor.CopyButton); Wait(editor.PendingAction);
Check(window.Clipboard is not null && Result(window.Clipboard.TryGetTextAsync()) == editor.CodeBox.Text && editor.Status.Contains("copied"), "native copy button publishes the actual exported deckstring to the isolated clipboard");
Fill(editor.NameBox, "Imported native fixture");
Click(editor.ImportButton); Wait(editor.PendingAction);
Check(window.Library.State.Decks.Length == 2 && editor.NameBox.Text == "Imported native fixture", "native import creates and selects a second persisted deck");
Click(editor.UseButton); Wait(editor.PendingAction);
Check(window.Library.State.SelectedDeckId == window.Library.State.Decks[1].Id, "native tracking selection changes only client-owned library state");
Check(editor.UseButton.Content?.ToString() == "Use for game history" && editor.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Game history deck: Imported native fixture"),
    "native deck choice visibly labels history instead of claiming it controls observed live counts");
Fill(editor.NotesBox, "Edited persistent notes");
Click(editor.SaveButton); Wait(editor.PendingAction);
Check(window.Library.State.Decks[1].Notes == "Edited persistent notes", "native deck edit preserves stable identity and updates notes");
window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, "native-library.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
var record = new MatchRecord(Guid.NewGuid().ToString(), window.Library.State.Decks[1].Id, DateTimeOffset.UtcNow.ToString("o"), "Manual acceptance fixture", "Win", "Mage", "Warrior", 8, null, "Anonymous test data");
Wait(window.Library.RecordAsync(record));
Click((TabItem)clientTabs.Items[2]!);
Check(window.GetVisualDescendants().OfType<ListBox>().Any(list => list.Items.OfType<MatchRecord>().Any(match => match.Id == record.Id)), "native history displays a persisted supplied match record");
window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, "native-history.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
Click((TabItem)clientTabs.Items[0]!);
Click(editor.ArchiveButton); Wait(editor.PendingAction);
Check(window.Library.State.Decks[1].Archived && window.Library.State.Decks[1].Notes == "Edited persistent notes", "native archive preserves deck content and notes");
Click(editor.ShowArchived);
Click(editor.Decks.GetVisualDescendants().OfType<ListBoxItem>().Single());
Check(editor.ArchiveButton.Content?.ToString() == "Restore" && !editor.SaveButton.IsEnabled && !editor.UseButton.IsEnabled,
    "archived browser exposes preserved deck for explicit restore without permitting tracking");
Click(editor.ArchiveButton); Wait(editor.PendingAction);
Check(!window.Library.State.Decks[1].Archived && window.Library.State.Decks[1].Notes == "Edited persistent notes"
    && window.Library.State.SelectedDeckId is null && window.Library.State.Matches.Single().DeckId == record.DeckId,
    "pointer restore preserves identity, notes and historical association without silently selecting the deck");
Click(editor.NewButton);
Check(editor.ShowArchived.IsChecked == false && !editor.NameBox.IsReadOnly && editor.NameBox.Text == "" && editor.SaveButton.IsEnabled,
    "new deck leaves archive-only browsing so newly saved decks cannot disappear into a hidden active list");
var savedLibrary = window.Library.State;
Fill(editor.CodeBox, "invalid deck code");
Click(editor.ImportButton); Wait(editor.PendingAction);
Check(!window.Library.Ready && window.Library.State.Revision == savedLibrary.Revision && !editor.SaveButton.IsEnabled, "invalid native import leaves the saved library intact and requires explicit reload");
Click(editor.ReloadButton); Wait(editor.PendingAction);
Check(window.Library.Ready && window.Library.State.Decks.Length == savedLibrary.Decks.Length && window.Library.State.Matches.Length == 1, "native library recovers from failed import by verifying the actual persisted state");
Click(editor.NewButton);
var catalogPath = Path.Combine(evidence, "illustrative-catalog.json");
var catalogJson = "[{\"dbfId\":777,\"id\":\"FIXTURE_CATALOG\",\"name\":\"Illustrative catalog card\",\"type\":\"MINION\",\"cardClass\":\"MAGE\",\"cost\":2,\"collectible\":true},{\"dbfId\":637,\"id\":\"HERO_FIXTURE\",\"name\":\"Illustrative base hero\",\"type\":\"HERO\",\"cardClass\":\"MAGE\"}]";
File.WriteAllText(catalogPath, catalogJson);
var catalog = editor.Catalog;
Click((Expander)catalog.Content!);
Fill(catalog.PathBox, catalogPath); Fill(catalog.QueryBox, "catalog mage");
Click(catalog.SearchButton); Wait(catalog.PendingSearch);
Click(catalog.Results.GetVisualDescendants().OfType<ListBoxItem>().Single());
Click(catalog.AddButton); Click(catalog.AddButton);
Click(catalog.RemoveButton);
Check(editor.CardsBox.Text == "777 1", "catalog remove action decrements the selected card without changing its identity");
Click(catalog.RemoveButton);
Check(editor.CardsBox.Text == "", "removing the final catalog copy removes the card rather than persisting a zero count");
Click(catalog.AddButton); Click(catalog.AddButton);
Check(editor.CardsBox.Text == "777 2" && !catalog.HeroButton.IsEnabled,
    "pointer catalog search reads real external JSON through Rust and adds named cards with exact multiplicity");
Click(catalog.IncludeNonCollectible); Fill(catalog.QueryBox, "hero");
Click(catalog.SearchButton); Wait(catalog.PendingSearch);
Click(catalog.Results.GetVisualDescendants().OfType<ListBoxItem>().Single()); Click(catalog.HeroButton);
Check(editor.HeroBox.Text == "637" && editor.ClassBox.Text == "MAGE", "explicit catalog hero choice sets exact hero identity without guessing a class mapping");
window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, "native-card-catalog.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
var catalogRevision = window.Library.State.Revision;
File.WriteAllText(catalogPath, "{}"); Click(catalog.SearchButton); Wait(catalog.PendingSearch);
Check(catalog.Results.ItemCount == 0 && !catalog.AddButton.IsEnabled && catalog.Status.Contains("unavailable") && window.Library.State.Revision == catalogRevision,
    "malformed local catalog clears stale results and cannot alter saved decks");
File.WriteAllText(catalogPath, catalogJson); Click(catalog.SearchButton); Wait(catalog.PendingSearch);
Check(catalog.Results.ItemCount == 1 && !catalog.Status.Contains("unavailable"), "local catalog search recovers after its actual source is repaired");
Click((Expander)catalog.Content!); Fill(editor.NameBox, "Catalog-built native deck");
Click(editor.SaveButton); Wait(editor.PendingAction);
Check(window.Library.State.Decks.Last().Name == "Catalog-built native deck" && window.Library.State.Decks.Last().Content.Cards.Single() == new CardCount(777, 2)
    && window.Library.State.Decks.Last().Content.HeroDbfId == 637, "catalog-built deck persists through real Rust library transaction");
window.Close();
var nativeFile = Path.Combine(evidence, $"native-observation-{Guid.NewGuid():N}.json");
var nativeLibraryFile = Path.Combine(evidence, $"observed-library-{Guid.NewGuid():N}.json");
var nativeLibrary = new ClientLibrary(nativeLibraryFile);
Wait(nativeLibrary.ReloadAsync()); Wait(nativeLibrary.ImportAsync("Observed game fixture", "AAECAf0EAU0BZAAA"));
var selectedForGame = nativeLibrary.State.SelectedDeckId;
var nativeSession = Guid.NewGuid().ToString();
var observedMatch = Guid.NewGuid().ToString();
NativeObservation NativeFrame(string mode, int sequence, ArenaObservation? arenaFrame = null, MatchRecord[]? matches = null)
{
    var time = DateTimeOffset.UtcNow;
    var menu = mode is "Menu" or "Offline";
    var arena = (arenaFrame ?? frames[2]) with
    {
        SessionId = nativeSession,
        ObservedAt = time,
        Sequence = sequence,
        Mode = mode == "Arena" ? ArenaMode.Drafting : ArenaMode.Inactive,
        Offers = mode == "Arena" ? (arenaFrame ?? frames[0]).Offers : []
    };
    ObservedCard[] card = menu ? [] : [new("fixture-card", 77, "Illustrative observed card", 2, 1, null)];
    return new(1, nativeSession, sequence, time, NativeAvailability.Available, null, Enum.Parse<NativeMode>(mode), mode == "Arena" ? "DRAFT" : "GAMEPLAY", mode,
        observedMatch, menu, mode != "Offline", menu ? null : 30, menu ? null : 27, menu ? null : 3, menu ? null : 4,
        menu ? null : 20, menu ? null : 18, card, card, card, card, arena, matches ?? [], 256);
}
var publishFailures = 0;
using var nativePublisher = new NativeSnapshotWriter(nativeFile,
    snapshot => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(snapshot, ReplaySource.JsonOptions),
    () => Interlocked.Increment(ref publishFailures));
var startNative = NativeFrame("Constructed", 1);
nativePublisher.Offer(startNative);
PumpUntil(() => File.Exists(nativeFile), "actual optional snapshot publisher emits initial observation");
window = new MainWindow(new(null, Path.Combine(evidence, "observed-settings.json"), ClientData: nativeLibraryFile, NativeState: nativeFile));
window.Show(); Wait(window.Initialization);
var nativeTabs = window.GetVisualDescendants().OfType<TabControl>().Single();
Check(window.SelectedPage == 5 && window.GameView.ModeText.Contains("Constructed") && window.GameView.CardCount == 4, "native game view consumes actual worker publication through bounded Rust reader");
window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, "native-constructed.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
Click((TabItem)nativeTabs.Items[1]!);
nativePublisher.Offer(NativeFrame("Arena", 2, frames[0]));
PumpUntil(() => window.OfferCount == 3 && window.PickedCount == 0, "real native poll observes initial live-contract offers");
Check(window.OfferCount == 3, "native acquisition contract supplies offers without replay navigation");
nativePublisher.Offer(NativeFrame("Arena", 3, frames[1]));
PumpUntil(() => window.OfferCount == 2 && window.PickedCount == 1, "real native poll replaces offers and picks");
Check(!window.NextButton.IsEnabled && window.PickedCount == 1, "live observation replacement has no replay-next semantics");
window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, "native-observed-arena.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
Click((TabItem)nativeTabs.Items[0]!);
Fill(window.DeckLibrary.NotesBox, "Unsaved note must survive observed history");
var observedResult = new MatchRecord(observedMatch, null, DateTimeOffset.UtcNow.ToString("o"), "Constructed fixture", "Win", "Mage", "Warrior", 8, null, "Anonymous source fixture");
nativePublisher.Offer(NativeFrame("Battlegrounds", 4, matches: [observedResult]));
PumpUntil(() => window.GameView.ModeText.Contains("Battlegrounds") && window.Library.State.Matches.Length == 1, "native mode transition records supplied completed game");
Check(window.OfferCount == 0 && window.Library.State.Matches[0].DeckId == selectedForGame, "leaving Arena clears offers and associates only previously observed native tracking selection");
Check(window.DeckLibrary.NotesBox.Text == "Unsaved note must survive observed history", "background history transactions preserve unsaved native deck edits");
var historyRevision = window.Library.State.Revision;
Wait(window.RefreshNativeAsync());
Check(window.Library.State.Revision == historyRevision, "repeated observation does not duplicate history or rewrite the library");
Click((TabItem)nativeTabs.Items[5]!);
window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, "native-battlegrounds.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
var staleNative = NativeFrame("Arena", 5, frames[1]);
staleNative = staleNative with { ObservedAt = DateTimeOffset.UtcNow.AddSeconds(-10), Arena = staleNative.Arena! with { ObservedAt = DateTimeOffset.UtcNow.AddSeconds(-10) } };
nativePublisher.Offer(staleNative);
PumpUntil(() => window.GameView.ModeText.Contains("unavailable"), "native source freshness clears previous game data");
Check(window.GameView.CardCount == 0 && window.OfferCount == 0 && window.PickedCount == 0, "stale native source cannot leave cards, picks or offers apparently live");
window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, "native-observation-stale.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
nativeSession = Guid.NewGuid().ToString();
nativePublisher.Offer(NativeFrame("Menu", 1));
PumpUntil(() => window.GameView.ModeText.StartsWith("Menu"), "native source recovers with a fresh process session");
Check(window.GameView.CardCount == 0 && window.OfferCount == 0, "new source session preserves explicit menu transition without resurrecting prior game");
var rollback = NativeFrame("Constructed", 2);
nativePublisher.Offer(rollback);
PumpUntil(() => window.GameView.ModeText.StartsWith("Constructed"), "fresh native source advances");
nativePublisher.Offer(NativeFrame("Battlegrounds", 1));
PumpUntil(() => window.GameView.ModeText.Contains("unavailable"), "backwards sequence is rejected");
Check(window.GameView.CardCount == 0, "backwards native source sequence cannot replace current facts");
nativePublisher.Offer(NativeFrame("Offline", 3));
PumpUntil(() => window.GameView.ModeText.StartsWith("Offline"), "native observation recovers from backwards sequence");
Check(window.GameView.CardCount == 0 && publishFailures == 0, "optional publisher and native consumer recover without leaking prior game facts");
nativePublisher.Offer(new { schemaVersion = 1, sessionId = nativeSession, sequence = 4, observedAt = DateTimeOffset.UtcNow, availability = "Unavailable", error = "Anonymous producer failure fixture" });
PumpUntil(() => window.GameView.ModeText.Contains("unavailable"), "partial unavailable producer envelope clears prior data");
Check(window.GameView.CardCount == 0 && window.OfferCount == 0, "explicit producer capture failure never renders partial game facts");
nativePublisher.Offer(NativeFrame("Menu", 5));
PumpUntil(() => window.GameView.ModeText.StartsWith("Menu"), "capture failure recovers on fresh complete publication");
window.Close(); nativePublisher.Dispose(); Wait(nativePublisher.Completion);
Check(nativePublisher.Completion.IsCompletedSuccessfully, "owned optional publisher worker exits after native window closes");
var failedOutput = Path.Combine(evidence, $"publisher-block-{Guid.NewGuid():N}", "state.json");
var publicationFailed = 0;
using var failedPublisher = new NativeSnapshotWriter(failedOutput, _ => [123, 125], () => Interlocked.Increment(ref publicationFailed));
failedPublisher.Offer(new object());
PumpUntil(() => Volatile.Read(ref publicationFailed) == 1, "publisher IO failure remains off dispatcher");
Directory.CreateDirectory(Path.GetDirectoryName(failedOutput)!);
failedPublisher.Offer(new object());
PumpUntil(() => File.Exists(failedOutput), "actual publisher recovers after owned storage becomes available");
Check(File.ReadAllText(failedOutput) == "{}", "optional publisher failure and recovery preserve complete atomic snapshots");
failedPublisher.Dispose(); Wait(failedPublisher.Completion);

var oversizedOutput = Path.Combine(evidence, $"publisher-size-{Guid.NewGuid():N}.json");
var oversizedFailures = 0;
using var boundedPublisher = new NativeSnapshotWriter(oversizedOutput, snapshot => (byte[])snapshot, () => Interlocked.Increment(ref oversizedFailures));
boundedPublisher.Offer(new byte[2 * 1024 * 1024 + 1]);
PumpUntil(() => Volatile.Read(ref oversizedFailures) == 1, "publisher rejects excess bytes");
Check(!File.Exists(oversizedOutput), "oversized producer publication cannot expose an incomplete snapshot");
boundedPublisher.Offer(new byte[] { 123, 125 });
PumpUntil(() => File.Exists(oversizedOutput), "publisher recovers after excess output");
boundedPublisher.Dispose(); Wait(boundedPublisher.Completion);
Check(File.ReadAllText(oversizedOutput) == "{}", "producer publication recovers after byte-bound failure");
var latestOutput = Path.Combine(evidence, $"publisher-latest-{Guid.NewGuid():N}.json");
using var serializationEntered = new ManualResetEventSlim();
using var allowSerialization = new ManualResetEventSlim();
var serialized = 0;
var serializationThread = 0;
var uiThread = Environment.CurrentManagedThreadId;
using var latestPublisher = new NativeSnapshotWriter(latestOutput, snapshot =>
{
    Interlocked.Exchange(ref serializationThread, Environment.CurrentManagedThreadId);
    if (Interlocked.Increment(ref serialized) == 1) { serializationEntered.Set(); allowSerialization.Wait(); }
    return System.Text.Encoding.UTF8.GetBytes(snapshot.ToString()!);
}, () => throw new InvalidOperationException("Unexpected publisher failure"));
latestPublisher.Offer(1);
PumpUntil(() => serializationEntered.IsSet, "producer worker begins serialization independently");
for (var sequence = 2; sequence <= 1000; sequence++) latestPublisher.Offer(sequence);
allowSerialization.Set();
PumpUntil(() => File.Exists(latestOutput) && File.ReadAllText(latestOutput) == "1000", "slow producer converges to newest complete snapshot");
latestPublisher.Dispose(); Wait(latestPublisher.Completion);
Check(serialized == 2 && serializationThread != uiThread, "producer serializes off dispatcher and coalesces pending captures instead of growing an unbounded queue");

var overlaySpec = Result(SnapshotFiles.InvokeJsonAsync<OverlaySpec>(["native-overlay", "info"], SnapshotFiles.StatusBytes));
overlaySpec.Validate();
var overlayObservation = NativeFrame("Arena", 20, frames[1]);
var overlayRaster = OverlayRendering.Render(overlayObservation, new TrackerSettings(), overlaySpec, Path.Combine(evidence, "native-overlay-render.png"));
Check(overlayRaster.Pixels.Length == overlaySpec.Width * overlaySpec.Height * 4
    && Enumerable.Range(0, overlayRaster.Pixels.Length / 4).All(index => overlayRaster.Pixels[index * 4 + 3] == 255), "actual Avalonia renderer produces a complete opaque bounded raster for Rust-owned overlay");
var alternateRaster = OverlayRendering.Render(NativeFrame("Arena", 21, frames[0]), new TrackerSettings(), overlaySpec);
Check(!overlayRaster.Pixels.SequenceEqual(alternateRaster.Pixels), "native overlay pixels change when observed offers and picks replace each other");
File.WriteAllText(Path.Combine(evidence, "native-overlay-frame.json"), System.Text.Json.JsonSerializer.Serialize(new
{
    kind = "frame",
    schemaVersion = 1,
    sourceSession = overlayRaster.SourceSession,
    sequence = overlayRaster.Sequence,
    observedMs = overlayRaster.ObservedMs,
    width = overlaySpec.Width,
    height = overlaySpec.Height,
    pixelFormat = overlaySpec.PixelFormat,
    pixels = Convert.ToBase64String(overlayRaster.Pixels)
}));
window = new MainWindow(new(fixture, Path.Combine(evidence, "overlay-fault-settings.json")));
window.Show(); Wait(window.Initialization);
var slowOverlay = Path.Combine(evidence, $"slow-overlay-{Guid.NewGuid():N}.sh");
File.WriteAllText(slowOverlay, "#!/bin/sh\nprintf '%s' \"$$\" > \"$0.pid\"\nexec sleep 30\n");
if (OperatingSystem.IsLinux()) File.SetUnixFileMode(slowOverlay, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
NativeOverlayActor? stalledOverlay = null;
try
{
    Environment.SetEnvironmentVariable("HDT_HOST_BINARY", slowOverlay);
    stalledOverlay = new(new(1, 1, "owned-supervision-fixture", 0, 0, 640, 720), _ => { });
    PumpUntil(() => File.Exists(slowOverlay + ".pid"), "owned overlay helper starts");
    Click(window.NextButton);
    Check(!stalledOverlay.Ready.IsCompleted && window.PickedCount == 1, "native pointer navigation remains responsive while overlay preparation stalls");
    try { Wait(stalledOverlay.Ready); throw new InvalidOperationException("Stalled overlay was accepted"); }
    catch (IOException) { Check(true, "native overlay preparation has a bounded owned-child deadline"); }
    Wait(stalledOverlay.Completion);
    var ownedPid = int.Parse(File.ReadAllText(slowOverlay + ".pid"));
    PumpUntil(() => { try { using var process = System.Diagnostics.Process.GetProcessById(ownedPid); return process.HasExited; } catch (ArgumentException) { return true; } }, "owned stalled overlay helper is reaped");
    Check(true, "failed native overlay owner is killed and reaped before restart becomes available");
}
finally { stalledOverlay?.Stop(); if (stalledOverlay is not null) Wait(stalledOverlay.Completion); Environment.SetEnvironmentVariable("HDT_HOST_BINARY", realHost); window.Close(); }

Console.WriteLine($"{checks} checks passed; renderer evidence: {evidence}");
