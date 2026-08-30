#pragma warning disable CS0618

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MapRegionizer.Core.Domain;
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
        : this(null)
    {
    }

    /// <summary>Creates a settings service at a specific path, primarily for isolated hosts and tests.</summary>
    public UserSettingsService(string? settingsPath)
    {
        var directory = string.IsNullOrWhiteSpace(settingsPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MapRegionizer")
            : Path.GetDirectoryName(Path.GetFullPath(settingsPath))!;
        Directory.CreateDirectory(directory);
        _settingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? Path.Combine(directory, "client-settings.json")
            : Path.GetFullPath(settingsPath);
    }

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new UserSettings();

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
            // GenerationMode was the persisted name before world context was
            // separated from spatial compatibility. Read it explicitly only
            // when canonical state is absent, so files containing both names
            // have deterministic canonical-field precedence.
            using var document = JsonDocument.Parse(json);
            var hasCanonicalWorldContext = document.RootElement.TryGetProperty("WorldContextMode", out var canonicalMode) &&
                canonicalMode.ValueKind == JsonValueKind.String &&
                Enum.TryParse<WorldContextMode>(canonicalMode.GetString(), ignoreCase: true, out _);
            if (!hasCanonicalWorldContext &&
                document.RootElement.TryGetProperty("GenerationMode", out var legacyMode) &&
                legacyMode.ValueKind == JsonValueKind.String &&
                Enum.TryParse<RegionalGenerationMode>(legacyMode.GetString(), ignoreCase: true, out var parsedLegacyMode))
            {
                settings.GenerationMode = parsedLegacyMode;
            }

            return settings;
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
    public string WorldMaskPath { get; set; } = string.Empty;
    public string LastOutputDirectory { get; set; } = string.Empty;
    public string LastPreviewLayer { get; set; } = "overview";
    public bool HasCompletedOnboarding { get; set; }
    public MapGenerationOptions GenerationOptions { get; set; } = new();
    private WorldContextMode _worldContextMode = WorldContextMode.Isolated;
    private bool _legacyCompatibilityEnabled;

    public WorldContextMode WorldContextMode
    {
        get => _worldContextMode;
        set
        {
            _worldContextMode = !Enum.IsDefined(value) || value == WorldContextMode.Custom
                ? WorldContextMode.Isolated
                : value;
            _legacyCompatibilityEnabled = false;
        }
    }

    [JsonIgnore]
    public bool LegacyCompatibilityEnabled
    {
        get => _legacyCompatibilityEnabled;
        set => _legacyCompatibilityEnabled = value;
    }

    /// <summary>
    /// Obsolete persisted alias. Old settings containing Legacy or Custom are
    /// read here and normalized by the App view model to Isolated context.
    /// </summary>
    [Obsolete("Use WorldContextMode. Legacy maps to Isolated plus compatibility semantics.")]
    [JsonIgnore]
    public RegionalGenerationMode GenerationMode
    {
        get => _legacyCompatibilityEnabled
            ? RegionalGenerationMode.Legacy
            : _worldContextMode.ToRegionalGenerationMode();
        set
        {
            WorldContextMode = value.ToWorldContextMode();
            _legacyCompatibilityEnabled = value == RegionalGenerationMode.Legacy;
        }
    }
    public int WorkingHaloCells { get; set; }
    public int RequestedOriginX { get; set; }
    public int RequestedOriginY { get; set; }
    public MapOutputOptions OutputOptions { get; set; } = new();
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
