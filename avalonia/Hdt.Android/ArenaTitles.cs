using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hdt.Mobile;

internal readonly record struct ScreenLine(string Text, int X, int Y);

internal sealed partial class ArenaTitles(Dictionary<string, string> catalog)
{
    private const string CatalogUrl = "https://api.hearthstonejson.com/v1/latest/enUS/cards.collectible.json";
    private static readonly HttpClient Http = new() { MaxResponseContentBufferSize = 8 * 1024 * 1024 };

    internal static async Task<ArenaTitles> Load(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        using var response = await Http.GetAsync(CatalogUrl, deadline.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: deadline.Token).ConfigureAwait(false);
        var cards = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var card in document.RootElement.EnumerateArray())
        {
            if (!card.TryGetProperty("name", out var entry) || entry.GetString() is not { } name)
                continue;
            var normalized = Normalize(name);
            if (normalized.Length >= 3)
                cards.TryAdd(normalized, name);
        }

        if (cards.Count == 0)
            throw new InvalidDataException("The card catalog is empty.");

        return new(cards);
    }

    internal string[] ReadOffers(IEnumerable<ScreenLine> lines, int width, int height)
    {
        if (width <= height)
            return [];

        // A normal card pick presents three titles at a similar height. Keep an
        // ambiguous read empty and retain source order, including duplicate names.
        var candidates = new List<(ScreenLine Line, string Name)>();
        foreach (var line in lines)
        {
            if (line.X > 0 && line.X < width && line.Y > height / 5 && line.Y < height * 4 / 5
                && catalog.TryGetValue(Normalize(line.Text), out var name))
                candidates.Add((line, name));
        }

        string[] offers = [];
        foreach (var anchor in candidates)
        {
            var row = candidates.Where(candidate => Math.Abs(candidate.Line.Y - anchor.Line.Y) <= height / 32)
                .OrderBy(candidate => candidate.Line.X).ToArray();
            if (row.Length != 3 || row[1].Line.X - row[0].Line.X < width / 10
                || row[2].Line.X - row[1].Line.X < width / 10)
                continue;

            var recognized = row.Select(candidate => candidate.Name).ToArray();
            if (offers.Length > 0 && !offers.SequenceEqual(recognized))
                return [];
            offers = recognized;
        }

        return offers;
    }

    private static string Normalize(string value) =>
        NonAlphanumeric().Replace(value.ToLowerInvariant(), "");

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphanumeric();
}
