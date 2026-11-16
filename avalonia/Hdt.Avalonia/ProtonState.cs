using System.Text.Json;
namespace Hdt.Avalonia;

// Reads only the account-free fields emitted by this repository's MIT plugin.
// The existing export contains no cards, picks or ratings: never infer those from flags.
public sealed record ProtonState(long ObservedMS, ProtonGameState State);
public sealed record ProtonGameState(string Mode, string ArenaClientState, string ArenaSessionState, bool ArenaDrafting, bool ArenaAvailable);
public sealed class ProtonStateReader(string path)
{
    public async Task<string> ReadAsync(DateTimeOffset? observedNow = null, CancellationToken cancellation = default)
    {
        var snapshot = await SnapshotFiles.ReadJsonAsync<ProtonState>(path, SnapshotFiles.StatusBytes, cancellation).ConfigureAwait(false);
        var now = observedNow ?? DateTimeOffset.UtcNow;
        if (snapshot.State is null || snapshot.ObservedMS <= 0 || snapshot.State.Mode is null
            || snapshot.State.ArenaClientState is null || snapshot.State.ArenaSessionState is null
            || snapshot.State.Mode.Length > 128 || snapshot.State.ArenaClientState.Length > 128 || snapshot.State.ArenaSessionState.Length > 128)
            throw new InvalidDataException("Proton state requires observation time and game/Arena states.");
        var age = now.ToUnixTimeMilliseconds() - snapshot.ObservedMS;
        if (age < -5000) throw new InvalidDataException("Proton observation is in the future.");
        if (age > 5000) throw new InvalidDataException("Proton status is STALE; current game/Arena state is unknown.");
        return $"Proton connected • {snapshot.State.Mode} • {snapshot.State.ArenaClientState}/{snapshot.State.ArenaSessionState} • drafting {snapshot.State.ArenaDrafting} • ratings available {snapshot.State.ArenaAvailable} • status only; no card data";
    }
}
