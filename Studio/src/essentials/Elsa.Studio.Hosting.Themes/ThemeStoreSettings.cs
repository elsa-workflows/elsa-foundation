using Microsoft.Extensions.Options;

namespace Elsa.Studio.Hosting.Themes;

internal sealed record ThemeStoreSettings(string StoreFile, string AssetsDirectory, string? ThemesDirectory, string ThemesMode, string? DefaultThemeId)
{
    public static ThemeStoreSettings Create(StudioThemeStoreOptions options, string contentRoot) => new(
        ThemeStoragePaths.Resolve(Path.GetFullPath(options.StoreFile, contentRoot)), ThemeStoragePaths.Resolve(Path.GetFullPath(options.AssetsDirectory, contentRoot)),
        string.IsNullOrWhiteSpace(options.ThemesDirectory) ? null : ThemeStoragePaths.Resolve(Path.GetFullPath(options.ThemesDirectory, contentRoot)),
        Enum.IsDefined(options.ThemesMode) ? options.ThemesMode.ToString() : throw new OptionsValidationException(Options.DefaultName, typeof(StudioThemeStoreOptions), ["Themes:ThemesMode must be BuiltInsOnly, DirectoryOnly or AddToBuiltIns."]),
        options.DefaultThemeId);

    public static void BindConfiguration(StudioThemeStoreOptions options, IConfigurationSection section)
    {
        var keys = section.GetChildren().Select(child => child.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (keys.Contains(nameof(options.StoreFile))) options.StoreFile = section[nameof(options.StoreFile)]!;
        if (keys.Contains(nameof(options.AssetsDirectory))) options.AssetsDirectory = section[nameof(options.AssetsDirectory)]!;
        if (keys.Contains(nameof(options.ThemesDirectory))) options.ThemesDirectory = section[nameof(options.ThemesDirectory)];
        if (keys.Contains(nameof(options.DefaultThemeId))) options.DefaultThemeId = section[nameof(options.DefaultThemeId)];
        // Configuration keeps its legacy named strings. Enum binding would otherwise accept numeric "0".
        if (keys.Contains(nameof(options.ThemesMode))) options.ThemesMode = ParseMode(section[nameof(options.ThemesMode)]);
    }

    private static StudioThemeSourceMode ParseMode(string? mode) => mode?.Trim().ToUpperInvariant() switch
    {
        "BUILTINSONLY" => StudioThemeSourceMode.BuiltInsOnly,
        "DIRECTORYONLY" => StudioThemeSourceMode.DirectoryOnly,
        "ADDTOBUILTINS" => StudioThemeSourceMode.AddToBuiltIns,
        _ => (StudioThemeSourceMode)(-1)
    };
}

internal sealed class ThemeOptionsValidator(string contentRoot) : IValidateOptions<StudioThemeStoreOptions>
{
    public ValidateOptionsResult Validate(string? name, StudioThemeStoreOptions options)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(options.ThemesMode))
            errors.Add("Themes:ThemesMode must be BuiltInsOnly, DirectoryOnly or AddToBuiltIns.");
        if (string.IsNullOrWhiteSpace(options.StoreFile)) errors.Add("Themes:StoreFile must be a nonblank file path.");
        if (string.IsNullOrWhiteSpace(options.AssetsDirectory)) errors.Add("Themes:AssetsDirectory must be a nonblank directory path.");
        if (errors.Count == 0)
        {
            try
            {
                var applied = ThemeStoreSettings.Create(options, contentRoot);
                if (Directory.Exists(applied.StoreFile)) errors.Add("Themes:StoreFile names an existing directory.");
                if (File.Exists(applied.AssetsDirectory)) errors.Add("Themes:AssetsDirectory names an existing file.");
                if (HasFileAncestor(applied.StoreFile)) errors.Add("Themes:StoreFile has an existing file in its containing path.");
                if (HasFileAncestor(applied.AssetsDirectory)) errors.Add("Themes:AssetsDirectory has an existing file in its containing path.");
                if (SamePath(applied.StoreFile, applied.AssetsDirectory) || applied.AssetsDirectory.StartsWith(
                        Path.TrimEndingDirectorySeparator(applied.StoreFile) + Path.DirectorySeparatorChar,
                        ThemeStoragePaths.ComparisonFor(applied.StoreFile)))
                    errors.Add("Themes:AssetsDirectory must not equal or descend from the writable StoreFile.");
                if (applied.StoreFile.StartsWith(
                        Path.EndsInDirectorySeparator(applied.AssetsDirectory) ? applied.AssetsDirectory : applied.AssetsDirectory + Path.DirectorySeparatorChar,
                        ThemeStoragePaths.ComparisonFor(applied.AssetsDirectory)))
                    errors.Add("Themes:StoreFile must be outside the writable AssetsDirectory.");
                if (applied.ThemesDirectory is { } source && SamePath(source, applied.StoreFile))
                    errors.Add("Themes:ThemesDirectory must not be the writable StoreFile.");
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                errors.Add("Themes storage contains an invalid path: " + error.Message);
            }
        }
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static bool SamePath(string first, string second) => string.Equals(first, second,
        ThemeStoragePaths.ComparisonFor(first));

    private static bool HasFileAncestor(string path)
    {
        for (var parent = Path.GetDirectoryName(path); parent is not null; parent = Path.GetDirectoryName(parent))
            if (File.Exists(parent)) return true;
        return false;
    }
}
