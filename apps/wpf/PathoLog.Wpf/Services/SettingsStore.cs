using System.IO;
using System.Text.Json;

namespace PathoLog.Wpf.Services;

public sealed class SettingsStore
{
    private readonly string _settingsPath;

    public SettingsStore()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folder = Path.Combine(docs, "PathoLog");
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }
        catch
        {
            // ignore and return defaults
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // ignored; best-effort persistence
        }
    }
}

public sealed class AppSettings
{
    public string? OpenAiApiKey { get; set; }
    public string? GeminiApiKey { get; set; }
}
