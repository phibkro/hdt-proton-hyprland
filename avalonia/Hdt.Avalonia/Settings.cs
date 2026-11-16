using System.Text.Json;
namespace Hdt.Avalonia;

public sealed record TrackerSettings(bool ShowArenasmithScore = true, bool ShowArenaDeckSynergies = true);
public sealed class SettingsStore(string path)
{
    public TrackerSettings Load() => File.Exists(path)
        ? JsonSerializer.Deserialize<TrackerSettings>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty settings.")
        : new();
    public void Save(TrackerSettings settings)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
