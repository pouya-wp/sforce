using System.IO;
using System.Text.Json;

namespace SForce.App.Services;

public sealed class BackupStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SFORCE");

    private string BackupsDirectory => Path.Combine(_root, "Backups");
    private string StatePath => Path.Combine(_root, "state.json");

    public AppState? LoadState()
    {
        try
        {
            return File.Exists(StatePath)
                ? JsonSerializer.Deserialize<AppState>(File.ReadAllText(StatePath), JsonOptions)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public BackupManifest? LoadActiveBackup()
    {
        var state = LoadState();
        if (state is null || string.IsNullOrWhiteSpace(state.ActiveBackupId))
        {
            return null;
        }

        var path = Path.Combine(BackupsDirectory, $"{state.ActiveBackupId}.json");
        return File.Exists(path)
            ? JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(path), JsonOptions)
            : null;
    }

    public void SaveBaseline(BackupManifest manifest)
    {
        Directory.CreateDirectory(BackupsDirectory);
        var path = Path.Combine(BackupsDirectory, $"{manifest.Id}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions));
        SetActive(manifest.Id, manifest.Profile);
    }

    public void SetActive(string backupId, ForceProfile profile)
    {
        Directory.CreateDirectory(_root);
        var state = new AppState(backupId, profile, DateTimeOffset.Now);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state, JsonOptions));
    }

    public void ClearActive()
    {
        if (File.Exists(StatePath))
        {
            File.Delete(StatePath);
        }
    }
}

public sealed record AppState(string ActiveBackupId, ForceProfile Profile, DateTimeOffset AppliedAt);
