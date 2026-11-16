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
var checks = 0;
void Check(bool condition, string reason)
{
    if (!condition) throw new InvalidOperationException(reason);
    checks++; Console.WriteLine($"PASS {reason}");
}
var frames = new ReplaySource(fixture).Read();
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
window.Show(); Dispatcher.UIThread.RunJobs();
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
Check(window.Scores.Count == 0 && !new SettingsStore(settings).Load().ShowArenasmithScore, "native checkbox pointer input persists score preference");
var persisted = new MainWindow(new(fixture, settings));
Check(persisted.ScoresToggle.IsChecked == false, "fresh window restores the nondefault saved preference");
persisted.Close();
window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, "options.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
Click((TabItem)tabs.Items[1]!);
Check(!window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("Arenasmith ") == true), "disabled scores are removed from rendered control tree");
Click((TabItem)tabs.Items[3]!);
Click(window.ScoresToggle);
Click((TabItem)tabs.Items[1]!);
window.CaptureRenderedFrame()?.Save(Path.Combine(evidence, "arena.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
Check(File.Exists(Path.Combine(evidence, "arena.png")), "real Avalonia renderer emits screenshot");
Click(window.NextButton);
Check(window.OfferCount == 0 && window.PickedCount == 2 && !window.NextButton.IsEnabled && window.Summary.Contains("Underground"), "terminal observation updates mode, deck and navigation boundary");
Click(window.ResetButton);
Check(window.OfferCount == 3 && window.PickedCount == 0, "rewind restores initial observation");
var invalid = Path.Combine(evidence, "invalid.json");
File.WriteAllText(invalid, "[{\"Sequence\":1}]");
try { new ReplaySource(invalid).Read(); throw new Exception("Invalid replay accepted"); }
catch (InvalidDataException) { checks++; Console.WriteLine("PASS incomplete replay fails explicitly"); }
var plugin = Path.Combine(evidence, "plugin-state.json");
var now = DateTimeOffset.UtcNow;
File.WriteAllText(plugin, System.Text.Json.JsonSerializer.Serialize(new { observedMS = now.ToUnixTimeMilliseconds(), state = new { mode = "DRAFT", arenaClientState = "DRAFTING", arenaSessionState = "PICKING", arenaDrafting = true, arenaAvailable = true } }));
Check(new ProtonStateReader(plugin).Read(now).Contains("connected"), "read-only adapter consumes actual MIT plugin field contract");
try { new ProtonStateReader(plugin).Read(now.AddSeconds(10)); throw new Exception("Stale state accepted"); }
catch (InvalidDataException) { checks++; Console.WriteLine("PASS old acquisition state is rejected as stale"); }
window.Close();
var restored = new MainWindow(new(fixture, settings));
Check(restored.ScoresToggle.IsChecked == true, "settings survive window reconstruction");
restored.Close();
Console.WriteLine($"{checks} checks passed; renderer evidence: {evidence}");
