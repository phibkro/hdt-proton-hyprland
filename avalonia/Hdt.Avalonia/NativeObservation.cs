using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Hdt.Avalonia;

public sealed record ObservedCard(string Id, int DbfId, string Name, int Mana, int Count, int? Health)
{
    public override string ToString() => $"{Mana}   {Name} ×{Count}{(Health is null ? "" : $" • {Health} health")}";
}
public enum NativeMode { Offline, Menu, Arena, Constructed, Battlegrounds, Mercenaries, Unknown }
public enum NativeAvailability { Unknown, Available, Unavailable }
public sealed record NativeObservation(int SchemaVersion, string SessionId, int Sequence, DateTimeOffset ObservedAt,
    NativeAvailability Availability, string? Error, NativeMode? Mode, string? RawMode, string? MatchMode, string? MatchId,
    bool InMenu, bool IsRunning, int? PlayerHealth, int? OpponentHealth, int? PlayerHandCount, int? OpponentHandCount,
    int? PlayerDeckCount, int? OpponentDeckCount, ObservedCard[]? RemainingDeck, ObservedCard[]? KnownHand,
    ObservedCard[]? PlayerBoard, ObservedCard[]? OpponentBoard, ArenaObservation? Arena,
    MatchRecord[]? CompletedMatches, int HistoryRetention);

public sealed class NativeGameView : UserControl
{
    private readonly TextBlock _mode = new() { FontSize = 25, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _counts = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListBox _remaining = new() { MaxHeight = 450 };
    private readonly ListBox _hand = new() { MaxHeight = 180 };
    private readonly ListBox _player = new() { MaxHeight = 220 };
    private readonly ListBox _opponent = new() { MaxHeight = 220 };
    private readonly Border _deck;
    public string ModeText => _mode.Text ?? "";
    public int CardCount => _remaining.ItemCount + _hand.ItemCount + _player.ItemCount + _opponent.ItemCount;
    public NativeGameView(bool compact = false)
    {
        _deck = Panel("Observed remaining deck", _remaining);
        var player = new StackPanel { Spacing = 12, Children = { Panel("Your known hand", _hand), Panel("Your public board", _player) } };
        var opponent = new StackPanel { Spacing = 12, Children = { Panel("Opponent public board", _opponent), new TextBlock { Text = "Opponent hand and secret card IDs are never exported. Combat odds, recommendations and authenticated services are unavailable.", TextWrapping = TextWrapping.Wrap } } };
        if (compact)
        {
            Content = new StackPanel { Spacing = 12, Children = { _mode, _counts, _deck, player, opponent } };
        }
        else
        {
            var columns = new Grid { ColumnDefinitions = new("*,*,*"), ColumnSpacing = 16, Children = { _deck, player, opponent } };
            Grid.SetColumn(player, 1);
            Grid.SetColumn(opponent, 2);
            Content = new ScrollViewer { Content = new StackPanel { Margin = new(24), Spacing = 18, Children = { _mode, _counts, columns } } };
        }
        Unavailable("No native observation source configured.");
    }
    private static Border Panel(string title, Control content) => TrackerLayout.Section(title, content);
    public void Unavailable(string reason)
    {
        _mode.Text = "Game observations unavailable"; _counts.Text = reason;
        _remaining.ItemsSource = _hand.ItemsSource = _player.ItemsSource = _opponent.ItemsSource = null;
    }
    public void Apply(NativeObservation value, string provenance = "observation file")
    {
        _mode.Text = value.Mode?.ToString() == value.MatchMode ? $"{value.Mode} • {provenance}" : $"{value.Mode} • {value.MatchMode} • {provenance}";
        static string Count(int? value) => value?.ToString() ?? "unknown";
        _counts.Text = $"Your health {Count(value.PlayerHealth)} • hand {Count(value.PlayerHandCount)} • deck {Count(value.PlayerDeckCount)}\nOpponent health {Count(value.OpponentHealth)} • hand {Count(value.OpponentHandCount)} • deck {Count(value.OpponentDeckCount)}";
        _deck.IsVisible = value.Mode != NativeMode.Battlegrounds;
        _remaining.ItemsSource = value.RemainingDeck; _hand.ItemsSource = value.KnownHand;
        _player.ItemsSource = value.PlayerBoard; _opponent.ItemsSource = value.OpponentBoard;
    }
}
