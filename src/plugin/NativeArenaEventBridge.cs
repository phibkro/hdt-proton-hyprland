// Independently authored event adapter. HDT owns watcher/context/acquisition lifetime.
// Only bounded copied values survive callbacks; no vendor objects or account IDs.
#if NET10_0_OR_GREATER
#nullable disable
#endif
using System;
using System.Collections.Generic;
using HearthMirror.Enums;
using HearthMirror.Objects;
using HearthWatcher;

internal sealed class NativeArenaEventBridge : IDisposable
{
    internal sealed class Metadata
    {
        public readonly string Id, Name, Type;
        public readonly int Mana;
        public Metadata(string id, string name, string type, int mana) { Id = id; Name = name; Type = type; Mana = mana; }
    }
    sealed class DeliveredChoice
    {
        public readonly Metadata Card;
        public readonly int SourceIndex;
        public DeliveredChoice(Metadata card, int index) { Card = card; SourceIndex = index; }
    }
    internal sealed class Frame
    {
        public readonly object Selection, Evidence;
        public readonly bool Drafting, Heroes, Underground;
        public Frame(object selection, object evidence, bool drafting, bool heroes, bool underground)
        { Selection = selection; Evidence = evidence; Drafting = drafting; Heroes = heroes; Underground = underground; }
        public object Observation(string session, int sequence, DateTime now, object[] picked)
        {
            if (!Drafting || Heroes) picked = new object[0];
            return new
            {
                schemaVersion = 3,
                sessionId = session,
                observedAt = now.ToString("o"),
                sequence = sequence,
                mode = Drafting ? "Drafting" : "Inactive",
                pickIndex = 0,
                selectedCardId = (string)null,
                hero = "Draft hero not correlated",
                underground = Underground,
                pickedDeck = new object[0],
                cachedDeck = new { origin = "HdtPublicTileModel", cards = picked },
                offers = new object[0],
                selection = Selection,
                choiceEvidence = Evidence,
                description = "Choices: last delivered HDT watcher event; memory freshness unknown. Ratings and synergies unavailable; last selected card unavailable."
            };
        }
    }
    readonly object gate = new object();
    readonly ArenaStateWatcher watcher;
    readonly Func<string, Metadata> resolve;
    DeliveredChoice[] choices;
    bool disposed, contextKnown, drafting, package, redraft, underground;
    long revision, generation;
    DateTime receivedAt;
    string reason = "Awaiting a delivered watcher context and choices event.";
    public NativeArenaEventBridge(ArenaStateWatcher source, Func<string, Metadata> metadata)
    {
        if (source == null || metadata == null) throw new ArgumentNullException();
        watcher = source; resolve = metadata;
        watcher.OnClientStateChanged += State;
        watcher.OnChoicesChanged += Choices;
        watcher.OnHeroPicked += Picked;
        watcher.OnHeroPowerPicked += Picked;
        watcher.OnDeckIdChanged += Deck;
        watcher.OnIsPackageSelectOpen += Package;
        watcher.OnIsUndergroundChanged += Underground;
    }
    void Clear(string why) { choices = null; reason = why; generation = checked(generation + 1); }
    void State(ValueTuple<ArenaClientStateType, ArenaSessionState> value)
    {
        lock (gate)
        {
            if (disposed) return;
            Clear("Watcher context changed; awaiting choices from that context.");
            contextKnown = Enum.IsDefined(typeof(ArenaClientStateType), value.Item1)
                && Enum.IsDefined(typeof(ArenaSessionState), value.Item2) && value.Item2 != ArenaSessionState.INVALID
                && value.Item2 != ArenaSessionState.EDITING_DECK;
            redraft = value.Item2 == ArenaSessionState.REDRAFTING || value.Item2 == ArenaSessionState.MIDRUN_REDRAFT_PENDING
                || value.Item1 == ArenaClientStateType.Normal_Redraft || value.Item1 == ArenaClientStateType.Underground_Redraft;
            underground = value.Item1 == ArenaClientStateType.Underground_Draft || value.Item1 == ArenaClientStateType.Underground_Redraft;
            drafting = value.Item1 == ArenaClientStateType.Normal_Draft || value.Item1 == ArenaClientStateType.Underground_Draft || redraft;
        }
    }
    void Picked(string ignored) { lock (gate) { if (!disposed) Clear("Selection changed; awaiting the next delivered choices event."); } }
    void Deck(long ignored) { lock (gate) { if (!disposed) Clear("Draft context changed; awaiting choices. Account deck identity is not retained."); } }
    void Package(bool value) { lock (gate) { if (!disposed) { package = value; Clear("Package context changed; awaiting choices."); } } }
    void Underground(bool value) { lock (gate) { if (!disposed) { underground = value; Clear("Arena mode changed; awaiting choices."); } } }
    void Choices(List<ArenaState.DraftChoice> supplied)
    {
        // Receipt is delivery time, not the memory sample time (not exposed by this API).
        var receipt = DateTime.UtcNow;
        lock (gate)
        {
            if (disposed) return;
            choices = null; revision = checked(revision + 1); receivedAt = receipt;
            reason = "No supported, unambiguous delivered choices.";
            if (!contextKnown || !drafting || package || redraft) return;
            try
            {
                if (supplied == null || supplied.Count < 1 || supplied.Count > 12) return;
                var copy = new List<DeliveredChoice>();
                var ids = new HashSet<string>(); var indices = new HashSet<int>();
                string type = null;
                foreach (var item in supplied)
                {
                    var actor = item == null ? null : item.Actor;
                    if (actor == null) return;
                    var index = actor.Index; var id = actor.CardId;
                    if (index < 0 || !indices.Add(index) || String.IsNullOrWhiteSpace(id)
                        || id.Length > 128 || !ids.Add(id)) return;
                    var card = resolve(id);
                    if (card == null || card.Id != id || String.IsNullOrWhiteSpace(card.Name)
                        || card.Name.Length > 512 || card.Mana < 0) return;
                    var kind = card.Type == "HERO" ? "Hero" : card.Type == "MINION" || card.Type == "SPELL"
                        || card.Type == "WEAPON" || card.Type == "LOCATION" ? "Card" : null;
                    if (kind == null || type != null && kind != type) return;
                    type = kind;
                    copy.Add(new DeliveredChoice(new Metadata(card.Id, card.Name, kind, card.Mana), index));
                }
                choices = copy.ToArray();
            }
            catch (Exception) { choices = null; reason = "Delivered choice metadata could not be copied within preview limits."; }
        }
    }
    public Frame Capture(bool inArena, bool packageVeto, bool redraftVeto, bool dualClassVeto)
    {
        lock (gate)
        {
            if (!inArena)
            {
                if (contextKnown || choices != null || package || redraft) Clear("Arena left; awaiting new watcher context.");
                contextKnown = false; drafting = false; package = false; redraft = false; underground = false;
                return new Frame(new { phase = "Inactive" }, null, false, false, false);
            }
            if (disposed) return Unavailable("Unknown", "Event bridge detached.");
            if (package || packageVeto) { choices = null; return Unavailable("Package", "Native package selection is unavailable."); }
            if (redraft || redraftVeto) { choices = null; return Unavailable("Redraft", "Native redraft selection is unavailable."); }
            if (dualClassVeto) { choices = null; return Unavailable("DualClass", "Native dual-class selection is unavailable."); }
            if (contextKnown && !drafting) return new Frame(new { phase = "Inactive" }, null, false, false, false);
            if (choices == null) return Unavailable("Unknown", reason);
            var evidence = new { origin = "HdtWatcherEvent", receivedAt = receivedAt.ToString("o"), revision = revision, generation = generation };
            var heroes = choices[0].Card.Type == "Hero";
            var exported = new List<object>();
            foreach (var choice in choices)
            {
                var card = choice.Card;
                if (heroes) exported.Add(new { id = card.Id, name = card.Name, @class = (string)null, sourceIndex = choice.SourceIndex });
                else exported.Add(new
                {
                    sourceIndex = choice.SourceIndex,
                    card = new { id = card.Id, name = card.Name, mana = card.Mana },
                    baseRating = (object)null,
                    dynamicRating = (object)null,
                    enabled = new { direct = new string[0], indirect = new string[0] },
                    enhancedBy = new { direct = new string[0], indirect = new string[0] }
                });
            }
            return new Frame(heroes ? (object)new { phase = "HeroSelection", choices = exported.ToArray() }
                : new { phase = "CardDraft", choices = exported.ToArray() }, evidence, true, heroes, underground);
        }
    }
    Frame Unavailable(string kind, string why) { return new Frame(new { phase = "Unavailable", kind = kind, reason = why }, null, true, false, underground); }
    public void Invalidate()
    { lock (gate) { if (!disposed) Clear("Public model capture failed; awaiting the next delivered choices event."); } }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; choices = null; }
        watcher.OnClientStateChanged -= State;
        watcher.OnChoicesChanged -= Choices;
        watcher.OnHeroPicked -= Picked;
        watcher.OnHeroPowerPicked -= Picked;
        watcher.OnDeckIdChanged -= Deck;
        watcher.OnIsPackageSelectOpen -= Package;
        watcher.OnIsUndergroundChanged -= Underground;
    }
}
