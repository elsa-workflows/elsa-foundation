namespace Elsa.Studio.Web;

/// <summary>
/// Configuration for theme loading and defaults. Bound from the "Themes" configuration section, which the
/// Configuration API populates from appsettings.json, environment variables, and any other registered source.
/// Environment variables use the standard double-underscore convention, e.g. Themes__ThemesDirectory.
/// </summary>
public class ThemeConfiguration
{
    /// <summary>
    /// Directory path containing theme files (a single JSON file or a directory of .json files).
    /// Relative paths are resolved from the application's content root.
    /// </summary>
    public string? ThemesDirectory { get; set; }

    /// <summary>
    /// Which sources are selectable.
    /// Values: "BuiltInsOnly" (built-ins only), "DirectoryOnly" (configured directory only),
    /// "AddToBuiltIns" (built-ins, directory themes and managed custom themes).
    /// Management retains every definition regardless of this selection policy.
    /// Default: "AddToBuiltIns".
    /// </summary>
    public string ThemesMode { get; set; } = "AddToBuiltIns";

    /// <summary>
    /// Default theme ID to select on first load (e.g., "classic").
    /// </summary>
    public string? DefaultThemeId { get; set; }

    internal string GetThemesMode() => ThemesMode?.Trim().ToUpperInvariant() switch
    {
        "BUILTINSONLY" => "BuiltInsOnly",
        "DIRECTORYONLY" => "DirectoryOnly",
        "ADDTOBUILTINS" => "AddToBuiltIns",
        _ => throw new InvalidOperationException("Themes:ThemesMode must be BuiltInsOnly, DirectoryOnly or AddToBuiltIns.")
    };
}
