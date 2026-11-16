using System.Text.Json;
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

// The producer is optional and source-only until separately deployed. This reader
// never infers card observations from the older status/visibility export.
public sealed class NativeObservationReader(string path)
{
    private string? _session;
    private int _sequence = -1;
    private DateTimeOffset _observedAt;
    private ArenaChoiceEvidence? _choiceEvidence;
    private string? _choiceIdentity;
    public async Task<NativeObservation> ReadAsync(CancellationToken cancellation = default)
    {
        var snapshot = await SnapshotFiles.ReadJsonAsync<NativeObservation>(path, 2 * 1024 * 1024, cancellation).ConfigureAwait(false);
        Validate(snapshot, DateTimeOffset.UtcNow);
        if (snapshot.SessionId == _session && (snapshot.Sequence < _sequence
            || snapshot.Sequence == _sequence && snapshot.ObservedAt != _observedAt || snapshot.ObservedAt < _observedAt))
            throw new InvalidDataException("Observation sequence/time moved backwards inside a source session.");
        var delivered = snapshot.Arena?.ChoiceEvidence;
        var identity = delivered is null ? null : JsonSerializer.Serialize(snapshot.Arena!.ViewSelection, ReplaySource.JsonOptions);
        if (snapshot.SessionId == _session && delivered is not null && _choiceEvidence is { } previous
            && (delivered.Revision < previous.Revision || delivered.Generation < previous.Generation
                || delivered.ReceivedAt < previous.ReceivedAt || delivered.Revision == previous.Revision && (delivered != previous || identity != _choiceIdentity)))
            throw new InvalidDataException("Delivered choice evidence moved backwards or changed under one revision.");
        if (snapshot.SessionId != _session) { _choiceEvidence = null; _choiceIdentity = null; }
        if (delivered is not null) { _choiceEvidence = delivered; _choiceIdentity = identity; }
        _session = snapshot.SessionId; _sequence = snapshot.Sequence; _observedAt = snapshot.ObservedAt;
        return snapshot;
    }
    public static void Validate(NativeObservation value, DateTimeOffset now)
    {
        if (value.SchemaVersion != 1 || !Guid.TryParse(value.SessionId, out _) || value.Sequence < 1
            || value.ObservedAt == default || (now - value.ObservedAt).TotalSeconds > 5 || (value.ObservedAt - now).TotalSeconds > 5)
            throw new InvalidDataException("Native observation has an incompatible version, identity/time, or is stale.");
        if (value.Availability == NativeAvailability.Unavailable) throw new IOException("Producer reports unavailable public tracker models.");
        if (value.Availability != NativeAvailability.Available || value.Mode is null || !Enum.IsDefined(value.Mode.Value)
            || value.RawMode is null || value.RawMode.Length > 128 || value.MatchMode is null || value.MatchMode.Length > 128
            || value.RemainingDeck is null || value.KnownHand is null || value.PlayerBoard is null || value.OpponentBoard is null
            || value.CompletedMatches is null || value.HistoryRetention != 256 || value.CompletedMatches.Length > value.HistoryRetention)
            throw new InvalidDataException("Native observation lacks explicit bounded game/card/history fields.");
        if (value.MatchId is not null && !Guid.TryParse(value.MatchId, out _)) throw new InvalidDataException("Invalid account-free match identity.");
        foreach (var cards in new[] { value.RemainingDeck, value.KnownHand, value.PlayerBoard, value.OpponentBoard })
        {
            if (cards.Length > 500) throw new InvalidDataException("Native card collection exceeds preview limit.");
            foreach (var card in cards)
                if (card is null || string.IsNullOrWhiteSpace(card.Id) || card.Id.Length > 128 || string.IsNullOrWhiteSpace(card.Name)
                    || card.Name.Length > 512 || card.DbfId < 0 || card.Mana is < 0 or > 1000 || card.Count is < 0 or > 1000
                    || card.Health is < -10000 or > 10000)
                    throw new InvalidDataException("Invalid observed card data.");
        }
        foreach (var count in new[] { value.PlayerHandCount, value.OpponentHandCount, value.PlayerDeckCount, value.OpponentDeckCount })
            if (count is < 0 or > 5000) throw new InvalidDataException("Invalid observed card count.");
        if (value.PlayerHealth is < -10000 or > 10000 || value.OpponentHealth is < -10000 or > 10000)
            throw new InvalidDataException("Invalid observed hero health.");
        if ((value.Mode == NativeMode.Offline) != !value.IsRunning || value.Mode == NativeMode.Menu && !value.InMenu
            || value.Mode is NativeMode.Constructed or NativeMode.Battlegrounds or NativeMode.Mercenaries && value.InMenu)
            throw new InvalidDataException("Native mode contradicts observed running/menu state.");
        if ((!value.IsRunning || value.InMenu) && (value.RemainingDeck.Length + value.KnownHand.Length + value.PlayerBoard.Length + value.OpponentBoard.Length != 0
            || value.PlayerHealth is not null || value.OpponentHealth is not null || value.PlayerHandCount is not null
            || value.OpponentHandCount is not null || value.PlayerDeckCount is not null || value.OpponentDeckCount is not null))
            throw new InvalidDataException("Menu/offline snapshots must clear previous game facts.");
        if (value.Arena is null) throw new InvalidDataException("Explicit inactive Arena state is required.");
        ReplaySource.Validate([value.Arena]);
        if (value.Arena.SessionId != value.SessionId || value.Arena.Sequence != value.Sequence || value.Arena.ObservedAt != value.ObservedAt
            || value.Mode != NativeMode.Arena && value.Arena.Mode != ArenaMode.Inactive)
            throw new InvalidDataException("Arena observation must share envelope identity/time and mode.");
        var ids = new HashSet<string>();
        foreach (var record in value.CompletedMatches)
        {
            if (record is null || !Guid.TryParse(record.Id, out _) || !ids.Add(record.Id) || record.DeckId is not null
                || !DateTimeOffset.TryParse(record.ObservedAt, out _) || record.Mode is null || record.Mode.Length is < 1 or > 128
                || record.Result is not ("Win" or "Loss" or "Draw" or "Unknown") || record.PlayerHero is null || record.PlayerHero.Length > 128
                || record.OpponentHero is null || record.OpponentHero.Length > 128 || record.Turns > 1000 || record.Placement is < 1 or > 8
                || record.Notes is null || record.Notes.Length > 8192)
                throw new InvalidDataException("Invalid anonymous completed-match observation.");
        }
    }
}

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
    public NativeGameView()
    {
        _deck = Panel("Observed remaining deck", _remaining);
        var columns = new Grid { ColumnDefinitions = new("*,*,*"), ColumnSpacing = 16 };
        columns.Children.Add(_deck);
        columns.Children.Add(new StackPanel { Spacing = 12, Children = { Panel("Your known hand", _hand), Panel("Your public board", _player) }, [Grid.ColumnProperty] = 1 });
        columns.Children.Add(new StackPanel { Spacing = 12, Children = { Panel("Opponent public board", _opponent), new TextBlock { Text = "Opponent hand and secret card IDs are never exported. Combat odds, recommendations and authenticated services are unavailable.", TextWrapping = TextWrapping.Wrap } }, [Grid.ColumnProperty] = 2 });
        Content = new ScrollViewer { Content = new StackPanel { Margin = new(24), Spacing = 18, Children = { _mode, _counts, columns } } };
        Unavailable("No native observation source configured.");
    }
    private static Border Panel(string title, Control content) => TrackerLayout.Section(title, content);
    public void Unavailable(string reason)
    {
        _mode.Text = "Game observations unavailable"; _counts.Text = reason;
        _remaining.ItemsSource = _hand.ItemsSource = _player.ItemsSource = _opponent.ItemsSource = null;
    }
    public void Apply(NativeObservation value)
    {
        _mode.Text = value.Mode?.ToString() == value.MatchMode ? $"{value.Mode} • observation file" : $"{value.Mode} • {value.MatchMode} • observation file";
        static string Count(int? value) => value?.ToString() ?? "unknown";
        _counts.Text = $"Your health {Count(value.PlayerHealth)} • hand {Count(value.PlayerHandCount)} • deck {Count(value.PlayerDeckCount)}\nOpponent health {Count(value.OpponentHealth)} • hand {Count(value.OpponentHandCount)} • deck {Count(value.OpponentDeckCount)}";
        _deck.IsVisible = value.Mode != NativeMode.Battlegrounds;
        _remaining.ItemsSource = value.RemainingDeck; _hand.ItemsSource = value.KnownHand;
        _player.ItemsSource = value.PlayerBoard; _opponent.ItemsSource = value.OpponentBoard;
    }
}
