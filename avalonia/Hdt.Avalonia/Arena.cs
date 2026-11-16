using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

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
public enum ArenaUnavailableKind { Unknown, HeroPower, DualClass, Package, Redraft }
public sealed record ArenaHeroChoice(string Id, string Name, string? Class);
// A phase owns exactly one choice shape. Schema 2 never puts hero choices in card offers.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "phase")]
[JsonDerivedType(typeof(ArenaInactive), "Inactive")]
[JsonDerivedType(typeof(ArenaHeroSelection), "HeroSelection")]
[JsonDerivedType(typeof(ArenaCardDraft), "CardDraft")]
[JsonDerivedType(typeof(ArenaUnavailable), "Unavailable")]
public abstract record ArenaSelection;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ArenaInactive : ArenaSelection;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ArenaHeroSelection(ArenaHeroChoice[] Choices, string? UnavailableReason = null) : ArenaSelection;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ArenaCardDraft(ArenaOffer[] Choices) : ArenaSelection;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ArenaUnavailable([property: JsonRequired] ArenaUnavailableKind Kind, string Reason) : ArenaSelection;
public sealed record ArenaObservation(int SchemaVersion, string SessionId, DateTimeOffset ObservedAt,
    int Sequence, ArenaMode Mode, int PickIndex, string? SelectedCardId, string Hero, bool Underground,
    ArenaCard[] PickedDeck, ArenaOffer[] Offers, string Description, ArenaSelection? Selection = null)
{
    // Explicit compatibility: schema 1 can only describe inactive/card drafts, never heroes.
    [JsonIgnore]
    public ArenaSelection ViewSelection => SchemaVersion == 1
        ? ArenaV1Adapter.CardOnly(this) : Selection ?? throw new InvalidDataException("Arena schema 2 requires an explicit selection phase.");
    [JsonIgnore] public ArenaOffer[] CardOffers => ViewSelection is ArenaCardDraft cards ? cards.Choices : [];
}
public static class ArenaV1Adapter
{
    public static ArenaSelection CardOnly(ArenaObservation value) => value.Mode == ArenaMode.Inactive
        ? new ArenaInactive() : new ArenaCardDraft(value.Offers);
}
public interface IArenaObservationSource
{
    string Description { get; }
    Task<IReadOnlyList<ArenaObservation>> ReadAsync(CancellationToken cancellation = default);
}
public sealed class ReplaySource(string path) : IArenaObservationSource
{
    public string Description => "Replay file • no live game connection";
    public async Task<IReadOnlyList<ArenaObservation>> ReadAsync(CancellationToken cancellation = default)
    {
        var frames = await SnapshotFiles.ReadJsonAsync<ArenaObservation[]>(path, SnapshotFiles.ReplayBytes, cancellation).ConfigureAwait(false);
        Validate(frames);
        return frames;
    }
    public static void Validate(ArenaObservation[] frames)
    {
        if (frames.Length is < 1 or > 4096) throw new InvalidDataException("Replay requires 1..4096 observations.");
        var previous = -1;
        foreach (var frame in frames)
        {
            if (frame is null || frame.SchemaVersion is not (1 or 2) || string.IsNullOrWhiteSpace(frame.SessionId)
                || frame.ObservedAt == default || !Enum.IsDefined(frame.Mode)
                || frame.Sequence <= previous || string.IsNullOrWhiteSpace(frame.Hero)
                || frame.PickedDeck is null || frame.Offers is null || frame.Description is null)
                throw new InvalidDataException("Observations require schema 1 or 2, session/time, known mode, increasing sequence, hero, deck, offers and description.");
            if (frame.SchemaVersion == 1 && frame.Selection is not null || frame.SchemaVersion == 2 && (frame.Selection is null || frame.Offers.Length != 0))
                throw new InvalidDataException("Schema 1 is card-only; schema 2 requires its tagged phase and empty legacy offers.");
            var selection = frame.ViewSelection;
            if ((selection is ArenaInactive) != (frame.Mode == ArenaMode.Inactive))
                throw new InvalidDataException("Arena phase must match active/inactive mode.");
            if (frame.SchemaVersion == 2 && selection is ArenaInactive && (frame.PickIndex != 0 || frame.SelectedCardId is not null))
                throw new InvalidDataException("Schema 2 inactive phase must clear previous picked-deck facts.");
            if (selection is ArenaHeroSelection heroes)
            {
                if (heroes.Choices is null || heroes.Choices.Length > 12 || heroes.UnavailableReason?.Length > 2048
                    || heroes.Choices.Length == 0 && string.IsNullOrWhiteSpace(heroes.UnavailableReason)
                    || frame.PickIndex != 0 || frame.SelectedCardId is not null)
                    throw new InvalidDataException("Hero selection requires bounded explicit choices or an unavailable reason, and no card picks.");
                var ids = new HashSet<string>();
                foreach (var hero in heroes.Choices)
                    if (hero is null || string.IsNullOrWhiteSpace(hero.Id) || hero.Id.Length > 128 || !ids.Add(hero.Id)
                        || string.IsNullOrWhiteSpace(hero.Name) || hero.Name.Length > 512
                        || hero.Class is not null && (string.IsNullOrWhiteSpace(hero.Class) || hero.Class.Length > 128))
                        throw new InvalidDataException("Invalid explicit hero identity/name/class.");
            }
            if (selection is ArenaUnavailable unavailable && (!Enum.IsDefined(unavailable.Kind)
                || string.IsNullOrWhiteSpace(unavailable.Reason) || unavailable.Reason.Length > 2048))
                throw new InvalidDataException("Unsupported selections require an explicit kind and bounded reason.");
            var offers = frame.CardOffers;
            if (offers is null) throw new InvalidDataException("Card draft requires explicit choices.");
            if (frame.PickedDeck.Length > 100 || offers.Length > 12 || frame.Hero.Length > 128 || frame.SessionId.Length > 128 || frame.Description.Length > 2048)
                throw new InvalidDataException("Replay exceeds preview collection/text limits.");
            if (offers.Any(offer => offer is null)) throw new InvalidDataException("Offers cannot be null.");
            foreach (var card in frame.PickedDeck.Concat(offers.Select(o => o.Card)))
                if (card is null || string.IsNullOrWhiteSpace(card.Id) || string.IsNullOrWhiteSpace(card.Name) || card.Mana < 0 || card.Id.Length > 128 || card.Name.Length > 512)
                    throw new InvalidDataException("Cards require an ID, name and nonnegative mana.");
            foreach (var offer in offers)
                if (offer.Enabled?.Direct is null || offer.Enabled.Indirect is null
                    || offer.EnhancedBy?.Direct is null || offer.EnhancedBy.Indirect is null)
                    throw new InvalidDataException("Offers require explicit related-card sets.");
            foreach (var rating in offers.SelectMany(offer => new[] { offer.BaseRating, offer.DynamicRating }))
                if (rating?.Score?.Length > 64) throw new InvalidDataException("Rating text exceeds preview limit.");
            foreach (var relation in offers.SelectMany(offer => new[] { offer.Enabled, offer.EnhancedBy }))
                if (relation.Direct.Length + relation.Indirect.Length > 1024
                    || relation.All.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 128))
                    throw new InvalidDataException("Related-card sets exceed preview limits or contain invalid IDs.");
            if (frame.PickIndex != frame.PickedDeck.Length || (frame.Mode == ArenaMode.Inactive && frame.Offers.Length != 0)
                || (frame.SelectedCardId is not null && !frame.PickedDeck.Any(card => card.Id == frame.SelectedCardId)))
                throw new InvalidDataException("Pick index must match picked cards, selected card must be picked, and inactive observations must have no offers.");
            previous = frame.Sequence;
        }
    }
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        // JSON object order is not semantic; Rust/other producers may sort keys.
        // The transport already bounds the complete document before parsing.
        AllowOutOfOrderMetadataProperties = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<ArenaUnavailableKind>(allowIntegerValues: false),
            new System.Text.Json.Serialization.JsonStringEnumConverter<ArenaMode>(allowIntegerValues: false),
            new System.Text.Json.Serialization.JsonStringEnumConverter<NativeMode>(allowIntegerValues: false),
            new System.Text.Json.Serialization.JsonStringEnumConverter<NativeAvailability>(allowIntegerValues: false) }
    };
}
