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
    /// How to combine directory themes with built-ins.
    /// Values: "BuiltInsOnly" (built-ins only), "DirectoryOnly" (directory only), "AddToBuiltIns" (both).
    /// Default: "AddToBuiltIns".
    /// </summary>
    public string ThemesMode { get; set; } = "AddToBuiltIns";

    /// <summary>
    /// Default theme ID to select on first load (e.g., "material-design").
    /// </summary>
    public string? DefaultThemeId { get; set; }
}
