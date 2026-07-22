namespace Elsa.Studio.Web;

/// <summary>
/// Configuration for theme loading and defaults.
/// </summary>
public class ThemeConfiguration
{
    /// <summary>
    /// Directory path containing theme files (JSON file or .json files).
    /// Relative paths are resolved from the application's content root.
    /// Environment variable: ELSA_THEMES_DIRECTORY
    /// </summary>
    public string? ThemesDirectory { get; set; }

    /// <summary>
    /// How to combine directory themes with built-ins.
    /// Values: "BuiltInsOnly" (built-ins only), "DirectoryOnly" (directory only), "AddToBuiltIns" (both).
    /// Default: "AddToBuiltIns"
    /// Environment variable: ELSA_THEMES_MODE
    /// </summary>
    public string ThemesMode { get; set; } = "AddToBuiltIns";

    /// <summary>
    /// Default theme ID to select on first load (e.g., "material-design").
    /// Environment variable: ELSA_DEFAULT_THEME
    /// </summary>
    public string? DefaultThemeId { get; set; }
}
