using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MapRegionizer.Core.Options;

namespace MapRegionizer.App.Services;

public sealed class UserSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _settingsPath;

    public UserSettingsService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MapRegionizer");
        Directory.CreateDirectory(directory);
        _settingsPath = Path.Combine(directory, "client-settings.json");
    }

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new UserSettings();

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
        }
        catch
        {
            return new UserSettings();
        }
    }

    public void Save(UserSettings settings)
    {
        try
        {
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // User preferences are convenience state; generation should never fail because saving them did.
        }
    }
}

public sealed class UserSettings
{
    public string Language { get; set; } = "ru-RU";
    public string Theme { get; set; } = "System";
    public string LastMaskPath { get; set; } = string.Empty;
    public string LastOutputDirectory { get; set; } = string.Empty;
    public string LastPreviewLayer { get; set; } = "overview";
    public bool HasCompletedOnboarding { get; set; }
    public MapGenerationOptions GenerationOptions { get; set; } = new();
    public double ExportScale { get; set; } = 1.0;
    public double ExportRegionBorderWidth { get; set; } = 2.0;
    public double ExportTectonicBoundaryWidth { get; set; } = 1.0;
    public bool ExportDrawCrustPlateBoundaries { get; set; }
    public bool ExportDrawFeaturePlateBoundaries { get; set; }
    public bool ExportDrawElevationHillshade { get; set; } = true;
    public bool ExportDrawElevationPlateBoundaries { get; set; }
    public bool ExportDrawClimateHillshade { get; set; } = true;
    public bool ExportDrawClimateRivers { get; set; } = true;
    public bool ExportDrawClimateRiverValleyAccents { get; set; } = true;
}
