using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Elsa.Studio.Web;

/// <summary>
/// Loads and manages custom theme configuration from filesystem and configuration sources.
/// Built-in themes remain in the frontend (presets.ts) for backward compatibility.
/// </summary>
internal sealed class ThemeConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ThemeConfiguration _config;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ThemeConfigurationService> _logger;

    public ThemeConfigurationService(
        ThemeConfiguration config,
        IWebHostEnvironment environment,
        ILogger<ThemeConfigurationService> logger)
    {
        _config = config;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// Loads custom themes from the configured directory.
    /// Returns theme definitions and the configured default theme ID.
    /// </summary>
    public async Task<ThemeConfigurationResult> LoadThemesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var customThemes = await LoadCustomThemesAsync(cancellationToken);

            return new ThemeConfigurationResult(
                Themes: customThemes.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
                DefaultThemeId: _config.DefaultThemeId ?? ""
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading custom themes from directory.");
            return new ThemeConfigurationResult(
                Themes: [],
                DefaultThemeId: _config.DefaultThemeId ?? ""
            );
        }
    }

    /// <summary>
    /// Loads custom theme definitions from the configured directory.
    /// Supports both a single JSON file and a directory of .json files.
    /// </summary>
    private async Task<StudioThemeDefinition[]> LoadCustomThemesAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_config.ThemesDirectory))
            return [];

        var themePath = Path.IsPathRooted(_config.ThemesDirectory)
            ? _config.ThemesDirectory
            : Path.Combine(_environment.ContentRootPath, _config.ThemesDirectory);

        if (!File.Exists(themePath) && !Directory.Exists(themePath))
        {
            _logger.LogWarning("Configured themes path not found: {Path}", themePath);
            return [];
        }

        var themes = new List<StudioThemeDefinition>();

        if (File.Exists(themePath) && themePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            // Single JSON file containing theme collection
            _logger.LogInformation("Loading themes from file: {Path}", themePath);
            try
            {
                var json = await File.ReadAllTextAsync(themePath, cancellationToken);
                var response = JsonSerializer.Deserialize<ThemesCollectionResponse>(json, JsonOptions);
                if (response?.Themes != null)
                    themes.AddRange(response.Themes);
                _logger.LogInformation("Loaded {Count} custom themes from file", themes.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error loading themes from file {Path}", themePath);
            }
        }
        else if (Directory.Exists(themePath))
        {
            // Directory of individual .json files
            _logger.LogInformation("Loading themes from directory: {Path}", themePath);
            var files = Directory.EnumerateFiles(themePath, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file, cancellationToken);
                    var response = JsonSerializer.Deserialize<ThemesCollectionResponse>(json, JsonOptions);
                    if (response?.Themes != null)
                        themes.AddRange(response.Themes);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error loading theme file {File}", file);
                }
            }
            _logger.LogInformation("Loaded {Count} custom themes from directory", themes.Count);
        }

        return themes.ToArray();
    }
}

internal sealed record ThemeConfigurationResult(
    StudioThemeDefinition[] Themes,
    string DefaultThemeId);

internal sealed record ThemesCollectionResponse(StudioThemeDefinition[]? Themes);
