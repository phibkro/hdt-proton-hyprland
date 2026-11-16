using System.Globalization;
using System.Text.Json;

namespace Hdt.Avalonia;

// Independently authored boundary: acquisition supplies facts; rendering never calls HSReplay or game memory.
public sealed record ArenaCard(string Id, string Name, int Mana);
public sealed record ArenaRating(string? Score, int Plaque);
public sealed record ArenaRelations(string[] Direct, string[] Indirect)
{
    public IEnumerable<string> All => Direct.Concat(Indirect);
}
public sealed record ArenaOffer(ArenaCard Card, ArenaRating? BaseRating, ArenaRating? DynamicRating,
    ArenaRelations Enabled, ArenaRelations EnhancedBy)
{
    public string DisplayScore
    {
        get
        {
            var raw = DynamicRating?.Score ?? BaseRating?.Score;
            if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var score)) return "—";
            return score >= 10 ? decimal.Truncate(score).ToString(CultureInfo.InvariantCulture)
                : score.ToString("0.############################", CultureInfo.InvariantCulture);
        }
    }
    public int Plaque => DynamicRating?.Plaque ?? BaseRating?.Plaque ?? 0;
    public int SynergyCount(IEnumerable<ArenaCard> deck)
    {
        var enabled = Enabled.All.ToArray();
        return enabled.Length + EnhancedBy.All.Where(id => !enabled.Contains(id))
            .Sum(id => deck.Count(card => card.Id == id));
    }
}
public enum ArenaMode { Inactive, Drafting }
public sealed record ArenaObservation(int SchemaVersion, string SessionId, DateTimeOffset ObservedAt,
    int Sequence, ArenaMode Mode, int PickIndex, string? SelectedCardId, string Hero, bool Underground,
    ArenaCard[] PickedDeck, ArenaOffer[] Offers, string Description);
public interface IArenaObservationSource
{
    string Description { get; }
    IReadOnlyList<ArenaObservation> Read();
}
public sealed class ReplaySource(string path) : IArenaObservationSource
{
    public string Description => "Replay file • no live game connection";
    public IReadOnlyList<ArenaObservation> Read()
    {
        var frames = JsonSerializer.Deserialize<ArenaObservation[]>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Replay must contain an observation array.");
        if (frames.Length == 0) throw new InvalidDataException("Replay contains no observations.");
        var previous = -1;
        foreach (var frame in frames)
        {
            if (frame is null || frame.SchemaVersion != 1 || string.IsNullOrWhiteSpace(frame.SessionId)
                || frame.ObservedAt == default || !Enum.IsDefined(frame.Mode)
                || frame.Sequence <= previous || string.IsNullOrWhiteSpace(frame.Hero)
                || frame.PickedDeck is null || frame.Offers is null || frame.Description is null)
                throw new InvalidDataException("Observations require schema 1, session/time, known mode, increasing sequence, hero, deck, offers and description.");
            if (frame.Offers.Any(offer => offer is null)) throw new InvalidDataException("Offers cannot be null.");
            foreach (var card in frame.PickedDeck.Concat(frame.Offers.Select(o => o.Card)))
                if (card is null || string.IsNullOrWhiteSpace(card.Id) || string.IsNullOrWhiteSpace(card.Name) || card.Mana < 0)
                    throw new InvalidDataException("Cards require an ID, name and nonnegative mana.");
            foreach (var offer in frame.Offers)
                if (offer.Enabled?.Direct is null || offer.Enabled.Indirect is null
                    || offer.EnhancedBy?.Direct is null || offer.EnhancedBy.Indirect is null)
                    throw new InvalidDataException("Offers require explicit related-card sets.");
            if (frame.PickIndex != frame.PickedDeck.Length || (frame.Mode == ArenaMode.Inactive && frame.Offers.Length != 0)
                || (frame.SelectedCardId is not null && !frame.PickedDeck.Any(card => card.Id == frame.SelectedCardId)))
                throw new InvalidDataException("Pick index must match picked cards, selected card must be picked, and inactive observations must have no offers.");
            previous = frame.Sequence;
        }
        return frames;
    }
    public static JsonSerializerOptions JsonOptions { get; } = new() { PropertyNameCaseInsensitive = true, WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter<ArenaMode>(allowIntegerValues: false) } };
}
