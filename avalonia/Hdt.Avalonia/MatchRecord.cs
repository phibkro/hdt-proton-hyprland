namespace Hdt.Avalonia;

public sealed record MatchRecord(string Id, string? DeckId, string ObservedAt, string Mode, string Result,
    string PlayerHero, string OpponentHero, uint Turns, byte? Placement, string Notes)
{
    public override string ToString() => $"{ObservedAt} • {Mode} • {Result} • {PlayerHero} vs {OpponentHero} • {Turns} turns";
}
