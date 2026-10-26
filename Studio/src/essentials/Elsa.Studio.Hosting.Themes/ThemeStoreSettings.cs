using Microsoft.Extensions.Options;

namespace Elsa.Studio.Hosting.Themes;

internal sealed record ThemeStoreSettings(string StoreFile, string AssetsDirectory, string? ThemesDirectory, string ThemesMode, string? DefaultThemeId)
{
    public static ThemeStoreSettings Create(StudioThemeStoreOptions options, string contentRoot) => new(
        Path.GetFullPath(options.StoreFile, contentRoot), Path.GetFullPath(options.AssetsDirectory, contentRoot),
        string.IsNullOrWhiteSpace(options.ThemesDirectory) ? null : Path.GetFullPath(options.ThemesDirectory, contentRoot),
        NormalizeMode(options.ThemesMode) ?? throw new OptionsValidationException(Options.DefaultName, typeof(StudioThemeStoreOptions), ["Themes:ThemesMode must be BuiltInsOnly, DirectoryOnly or AddToBuiltIns."]),
        options.DefaultThemeId);

    public static string? NormalizeMode(string? mode) => mode?.Trim().ToUpperInvariant() switch
    {
        "BUILTINSONLY" => "BuiltInsOnly",
        "DIRECTORYONLY" => "DirectoryOnly",
        "ADDTOBUILTINS" => "AddToBuiltIns",
        _ => null
    };
}

internal sealed class ThemeOptionsValidator(string contentRoot) : IValidateOptions<StudioThemeStoreOptions>
{
    public ValidateOptionsResult Validate(string? name, StudioThemeStoreOptions options)
    {
        var errors = new List<string>();
        if (ThemeStoreSettings.NormalizeMode(options.ThemesMode) is null)
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
                if (applied.ThemesDirectory is { } source && SamePath(source, applied.StoreFile))
                    errors.Add("Themes:ThemesDirectory must not be the writable StoreFile.");
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            {
                errors.Add("Themes storage contains an invalid path: " + error.Message);
            }
        }
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static bool SamePath(string first, string second) => string.Equals(first, second,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
