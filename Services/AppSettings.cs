using System;
using System.IO;
using System.Text.Json;

namespace InvoiceManager.Services;

public sealed class AppSettings
{
    public bool KeepOriginalFiles { get; set; }
    public string DestinationFolder { get; set; } = string.Empty;
    public string ScanFolder { get; set; } = string.Empty;
    public string ScannerDeviceId { get; set; } = string.Empty;

    public static AppSettings CreateDefault()
    {
        var documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return new AppSettings
        {
            DestinationFolder = Path.Combine(documentsFolder, "Számlák"),
            ScanFolder = Path.Combine(documentsFolder, "szamla_scan")
        };
    }
}

public sealed class UserSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SzamlaRendezo",
        "settings.json");

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return AppSettings.CreateDefault();
        }

        var json = File.ReadAllText(_settingsPath);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        if (settings is null || string.IsNullOrWhiteSpace(settings.DestinationFolder))
        {
            throw new JsonException("A beállításfájl nem tartalmaz érvényes célmappát.");
        }

        if (string.IsNullOrWhiteSpace(settings.ScanFolder))
        {
            settings.ScanFolder = AppSettings.CreateDefault().ScanFolder;
        }

        return settings;
    }

    public void Save(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.DestinationFolder) ||
            string.IsNullOrWhiteSpace(settings.ScanFolder))
        {
            throw new ArgumentException("A célmappa és a scan mappa nem lehet üres.", nameof(settings));
        }

        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("A beállításfájl célmappája nem állapítható meg.");
        Directory.CreateDirectory(directory);

        var temporaryPath = _settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, _settingsPath, true);
    }
}
