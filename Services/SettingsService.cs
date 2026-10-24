using System.IO;
using System.Text.Json;
using GitHubReleaseWatcher.Models;

namespace GitHubReleaseWatcher.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string SettingsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitHubReleaseWatcher");
    public string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions);
                if (loaded is not null)
                {
                    loaded.CheckIntervalMinutes = Math.Clamp(loaded.CheckIntervalMinutes, 1, 1440);
                    loaded.Repositories ??= [];
                    loaded.ReleaseHistory ??= [];
                    foreach (var repo in loaded.Repositories) repo.KnownReleaseIds ??= [];
                    return loaded;
                }
            }
        }
        catch { /* Keep the app usable if settings are damaged; the file is replaced on save. */ }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, SettingsPath, true);
    }
}
