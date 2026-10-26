namespace Elsa.Studio.Hosting.Themes;

/// <summary>Application-owned theme sources and writable storage. Relative paths use ContentRoot.</summary>
public sealed class StudioThemeStoreOptions
{
    /// <summary>Managed JSON store, independent of directory sources. Defaults to studio-theme-store.json.</summary>
    public string StoreFile { get; set; } = "studio-theme-store.json";

    /// <summary>Material-asset storage directory. Defaults to theme-assets.</summary>
    public string AssetsDirectory { get; set; } = "theme-assets";

    /// <summary>Optional JSON collection file or directory of collections.</summary>
    public string? ThemesDirectory { get; set; }

    /// <summary>BuiltInsOnly, DirectoryOnly or AddToBuiltIns (default); case-insensitive.</summary>
    public string ThemesMode { get; set; } = "AddToBuiltIns";

    /// <summary>Optional configured default, taking precedence over the managed default.</summary>
    public string? DefaultThemeId { get; set; }
}
