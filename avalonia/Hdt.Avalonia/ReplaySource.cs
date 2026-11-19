using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hdt.Avalonia;

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
        ArenaReplay.Validate(frames);
        return frames;
    }

    public static void Validate(ArenaObservation[] frames) => ArenaReplay.Validate(frames);

    public static JsonSerializerOptions JsonOptions { get; } = new(ArenaReplay.JsonOptions)
    {
        Converters =
        {
            new JsonStringEnumConverter<NativeMode>(allowIntegerValues: false),
            new JsonStringEnumConverter<NativeAvailability>(allowIntegerValues: false)
        }
    };
}
