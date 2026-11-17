using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hdt.Avalonia;

var ownedWindows = new List<MainWindow>();
var ownedActors = new List<NativeOverlayActor>();
var ownedWriters = new List<NativeSnapshotWriter>();
MainWindow CreateWindow(LaunchOptions options) { var value = new MainWindow(options); ownedWindows.Add(value); return value; }
try
{
#if WATCHER_ACCEPTANCE
    if (args is ["--watcher-events", var watcherEvidence])
    { Environment.ExitCode = NativeWatcherChecks.Run(watcherEvidence); return; }
#endif
    if (args is ["--overlay-reference", var sourcePath, var settingsPath, var outputPath])
    {
        AppBuilder.Configure<App>().UseHeadless(new() { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
        var observation = new NativeObservationReader(sourcePath).ReadAsync().GetAwaiter().GetResult();
        var preferences = new SettingsStore(settingsPath).LoadAsync().GetAwaiter().GetResult();
        var spec = SnapshotFiles.InvokeJsonAsync<OverlaySpec>(["native-overlay", "info"], SnapshotFiles.StatusBytes).GetAwaiter().GetResult();
        File.WriteAllBytes(outputPath, OverlayRendering.Render(observation, preferences, spec).Pixels);
        return;
    }
    var negativeControl = args is ["--negative-control", _, _];
    if (negativeControl) args = args[1..];
    if (args is ["--desktop-overlay-ui", var desktopEvidence])
    { Environment.ExitCode = OverlayDesktopAcceptance.Run(desktopEvidence); return; }
    if (args.Length != 2) throw new ArgumentException("Usage: Hdt.Checks [--negative-control] FIXTURE EVIDENCE_DIRECTORY | --desktop-overlay-ui EVIDENCE_DIRECTORY");
    var fixture = Path.GetFullPath(args[0]);
    var evidence = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(evidence);
    AppBuilder.Configure<App>().UseHeadless(new() { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
    AvaloniaSynchronizationContext.InstallIfNeeded();
    Console.WriteLine($"CHECK RUNNER PID {Environment.ProcessId}");
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
    var window = CreateWindow(new(fixture, settings));
    Console.WriteLine($"OPTIONS VIEW IMPLEMENTATION {window.SettingsPanel.GetType().Assembly.FullName}");
    window.Show(); Wait(window.Initialization); Dispatcher.UIThread.RunJobs();
    if (negativeControl)
    {
        var helper = Path.Combine(evidence, "negative-control-helper.sh");
        File.WriteAllText(helper, "#!/bin/sh\nprintf '%s' \"$$\" > \"$0.pid\"\nexec sleep 30\n");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Environment.SetEnvironmentVariable("HDT_HOST_BINARY", helper);
        var actor = new NativeOverlayActor(new(1, 1, "owned-negative-control", 0, 0, 640, 720), _ => { });
        ownedActors.Add(actor);
        PumpUntil(() => File.Exists(helper + ".pid"), "negative control starts its real owned helper");
        Check(false, "INTENTIONAL NEGATIVE CONTROL: runner must return 1 after orderly cleanup");
    }

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
    var persisted = CreateWindow(new(fixture, settings));
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
    var restored = CreateWindow(new(fixture, settings));
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
    window = CreateWindow(new(fixture, settings, plugin));
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
    window = CreateWindow(new(fixture, Path.Combine(blocker, "settings.json")));
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

    var clientDataPath = Path.Combine(evidence, $"library-{Guid.NewGuid():N}.json");
    window = CreateWindow(new(fixture, Path.Combine(evidence, "private-" + new string('p', 140), "client-settings.json"), ClientData: clientDataPath));
    window.Show(); Wait(window.Initialization);
    var clientTabs = window.GetVisualDescendants().OfType<TabControl>().Single();
    Click((TabItem)clientTabs.Items[0]!);
    var editor = window.DeckLibrary;
    Check(editor.GetType().BaseType == typeof(DeckLibrarySurface)
        && editor.Catalog.GetType().BaseType == typeof(CatalogSurface)
        && editor.GetVisualDescendants().OfType<HistorySurface>().Any()
        && typeof(DeckLibrarySurface).Assembly == typeof(SettingsView).Assembly,
        "production library, editor, catalog and embedded history use the F# presentation assembly");
    Check(editor.SelectedPanel.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "No deck selected")
        && editor.SaveButton.IsEnabled == (window.Library.Ready && !window.Library.Busy),
        "empty F# library renders explicit empty selection with verified-storage availability");
    var deckAction = window.GetVisualDescendants().OfType<Menu>().Single().Items.Cast<MenuItem>().Single(item => item.Header?.ToString() == "Deck");
    var emptyRevision = window.Library.State.Revision;
    foreach (var size in new[] { (Width: 1120, Height: 760), (Width: 800, Height: 600) })
    {
        window.Width = size.Width; window.Height = size.Height;
        Click(deckAction);
        Check(!editor.EditorPane.IsExpanded && editor.SelectedPanel.IsEffectivelyVisible
            && editor.Status == "Select a saved deck before editing." && window.Library.State.Revision == emptyRevision
            && window.Library.State.Decks.Length == 0, $"{size.Width}: top Deck requires a saved selection without opening or creating a deck");
        FitsWindowBounds(editor.StatusText, $"{size.Width}: missing-selection instruction is visible");
        window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, $"deck-action-empty-{size.Width}.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
    window.Width = 1120; window.Height = 760;
    void Fill(TextBox box, string text)
    {
        Click(box);
        window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
        window.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
        if (text.Length == 0) { window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null); window.KeyRelease(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null); }
        else window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
    }
    Click(window.GetVisualDescendants().OfType<Menu>().Single().Items.Cast<MenuItem>().Single(item => item.Header?.ToString() == "Import"));
    FitsWindowBounds(editor.CodeBox, "top Import action exposes code before input helper scrolling");
    Click(editor.NewButton);
    FitsWindowBounds(editor.NameBox, "New action exposes the name field before input helper scrolling");
    Fill(editor.NameBox, "Native fixture deck");
    Click(deckAction);
    Check(editor.EditorPane.IsExpanded && editor.NameBox.Text == "Native fixture deck"
        && window.Library.State.Revision == emptyRevision && editor.Status == "Select a saved deck before editing.",
        "top Deck preserves a new unsaved deck instead of treating it as a saved selection");
    Fill(editor.ClassBox, "Mage");
    Fill(editor.HeroBox, "637");
    Fill(editor.CardsBox, "77 1\n100 2");
    Fill(editor.NotesBox, "Native pointer and keyboard acceptance");
    Click(editor.SaveButton); Wait(editor.PendingAction);
    Check(window.Library.State.Decks.Length == 1 && window.Library.State.Decks[0].Name == "Native fixture deck", "native deck editor saves through real Rust transaction boundary");
    Check(editor.SelectedPanel.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Native fixture deck")
        && editor.SelectedPanel.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "2 × DBF 100"),
        "F# selected facts render saved identities and multiplicity rather than unsaved input");
    Click(editor.ExportButton); Wait(editor.PendingAction);
    FitsWindowBounds(editor.CodeBox, "Export action exposes code before input helper scrolling");
    Check(editor.CodeBox.Text?.Contains("AAECAf0EAU0BZAAA") == true, "native export produces independently specified Hearthstone deckstring");
    Click(editor.CopyButton); Wait(editor.PendingAction);
    Check(window.Clipboard is not null && Result(window.Clipboard.TryGetTextAsync()) == editor.CodeBox.Text && editor.Status.Contains("copied"), "native copy button publishes the actual exported deckstring to the isolated clipboard");
    Fill(editor.CodeBox, "### " + new string('z', 300) + "\n" + editor.CodeBox.Text);
    FitsWindowBounds(editor.CodeBox, "long commented deck export stays confined to its code field");
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
    // Representative-size acceptance exercises real controls, not only style constants.
    var longSavedNotes = "Saved notes — " + new string('N', 600);
    Fill(editor.NotesBox, longSavedNotes);
    Fill(editor.NameBox, "Long native deck name — " + new string('W', 110));
    Click(editor.SaveButton); Wait(editor.PendingAction);
    Click(editor.OverviewButton);
    Check(!editor.EditorPane.IsExpanded && !editor.NameBox.IsEffectivelyVisible, "default deck overview hides raw editor fields behind an explicit action");
    void FitsWindowBounds(Control control, string reason)
    {
        var point = control.TranslatePoint(default, window) ?? throw new InvalidOperationException("Missing layout position");
        Check(control.IsEffectivelyVisible && control.Bounds.Width > 0 && control.Bounds.Height > 0
            && point.X >= 0 && point.Y >= 0 && point.X + control.Bounds.Width <= window.Bounds.Width + 1
            && point.Y + control.Bounds.Height <= window.Bounds.Height + 1, reason);
    }
    bool InsideScrollViewport(Control control)
    {
        var viewport = control.GetVisualAncestors().OfType<ScrollViewer>().First();
        var origin = control.TranslatePoint(default, viewport)!.Value;
        return origin.X >= 0 && origin.Y >= 0 && origin.X + control.Bounds.Width <= viewport.Viewport.Width + 1
            && origin.Y + control.Bounds.Height <= viewport.Viewport.Height + 1;
    }
    void Ordered(Control first, Control second, string reason)
    {
        var left = first.TranslatePoint(default, window)!.Value;
        var right = second.TranslatePoint(default, window)!.Value;
        Check(left.X + first.Bounds.Width <= right.X + 1, reason);
    }
    foreach (var size in new[] { (Width: 1120, Height: 760), (Width: 800, Height: 600) })
    {
        window.Width = size.Width; window.Height = size.Height;
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); window.CaptureRenderedFrame();
        FitsWindowBounds(editor.BrowserPanel, $"{size.Width}: deck browser remains inside viewport");
        FitsWindowBounds(editor.GamesPanel, $"{size.Width}: actual My Games panel remains inside viewport");
        FitsWindowBounds(editor.SelectedPanel, $"{size.Width}: long selected deck facts remain inside viewport");
        Ordered(editor.BrowserPanel, editor.GamesPanel, $"{size.Width}: browser and games do not overlap");
        Ordered(editor.GamesPanel, editor.SelectedPanel, $"{size.Width}: games and selected deck do not overlap");
        var savedNotesLabel = editor.SelectedPanel.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == longSavedNotes);
        Check(savedNotesLabel.TextWrapping == global::Avalonia.Media.TextWrapping.Wrap && savedNotesLabel.Bounds.Width <= editor.SelectedPanel.Bounds.Width,
            $"{size.Width}: long saved F# notes wrap within selected-deck facts");
        foreach (var tab in clientTabs.Items.Cast<TabItem>()) FitsWindowBounds(tab, $"{size.Width}: navigation {tab.Header} remains visible");
        window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, $"layout-library-{size.Width}.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        Click((TabItem)clientTabs.Items[3]!);
        FitsWindowBounds(window.SettingsPanel.CategoryPanel, $"{size.Width}: settings sidebar remains visible");
        FitsWindowBounds(window.SettingsPanel.DetailPanel, $"{size.Width}: settings content remains visible");
        Ordered(window.SettingsPanel.CategoryPanel, window.SettingsPanel.DetailPanel, $"{size.Width}: settings categories and content do not overlap");
        window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, $"layout-settings-{size.Width}.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        Click(window.SettingsPanel.StorageCategory);
        FitsWindowBounds(window.SettingsPanel.DetailPanel, $"{size.Width}: local paths remain confined to settings content");
        var pathLabel = window.SettingsPanel.DetailPanel.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text?.Contains(new string('p', 140)) == true);
        Check(pathLabel.IsEffectivelyVisible && pathLabel.TextWrapping == global::Avalonia.Media.TextWrapping.Wrap
            && pathLabel.Bounds.Width <= window.SettingsPanel.DetailPanel.Bounds.Width, $"{size.Width}: compiled XAML path binding wraps the exact configured path within its visible section");
        var pathLabels = window.SettingsPanel.DetailPanel.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Name is "PreferencesPath" or "LibraryPath" or "ObservationsPath").ToArray();
        Check(pathLabels.Length == 3 && pathLabels.All(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text))
            && pathLabels.Single(text => text.Name == "LibraryPath").Text == clientDataPath,
            $"{size.Width}: local files exposes all three supplied paths through compiled XAML bindings");
        window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, $"layout-local-files-{size.Width}.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        Click(window.SettingsPanel.ArenaCategory);
        Click(window.SettingsPanel.OverlayCategory);
        Check(window.NativeOverlay.IsEffectivelyVisible && !window.ScoresToggle.IsEffectivelyVisible
            && window.SettingsPanel.DetailPanel.GetVisualDescendants().Contains(window.NativeOverlay),
            $"{size.Width}: F# settings navigation retains the real native overlay controls");
        Click(window.SettingsPanel.ArenaCategory);
        Check(window.ScoresToggle.IsEffectivelyVisible && window.SynergiesToggle.IsEffectivelyVisible
            && !window.NativeOverlay.IsEffectivelyVisible && pathLabels.All(text => !text.IsEffectivelyVisible),
            $"{size.Width}: Arena category restores its injected controls and hides other XAML panes");
        Click((TabItem)clientTabs.Items[1]!);
        var arenaColumns = (Grid)((TabItem)clientTabs.Items[1]!).Content!;
        foreach (var pane in arenaColumns.Children) FitsWindowBounds(pane, $"{size.Width}: Arena offers and picked-deck columns remain visible");
        window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, $"layout-arena-{size.Width}.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        Click((TabItem)clientTabs.Items[0]!);
    }
    Fill(editor.SearchBox, "no matching owned fixture");
    Check(editor.Decks.ItemCount == 0, "deck browser search filters actual persisted decks");
    Fill(editor.SearchBox, "");
    Check(editor.Decks.ItemCount == 2, "clearing search restores saved decks without rewriting the library");
    Click(deckAction);
    FitsWindowBounds(editor.NameBox, "minimum width top Deck action exposes selected name before helper scrolling");
    Click(editor.OverviewButton);
    Check(!editor.EditorPane.IsExpanded && editor.SelectedPanel.IsEffectivelyVisible,
        "minimum width pointer overview restores the selected-deck edit control");
    Click(editor.EditButton);
    FitsWindowBounds(editor.NameBox, "minimum width Edit selected deck action exposes name before helper scrolling");
    Fill(editor.NotesBox, "Edited persistent notes");
    Click(editor.SaveButton); Wait(editor.PendingAction);
    FitsWindowBounds(editor.SaveButton, "minimum width save remains visible after pointer activation");
    Click(editor.ExportButton); Wait(editor.PendingAction);
    Click(editor.CopyButton); Wait(editor.PendingAction);
    FitsWindowBounds(editor.CopyButton, "minimum width export and copy actions stay inside secondary editor");
    Check(editor.CodeBox.Text?.Contains("AAECAf0EAU0BZAAA") == true, "minimum width pointer save/export preserves actual deck code");
    window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, "layout-editor-800.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    window.Width = 1120; window.Height = 760;
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
    Fill(editor.SearchBox, "filter that must not hide the new deck");
    Click(editor.NewButton);
    Check(editor.SearchBox.Text == "", "New clears an active browser search so a newly saved deck cannot remain hidden");
    FitsWindowBounds(editor.NameBox, "New from archived browsing exposes name in one action before helper scrolling");
    Check(editor.SelectedPanel.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "No deck selected")
        && !editor.SelectedPanel.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "2 × DBF 100"),
        "F# New transition clears previous selected cards even while the overview is hidden");
    Check(editor.ShowArchived.IsChecked == false && !editor.NameBox.IsReadOnly && editor.NameBox.Text == "" && editor.SaveButton.IsEnabled && editor.EditorPane.IsExpanded,
        "new deck leaves archive-only browsing so newly saved decks cannot disappear into a hidden active list");
    var savedLibrary = window.Library.State;
    Fill(editor.CodeBox, "invalid deck code");
    Click(editor.ImportButton); Wait(editor.PendingAction);
    Check(!window.Library.Ready && window.Library.State.Revision == savedLibrary.Revision && !editor.SaveButton.IsEnabled, "invalid native import leaves the saved library intact and requires explicit reload");
    Click(editor.ReloadButton); Wait(editor.PendingAction);
    Check(window.Library.Ready && window.Library.State.Decks.Length == savedLibrary.Decks.Length && window.Library.State.Matches.Length == 1, "native library recovers from failed import by verifying the actual persisted state");
    Click(editor.NewButton);
    var catalogPath = Path.Combine(evidence, new string('c', 120), "illustrative-catalog.json");
    Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
    window.Width = 800; window.Height = 600;
    Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    var catalogJson = "[{\"dbfId\":777,\"id\":\"FIXTURE_CATALOG\",\"name\":\"Illustrative catalog card\",\"type\":\"MINION\",\"cardClass\":\"MAGE\",\"cost\":2,\"collectible\":true},{\"dbfId\":637,\"id\":\"HERO_FIXTURE\",\"name\":\"Illustrative base hero\",\"type\":\"HERO\",\"cardClass\":\"MAGE\"}]";
    File.WriteAllText(catalogPath, catalogJson);
    var catalog = editor.Catalog;
    Click((Expander)catalog.Content!);
    Fill(catalog.PathBox, catalogPath); Fill(catalog.QueryBox, "catalog mage");
    FitsWindowBounds(catalog.PathBox, "minimum-width catalog confines a long local path to its input");
    Click(catalog.SearchButton); Wait(catalog.PendingSearch);
    Click(catalog.Results.GetVisualDescendants().OfType<ListBoxItem>().Single());
    Click(catalog.AddButton); Click(catalog.AddButton);
    Click(catalog.RemoveButton);
    FitsWindowBounds(catalog.RemoveButton, "minimum-width catalog card actions wrap within the editor");
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
    FitsWindowBounds(catalog.HeroButton, "minimum-width catalog hero action remains reachable");
    Check(catalog.Results.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text?.Contains("? mana • Illustrative base hero") == true),
        "F# catalog row renders unavailable cost as unknown rather than zero");
    window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, "layout-catalog-800.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    window.Width = 1120; window.Height = 760;
    Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
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
    const string savedSideboards = "777 100 1\n777 4294967294 99\n4294967295 4294967294 100";
    Fill(editor.CardsBox, "777 2\n4294967295 1");
    Fill(editor.SideboardsBox, savedSideboards);
    Click(editor.SaveButton); Wait(editor.PendingAction);
    Check(window.Library.State.Decks.Last().Content.Sideboards.SequenceEqual(new[] {
        new SideboardCard(777, 100, 1), new SideboardCard(777, 4294967294, 99), new SideboardCard(4294967295, 4294967294, 100) }),
        "F# sideboard editor persists exact owners, card identities and large multiplicities through Rust");
    Click(editor.NewButton);
    Fill(editor.SearchBox, "Catalog-built native deck");
    Click(editor.Decks.GetVisualDescendants().OfType<ListBoxItem>().Single());
    Check(editor.SideboardsBox.Text == savedSideboards && editor.CardsBox.Text == "777 2\n4294967295 1" && editor.HeroBox.Text == "637" && editor.FormatBox.Text == "2",
        "F# editor reload projects saved sideboards, cards, hero and format after clearing unsaved fields");
    Click(editor.OverviewButton);
    StackPanel SideboardGroup(uint owner) => editor.SelectedPanel.GetVisualDescendants().OfType<StackPanel>()
        .Single(panel => panel.Children.OfType<TextBlock>().Any(text => text.Text == $"SIDEBOARD • owner DBF {owner}"));
    bool SideboardVisible() => editor.SelectedPanel.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text?.StartsWith("SIDEBOARD • owner DBF", StringComparison.Ordinal) == true);
    foreach (var size in new[] { (Width: 1120, Height: 760), (Width: 800, Height: 600) })
    {
        window.Width = size.Width; window.Height = size.Height; Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); window.CaptureRenderedFrame();
        Check(SideboardVisible(), $"{size.Width}: saved sideboards appear in the actual selected-deck overview");
        Check(SideboardGroup(777).GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "99 × DBF 4294967294")
            && SideboardGroup(uint.MaxValue).GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "100 × DBF 4294967294"),
            $"{size.Width}: saved sideboards render exact copies separately under each actual owner");
        Check(editor.SelectedPanel.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "MAGE • 3 cards\nHero DBF 637 • format 2"),
            $"{size.Width}: main-deck count excludes sideboard copies");
        foreach (var group in new[] { SideboardGroup(777), SideboardGroup(uint.MaxValue) })
        {
            Check(group.Bounds.Width > 0 && group.Bounds.Width <= editor.SelectedPanel.Bounds.Width,
                $"{size.Width}: sideboard group remains within selected-deck column");
            foreach (var label in group.GetVisualDescendants().OfType<TextBlock>())
                Check(label.TextWrapping == global::Avalonia.Media.TextWrapping.Wrap && label.Bounds.Width <= group.Bounds.Width,
                    $"{size.Width}: long sideboard identity and count labels wrap within their group");
        }
        FitsWindowBounds(editor.BrowserPanel, $"{size.Width}: sideboard overview keeps deck browser visible");
        FitsWindowBounds(editor.GamesPanel, $"{size.Width}: sideboard overview keeps games visible");
        FitsWindowBounds(editor.SelectedPanel, $"{size.Width}: sideboard overview keeps selected column visible");
        window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, $"layout-sideboards-{size.Width}.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
    Click(editor.EditButton); Fill(editor.SideboardsBox, "777 100 2"); Click(editor.OverviewButton);
    Check(SideboardGroup(777).GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "99 × DBF 4294967294"),
        "unsaved sideboard edit cannot replace saved overview facts");
    Click(editor.NewButton);
    Check(!SideboardVisible(), "New clears prior sideboard owner groups and rows");
    Fill(editor.SearchBox, "Native fixture deck");
    Click(editor.Decks.GetVisualDescendants().OfType<ListBoxItem>().Single()); Click(editor.OverviewButton);
    Check(!SideboardVisible() && editor.SelectedPanel.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Native fixture deck"),
        "selecting a saved deck without sideboards leaves no stale sideboard heading or rows");
    Click(editor.NewButton); Fill(editor.NameBox, "Long sideboard fixture"); Fill(editor.HeroBox, "637"); Fill(editor.CardsBox, "777 1");
    Fill(editor.SideboardsBox, string.Join('\n', Enumerable.Range(200, 25).Select(id => $"777 {id} 1")));
    Click(editor.SaveButton); Wait(editor.PendingAction); Click(editor.OverviewButton);
    foreach (var size in new[] { (Width: 1120, Height: 760), (Width: 800, Height: 600) })
    {
        window.Width = size.Width; window.Height = size.Height; Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); window.CaptureRenderedFrame();
        var selectedScroll = editor.SelectedPanel.GetVisualDescendants().OfType<ScrollViewer>().Single();
        var pointer = editor.SelectedPanel.TranslatePoint(new Point(editor.SelectedPanel.Bounds.Width / 2, editor.SelectedPanel.Bounds.Height / 2), window)!.Value;
        window.MouseMove(pointer); window.MouseWheel(pointer, new Vector(0, -100), RawInputModifiers.None); Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame();
        Check(selectedScroll.Offset.Y > 0 && SideboardGroup(777).GetVisualDescendants().OfType<TextBlock>().Count() == 26,
            $"{size.Width}: actual wheel input scrolls all saved long-sideboard rows without losing entries");
        FitsWindowBounds(SideboardGroup(777).GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "1 × DBF 224"),
            $"{size.Width}: final sideboard row is reachable through actual wheel input");
        FitsWindowBounds(editor.BrowserPanel, $"{size.Width}: long sideboard cannot expand or overlap the deck browser");
        FitsWindowBounds(editor.GamesPanel, $"{size.Width}: long sideboard cannot expand or overlap the games panel");
        window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, $"layout-sideboards-long-{size.Width}.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
    window.Close();
    var historyPath = Path.Combine(evidence, "deck-history-library.json");
    var historySeed = new ClientLibrary(historyPath);
    Wait(historySeed.ReloadAsync());
    Wait(historySeed.SaveAsync(null, "History deck A", "Mage", new(637, 2, [new(77, 1)], []), "Saved A"));
    var deckA = historySeed.State.Decks.Single().Id;
    Wait(historySeed.SaveAsync(null, "History deck B", "Warrior", new(637, 2, [new(100, 1)], []), "Saved B"));
    var deckB = historySeed.State.Decks.Last().Id;
    var matchA = new MatchRecord(Guid.NewGuid().ToString(), deckA, DateTimeOffset.UtcNow.ToString("o"), "History A", "Win", "Mage", "Warrior", 8, null, "");
    var matchB = matchA with { Id = Guid.NewGuid().ToString(), DeckId = deckB, Mode = "History B", Result = "Loss" };
    var unmatched = matchA with { Id = Guid.NewGuid().ToString(), DeckId = null, Mode = "Unassociated", Result = "Draw" };
    foreach (var match in new[] { matchA, matchB, unmatched }) Wait(historySeed.RecordAsync(match));
    window = CreateWindow(new(null, Path.Combine(evidence, "deck-history-settings.json"), ClientData: historyPath));
    window.Show(); Wait(window.Initialization);
    editor = window.DeckLibrary;
    var historyTabs = window.GetVisualDescendants().OfType<TabControl>().Single();
    void ChooseHistoryDeck(string id)
    {
        Click(editor.Decks.GetVisualDescendants().OfType<ListBoxItem>().Single(item => item.DataContext is DeckEntry entry && entry.Id == id));
    }
    string[] ShownHistoryIds(HistoryView view) => view.Matches.Items.Cast<MatchRecord>().Select(match => match.Id).ToArray();
    Check(ShownHistoryIds(editor.History).SequenceEqual(new[] { matchB.Id }) && editor.History.Summary == "1 supplied games • 0 wins / 1 losses",
        "reconstructed selected-deck history excludes other-deck and anonymous persisted records");
    var deckHistoryRevision = window.Library.State.Revision;
    foreach (var size in new[] { (Width: 1120, Height: 760), (Width: 800, Height: 600) })
    {
        window.Width = size.Width; window.Height = size.Height;
        ChooseHistoryDeck(deckA);
        Check(ShownHistoryIds(editor.History).SequenceEqual(new[] { matchA.Id }) && editor.History.Summary == "1 supplied games • 1 wins / 0 losses",
            $"{size.Width}: actual deck-row pointer selection changes both history rows and derived counts");
        FitsWindowBounds(editor.GamesPanel, $"{size.Width}: selected-deck games stay visible within library layout");
        ChooseHistoryDeck(deckB);
        Check(ShownHistoryIds(editor.History).SequenceEqual(new[] { matchB.Id }) && window.Library.State.Revision == deckHistoryRevision,
            $"{size.Width}: browsing another deck neither shows stale history nor writes selection or stored associations");
        window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, $"deck-history-{size.Width}.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
    Click(editor.EditButton); Fill(editor.NotesBox, "Unsaved history draft");
    var extraB = matchB with { Id = Guid.NewGuid().ToString(), Result = "Win" };
    Wait(window.Library.RecordAsync(extraB));
    Check(ShownHistoryIds(editor.History).SequenceEqual(new[] { extraB.Id, matchB.Id })
        && editor.History.Summary == "2 supplied games • 1 wins / 1 losses" && editor.NotesBox.Text == "Unsaved history draft" && editor.EditorPane.IsExpanded,
        "background history refresh preserves unsaved editor state while showing only the displayed deck's new game");
    Click((TabItem)historyTabs.Items[2]!);
    var globalHistory = window.GetVisualDescendants().OfType<HistoryView>().Single(view => view != editor.History);
    Check(ShownHistoryIds(globalHistory).ToHashSet().SetEquals(new[] { matchA.Id, matchB.Id, unmatched.Id, extraB.Id })
        && globalHistory.Summary == "4 supplied games • 2 wins / 1 losses",
        "global Stats retains every persisted game including anonymous and other-deck records");
    Click((TabItem)historyTabs.Items[0]!); Click(editor.EditButton);
    Click(editor.ArchiveButton); Wait(editor.PendingAction);
    Check(editor.History.Matches.ItemCount == 0 && editor.History.EmptyMessage == "Select a saved deck to see its games.",
        "archiving clears displayed-deck history and asks for explicit selection");
    Click(editor.ShowArchived); ChooseHistoryDeck(deckB);
    Check(ShownHistoryIds(editor.History).SequenceEqual(new[] { extraB.Id, matchB.Id }), "archived deck preserves its own historical games");
    Click(editor.ArchiveButton); Wait(editor.PendingAction);
    Click(editor.NewButton);
    Check(editor.History.Matches.ItemCount == 0 && editor.History.EmptyMessage == "Select a saved deck to see its games.",
        "New deck clears prior history rather than presenting an anonymous-record group");
    Wait(window.Library.SaveAsync(null, "History empty deck", "Mage", new(637, 2, [new(77, 1)], []), ""));
    ChooseHistoryDeck(window.Library.State.Decks.Last().Id);
    Check(editor.History.Matches.ItemCount == 0 && editor.History.EmptyMessage == "No supplied games recorded." && editor.History.Summary == "0 supplied games • 0 wins / 0 losses",
        "saved deck with no matches has a distinct truthful empty history");
    var verifiedHistory = new ClientLibrary(historyPath); Wait(verifiedHistory.ReloadAsync());
    Check(verifiedHistory.State.Matches.Single(match => match.Id == matchA.Id).DeckId == deckA
        && verifiedHistory.State.Matches.Single(match => match.Id == matchB.Id).DeckId == deckB
        && verifiedHistory.State.Matches.Single(match => match.Id == unmatched.Id).DeckId is null,
        "actual storage retains original deck associations and anonymous records after all navigation and archive transitions");
    window.Close();
    var heroFrame = frames[0] with
    {
        SchemaVersion = 2,
        Sequence = 1,
        Offers = [],
        Selection = new ArenaHeroSelection([new("FIXTURE_HERO_A", "Illustrative Mage", "Mage"), new("FIXTURE_HERO_B", "Illustrative Warrior", "Warrior")])
    };
    var cardFrame = frames[0] with { SchemaVersion = 2, Sequence = 2, Offers = [], Selection = new ArenaCardDraft(frames[0].Offers) };
    var replacementFrame = frames[1] with { SchemaVersion = 2, Sequence = 3, Offers = [], Selection = new ArenaCardDraft(frames[1].Offers) };
    var unsupportedFrame = replacementFrame with { Sequence = 4, Selection = new ArenaUnavailable(ArenaUnavailableKind.Redraft, "Redraft fixture has no supported native suggestions.") };
    var menuFrame = frames[2] with { SchemaVersion = 2, Sequence = 5, PickedDeck = [], PickIndex = 0, SelectedCardId = null, Selection = new ArenaInactive() };
    ArenaObservation[] phaseFrames = [heroFrame, cardFrame, replacementFrame, unsupportedFrame, menuFrame];
    Check(frames[0].ViewSelection is ArenaCardDraft && frames[2].ViewSelection is ArenaInactive, "schema 1 adapter remains explicitly card-only");
    var eventHero = heroFrame with
    {
        SchemaVersion = 3,
        ChoiceEvidence = new(ArenaChoiceOrigin.HdtWatcherEvent, heroFrame.ObservedAt.ToUniversalTime(), 1, 0),
        Selection = new ArenaHeroSelection(((ArenaHeroSelection)heroFrame.Selection!).Choices.Select((hero, index) => hero with { SourceIndex = index }).ToArray()),
        CachedDeck = new(ArenaCachedDeckOrigin.HdtPublicTileModel, [])
    };
    ReplaySource.Validate([eventHero]);
    Check(eventHero.ViewSelection is ArenaHeroSelection, "schema 3 retains explicit hero identity with separate event receipt evidence");
    foreach (var invalidEvent in new[] { eventHero with { ChoiceEvidence = null }, eventHero with { SchemaVersion = 2 },
        eventHero with { ChoiceEvidence = eventHero.ChoiceEvidence! with { Origin = (ArenaChoiceOrigin)999 } },
        eventHero with { ChoiceEvidence = eventHero.ChoiceEvidence! with { ReceivedAt = eventHero.ObservedAt.AddSeconds(1) } },
        eventHero with { ChoiceEvidence = eventHero.ChoiceEvidence! with { Revision = 0 } },
        eventHero with { ChoiceEvidence = eventHero.ChoiceEvidence! with { Generation = -1 } },
        eventHero with { Selection = new ArenaHeroSelection([new("H1", "Hero 1", null)]) },
        eventHero with { Selection = new ArenaHeroSelection([new("H1", "Hero 1", null, 1), new("H2", "Hero 2", null, 1)]) },
        eventHero with { CachedDeck = null },
        eventHero with { PickedDeck = [new("OLD", "Separate cached card", 1)], PickIndex = 1 },
        eventHero with { Selection = new ArenaInactive(), Mode = ArenaMode.Inactive },
        cardFrame with { SchemaVersion = 3, ChoiceEvidence = eventHero.ChoiceEvidence } })
    {
        try { ReplaySource.Validate([invalidEvent]); throw new InvalidOperationException("Unsupported event evidence accepted."); }
        catch (InvalidDataException) { Check(true, "raw-event provenance and unmatched rating generations fail closed"); }
    }
    foreach (var invalidPhase in new[] { heroFrame with { Offers = frames[0].Offers }, heroFrame with { PickedDeck = frames[1].PickedDeck, PickIndex = 1 },
        heroFrame with { Selection = null }, menuFrame with { PickIndex = 1, PickedDeck = frames[1].PickedDeck }, cardFrame with { Mode = ArenaMode.Inactive }, frames[0] with { Selection = new ArenaHeroSelection([]) } })
    {
        try { ReplaySource.Validate([invalidPhase]); throw new InvalidOperationException("Mixed or contradictory Arena payload accepted."); }
        catch (InvalidDataException) { Check(true, "contradictory phase/choice payload fails closed"); }
    }
    var phaseFile = Path.Combine(evidence, "arena-phases.json");
    File.WriteAllText(phaseFile, System.Text.Json.JsonSerializer.Serialize(phaseFrames, ReplaySource.JsonOptions));
    Check(Result(new ReplaySource(phaseFile).ReadAsync())[0].ViewSelection is ArenaHeroSelection, "bounded source reader preserves tagged hero choices through JSON");
    var mixedJson = System.Text.Json.JsonSerializer.Serialize(heroFrame, ReplaySource.JsonOptions).Replace("\"phase\": \"HeroSelection\"", "\"phase\": \"HeroSelection\", \"CardChoices\": []");
    try { System.Text.Json.JsonSerializer.Deserialize<ArenaObservation>(mixedJson, ReplaySource.JsonOptions); throw new InvalidOperationException("Unknown hero/card mixture accepted."); }
    catch (System.Text.Json.JsonException) { Check(true, "tagged hero payload rejects fields from other choice shapes"); }
    var missingKindJson = System.Text.Json.JsonSerializer.Serialize(unsupportedFrame, ReplaySource.JsonOptions).Replace("\"Kind\": \"Redraft\",", "");
    try { System.Text.Json.JsonSerializer.Deserialize<ArenaObservation>(missingKindJson, ReplaySource.JsonOptions); throw new InvalidOperationException("Unavailable variant kind was guessed."); }
    catch (System.Text.Json.JsonException) { Check(true, "unsupported selection requires explicit choice kind instead of defaulting omitted kind"); }
    var missingPhaseFile = Path.Combine(evidence, "arena-missing-phase.json");
    File.WriteAllText(missingPhaseFile, System.Text.Json.JsonSerializer.Serialize(phaseFrames, ReplaySource.JsonOptions).Replace("\"phase\": \"HeroSelection\",", ""));
    try { Result(new ReplaySource(missingPhaseFile).ReadAsync()); throw new InvalidOperationException("Missing selection phase accepted."); }
    catch (InvalidDataException) { Check(true, "actual bounded JSON boundary normalizes missing polymorphic tag to controlled input failure"); }
    window = CreateWindow(new(phaseFile, Path.Combine(evidence, "phase-settings.json")));
    window.Show(); Wait(window.Initialization);
    var phaseTabs = window.GetVisualDescendants().OfType<TabControl>().Single();
    bool HasText(string text) => window.GetVisualDescendants().OfType<TextBlock>().Any(label => label.IsEffectivelyVisible && label.Text?.Contains(text, StringComparison.OrdinalIgnoreCase) == true);
    foreach (var size in new[] { (Width: 1120, Height: 760), (Width: 800, Height: 600) })
    {
        window.Width = size.Width; window.Height = size.Height;
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); window.CaptureRenderedFrame();
        Check(window.Summary.Contains("Choose a hero") && window.OfferCount == 2 && HasText("Illustrative Mage") && !HasText("mana") && !HasText("Deck synergies:"), "hero phase renders identities without card mana, scores or synergy advice");
        FitsWindowBounds(window.NextButton, $"{size.Width}: phase navigation is reachable without scrolling");
        foreach (var pane in ((Grid)((TabItem)phaseTabs.Items[1]!).Content!).Children) FitsWindowBounds(pane, $"{size.Width}: hero and deck panels stay inside viewport");
        window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, $"arena-hero-{size.Width}.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        Click(window.NextButton);
        Check(window.Summary.Contains("Card draft") && window.OfferCount == 3 && !HasText("Illustrative Mage") && HasText("mana"), "pointer next replaces hero controls with card controls");
        window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, $"arena-cards-{size.Width}.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        Click(window.NextButton);
        Check(window.OfferCount == 2 && window.PickedCount == 1 && window.Scores[0] == "10", "pointer next replaces the actual card offer and picked-deck facts");
        Click(window.NextButton);
        Check(window.OfferCount == 0 && window.Scores.Count == 0 && HasText("Redraft choices unavailable") && !HasText("Arenasmith "), "unsupported phase clears card advice and visibly reports unavailable");
        Click(window.NextButton);
        Check(window.OfferCount == 0 && window.PickedCount == 0 && window.Summary.Contains("Arena menu") && !HasText("Redraft choices unavailable"), "menu transition clears obsolete unsupported status and choices");
        window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, $"arena-menu-{size.Width}.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        Click(window.ResetButton);
    }
    window.Close();
    var rawCardFrame = cardFrame with
    {
        SchemaVersion = 3,
        PickedDeck = [],
        PickIndex = 0,
        SelectedCardId = null,
        ChoiceEvidence = eventHero.ChoiceEvidence,
        CachedDeck = new(ArenaCachedDeckOrigin.HdtPublicTileModel, []),
        Selection = new ArenaCardDraft(cardFrame.CardOffers.Select((offer, index) => offer with
        {
            BaseRating = null,
            DynamicRating = null,
            Enabled = new([], []),
            EnhancedBy = new([], []),
            SourceIndex = index
        }).ToArray())
    };
    foreach (var adviceFrame in new[] { cardFrame, rawCardFrame })
    {
        var adviceFile = Path.Combine(evidence, $"advice-{adviceFrame.SchemaVersion}.json");
        File.WriteAllText(adviceFile, System.Text.Json.JsonSerializer.Serialize(new[] { adviceFrame }, ReplaySource.JsonOptions));
        window = CreateWindow(new(adviceFile, Path.Combine(evidence, $"advice-settings-{adviceFrame.SchemaVersion}.json")));
        window.Show(); Wait(window.Initialization);
        Console.WriteLine($"ARENA CHOICE IMPLEMENTATION {typeof(ArenaChoiceViews).Assembly.FullName}");
        var adviceTabs = window.GetVisualDescendants().OfType<TabControl>().Single();
        foreach (var (scores, synergies) in new[] { (true, true), (false, true), (false, false), (true, false) })
        {
            Click((TabItem)adviceTabs.Items[3]!);
            if (window.ScoresToggle.IsChecked != scores) { Click(window.ScoresToggle); Wait(window.PendingPreferences); }
            if (window.SynergiesToggle.IsChecked != synergies) { Click(window.SynergiesToggle); Wait(window.PendingPreferences); }
            Click((TabItem)adviceTabs.Items[1]!);
            Check(window.OfferCount == cardFrame.CardOffers.Length && HasText("mana"), "F# advice preference changes preserve the offered card identities");
            Check(adviceFrame.SchemaVersion == 3
                ? HasText("no matching source generation") && !HasText("Arenasmith ") && !HasText("Deck synergies:")
                : HasText("Arenasmith ") == scores && HasText("Deck synergies:") == synergies,
                $"schema {adviceFrame.SchemaVersion}: scores={scores}, synergies={synergies} preserves available versus unavailable advice");
        }
        window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, $"advice-{adviceFrame.SchemaVersion}.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        window.Close();
    }
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
            Offers = mode == "Arena" ? (arenaFrame ?? frames[0]).Offers : [],
            Selection = (arenaFrame ?? frames[2]).SchemaVersion == 2 ? mode == "Arena" ? arenaFrame!.Selection : new ArenaInactive() : null
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
    var startNative = NativeFrame("Constructed", 1) with
    {
        PlayerHealth = null,
        OpponentHealth = 0,
        KnownHand = [new("hand-fixture", 78, "Long supplied known hand name for width containment", 5, 2, 0)]
    };
    ownedWriters.Add(nativePublisher);
    nativePublisher.Offer(startNative);
    PumpUntil(() => File.Exists(nativeFile), "actual optional snapshot publisher emits initial observation");
    window = CreateWindow(new(null, Path.Combine(evidence, "observed-settings.json"), ClientData: nativeLibraryFile, NativeState: nativeFile));
    window.Show(); Wait(window.Initialization);
    var nativeTabs = window.GetVisualDescendants().OfType<TabControl>().Single();
    Check(window.SelectedPage == 5 && window.GameView.ModeText.Contains("Constructed") && window.GameView.CardCount == 4, "native game view consumes actual worker publication through bounded Rust reader");
    bool DeckSectionVisible() => window.GameView.GetVisualDescendants().OfType<TextBlock>()
        .Any(text => text.Text == "OBSERVED REMAINING DECK" && text.IsEffectivelyVisible);
    Check(HasText("Your health unknown") && HasText("Opponent health 0") && HasText("×2 • 0 health"),
        "F# live view distinguishes unknown from zero in observed counts and card health");
    foreach (var size in new[] { (Width: 1120, Height: 760), (Width: 800, Height: 600) })
    {
        window.Width = size.Width; window.Height = size.Height; Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); window.CaptureRenderedFrame();
        var lists = window.GameView.GetVisualDescendants().OfType<ListBox>().ToArray();
        Check(lists.Length == 4 && DeckSectionVisible(), $"{size.Width}: constructed game exposes four supplied card lists");
        foreach (var list in lists) FitsWindowBounds(list, $"{size.Width}: live-game list stays inside the window");
        Ordered(lists[0], lists[1], $"{size.Width}: deck and player columns do not overlap");
        Ordered(lists[1], lists[3], $"{size.Width}: player and opponent columns do not overlap");
        var longName = window.GameView.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Text?.Contains("Long supplied known hand name") == true);
        Check(longName.Bounds.Width <= lists[1].Bounds.Width, $"{size.Width}: supplied long card name wraps inside its list");
        window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, $"native-game-{size.Width}.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
    window.Width = 1120; window.Height = 760; Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); window.CaptureRenderedFrame();
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
    Click(window.DeckLibrary.EditButton);
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
    Check(!DeckSectionVisible() && window.GameView.CardCount == 3,
        "Battlegrounds hides and clears remaining-deck rows while preserving supplied hand and public boards");
    window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, "native-battlegrounds.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    var staleNative = NativeFrame("Arena", 5, frames[1]);
    staleNative = staleNative with { ObservedAt = DateTimeOffset.UtcNow.AddSeconds(-10), Arena = staleNative.Arena! with { ObservedAt = DateTimeOffset.UtcNow.AddSeconds(-10) } };
    nativePublisher.Offer(staleNative);
    PumpUntil(() => window.GameView.ModeText.Contains("unavailable"), "native source freshness clears previous game data");
    Check(window.GameView.CardCount == 0 && window.OfferCount == 0 && window.PickedCount == 0 && !DeckSectionVisible(), "stale native source cannot leave cards, picks, offers or deck sections apparently live");
    window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, "native-observation-stale.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    nativeSession = Guid.NewGuid().ToString();
    nativePublisher.Offer(NativeFrame("Menu", 1));
    PumpUntil(() => window.GameView.ModeText.StartsWith("Menu"), "native source recovers with a fresh process session");
    Check(window.GameView.CardCount == 0 && window.OfferCount == 0, "new source session preserves explicit menu transition without resurrecting prior game");
    var rollback = NativeFrame("Constructed", 2);
    nativePublisher.Offer(rollback);
    PumpUntil(() => window.GameView.ModeText.StartsWith("Constructed"), "fresh native source advances");
    Check(DeckSectionVisible() && window.GameView.CardCount == 4,
        "fresh constructed recovery restores the deck section and replaces unavailable data");
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
    Click((TabItem)nativeTabs.Items[1]!);
    nativePublisher.Offer(NativeFrame("Arena", 6, heroFrame));
    PumpUntil(() => window.Summary.Contains("Choose a hero") && window.OfferCount == 2, "native source independently publishes explicit hero phase");
    Check(!window.NextButton.IsEnabled && HasText("Illustrative Mage"), "observed hero choices are read-only with no game-selection control");
    window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, "native-observed-heroes.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    nativePublisher.Offer(NativeFrame("Arena", 7, cardFrame));
    PumpUntil(() => window.Summary.Contains("Card draft") && window.OfferCount == 3, "native source hero-to-card transition replaces choice kind");
    nativePublisher.Offer(NativeFrame("Arena", 8, replacementFrame));
    PumpUntil(() => window.OfferCount == 2 && window.PickedCount == 1, "native source replaces next offered cards without replay input");
    nativePublisher.Offer(NativeFrame("Menu", 9, menuFrame));
    PumpUntil(() => window.Summary.Contains("Arena menu") && window.OfferCount == 0, "native menu envelope clears current draft choices");
    nativePublisher.Offer(NativeFrame("Arena", 10, heroFrame));
    PumpUntil(() => window.Summary.Contains("Choose a hero"), "hero source resumes before fault");
    nativePublisher.Offer(new { schemaVersion = 1, sessionId = nativeSession, sequence = 11, observedAt = DateTimeOffset.UtcNow, availability = "Available", arena = new { schemaVersion = 2, selection = new { phase = "CardDraft", choices = "malformed" } } });
    PumpUntil(() => window.Summary.Contains("unavailable") && window.OfferCount == 0, "malformed source clears hero controls immediately");
    nativePublisher.Offer(NativeFrame("Arena", 12, heroFrame));
    PumpUntil(() => window.Summary.Contains("Choose a hero") && window.OfferCount == 2, "fresh valid tagged source recovers after malformed selection");
    var staleHero = NativeFrame("Arena", 13, heroFrame);
    var staleHeroTime = DateTimeOffset.UtcNow.AddSeconds(-10);
    nativePublisher.Offer(staleHero with { ObservedAt = staleHeroTime, Arena = staleHero.Arena! with { ObservedAt = staleHeroTime } });
    PumpUntil(() => window.Summary.Contains("unavailable") && window.OfferCount == 0, "stale source clears hero controls and keeps no prior advice");
    nativePublisher.Offer(NativeFrame("Arena", 14, cardFrame));
    PumpUntil(() => window.Summary.Contains("Card draft") && window.OfferCount == 3, "fresh source recovers into the explicitly supplied card phase");
    var missingPhaseSource = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(NativeFrame("Arena", 15, heroFrame), ReplaySource.JsonOptions))!;
    missingPhaseSource["Arena"]!["Selection"]!.AsObject().Remove("phase");
    nativePublisher.Offer(missingPhaseSource);
    PumpUntil(() => window.Summary.Contains("unavailable") && window.OfferCount == 0, "complete source with missing phase tag clears controls without escaping dispatcher");
    nativePublisher.Offer(NativeFrame("Arena", 16, heroFrame));
    PumpUntil(() => window.Summary.Contains("Choose a hero") && window.OfferCount == 2, "controlled unsupported-shape failure recovers on a fresh complete source");
    window.Close(); nativePublisher.Dispose(); Wait(nativePublisher.Completion);
    Check(nativePublisher.Completion.IsCompletedSuccessfully, "owned optional publisher worker exits after native window closes");
    var metadataFile = Path.Combine(evidence, "metadata-library.json");
    var metadataSeed = new ClientLibrary(metadataFile);
    Wait(metadataSeed.ReloadAsync());
    Wait(metadataSeed.ImportAsync("Metadata deck A", "AAECAf0EAU0BZAAA"));
    var metadataDeckA = metadataSeed.State.SelectedDeckId!;
    Wait(metadataSeed.ImportAsync("Metadata deck B", "AAECAf0EAU0BZAAA"));
    var metadataDeckB = metadataSeed.State.SelectedDeckId!;
    Wait(metadataSeed.SelectAsync(metadataDeckA));
    nativeSession = Guid.NewGuid().ToString(); observedMatch = Guid.NewGuid().ToString();
    var metadataSource = Path.Combine(evidence, "metadata-observation.json");
    var metadataFailures = 0;
    using var metadataPublisher = new NativeSnapshotWriter(metadataSource,
        snapshot => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(snapshot, ReplaySource.JsonOptions),
        () => Interlocked.Increment(ref metadataFailures));
    ownedWriters.Add(metadataPublisher);
    metadataPublisher.Offer(NativeFrame("Constructed", 1));
    PumpUntil(() => File.Exists(metadataSource), "metadata journey starts its actual observation publisher");
    var metadataSettings = Path.Combine(evidence, "metadata-settings.json");
    window = CreateWindow(new(null, metadataSettings, ClientData: metadataFile, NativeState: metadataSource));
    window.Show(); Wait(window.Initialization);
    var metadataTabs = window.GetVisualDescendants().OfType<TabControl>().Single();
    Click((TabItem)metadataTabs.Items[0]!);
    editor = window.DeckLibrary;
    var metadataView = editor.Metadata;
    Check(metadataView.SavedTags.Text == "No tags" && metadataView.SavedWebsite.Text == "No website",
        "newly imported deck renders explicit empty metadata through its compiled AXAML view");
    var metadataRevision = window.Library.State.Revision;
    Click(metadataView.OpenButton);
    Fill(metadataView.TagsBox, "Discard this tag"); Fill(metadataView.WebsiteBox, "https://example.test/discard");
    Click(metadataView.OpenButton);
    Check(!metadataView.OpenButton.IsEnabled && metadataView.TagsBox.Text == "Discard this tag",
        "repeated metadata edit pointer activation cannot reset unsaved input");
    Click(metadataView.CancelButton); Click(metadataView.OpenButton);
    Check(metadataView.TagsBox.Text == "" && metadataView.WebsiteBox.Text == "" && window.Library.State.Revision == metadataRevision,
        "cancel and reopen restore saved metadata without changing persisted revision");
    var metadataLongTag = new string('A', 64);
    var metadataLongWebsite = "https://example.test/" + new string('w', 380);
    Fill(metadataView.TagsBox, metadataLongTag + "\nArena\narena");
    Fill(metadataView.WebsiteBox, "ftp://example.test/unsupported");
    var metadataBeforeInvalid = File.ReadAllBytes(metadataFile);
    Click(metadataView.SaveButton); Wait(editor.PendingAction);
    Check(!window.Library.Ready && metadataView.Editor.IsEffectivelyVisible
        && metadataView.WebsiteBox.Text == "ftp://example.test/unsupported"
        && editor.Status.Contains("website", StringComparison.OrdinalIgnoreCase)
        && File.ReadAllBytes(metadataFile).SequenceEqual(metadataBeforeInvalid),
        "invalid metadata save keeps exact stored bytes and unsaved input with a visible backend error");
    Click(editor.ReloadButton); Wait(editor.PendingAction);
    Check(window.Library.Ready && metadataView.Editor.IsEffectivelyVisible
        && metadataView.TagsBox.Text == metadataLongTag + "\nArena\narena"
        && metadataView.WebsiteBox.Text == "ftp://example.test/unsupported",
        "actual Reload recovers metadata edit availability without discarding the rejected unsaved draft");
    Fill(metadataView.WebsiteBox, metadataLongWebsite);
    Click(metadataView.SaveButton); Wait(editor.PendingAction);
    Check(window.Library.State.Decks.Single(deck => deck.Id == metadataDeckA).Metadata.Tags.SequenceEqual(new[] { metadataLongTag, "Arena", "arena" })
        && window.Library.State.Decks.Single(deck => deck.Id == metadataDeckA).Metadata.Website == metadataLongWebsite
        && window.Library.State.SelectedDeckId == metadataDeckA && !metadataView.Editor.IsVisible,
        "pointer save persists exact-case tags and website without changing library tracking selection");
    window.Close();
    // A newly constructed native UI must read the same saved metadata from the real Rust store.
    metadataPublisher.Offer(NativeFrame("Constructed", 2));
    window = CreateWindow(new(null, metadataSettings, ClientData: metadataFile, NativeState: metadataSource));
    window.Show(); Wait(window.Initialization);
    metadataTabs = window.GetVisualDescendants().OfType<TabControl>().Single();
    Click((TabItem)metadataTabs.Items[0]!); editor = window.DeckLibrary; metadataView = editor.Metadata;
    Click(metadataView.OpenButton);
    Check(metadataView.TagsBox.Text == metadataLongTag + "\nArena\narena" && metadataView.WebsiteBox.Text == metadataLongWebsite,
        "reconstructed metadata editor reloads the persisted tags and website");
    Click(metadataView.CancelButton);
    foreach (var size in new[] { (Width: 1120, Height: 760), (Width: 800, Height: 600) })
    {
        window.Width = size.Width; window.Height = size.Height;
        Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame();
        metadataView.BringIntoView(); Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame();
        Check(metadataView.SavedTags.TextWrapping == global::Avalonia.Media.TextWrapping.Wrap
            && metadataView.SavedWebsite.TextWrapping == global::Avalonia.Media.TextWrapping.Wrap
            && metadataView.SavedTags.Bounds.Width <= editor.SelectedPanel.Bounds.Width
            && metadataView.SavedWebsite.Bounds.Width <= editor.SelectedPanel.Bounds.Width
            && metadataView.SavedTags.Bounds.Height <= 60 && metadataView.SavedWebsite.Bounds.Height <= 60
            && InsideScrollViewport(metadataView.OpenButton) && InsideScrollViewport(metadataView.SavedTags)
            && InsideScrollViewport(metadataView.SavedWebsite),
            $"{size.Width}: long saved metadata remains bounded inside the actual selected-deck panel");
        FitsWindowBounds(metadataView.OpenButton, $"{size.Width}: metadata edit action fits alongside both bounded summaries without further scrolling");
        FitsWindowBounds(metadataView.SavedTags, $"{size.Width}: complete bounded tag summary shares the edit action viewport");
        FitsWindowBounds(metadataView.SavedWebsite, $"{size.Width}: bounded website summary shares the edit action viewport");
        window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, $"metadata-saved-{size.Width}.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        Click(metadataView.OpenButton);
        Fill(metadataView.WebsiteBox, metadataLongWebsite);
        FitsWindowBounds(metadataView.WebsiteBox, $"{size.Width}: metadata website input remains reachable at representative width");
        Click(metadataView.CancelButton);
    }
    Click(editor.EditButton); Fill(editor.NotesBox, "Unsaved card notes beside metadata");
    Click(editor.OverviewButton); Click(metadataView.OpenButton);
    Fill(metadataView.TagsBox, "Unsaved metadata\n日本語");
    Fill(metadataView.WebsiteBox, "https://example.test/updated");
    var metadataBeforeObservation = window.Library.State.Revision;
    var metadataMatch = new MatchRecord(observedMatch, null, DateTimeOffset.UtcNow.ToString("o"), "Metadata observation", "Win", "Mage", "Warrior", 9, null, "");
    metadataPublisher.Offer(NativeFrame("Menu", 3, matches: [metadataMatch]));
    PumpUntil(() => window.Library.State.Matches.Length == 1 && window.Library.State.Revision > metadataBeforeObservation,
        "actual native observation polling records a match while metadata editor is open");
    Check(metadataView.Editor.IsEffectivelyVisible && metadataView.TagsBox.Text == "Unsaved metadata\n日本語"
        && metadataView.WebsiteBox.Text == "https://example.test/updated" && editor.NotesBox.Text == "Unsaved card notes beside metadata"
        && window.Library.State.Decks.Single(deck => deck.Id == metadataDeckA).Metadata.Website == metadataLongWebsite,
        "same-deck background observation preserves unsaved metadata and card notes without implicitly saving either");
    Click(metadataView.SaveButton); Wait(editor.PendingAction);
    Check(editor.NotesBox.Text == "Unsaved card notes beside metadata" && window.Library.State.Matches.Single().DeckId == metadataDeckA,
        "metadata-only save preserves unsaved card notes and the observed match association");
    var metadataSaved = window.Library.State.Decks.Single(deck => deck.Id == metadataDeckA).Metadata;
    Click(editor.EditButton); Click(editor.SaveButton); Wait(editor.PendingAction);
    Check(window.Library.State.Decks.Single(deck => deck.Id == metadataDeckA).Metadata.Tags.SequenceEqual(metadataSaved.Tags)
        && window.Library.State.Decks.Single(deck => deck.Id == metadataDeckA).Metadata.Website == metadataSaved.Website,
        "ordinary card-and-note save cannot erase metadata omitted by its unchanged request contract");
    Click(editor.OverviewButton); Click(metadataView.OpenButton);
    Fill(metadataView.TagsBox, "Unsaved switch-away text");
    Click(editor.Decks.GetVisualDescendants().OfType<ListBoxItem>().Single(item => item.DataContext is DeckEntry deck && deck.Id == metadataDeckB));
    Check(!metadataView.Editor.IsVisible && metadataView.SavedTags.Text == "No tags" && metadataView.SavedWebsite.Text == "No website",
        "switching decks clears the prior metadata editor and displays only the newly selected deck's metadata");
    Click(editor.Decks.GetVisualDescendants().OfType<ListBoxItem>().Single(item => item.DataContext is DeckEntry deck && deck.Id == metadataDeckA));
    Click(metadataView.OpenButton); Fill(metadataView.TagsBox, ""); Fill(metadataView.WebsiteBox, "");
    Click(metadataView.SaveButton); Wait(editor.PendingAction);
    var metadataReopened = new ClientLibrary(metadataFile); Wait(metadataReopened.ReloadAsync());
    var metadataCleared = metadataReopened.State.Decks.Single(deck => deck.Id == metadataDeckA);
    Check(metadataCleared.Metadata.Tags.Length == 0 && metadataCleared.Metadata.Website is null
        && metadataCleared.Notes == "Unsaved card notes beside metadata" && metadataReopened.State.Matches.Single().DeckId == metadataDeckA
        && metadataReopened.State.SelectedDeckId == metadataDeckA,
        "explicit pointer clearing survives a second storage read without losing deck notes, selection or match identity");
    window.Close(); metadataPublisher.Dispose(); Wait(metadataPublisher.Completion);
    Check(metadataFailures == 0 && metadataPublisher.Completion.IsCompletedSuccessfully,
        "metadata observation journey reaps its actual publisher without failure");
    var failedOutput = Path.Combine(evidence, $"publisher-block-{Guid.NewGuid():N}", "state.json");
    var publicationFailed = 0;
    using var failedPublisher = new NativeSnapshotWriter(failedOutput, _ => [123, 125], () => Interlocked.Increment(ref publicationFailed));
    ownedWriters.Add(failedPublisher);
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
    ownedWriters.Add(boundedPublisher);
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
    ownedWriters.Add(latestPublisher);
    latestPublisher.Offer(1);
    PumpUntil(() => serializationEntered.IsSet, "producer worker begins serialization independently");
    for (var sequence = 2; sequence <= 1000; sequence++) latestPublisher.Offer(sequence);
    allowSerialization.Set();
    PumpUntil(() => File.Exists(latestOutput) && File.ReadAllText(latestOutput) == "1000", "slow producer converges to newest complete snapshot");
    latestPublisher.Dispose(); Wait(latestPublisher.Completion);
    Check(serialized == 2 && serializationThread != uiThread, "producer serializes off dispatcher and coalesces pending captures instead of growing an unbounded queue");

    var overlaySpec = Result(SnapshotFiles.InvokeJsonAsync<OverlaySpec>(["native-overlay", "info"], SnapshotFiles.StatusBytes));
    overlaySpec.Validate();
    void SaveOverlayFrame(OverlayRaster raster, string name) => File.WriteAllText(Path.Combine(evidence, name), System.Text.Json.JsonSerializer.Serialize(new
    {
        kind = "frame",
        schemaVersion = 1,
        sourceSession = raster.SourceSession,
        sequence = raster.Sequence,
        observedMs = raster.ObservedMs,
        width = overlaySpec.Width,
        height = overlaySpec.Height,
        pixelFormat = overlaySpec.PixelFormat,
        pixels = Convert.ToBase64String(raster.Pixels)
    }));
    var overlayObservation = NativeFrame("Arena", 20, frames[1]);
    File.WriteAllText(Path.Combine(evidence, "native-overlay-observation.json"), System.Text.Json.JsonSerializer.Serialize(overlayObservation,
        new System.Text.Json.JsonSerializerOptions(ReplaySource.JsonOptions) { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
    var constructedOverlay = startNative with { RemainingDeck = [new("private-fixture-id", 79, "Supplied overlay card", 0, 0, 0)] };
    var constructedContent = OverlayRendering.CreateContent(constructedOverlay, new TrackerSettings(), overlaySpec);
    var overlayLabels = constructedContent.GetLogicalDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
    Check(overlayLabels.Contains("0   Supplied overlay card ×0 • 0 health") && !overlayLabels.Any(text => text?.Contains("DbfId") == true),
        "production non-Arena overlay renders shared F# card text, preserving zero values without wire diagnostic fields");
    var constructedRaster = OverlayRendering.Render(constructedOverlay, new TrackerSettings(), overlaySpec, Path.Combine(evidence, "native-constructed-overlay.png"));
    SaveOverlayFrame(constructedRaster, "native-constructed-overlay-frame.json");
    var overlayRaster = OverlayRendering.Render(overlayObservation, new TrackerSettings(), overlaySpec, Path.Combine(evidence, "native-overlay-render.png"));
    Check(overlayRaster.Pixels.Length == overlaySpec.Width * overlaySpec.Height * 4
        && Enumerable.Range(0, overlayRaster.Pixels.Length / 4).All(index => overlayRaster.Pixels[index * 4 + 3] == 255), "actual Avalonia renderer produces a complete opaque bounded raster for Rust-owned overlay");
    var phaseOverlayJson = new System.Text.Json.JsonSerializerOptions(ReplaySource.JsonOptions) { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
    File.WriteAllText(Path.Combine(evidence, "native-overlay-cards.json"), System.Text.Json.JsonSerializer.Serialize(NativeFrame("Arena", 24, cardFrame), phaseOverlayJson));
    File.WriteAllText(Path.Combine(evidence, "native-overlay-heroes.json"), System.Text.Json.JsonSerializer.Serialize(NativeFrame("Arena", 22, heroFrame), phaseOverlayJson));
    File.WriteAllText(Path.Combine(evidence, "native-overlay-menu.json"), System.Text.Json.JsonSerializer.Serialize(NativeFrame("Menu", 23, menuFrame), phaseOverlayJson));
    var heroRaster = OverlayRendering.Render(NativeFrame("Arena", 22, heroFrame), new TrackerSettings(), overlaySpec, Path.Combine(evidence, "native-overlay-heroes.png"));
    Check(!overlayRaster.Pixels.SequenceEqual(heroRaster.Pixels), "same production overlay renderer distinguishes typed hero choices from cards in actual pixels");
    var alternateRaster = OverlayRendering.Render(NativeFrame("Arena", 21, frames[0]), new TrackerSettings(), overlaySpec);
    File.WriteAllText(Path.Combine(evidence, "native-overlay-alternate.json"), System.Text.Json.JsonSerializer.Serialize(NativeFrame("Arena", 21, frames[0]),
        new System.Text.Json.JsonSerializerOptions(ReplaySource.JsonOptions) { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
    Check(!overlayRaster.Pixels.SequenceEqual(alternateRaster.Pixels), "native overlay pixels change when observed offers and picks replace each other");
    SaveOverlayFrame(overlayRaster, "native-overlay-frame.json");
    window = CreateWindow(new(fixture, Path.Combine(evidence, "overlay-fault-settings.json")));
    window.Show(); Wait(window.Initialization);
    var slowOverlay = Path.Combine(evidence, $"slow-overlay-{Guid.NewGuid():N}.sh");
    File.WriteAllText(slowOverlay, "#!/bin/sh\nprintf '%s' \"$$\" > \"$0.pid\"\nexec sleep 30\n");
    if (OperatingSystem.IsLinux()) File.SetUnixFileMode(slowOverlay, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    NativeOverlayActor? stalledOverlay = null;
    try
    {
        Environment.SetEnvironmentVariable("HDT_HOST_BINARY", slowOverlay);
        stalledOverlay = new(new(1, 1, "owned-supervision-fixture", 0, 0, 640, 720), _ => { });
        ownedActors.Add(stalledOverlay);
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

}
catch (Exception error)
{
    Console.Error.WriteLine(error.ToString());
    Environment.ExitCode = 1;
}
finally
{
    try
    {
        foreach (var writer in ownedWriters) writer.Dispose();
        foreach (var actor in ownedActors) actor.Stop();
        foreach (var owned in ownedWindows) if (owned.IsVisible) owned.Close();
        var completion = Task.WhenAll(ownedActors.Select(actor => actor.Completion)
            .Concat(ownedWriters.Select(writer => writer.Completion))
            .Concat(ownedWindows.SelectMany(owned => new[] { owned.Initialization, owned.PendingPreferences, owned.NativeOverlay.OwnerCompletion, owned.DeckLibrary.PendingAction })));
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!completion.IsCompleted)
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Owned test resources did not finish cleanup.");
            Dispatcher.UIThread.RunJobs();
            using var turn = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));
            Dispatcher.UIThread.MainLoop(turn.Token);
        }
        // Canceled operations are expected after closing their owning window. Other faults retain diagnostics.
        try { completion.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        Console.Error.WriteLine($"CHECK RUNNER CLEANUP: {ownedWindows.Count} windows closed; {ownedActors.Count} actors and {ownedWriters.Count} writers completed.");
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"CHECK RUNNER CLEANUP FAILURE: {error}");
        Environment.ExitCode = 1;
    }
}
