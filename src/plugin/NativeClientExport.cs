// Original optional bridge. Copies public, already observed tracker models only.
// No memory acquisition, HSReplay requests, account identifiers or game writes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Api = Hearthstone_Deck_Tracker.API.Core;
using Card = Hearthstone_Deck_Tracker.Hearthstone.Card;
using Entity = Hearthstone_Deck_Tracker.Hearthstone.Entities.Entity;

internal sealed class NativeClientExport : IDisposable
{
    readonly string session = Guid.NewGuid().ToString();
    readonly List<object> completed = new List<object>();
    readonly NativeSnapshotWriter writer;
    int sequence;
    DateTime lastCapture = DateTime.MinValue;
    object gameIdentity;
    string matchId;
    bool matchRecorded;
    NativeClientExport(string path)
    {
        writer = new NativeSnapshotWriter(path, snapshot =>
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024, RecursionLimit = 64 };
            return new UTF8Encoding(false).GetBytes(serializer.Serialize(snapshot));
        }, () => Hearthstone_Deck_Tracker.Utility.Logging.Log.Warn("Native observation publication failed; native readers will mark old data unavailable."));
    }
    public static NativeClientExport Start()
    {
        var path = Environment.GetEnvironmentVariable("HDT_NATIVE_CLIENT_FILE");
        if (String.IsNullOrWhiteSpace(path)) return null;
        try { return new NativeClientExport(path); }
        catch (Exception) { Hearthstone_Deck_Tracker.Utility.Logging.Log.Warn("Native observation export could not start; check its explicitly configured private path."); return null; }
    }
    public void Dispose()
    {
        writer.Dispose();
        GC.SuppressFinalize(this);
    }
    public void Capture()
    {
        var now = DateTime.UtcNow;
        if ((now - lastCapture).TotalMilliseconds < 500) return;
        lastCapture = now;
        object snapshot;
        var serial = checked(++sequence);
        try
        {
            var game = Api.Game;
            var stats = game.CurrentGameStats;
            if (!Object.ReferenceEquals(stats, gameIdentity))
            {
                gameIdentity = stats; matchId = stats == null ? null : Guid.NewGuid().ToString(); matchRecorded = false;
            }
            var result = stats == null ? "None" : stats.Result.ToString();
            if (stats != null && !matchRecorded && (result == "Win" || result == "Loss" || result == "Draw") && stats.EndTime > stats.StartTime)
            {
                completed.Add(new
                {
                    id = matchId,
                    deckId = (string)null,
                    observedAt = stats.EndTime.ToUniversalTime().ToString("o"),
                    mode = Text(stats.GameMode.ToString(), 128),
                    result = result,
                    playerHero = Text(stats.PlayerHero, 128),
                    opponentHero = Text(stats.OpponentHero, 128),
                    turns = Nonnegative(stats.Turns),
                    placement = stats.BattlegroundsDetails == null ? null : stats.BattlegroundsDetails.FinalPlacement,
                    notes = "Observed by optional Proton bridge; no Windows database import."
                });
                if (completed.Count > 256) completed.RemoveAt(0);
                matchRecorded = true;
            }
            var arena = Api.OverlayWindow.ArenaPickHelperViewModel;
            var rawMode = game.CurrentMode.ToString();
            var matchMode = stats == null ? "None" : stats.GameMode.ToString();
            var mode = !game.IsRunning ? "Offline" : rawMode == "DRAFT" ? "Arena" : game.IsInMenu ? "Menu"
                : matchMode == "Battlegrounds" ? "Battlegrounds" : matchMode == "Mercenaries" ? "Mercenaries"
                : matchMode == "None" ? "Unknown" : "Constructed";
            var offers = new List<object>();
            if (mode == "Arena" && arena.IsDrafting && arena.CardStats != null)
            {
                if (arena.CardStats.Count > 12) throw new InvalidDataException();
                foreach (var option in arena.CardStats)
                {
                    var ratings = option.CardStats;
                    var related = ratings == null ? null : ratings.RelatedCards;
                    offers.Add(new
                    {
                        card = ArenaCard(option.Card),
                        baseRating = ratings == null || ratings.Arenasmith == null ? null : new { score = TextOrNull(ratings.Arenasmith.Score, 64), plaque = ratings.Arenasmith.Plaque },
                        dynamicRating = ratings == null || ratings.ArenasmithDyn == null ? null : new { score = TextOrNull(ratings.ArenasmithDyn.Score, 64), plaque = ratings.ArenasmithDyn.Plaque },
                        enabled = Relations(related == null || related.CardIdsEnabled == null ? null : related.CardIdsEnabled.Direct, related == null || related.CardIdsEnabled == null ? null : related.CardIdsEnabled.Indirect),
                        enhancedBy = Relations(related == null || related.EnhancedByCardIds == null ? null : related.EnhancedByCardIds.Direct, related == null || related.EnhancedByCardIds == null ? null : related.EnhancedByCardIds.Indirect)
                    });
                }
            }
            var picks = new List<object>();
            if (mode == "Arena")
            {
                if (arena.TileViewModels.Count > 100) throw new InvalidDataException();
                foreach (var tile in arena.TileViewModels)
                {
                    if (tile.Count < 0 || picks.Count + tile.Count > 100) throw new InvalidDataException();
                    for (var i = 0; i < tile.Count; i++) picks.Add(ArenaCard(Hearthstone_Deck_Tracker.Hearthstone.Database.GetCardFromId(tile.CardId)));
                }
            }
            var playing = game.IsRunning && !game.IsInMenu;
            var playerHero = playing ? game.Player.Hero : null;
            var opponentHero = playing ? game.Opponent.Hero : null;
            var arenaObservation = new
            {
                schemaVersion = 1,
                sessionId = session,
                observedAt = now.ToString("o"),
                sequence = serial,
                mode = mode == "Arena" && arena.IsDrafting ? "Drafting" : "Inactive",
                pickIndex = picks.Count,
                selectedCardId = (string)null,
                hero = playerHero == null ? "Hero not observed" : Text(playerHero.LocalizedName, 128),
                underground = arena.IsUnderground,
                pickedDeck = picks,
                offers = offers,
                description = "Already observed public tracker models; last selected card unavailable."
            };
            snapshot = new
            {
                schemaVersion = 1,
                sessionId = session,
                sequence = serial,
                observedAt = now.ToString("o"),
                availability = "Available",
                error = (string)null,
                mode = mode,
                rawMode = rawMode,
                matchMode = matchMode,
                matchId = matchId,
                inMenu = game.IsInMenu,
                isRunning = game.IsRunning,
                playerHealth = playerHero == null ? (int?)null : playerHero.Health,
                opponentHealth = opponentHero == null ? (int?)null : opponentHero.Health,
                playerHandCount = playing ? (int?)game.Player.HandCount : null,
                opponentHandCount = playing ? (int?)game.Opponent.HandCount : null,
                playerDeckCount = playing ? (int?)game.Player.DeckCount : null,
                opponentDeckCount = playing ? (int?)game.Opponent.DeckCount : null,
                remainingDeck = playing ? Cards(game.Player.PlayerCardList) : Array.Empty<object>(),
                knownHand = playing ? Entities(game.Player.Hand) : Array.Empty<object>(),
                playerBoard = playing ? Entities(game.Player.Board) : Array.Empty<object>(),
                opponentBoard = playing ? Entities(game.Opponent.Board) : Array.Empty<object>(),
                arena = arenaObservation,
                completedMatches = completed.ToArray(),
                historyRetention = 256
            };
        }
        catch (Exception)
        {
            // Publish no partial/stale game facts after a capture failure.
            snapshot = new
            {
                schemaVersion = 1,
                sessionId = session,
                sequence = serial,
                observedAt = now.ToString("o"),
                availability = "Unavailable",
                error = "Public tracker model capture failed or exceeded preview limits."
            };
        }
        writer.Offer(snapshot);
    }
    static int Nonnegative(int value) { if (value < 0) throw new InvalidDataException(); return value; }
    static string Text(string value, int maximum)
    {
        value = value ?? "";
        if (value.Length > maximum) throw new InvalidDataException();
        return value;
    }
    static string TextOrNull(string value, int maximum) { return value == null ? null : Text(value, maximum); }
    static object ArenaCard(Card card) { if (card == null) throw new InvalidDataException(); return new { id = Text(card.Id, 128), name = Text(card.LocalizedName, 512), mana = Nonnegative(card.Cost) }; }
    static object Relations(IEnumerable<string> direct, IEnumerable<string> indirect)
    {
        var first = (direct ?? Array.Empty<string>()).Take(1025).Select(id => Text(id, 128)).ToArray();
        var second = (indirect ?? Array.Empty<string>()).Take(1025).Select(id => Text(id, 128)).ToArray();
        if (first.Length + second.Length > 1024) throw new InvalidDataException();
        return new { direct = first, indirect = second };
    }
    static object[] Cards(IEnumerable<Card> cards)
    {
        var result = cards.Take(501).Select(card => (object)new
        {
            id = Text(card.Id, 128),
            dbfId = card.DbfId,
            name = Text(card.LocalizedName, 512),
            mana = Nonnegative(card.Cost),
            count = card.Count,
            health = (int?)null
        }).ToArray();
        if (result.Length > 500) throw new InvalidDataException();
        return result;
    }
    static object[] Entities(IEnumerable<Entity> entities)
    {
        // Only known own-hand IDs and public board IDs. Opponent hand/secret IDs are never read.
        var result = entities.Where(entity => !entity.Info.Hidden && !String.IsNullOrEmpty(entity.CardId)).Take(501)
            .Select(entity => (object)new
            {
                id = Text(entity.CardId, 128),
                dbfId = entity.Card.DbfId,
                name = Text(entity.LocalizedName, 512),
                mana = Nonnegative(entity.Cost),
                count = 1,
                health = (int?)entity.Health
            }).ToArray();
        if (result.Length > 500) throw new InvalidDataException();
        return result;
    }
}
