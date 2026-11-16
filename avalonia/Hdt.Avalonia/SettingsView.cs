using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Hdt.Avalonia;

public sealed class SettingsView : UserControl
{
    public Button ArenaCategory { get; } = new() { Content = "Arena draft", HorizontalAlignment = HorizontalAlignment.Stretch };
    public Button OverlayCategory { get; } = new() { Content = "Native overlay", HorizontalAlignment = HorizontalAlignment.Stretch };
    public Button StorageCategory { get; } = new() { Content = "Local files", HorizontalAlignment = HorizontalAlignment.Stretch };
    public Border CategoryPanel { get; }
    public ContentControl DetailPanel { get; } = new();
    public SettingsView(Control arena, NativeOverlayControls overlay, LaunchOptions paths)
    {
        var storage = new StackPanel
        {
            Spacing = 12,
            Children = {
            TrackerLayout.Text("These files belong to this native client. Their paths are supplied when it starts; no Windows HDT database is imported."),
            TrackerLayout.Text("Preferences"), TrackerLayout.Text(paths.Settings),
            TrackerLayout.Text("Deck library and supplied match history"), TrackerLayout.Text(paths.ClientData ?? "library.json beside the preferences file"),
            TrackerLayout.Text("Read-only game observations"), TrackerLayout.Text(paths.NativeState ?? "No source configured.") }
        };
        var arenaPane = TrackerLayout.Section("Overlay / Arena draft", arena, scroll: true);
        var overlayPane = TrackerLayout.Section("Overlay / Native tracking", overlay, scroll: true);
        var storagePane = TrackerLayout.Section("Tracker / Local files", storage, scroll: true);
        void Show(Button selected, Control content)
        {
            foreach (var button in new[] { ArenaCategory, OverlayCategory, StorageCategory })
                button.Background = button == selected ? TrackerLayout.Accent : TrackerLayout.Surface;
            DetailPanel.Content = content;
        }
        ArenaCategory.Click += (_, _) => Show(ArenaCategory, arenaPane);
        OverlayCategory.Click += (_, _) => Show(OverlayCategory, overlayPane);
        StorageCategory.Click += (_, _) => Show(StorageCategory, storagePane);
        CategoryPanel = new Border
        {
            Background = TrackerLayout.Surface,
            BorderBrush = TrackerLayout.Line,
            BorderThickness = new(1),
            Padding = new(8),
            Child = new StackPanel
            {
                Spacing = 6,
                Children = { TrackerLayout.Text("OVERLAY", 12), ArenaCategory, OverlayCategory,
                new Border { Height = 12 }, TrackerLayout.Text("TRACKER", 12), StorageCategory }
            }
        };
        var columns = new Grid { ColumnDefinitions = new("170,*"), ColumnSpacing = 12, Margin = new(12) };
        columns.Children.Add(CategoryPanel); columns.Children.Add(DetailPanel); Grid.SetColumn(DetailPanel, 1);
        Content = columns; Show(ArenaCategory, arenaPane);
    }
}
