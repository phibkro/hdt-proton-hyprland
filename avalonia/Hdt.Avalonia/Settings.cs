using System.Text.Json;
namespace Hdt.Avalonia;

public sealed record TrackerSettings(bool ShowArenasmithScore = true, bool ShowArenaDeckSynergies = true);
public sealed class SettingsStore(string path)
{
    public async Task<TrackerSettings> LoadAsync(CancellationToken cancellation = default)
    {
        try { return await SnapshotFiles.ReadJsonAsync<TrackerSettings>(path, SnapshotFiles.SettingsBytes, cancellation).ConfigureAwait(false); }
        catch (FileNotFoundException) { return new(); }
    }
    public Task SaveAsync(TrackerSettings settings) => Task.Run(() => Save(settings));
    private void Save(TrackerSettings settings)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, settings);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
