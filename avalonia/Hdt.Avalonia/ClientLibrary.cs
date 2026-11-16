using System.Text.Json;

namespace Hdt.Avalonia;

public sealed record CardCount(uint DbfId, uint Count);
public sealed record SideboardCard(uint OwnerDbfId, uint DbfId, uint Count);
public sealed record DeckContent(uint HeroDbfId, uint Format, CardCount[] Cards, SideboardCard[] Sideboards);
public sealed record DeckEntry(string Id, string Name, string HeroClass, DeckContent Content, string Notes, bool Archived, string CreatedAt, string UpdatedAt)
{
    public override string ToString() => $"{Name} • {HeroClass} • {Content.Cards.Sum(card => (long)card.Count)} cards";
}
public sealed record MatchRecord(string Id, string? DeckId, string ObservedAt, string Mode, string Result,
    string PlayerHero, string OpponentHero, uint Turns, byte? Placement, string Notes)
{
    public override string ToString() => $"{ObservedAt} • {Mode} • {Result} • {PlayerHero} vs {OpponentHero} • {Turns} turns";
}
public sealed record ClientState(byte SchemaVersion, ulong Revision, string? SelectedDeckId, DeckEntry[] Decks, MatchRecord[] Matches);
public sealed record DeckExport(string Code, string Name);

public sealed class ClientLibrary(string path, CancellationToken cancellation = default)
{
    public ClientState State { get; private set; } = new(1, 0, null, [], []);
    public bool Busy { get; private set; }
    public bool Ready { get; private set; }
    public string Status { get; private set; } = "Loading native library…";
    public event Action? Changed;
    public async Task ReloadAsync() => await ExecuteAsync(new { operation = "list" });
    public Task SaveAsync(string? id, string name, string heroClass, DeckContent content, string notes)
        => ExecuteAsync(new { operation = "upsertDeck", expectedRevision = State.Revision, deck = new { id, name, heroClass, content, notes } });
    public Task ImportAsync(string? name, string code)
        => ExecuteAsync(new { operation = "importDeck", expectedRevision = State.Revision, name, code });
    public Task SelectAsync(string id) => ExecuteAsync(new { operation = "selectDeck", expectedRevision = State.Revision, id });
    public Task ArchiveAsync(string id) => ExecuteAsync(new { operation = "archiveDeck", expectedRevision = State.Revision, id });
    public Task RecordAsync(MatchRecord record) => ExecuteAsync(new { operation = "recordMatch", expectedRevision = State.Revision, record });
    public async Task<DeckExport> ExportAsync(string id)
        => await RequestAsync<DeckExport>(new { operation = "exportDeck", id });
    private Task<T> RequestAsync<T>(object request)
        => SnapshotFiles.InvokeJsonAsync<T>(["client", Path.GetFullPath(path)], 2 * 1024 * 1024, cancellation,
            JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    private async Task ExecuteAsync(object request)
    {
        if (Busy) throw new InvalidOperationException("A library operation is already running.");
        Busy = true; Changed?.Invoke();
        try
        {
            var state = await RequestAsync<ClientState>(request);
            if (state.SchemaVersion != 1 || state.Decks is null || state.Matches is null
                || state.Decks.Any(deck => deck is null || deck.Content?.Cards is null || deck.Content.Sideboards is null))
                throw new InvalidDataException("Native library returned an incompatible state contract.");
            State = state;
            Ready = true; Status = $"Native library • revision {State.Revision} • {State.Decks.Count(deck => !deck.Archived)} decks • {State.Matches.Length} matches";
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidDataException)
        {
            Ready = false;
            Status = $"Library action failed; reload to verify saved state: {error.Message}";
            throw;
        }
        finally { Busy = false; Changed?.Invoke(); }
    }
}
