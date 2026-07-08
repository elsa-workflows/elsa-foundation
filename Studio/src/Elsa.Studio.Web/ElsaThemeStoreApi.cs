using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace Elsa.Studio.Web;

internal static class ElsaThemeStoreApi
{
    private const string StoreFileName = "studio-theme-store.json";
    private const string AssetFolderName = "theme-assets";
    private const long MaxAssetSize = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Regex ThemeIdPattern = new("^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TokenValuePattern = new("^(?:#[0-9a-f]{3,8}|oklch\\(\\s*(?:0?\\.\\d+|1(?:\\.0+)?|0)\\s+\\d*\\.?\\d+\\s+\\d*\\.?\\d+\\s*\\)|rgba?\\([\\d\\s.,%/]+\\)|hsla?\\([\\d\\s.,%/]+\\)|var\\(--[a-z0-9-]+\\))$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex MaterialVariableNamePattern = new("^--studio-material-[a-z0-9-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MaterialVariableValuePattern = new("^[a-z0-9 .,%#()/+-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> AllowedAssetContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/webp",
        "image/jpeg"
    };

    public static IEndpointRouteBuilder MapElsaThemeStoreCoreApi(
        this IEndpointRouteBuilder endpoints,
        string authorizationPolicy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationPolicy);
        var group = endpoints.MapGroup("/_elsa/theme-store")
            .RequireAuthorization(authorizationPolicy);

        group.MapGet("/capabilities/core", () => Results.Ok(new ThemeFeatureCapabilityResponse(true)));
        group.MapGet("", GetThemeStoreAsync);
        group.MapGet("/assets/{id}/content", GetAssetContentAsync);

        return endpoints;
    }

    public static IEndpointRouteBuilder MapElsaThemePickerApi(
        this IEndpointRouteBuilder endpoints,
        string authorizationPolicy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationPolicy);
        var group = endpoints.MapGroup("/_elsa/theme-store")
            .RequireAuthorization(authorizationPolicy);

        group.MapGet("/capabilities/picker", () => Results.Ok(new ThemeFeatureCapabilityResponse(true)));

        return endpoints;
    }

    public static IEndpointRouteBuilder MapElsaThemeManagementApi(
        this IEndpointRouteBuilder endpoints,
        string authorizationPolicy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationPolicy);
        var group = endpoints.MapGroup("/_elsa/theme-store")
            .RequireAuthorization(authorizationPolicy);

        group.MapGet("/capabilities/management", () => Results.Ok(new ThemeFeatureCapabilityResponse(true)));
        group.MapPut("/themes/{id}", SaveThemeAsync);
        group.MapPost("/themes/{id}/duplicate", DuplicateThemeAsync);
        group.MapDelete("/themes/{id}", DeleteThemeAsync);
        group.MapPut("/default", SetDefaultThemeAsync);
        group.MapPost("/assets", UploadAssetAsync)
            .Accepts<IFormFile>("multipart/form-data")
            .DisableAntiforgery();
        group.MapDelete("/assets/{id}", DeleteAssetAsync);
        group.MapPost("/import", ImportThemePackAsync);
        group.MapGet("/export", ExportThemePackAsync);

        return endpoints;
    }

    private static async Task<IResult> GetThemeStoreAsync(
        [FromServices] IWebHostEnvironment environment,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var store = await ReadStoreAsync(environment, request, cancellationToken);
        return Results.Ok(store);
    }

    private static async Task<IResult> SaveThemeAsync(
        string id,
        [FromBody] StudioThemeDefinition theme,
        [FromServices] IWebHostEnvironment environment,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (theme is null)
            return Results.BadRequest(new ThemeStoreErrorResponse("Theme payload is required."));

        if (!StringComparer.OrdinalIgnoreCase.Equals(id, theme.Id))
            return Results.BadRequest(new ThemeStoreErrorResponse("Theme ID in the route must match the payload."));

        if (ValidateCustomTheme(theme) is { } validationError)
            return Results.BadRequest(new ThemeStoreErrorResponse(validationError));

        await ConfigFileWriter.WriteGate.WaitAsync(cancellationToken);
        try
        {
            var store = await ReadStoreAsync(environment, request, cancellationToken);
            var themes = store.Themes.Where(x => !StringComparer.OrdinalIgnoreCase.Equals(x.Id, theme.Id)).ToList();
            themes.Add(theme with { Source = "custom" });
            store = store with { Themes = themes.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray() };
            await WriteStoreAsync(environment, store, cancellationToken);
            return Results.Ok(store);
        }
        finally
        {
            ConfigFileWriter.WriteGate.Release();
        }
    }

    private static async Task<IResult> DuplicateThemeAsync(
        string id,
        [FromBody] ThemeDuplicateRequest request,
        [FromServices] IWebHostEnvironment environment,
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        await ConfigFileWriter.WriteGate.WaitAsync(cancellationToken);
        try
        {
            var store = await ReadStoreAsync(environment, httpRequest, cancellationToken);
            var source = store.Themes.FirstOrDefault(theme => StringComparer.OrdinalIgnoreCase.Equals(theme.Id, id));
            if (source is null)
                return Results.NotFound(new ThemeStoreErrorResponse($"Theme '{id}' was not found."));

            var duplicateName = request?.Name;
            var duplicateId = UniqueThemeId(Slugify(duplicateName ?? $"{source.Name} Copy"), store.Themes);
            var duplicate = source with
            {
                Id = duplicateId,
                Name = string.IsNullOrWhiteSpace(duplicateName) ? $"{source.Name} Copy" : duplicateName.Trim(),
                Source = "custom",
                Enabled = false,
                Published = false
            };
            if (ValidateCustomTheme(duplicate) is { } validationError)
                return Results.BadRequest(new ThemeStoreErrorResponse(validationError));

            store = store with { Themes = store.Themes.Concat([duplicate]).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray() };
            await WriteStoreAsync(environment, store, cancellationToken);
            return Results.Ok(duplicate);
        }
        finally
        {
            ConfigFileWriter.WriteGate.Release();
        }
    }

    private static async Task<IResult> DeleteThemeAsync(
        string id,
        [FromServices] IWebHostEnvironment environment,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        await ConfigFileWriter.WriteGate.WaitAsync(cancellationToken);
        try
        {
            var store = await ReadStoreAsync(environment, request, cancellationToken);
            var theme = store.Themes.FirstOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(x.Id, id));
            if (theme is null)
                return Results.NotFound(new ThemeStoreErrorResponse($"Theme '{id}' was not found."));
            if (theme.Source != "custom")
                return Results.BadRequest(new ThemeStoreErrorResponse("Built-in themes cannot be deleted."));

            var themes = store.Themes.Where(x => !StringComparer.OrdinalIgnoreCase.Equals(x.Id, id)).ToArray();
            var defaultThemeId = StringComparer.OrdinalIgnoreCase.Equals(store.DefaultThemeId, id) ? "" : store.DefaultThemeId;
            store = store with { Themes = themes, DefaultThemeId = defaultThemeId };
            await WriteStoreAsync(environment, store, cancellationToken);
            return Results.Ok(store);
        }
        finally
        {
            ConfigFileWriter.WriteGate.Release();
        }
    }

    private static async Task<IResult> SetDefaultThemeAsync(
        [FromBody] ThemeDefaultRequest request,
        [FromServices] IWebHostEnvironment environment,
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        await ConfigFileWriter.WriteGate.WaitAsync(cancellationToken);
        try
        {
            if (request is null || string.IsNullOrWhiteSpace(request.ThemeId))
                return Results.BadRequest(new ThemeStoreErrorResponse("Theme ID is required."));

            var store = await ReadStoreAsync(environment, httpRequest, cancellationToken);
            var theme = store.Themes.FirstOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(x.Id, request.ThemeId));
            if (theme is null)
                return Results.NotFound(new ThemeStoreErrorResponse($"Theme '{request.ThemeId}' was not found."));
            if (!theme.Enabled || !theme.Published)
                return Results.BadRequest(new ThemeStoreErrorResponse("Default theme must be published and enabled."));

            store = store with { DefaultThemeId = request.ThemeId };
            await WriteStoreAsync(environment, store, cancellationToken);
            return Results.Ok(store);
        }
        finally
        {
            ConfigFileWriter.WriteGate.Release();
        }
    }

    private static async Task<IResult> UploadAssetAsync(
        HttpRequest request,
        [FromServices] IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
            return Results.BadRequest(new ThemeStoreErrorResponse("Expected multipart form data."));

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("asset") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
            return Results.BadRequest(new ThemeStoreErrorResponse("Choose a non-empty texture asset."));
        if (file.Length > MaxAssetSize)
            return Results.BadRequest(new ThemeStoreErrorResponse("Texture assets must be 2 MB or smaller."));
        if (!AllowedAssetContentTypes.Contains(file.ContentType))
            return Results.BadRequest(new ThemeStoreErrorResponse("Texture assets must be PNG, WebP, or JPEG."));

        var extension = file.ContentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase)
            ? ".webp"
            : file.ContentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
                ? ".jpg"
                : ".png";
        var id = $"{Guid.NewGuid():N}{extension}";
        var folder = GetAssetFolder(environment);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, id);
        await using (var output = File.Create(path))
            await file.CopyToAsync(output, cancellationToken);

        var asset = new ThemeStoreAsset(id, Path.GetFileName(file.FileName), file.ContentType, file.Length, AssetUrl(request, id));
        return Results.Ok(asset);
    }

    private static IResult GetAssetContentAsync(string id, [FromServices] IWebHostEnvironment environment)
    {
        var safeId = Path.GetFileName(id);
        var path = Path.Combine(GetAssetFolder(environment), safeId);
        if (!File.Exists(path))
            return Results.NotFound(new ThemeStoreErrorResponse($"Asset '{safeId}' was not found."));

        var contentType = Path.GetExtension(safeId).ToLowerInvariant() switch
        {
            ".webp" => "image/webp",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "image/png"
        };
        return Results.File(path, contentType);
    }

    private static async Task<IResult> DeleteAssetAsync(
        string id,
        [FromServices] IWebHostEnvironment environment,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var safeId = Path.GetFileName(id);
        var path = Path.Combine(GetAssetFolder(environment), safeId);
        if (File.Exists(path))
            File.Delete(path);

        return Results.Ok(await ReadStoreAsync(environment, request, cancellationToken));
    }

    private static async Task<IResult> ImportThemePackAsync(
        [FromBody] ThemePack pack,
        [FromServices] IWebHostEnvironment environment,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (pack is null)
            return Results.BadRequest(new ThemeStoreErrorResponse("Theme pack payload is required."));
        if (pack.Version < 1)
            return Results.BadRequest(new ThemeStoreErrorResponse("Theme pack version is required."));

        var themes = pack.Themes ?? [];
        foreach (var theme in themes)
        {
            if (theme is null)
                return Results.BadRequest(new ThemeStoreErrorResponse("Theme pack contains an empty theme."));
            if (ValidateCustomTheme(theme with { Source = "custom" }) is { } validationError)
                return Results.BadRequest(new ThemeStoreErrorResponse(validationError));
        }

        await ConfigFileWriter.WriteGate.WaitAsync(cancellationToken);
        try
        {
            var store = await ReadStoreAsync(environment, request, cancellationToken);
            var imported = themes.Select(theme => theme with { Source = "custom" }).ToArray();
            var importedIds = imported.Select(theme => theme.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            store = store with
            {
                Themes = store.Themes.Where(theme => !importedIds.Contains(theme.Id)).Concat(imported).OrderBy(theme => theme.Name, StringComparer.OrdinalIgnoreCase).ToArray()
            };
            await WriteStoreAsync(environment, store, cancellationToken);
            return Results.Ok(store);
        }
        finally
        {
            ConfigFileWriter.WriteGate.Release();
        }
    }

    private static async Task<IResult> ExportThemePackAsync(
        [FromServices] IWebHostEnvironment environment,
        HttpRequest request,
        string? themeId,
        CancellationToken cancellationToken)
    {
        var store = await ReadStoreAsync(environment, request, cancellationToken);
        var themes = string.IsNullOrWhiteSpace(themeId)
            ? store.Themes
            : store.Themes.Where(theme => StringComparer.OrdinalIgnoreCase.Equals(theme.Id, themeId)).ToArray();
        return Results.Ok(new ThemePack(1, themes, store.Assets));
    }

    private static async Task<ThemeStoreResponse> ReadStoreAsync(IWebHostEnvironment environment, HttpRequest request, CancellationToken cancellationToken)
    {
        var path = GetStorePath(environment);
        var store = File.Exists(path)
            ? JsonSerializer.Deserialize<ThemeStoreResponse>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions) ?? EmptyStore()
            : EmptyStore();

        return store with { Assets = ListAssets(environment, request) };
    }

    private static Task WriteStoreAsync(IWebHostEnvironment environment, ThemeStoreResponse store, CancellationToken cancellationToken)
    {
        var persisted = store with { Assets = [] };
        return ConfigFileWriter.WriteAtomicAsync(GetStorePath(environment), JsonSerializer.Serialize(persisted, JsonOptions) + Environment.NewLine, cancellationToken);
    }

    private static ThemeStoreResponse EmptyStore() => new([], "", []);

    private static ThemeStoreAsset[] ListAssets(IWebHostEnvironment environment, HttpRequest request)
    {
        var folder = GetAssetFolder(environment);
        if (!Directory.Exists(folder))
            return [];

        return Directory.EnumerateFiles(folder)
            .Select(path =>
            {
                var file = new FileInfo(path);
                var contentType = file.Extension.ToLowerInvariant() switch
                {
                    ".webp" => "image/webp",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    _ => "image/png"
                };
                return new ThemeStoreAsset(file.Name, file.Name, contentType, file.Length, AssetUrl(request, file.Name));
            })
            .OrderBy(asset => asset.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ValidateCustomTheme(StudioThemeDefinition? theme)
    {
        if (theme is null)
            return "Theme payload is required.";
        if (theme.Source != "custom")
            return "Only custom themes can be saved in the Studio Theme Store.";
        if (string.IsNullOrWhiteSpace(theme.Id) || !ThemeIdPattern.IsMatch(theme.Id))
            return "Theme ID must be kebab-case and between 3 and 64 characters.";
        if (string.IsNullOrWhiteSpace(theme.Name))
            return "Theme name is required.";
        if (theme.Modes is null)
            return "Theme modes are required.";
        if (theme.Modes.Light is null)
            return "Theme light mode is required.";
        if (theme.Modes.Dark is null)
            return "Theme dark mode is required.";

        foreach (var (modeName, mode) in new[] { ("light", theme.Modes.Light), ("dark", theme.Modes.Dark) })
        {
            if (mode.ChartColors is null || mode.ChartColors.Length != 5)
                return $"{modeName}.ChartColors must contain five validated token values.";

            foreach (var token in mode.TokenValues())
            {
                if (!IsAllowedTokenValue(token.Value))
                    return $"{modeName}.{token.Key} must be a validated token value.";
            }

            if (ValidateMaterial(modeName, mode.Material) is { } materialError)
                return materialError;
        }

        return null;
    }

    private static bool IsAllowedTokenValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return TokenValuePattern.IsMatch(value);
    }

    private static string? ValidateMaterial(string modeName, StudioThemeModeMaterial? material)
    {
        if (material is null)
            return null;

        if (material.TextureSize is < 32 or > 1024)
            return $"{modeName}.Material.TextureSize must be between 32 and 1024 pixels.";

        foreach (var (name, value) in material.CssVariables ?? [])
        {
            if (!MaterialVariableNamePattern.IsMatch(name))
                return $"{modeName}.Material.CssVariables contains an unsupported material variable name.";
            if (string.IsNullOrWhiteSpace(value) || !MaterialVariableValuePattern.IsMatch(value))
                return $"{modeName}.Material.CssVariables contains an unsupported material variable value.";
        }

        return null;
    }

    private static string UniqueThemeId(string baseId, IReadOnlyCollection<StudioThemeDefinition> themes)
    {
        var id = string.IsNullOrWhiteSpace(baseId) ? "custom-theme" : baseId;
        var candidate = id;
        var index = 2;
        while (themes.Any(theme => StringComparer.OrdinalIgnoreCase.Equals(theme.Id, candidate)))
            candidate = $"{id}-{index++}";
        return candidate;
    }

    private static string Slugify(string value)
    {
        var chars = value.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string AssetUrl(HttpRequest request, string id) => $"{request.Scheme}://{request.Host}/_elsa/theme-store/assets/{Uri.EscapeDataString(id)}/content";

    private static string GetStorePath(IWebHostEnvironment environment) => Path.Combine(environment.ContentRootPath, StoreFileName);

    private static string GetAssetFolder(IWebHostEnvironment environment) => Path.Combine(environment.ContentRootPath, AssetFolderName);
}

internal sealed record ThemeStoreResponse(StudioThemeDefinition[] Themes, string DefaultThemeId, ThemeStoreAsset[] Assets);
internal sealed record ThemeStoreAsset(string Id, string FileName, string ContentType, long Size, string Url);
internal sealed record ThemeStoreErrorResponse(string Message);
internal sealed record ThemeFeatureCapabilityResponse(bool Enabled);
internal sealed record ThemeDuplicateRequest(string? Name);
internal sealed record ThemeDefaultRequest(string ThemeId);
internal sealed record ThemePack(int Version, StudioThemeDefinition[] Themes, ThemeStoreAsset[]? Assets);

internal sealed record StudioThemeDefinition(
    string Id,
    string Name,
    string? Description,
    string Source,
    int Version,
    bool Enabled,
    bool Published,
    StudioThemeModes Modes,
    StudioThemeMaterial? Material);

internal sealed record StudioThemeModes(StudioThemeModeDefinition Light, StudioThemeModeDefinition Dark);
internal sealed record StudioThemeMaterial(Dictionary<string, string>? TextureAssets, Dictionary<string, string>? CssVariables);

internal sealed record StudioThemeModeDefinition(
    string Primary,
    string PrimaryForeground,
    string Secondary,
    string SecondaryForeground,
    string Accent,
    string AccentForeground,
    string Success,
    string SuccessForeground,
    string Warning,
    string WarningForeground,
    string Danger,
    string DangerForeground,
    string Background,
    string Foreground,
    string Card,
    string CardForeground,
    string Muted,
    string MutedForeground,
    string Border,
    string Input,
    string Sidebar,
    string SidebarForeground,
    string SidebarActive,
    string SidebarActiveForeground,
    string Ring,
    string[] ChartColors,
    StudioThemeModeMaterial? Material)
{
    public IEnumerable<KeyValuePair<string, string?>> TokenValues()
    {
        yield return new(nameof(Primary), Primary);
        yield return new(nameof(PrimaryForeground), PrimaryForeground);
        yield return new(nameof(Secondary), Secondary);
        yield return new(nameof(SecondaryForeground), SecondaryForeground);
        yield return new(nameof(Accent), Accent);
        yield return new(nameof(AccentForeground), AccentForeground);
        yield return new(nameof(Success), Success);
        yield return new(nameof(SuccessForeground), SuccessForeground);
        yield return new(nameof(Warning), Warning);
        yield return new(nameof(WarningForeground), WarningForeground);
        yield return new(nameof(Danger), Danger);
        yield return new(nameof(DangerForeground), DangerForeground);
        yield return new(nameof(Background), Background);
        yield return new(nameof(Foreground), Foreground);
        yield return new(nameof(Card), Card);
        yield return new(nameof(CardForeground), CardForeground);
        yield return new(nameof(Muted), Muted);
        yield return new(nameof(MutedForeground), MutedForeground);
        yield return new(nameof(Border), Border);
        yield return new(nameof(Input), Input);
        yield return new(nameof(Sidebar), Sidebar);
        yield return new(nameof(SidebarForeground), SidebarForeground);
        yield return new(nameof(SidebarActive), SidebarActive);
        yield return new(nameof(SidebarActiveForeground), SidebarActiveForeground);
        yield return new(nameof(Ring), Ring);
        foreach (var chartColor in ChartColors ?? [])
            yield return new(nameof(ChartColors), chartColor);
    }
}

internal sealed record StudioThemeModeMaterial(Dictionary<string, string>? TextureAssets, Dictionary<string, string>? CssVariables, int? TextureSize);
