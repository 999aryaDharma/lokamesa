using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DocumentIDE.Core.Settings;

namespace DocumentIDE.Infrastructure.Settings;

public class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath;
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true
    };

    public SettingsService(string? settingsFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(settingsFilePath))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDir = Path.Combine(localAppData, "DocumentIDE");
            _settingsFilePath = Path.Combine(appDir, "settings.json");
        }
        else
        {
            _settingsFilePath = settingsFilePath;
        }
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
            return settings ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        var dir = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        await File.WriteAllTextAsync(_settingsFilePath, json);
    }
}
