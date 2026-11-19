namespace Hdt.Avalonia;

// The producer is optional and source-only until separately deployed. This reader
// never infers card observations from the older status/visibility export.
public sealed class NativeObservationReader(string path)
{
    private string? _session;
    private int _sequence = -1;
    private DateTimeOffset _observedAt;
    public async Task<NativeObservation> ReadAsync(CancellationToken cancellation = default)
    {
        var snapshot = await SnapshotFiles.ReadJsonAsync<NativeObservation>(path, 2 * 1024 * 1024, cancellation).ConfigureAwait(false);
        Validate(snapshot, DateTimeOffset.UtcNow);
        if (snapshot.SessionId == _session && (snapshot.Sequence < _sequence
            || snapshot.Sequence == _sequence && snapshot.ObservedAt != _observedAt || snapshot.ObservedAt < _observedAt))
            throw new InvalidDataException("Observation sequence/time moved backwards inside a source session.");
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
