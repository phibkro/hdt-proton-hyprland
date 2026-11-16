using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Hdt.Avalonia;

// Original native presentation; shared section geometry keeps pages consistent.
public static class TrackerLayout
{
    public static IBrush Surface { get; } = new SolidColorBrush(Color.Parse("#20262D"));
    public static IBrush Line { get; } = new SolidColorBrush(Color.Parse("#394653"));
    public static IBrush Accent { get; } = new SolidColorBrush(Color.Parse("#2489BD"));
    public static TextBlock Text(string text, double size = 13) => new()
    { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
    public static Border Section(string title, Control body, bool scroll = false)
    {
        var dock = new DockPanel();
        var header = new Border { Background = Accent, Padding = new(10, 6), Child = Text(title.ToUpperInvariant(), 13) };
        DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
        dock.Children.Add(new Border { Padding = new(10), Child = scroll ? new ScrollViewer { Content = body } : body });
        return new Border { Background = Surface, BorderBrush = Line, BorderThickness = new(1), Child = dock };
    }
}
