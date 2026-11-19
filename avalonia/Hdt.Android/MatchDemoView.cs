using Avalonia;
using Avalonia.Controls;
using Hdt.Avalonia;
using Button = Avalonia.Controls.Button;

namespace Hdt.Mobile;

internal sealed class MatchDemoView : UserControl
{
    private static readonly DateTimeOffset SampleTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ObservedCard[] Deck = [new("DEMO_A", 0, "Illustrative spell", 2, 2, null)];
    private static readonly ObservedCard[] Hand = [new("DEMO_B", 0, "Illustrative minion", 3, 1, null)];
    private static readonly ObservedCard[] Player = [new("DEMO_C", 0, "Illustrative ally", 4, 1, 3)];
    private static readonly ObservedCard[] Opponent = [new("DEMO_D", 0, "Illustrative opponent", 5, 1, 4)];
    private static readonly MatchRecord[] History =
    [
        new("f109f622-02f5-4ce9-b593-5a842b1cac8c", null, "2026-01-01", "Constructed", "Win", "Mage", "Druid", 8, null, ""),
        new("bfa8393e-c2d5-4197-832f-94f3786fb3a6", null, "2026-01-01", "Battlegrounds", "Loss", "Sample hero", "Sample opponent", 12, 5, ""),
    ];

    private readonly NativeGameView game = new(compact: true);

    public MatchDemoView()
    {
        var constructed = new Button { Content = "Constructed sample" };
        var battlegrounds = new Button { Content = "Battlegrounds sample" };
        var menu = new Button { Content = "Menu sample" };
        constructed.Click += (_, _) => Apply(NativeMode.Constructed);
        battlegrounds.Click += (_, _) => Apply(NativeMode.Battlegrounds);
        menu.Click += (_, _) => Apply(NativeMode.Menu);
        var results = new StackPanel { Spacing = 8 };
        var wins = History.Count(match => match.Result == "Win");
        var losses = History.Count(match => match.Result == "Loss");
        results.Children.Add(TrackerLayout.Text($"{History.Length} matches · {wins} win · {losses} loss"));
        foreach (var match in History)
            results.Children.Add(TrackerLayout.Text(match.Placement is { } placement
                ? $"{match} · Placement {placement}" : match.ToString()));

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                MaxWidth = 540,
                Margin = new Thickness(20),
                Spacing = 12,
                Children =
                {
                    TrackerLayout.Text("Sample public game observations and match history.", 14),
                    new WrapPanel { Children = { constructed, battlegrounds, menu } },
                    game,
                    TrackerLayout.Section("Sample match history", results),
                },
            },
        };
        Apply(NativeMode.Constructed);
    }

    private void Apply(NativeMode mode)
    {
        var constructed = mode == NativeMode.Constructed;
        var inMatch = mode != NativeMode.Menu;
        var arena = new ArenaObservation(1, "8e0cf0c0-987e-4418-aaca-2fdea154e650", SampleTime, 1, ArenaMode.Inactive,
            0, null, "Mage", false, [], [], "Sample");
        var sample = new NativeObservation(1, arena.SessionId, arena.Sequence, arena.ObservedAt,
            NativeAvailability.Available, null, mode, mode.ToString(), mode.ToString(), null,
            InMenu: !inMatch, IsRunning: true, PlayerHealth: inMatch ? 26 : null,
            OpponentHealth: constructed ? 18 : null, PlayerHandCount: constructed ? 3 : null,
            OpponentHandCount: constructed ? 4 : null, PlayerDeckCount: constructed ? 15 : null,
            OpponentDeckCount: constructed ? 17 : null, RemainingDeck: constructed ? Deck : [],
            KnownHand: constructed ? Hand : [], PlayerBoard: inMatch ? Player : [],
            OpponentBoard: inMatch ? Opponent : [], Arena: arena, CompletedMatches: History,
            HistoryRetention: 256);
        game.Apply(sample, "sample observation");
    }
}
