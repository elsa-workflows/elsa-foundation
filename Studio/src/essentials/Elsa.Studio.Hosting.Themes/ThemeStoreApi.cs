using System.Text.Json;
using Elsa.Studio.Hosting;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace Elsa.Studio.Hosting.Themes;

internal static class ThemeStoreApi
{
    private const long MaxAssetSize = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Regex ThemeIdPattern = new("^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex AssetIdPattern = new("^[a-f0-9]{32}\\.(?:png|webp|jpe?g)$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex TokenValuePattern = new("^(?:#[0-9a-f]{3,8}|oklch\\(\\s*(?:0?\\.\\d+|1(?:\\.0+)?|0)\\s+\\d*\\.?\\d+\\s+\\d*\\.?\\d+\\s*\\)|rgba?\\([\\d\\s.,%/]+\\)|hsla?\\([\\d\\s.,%/]+\\)|var\\(--[a-z0-9-]+\\))$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex MaterialVariableNamePattern = new("^--studio-material-[a-z0-9-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MaterialVariableValuePattern = new("^[a-z0-9 .,%#()/+-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    // Mirrors isAllowedFontStack / isAllowedRadius in the client's themeStoreApi.ts, which also
    // match against the trimmed value — keep the two in step or a value valid in the Theme Builder 400s here.
    private static readonly Regex FontStackPattern = new("^[a-z0-9\\s,'\"._-]{1,512}$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex RadiusPattern = new("^(?:0|\\d{1,3}(?:\\.\\d+)?(?:px|rem))$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly string[] ThemeModeNames = ["light", "dark", "dim", "high-contrast"];
    // Mirrors studioThemeLayouts in the SDK's themeLayout.ts.
    private static readonly string[] ThemeLayoutNames = ["classic", "floating", "workbench", "editorial"];
    private static readonly string[] WorkspaceStyleNames = ["linen", "minimal", "quiet", "focus", "open"];
    internal static readonly HashSet<string> BuiltInThemeIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "linen",
        "builtin:porcelain",
        "builtin:atelier",
        "builtin:graphite",
        "classic",
        "meridian",
        "drift",
        "schematic",
        "atelier",
        "elsa-cloud",
        "signal",
        "dusk"
    };
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
        group.MapPut("/themes/{id}/visibility", SetThemeVisibilityAsync);
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
        [FromServices] ThemeStoreSettings environment,
        [FromServices] ThemeDirectorySource themeService,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var snapshot = await ReadStoreAsync(environment, themeService, request, cancellationToken);
        var store = snapshot.View;
        return Results.Ok(ToResponse(store));
    }

    private static async Task<IResult> SaveThemeAsync(
        string id,
        [FromBody] StudioThemeDefinition theme,
        [FromServices] ThemeStoreSettings environment,
        [FromServices] ThemeDirectorySource themeService,
        HttpRequest request,
        [FromServices] StudioConfigurationMutationGate mutationGate,
        CancellationToken cancellationToken)
    {
        if (theme is null)
            return Results.BadRequest(new ThemeStoreErrorResponse("Theme payload is required."));

        if (!StringComparer.OrdinalIgnoreCase.Equals(id, theme.Id))
            return Results.BadRequest(new ThemeStoreErrorResponse("Theme ID in the route must match the payload."));

        if (ValidateCustomTheme(theme) is { } validationError)
            return Results.BadRequest(new ThemeStoreErrorResponse(validationError));

        await using var mutation = await mutationGate.AcquireAsync(cancellationToken);
        var snapshot = await ReadStoreAsync(environment, themeService, request, cancellationToken);
        var store = snapshot.View;
        var themes = (snapshot.Managed.Themes ?? []).Where(x => !StringComparer.OrdinalIgnoreCase.Equals(x.Id, theme.Id)).ToList();
        themes.Add(theme with { Source = "custom" });
        var managed = snapshot.Managed with { Themes = themes.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray() };
        await WriteStoreAsync(environment, managed, cancellationToken);
        return Results.Ok(ToResponse((await ReadStoreAsync(environment, themeService, request, cancellationToken)).View));
    }

    /// <summary>
    /// Toggles a BUILT-IN theme's visibility in the theme picker. Built-in theme definitions stay
    /// read-only seeds (they cannot be edited or deleted), but admins can hide them from the picker
    /// dropdown by recording the id in the store's disabled list. Custom themes keep using the
    /// regular save endpoint's <c>Enabled</c> flag.
    /// </summary>
    private static async Task<IResult> SetThemeVisibilityAsync(
        string id,
        [FromBody] ThemeVisibilityRequest request,
        [FromServices] ThemeStoreSettings environment,
        [FromServices] ThemeDirectorySource themeService,
        HttpRequest httpRequest,
        [FromServices] StudioConfigurationMutationGate mutationGate,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return Results.BadRequest(new ThemeStoreErrorResponse("Visibility payload is required."));
        if (!BuiltInThemeIds.Contains(id))
            return Results.BadRequest(new ThemeStoreErrorResponse("Only built-in themes use the visibility endpoint. Save custom themes with their enabled flag instead."));

        await using var mutation = await mutationGate.AcquireAsync(cancellationToken);
        var snapshot = await ReadStoreAsync(environment, themeService, httpRequest, cancellationToken);
        var store = snapshot.View;
        var disabled = new HashSet<string>(store.DisabledBuiltInThemeIds ?? [], StringComparer.OrdinalIgnoreCase);

        if (request.Enabled)
        {
            disabled.Remove(id);
        }
        else
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(store.DefaultThemeId, id))
                return Results.BadRequest(new ThemeStoreErrorResponse("The default theme cannot be disabled. Pick another default theme first."));

            disabled.Add(id);
            var enabledBuiltInCount = BuiltInThemeIds.Count(builtIn => IsSourceAllowed(store, builtIn, true) && !disabled.Contains(builtIn));
            var enabledCustomCount = store.Themes.Count(theme => theme.Source == "custom" && theme is { Enabled: true, Published: true } && IsSourceAllowed(store, theme.Id, false));
            if (IsSourceAllowed(store, id, true) && enabledBuiltInCount + enabledCustomCount == 0)
                return Results.BadRequest(new ThemeStoreErrorResponse("At least one selectable theme must remain enabled."));
        }

        var managed = snapshot.Managed with { DisabledBuiltInThemeIds = disabled.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray() };
        await WriteStoreAsync(environment, managed, cancellationToken);
        return Results.Ok(ToResponse((await ReadStoreAsync(environment, themeService, httpRequest, cancellationToken)).View));
    }

    private static async Task<IResult> DuplicateThemeAsync(
        string id,
        [FromBody] ThemeDuplicateRequest request,
        [FromServices] ThemeStoreSettings environment,
        [FromServices] ThemeDirectorySource themeService,
        HttpRequest httpRequest,
        [FromServices] StudioConfigurationMutationGate mutationGate,
        CancellationToken cancellationToken)
    {
        await using var mutation = await mutationGate.AcquireAsync(cancellationToken);
        var snapshot = await ReadStoreAsync(environment, themeService, httpRequest, cancellationToken);
        var store = snapshot.View;
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

        var managed = snapshot.Managed with { Themes = (snapshot.Managed.Themes ?? []).Concat([duplicate]).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray() };
        await WriteStoreAsync(environment, managed, cancellationToken);
        return Results.Ok(duplicate);
    }

    private static async Task<IResult> DeleteThemeAsync(
        string id,
        [FromServices] ThemeStoreSettings environment,
        [FromServices] ThemeDirectorySource themeService,
        HttpRequest request,
        [FromServices] StudioConfigurationMutationGate mutationGate,
        CancellationToken cancellationToken)
    {
        await using var mutation = await mutationGate.AcquireAsync(cancellationToken);
        var snapshot = await ReadStoreAsync(environment, themeService, request, cancellationToken);
        var store = snapshot.View;
        var theme = store.Themes.FirstOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(x.Id, id));
        if (theme is null)
            return Results.NotFound(new ThemeStoreErrorResponse($"Theme '{id}' was not found."));
        if (theme.Source != "custom")
            return Results.BadRequest(new ThemeStoreErrorResponse("Built-in themes cannot be deleted."));

        if ((store.DirectoryThemeIds ?? []).Contains(id, StringComparer.OrdinalIgnoreCase))
            return Results.Conflict(new ThemeStoreErrorResponse("This theme is supplied by the configured directory. Remove it from that source before deleting managed state."));
        var themes = (snapshot.Managed.Themes ?? []).Where(x => !StringComparer.OrdinalIgnoreCase.Equals(x.Id, id)).ToArray();
        var defaultThemeId = StringComparer.OrdinalIgnoreCase.Equals(snapshot.Managed.DefaultThemeId, id) ? "" : snapshot.Managed.DefaultThemeId;
        var managed = snapshot.Managed with { Themes = themes, DefaultThemeId = defaultThemeId };
        await WriteStoreAsync(environment, managed, cancellationToken);
        return Results.Ok(ToResponse((await ReadStoreAsync(environment, themeService, request, cancellationToken)).View));
    }

    private static async Task<IResult> SetDefaultThemeAsync(
        [FromBody] ThemeDefaultRequest request,
        [FromServices] ThemeStoreSettings environment,
        [FromServices] ThemeDirectorySource themeService,
        HttpRequest httpRequest,
        [FromServices] StudioConfigurationMutationGate mutationGate,
        CancellationToken cancellationToken)
    {
        await using var mutation = await mutationGate.AcquireAsync(cancellationToken);
        if (request is null || string.IsNullOrWhiteSpace(request.ThemeId))
            return Results.BadRequest(new ThemeStoreErrorResponse("Theme ID is required."));
        if (!BuiltInThemeIds.Contains(request.ThemeId) && !ThemeIdPattern.IsMatch(request.ThemeId))
            return Results.BadRequest(new ThemeStoreErrorResponse("Theme ID must be kebab-case and between 3 and 64 characters."));

        var snapshot = await ReadStoreAsync(environment, themeService, httpRequest, cancellationToken);
        var store = snapshot.View;
        var theme = store.Themes.FirstOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(x.Id, request.ThemeId));
        if (theme is null && !BuiltInThemeIds.Contains(request.ThemeId))
            return Results.NotFound(new ThemeStoreErrorResponse($"Theme '{request.ThemeId}' was not found."));
        if (!IsSourceAllowed(store, request.ThemeId, theme is null))
            return Results.BadRequest(new ThemeStoreErrorResponse("Default theme must be available under the configured theme source mode."));
        if (theme is not null && (!theme.Enabled || !theme.Published))
            return Results.BadRequest(new ThemeStoreErrorResponse("Default theme must be published and enabled."));
        if (theme is null && (store.DisabledBuiltInThemeIds ?? []).Contains(request.ThemeId, StringComparer.OrdinalIgnoreCase))
            return Results.BadRequest(new ThemeStoreErrorResponse("Default theme must be enabled. Re-enable the built-in theme first."));

        var managed = snapshot.Managed with { DefaultThemeId = request.ThemeId };
        await WriteStoreAsync(environment, managed, cancellationToken);
        return Results.Ok(ToResponse((await ReadStoreAsync(environment, themeService, httpRequest, cancellationToken)).View));
    }

    private static async Task<IResult> UploadAssetAsync(
        HttpRequest request,
        [FromServices] ThemeStoreSettings environment,
        [FromServices] StudioConfigurationMutationGate mutationGate,
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
        var temporary = Path.Combine(folder, $".{id}.{Guid.NewGuid():N}.upload");
        try
        {
            // Incomplete uploads stay outside the published asset inventory. Only the final rename holds the gate.
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                await file.CopyToAsync(output, cancellationToken);
                if (output.Length is 0 or > MaxAssetSize)
                    return Results.BadRequest(new ThemeStoreErrorResponse("Texture assets must be non-empty and 2 MB or smaller."));
            }
            await using var mutation = await mutationGate.AcquireAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path);
            var asset = new ThemeStoreAsset(id, Path.GetFileName(file.FileName), file.ContentType, new FileInfo(path).Length, AssetUrl(request, id));
            return Results.Ok(asset);
        }
        finally { File.Delete(temporary); }
    }

    private static async Task<IResult> GetAssetContentAsync(string id, [FromServices] ThemeStoreSettings environment,
        [FromServices] ThemeDirectorySource themeService,
        [FromServices] StudioConfigurationMutationGate mutationGate, CancellationToken cancellationToken)
    {
        if (!AssetIdPattern.IsMatch(id)) return Results.BadRequest(new ThemeStoreErrorResponse("A valid material-asset ID is required."));
        await using var mutation = await mutationGate.AcquireAsync(cancellationToken);
        var path = Path.Combine(GetAssetFolder(environment), id);
        if (themeService.IsSourceFile(path)) return Results.NotFound(new ThemeStoreErrorResponse($"Asset '{id}' was not found."));
        try
        {
            // Own the opened file across result execution, so a concurrent unlink cannot replace the bytes being served.
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, FileOptions.Asynchronous);
            return Results.Stream(stream, AssetContentType(id));
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return Results.NotFound(new ThemeStoreErrorResponse($"Asset '{id}' was not found."));
        }
    }

    private static async Task<IResult> DeleteAssetAsync(
        string id,
        [FromServices] ThemeStoreSettings environment,
        [FromServices] ThemeDirectorySource themeService,
        HttpRequest request,
        [FromServices] StudioConfigurationMutationGate mutationGate,
        CancellationToken cancellationToken)
    {
        if (!AssetIdPattern.IsMatch(id)) return Results.BadRequest(new ThemeStoreErrorResponse("A valid material-asset ID is required."));
        await using var mutation = await mutationGate.AcquireAsync(cancellationToken);
        var path = Path.Combine(GetAssetFolder(environment), id);
        if (themeService.IsSourceFile(path)) return Results.NotFound(new ThemeStoreErrorResponse($"Asset '{id}' was not found."));
        if (File.Exists(path))
            File.Delete(path);

        return Results.Ok(ToResponse((await ReadStoreAsync(environment, themeService, request, cancellationToken)).View));
    }

    private static async Task<IResult> ImportThemePackAsync(
        [FromBody] ThemePack pack,
        [FromServices] ThemeStoreSettings environment,
        [FromServices] ThemeDirectorySource themeService,
        HttpRequest request,
        [FromServices] StudioConfigurationMutationGate mutationGate,
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

        await using var mutation = await mutationGate.AcquireAsync(cancellationToken);
        var snapshot = await ReadStoreAsync(environment, themeService, request, cancellationToken);
        var store = snapshot.View;
        var imported = themes.Select(theme => theme with { Source = "custom" }).ToArray();
        var importedIds = imported.Select(theme => theme.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var managed = snapshot.Managed with
        {
            Themes = (snapshot.Managed.Themes ?? []).Where(theme => !importedIds.Contains(theme.Id)).Concat(imported).OrderBy(theme => theme.Name, StringComparer.OrdinalIgnoreCase).ToArray()
        };
        await WriteStoreAsync(environment, managed, cancellationToken);
        return Results.Ok(ToResponse((await ReadStoreAsync(environment, themeService, request, cancellationToken)).View));
    }

    private static async Task<IResult> ExportThemePackAsync(
        [FromServices] ThemeStoreSettings environment,
        [FromServices] ThemeDirectorySource themeService,
        HttpRequest request,
        string? themeId,
        CancellationToken cancellationToken)
    {
        var snapshot = await ReadStoreAsync(environment, themeService, request, cancellationToken);
        var store = snapshot.View;
        var themes = string.IsNullOrWhiteSpace(themeId)
            ? store.Themes
            : store.Themes.Where(theme => StringComparer.OrdinalIgnoreCase.Equals(theme.Id, themeId)).ToArray();
        return Results.Ok(new ThemePack(1, themes, store.Assets));
    }

    private static async Task<ThemeReadSnapshot> ReadStoreAsync(
        ThemeStoreSettings environment,
        ThemeDirectorySource themeService,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var path = GetStorePath(environment);
        var store = File.Exists(path)
            ? await ReadManagedStateAsync(path, cancellationToken)
            : EmptyStore();

        // Load custom themes from configured directory if available
        var configResult = await themeService.LoadThemesAsync(cancellationToken);
        var persistedThemes = store.Themes ?? [];
        var customThemes = persistedThemes
            .Where(t => t.Source == "custom" && !BuiltInThemeIds.Contains(t.Id))
            .ToList();

        // Merge directory themes with persisted themes (directory themes take precedence by ID).
        // Reserved built-in IDs are dropped earlier in ThemeDirectorySource with a warning.
        var directoryThemeIds = new HashSet<string>(configResult.Themes.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
        customThemes = customThemes.Where(t => !directoryThemeIds.Contains(t.Id)).Concat(configResult.Themes).ToList();

        // Use configured default if available
        var defaultThemeId = !string.IsNullOrWhiteSpace(configResult.DefaultThemeId)
            ? configResult.DefaultThemeId
            : store.DefaultThemeId ?? "";

        // A default naming a retired built-in or a deleted theme is reported as unset, like the retired
        // disabled-built-in ids below, rather than as a default the client silently falls back from.
        if (!BuiltInThemeIds.Contains(defaultThemeId)
            && !customThemes.Any(t => StringComparer.OrdinalIgnoreCase.Equals(t.Id, defaultThemeId)))
            defaultThemeId = "";

        return new ThemeReadSnapshot(store, new ThemeStoreResponse(
            Themes: customThemes.ToArray(),
            DefaultThemeId: defaultThemeId,
            Assets: ListAssets(environment, themeService, request),
            DisabledBuiltInThemeIds: (store.DisabledBuiltInThemeIds ?? [])
                .Where(BuiltInThemeIds.Contains)
                .ToArray(),
            ThemesMode: configResult.ThemesMode,
            DirectoryThemeIds: directoryThemeIds.ToArray()
        ));
    }

    private static Task WriteStoreAsync(ThemeStoreSettings environment, ManagedThemeState store, CancellationToken cancellationToken) =>
        AtomicConfigWriter.WriteAtomicAsync(GetStorePath(environment), JsonSerializer.Serialize(store, JsonOptions) + Environment.NewLine, cancellationToken);

    private static async Task<ManagedThemeState> ReadManagedStateAsync(string path, CancellationToken cancellationToken)
    {
        // A reader retains its original complete document while an owned atomic writer replaces the pathname.
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<ManagedThemeState>(input, JsonOptions, cancellationToken) ?? EmptyStore();
    }

    private static ManagedThemeState EmptyStore() => new([], "", []);

    private static bool IsSourceAllowed(ThemeStoreResponse store, string themeId, bool builtIn) => store.ThemesMode switch
    {
        "BuiltInsOnly" => builtIn,
        "DirectoryOnly" => !builtIn && (store.DirectoryThemeIds ?? []).Contains(themeId, StringComparer.OrdinalIgnoreCase),
        _ => true
    };

    private static ThemeStoreResponse ToResponse(ThemeStoreResponse store)
    {
        var custom = store.Themes.FirstOrDefault(theme => StringComparer.OrdinalIgnoreCase.Equals(theme.Id, store.DefaultThemeId));
        var available = custom is not null
            ? IsSourceAllowed(store, custom.Id, false) && custom.Enabled && custom.Published
            : IsSourceAllowed(store, store.DefaultThemeId, true) && BuiltInThemeIds.Contains(store.DefaultThemeId)
                && !(store.DisabledBuiltInThemeIds ?? []).Contains(store.DefaultThemeId, StringComparer.OrdinalIgnoreCase);
        return available ? store : store with { DefaultThemeId = "" };
    }

    private static ThemeStoreAsset[] ListAssets(ThemeStoreSettings environment, ThemeDirectorySource themeService, HttpRequest request)
    {
        var folder = GetAssetFolder(environment);
        if (!Directory.Exists(folder))
            return [];

        return Directory.EnumerateFiles(folder)
            .Where(path => AssetIdPattern.IsMatch(Path.GetFileName(path)))
            .Where(path => !themeService.IsSourceFile(path))
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

    private static string AssetContentType(string id) => Path.GetExtension(id).ToLowerInvariant() switch
    {
        ".webp" => "image/webp",
        ".jpg" or ".jpeg" => "image/jpeg",
        _ => "image/png"
    };

    private static string? ValidateCustomTheme(StudioThemeDefinition? theme)
    {
        if (theme is null)
            return "Theme payload is required.";
        if (theme.Source != "custom")
            return "Only custom themes can be saved in the Studio Theme Store.";
        if (string.IsNullOrWhiteSpace(theme.Id) || !ThemeIdPattern.IsMatch(theme.Id))
            return "Theme ID must be kebab-case and between 3 and 64 characters.";
        if (BuiltInThemeIds.Contains(theme.Id))
            return "Theme ID is reserved for a built-in theme.";
        if (string.IsNullOrWhiteSpace(theme.Name))
            return "Theme name is required.";
        if (theme.Modes is null)
            return "Theme modes are required.";
        if (theme.Modes.Light is null)
            return "Theme light mode is required.";
        if (theme.Modes.Dark is null)
            return "Theme dark mode is required.";

        foreach (var modeName in theme.SupportedModes ?? [])
        {
            if (!ThemeModeNames.Contains(modeName))
                return "Theme supported modes must be light, dark, dim or high-contrast.";
            if (theme.Modes.ForMode(modeName) is null)
                return $"Theme supports {modeName} mode but does not define it.";
        }

        foreach (var (modeName, mode) in theme.Modes.Defined())
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

        foreach (var (name, value) in theme.Typography?.Values() ?? [])
        {
            if (value is not null && !FontStackPattern.IsMatch(value.Trim()))
                return $"Typography.{name} may only contain font family names separated by commas.";
        }

        if (theme.Layout is not null && !ThemeLayoutNames.Contains(theme.Layout))
            return "Theme layout must be classic, floating, workbench or editorial.";
        if (theme.WorkspaceStyle is not null && !WorkspaceStyleNames.Contains(theme.WorkspaceStyle))
            return "Theme workspace style must be linen, minimal, quiet, focus or open.";

        foreach (var (name, value) in theme.Shape?.Values() ?? [])
        {
            if (value is not null && !RadiusPattern.IsMatch(value.Trim()))
                return $"Shape.{name} must be a length in px or rem.";
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

    private static string GetStorePath(ThemeStoreSettings environment) => environment.StoreFile;

    private static string GetAssetFolder(ThemeStoreSettings environment) => environment.AssetsDirectory;
}

internal sealed record ThemeStoreResponse(
    StudioThemeDefinition[] Themes,
    string DefaultThemeId,
    ThemeStoreAsset[] Assets,
    string[]? DisabledBuiltInThemeIds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ThemesMode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? DirectoryThemeIds = null);
internal sealed record ThemeStoreAsset(string Id, string FileName, string ContentType, long Size, string Url);
internal sealed record ThemeStoreErrorResponse(string Message);
internal sealed record ThemeFeatureCapabilityResponse(bool Enabled);
internal sealed record ThemeDuplicateRequest(string? Name);
internal sealed record ThemeVisibilityRequest(bool Enabled);
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
    StudioThemeMaterial? Material,
    string[]? SupportedModes = null,
    StudioThemeTypography? Typography = null,
    StudioThemeShape? Shape = null,
    string? Layout = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkspaceStyle { get; init; }
}

/// <summary>A theme's palettes. Light and dark are required; dim and high contrast are optional.</summary>
internal sealed record StudioThemeModes(
    StudioThemeModeDefinition Light,
    StudioThemeModeDefinition Dark,
    StudioThemeModeDefinition? Dim = null,
    StudioThemeModeDefinition? HighContrast = null)
{
    public StudioThemeModeDefinition? ForMode(string mode) => mode switch
    {
        "light" => Light,
        "dark" => Dark,
        "dim" => Dim,
        "high-contrast" => HighContrast,
        _ => null
    };

    public IEnumerable<(string Name, StudioThemeModeDefinition Mode)> Defined()
    {
        yield return ("light", Light);
        yield return ("dark", Dark);
        if (Dim is not null)
            yield return ("dim", Dim);
        if (HighContrast is not null)
            yield return ("highContrast", HighContrast);
    }
}

internal sealed record StudioThemeTypography(string? Sans, string? Mono, string? Display)
{
    public IEnumerable<(string Name, string? Value)> Values() => [(nameof(Sans), Sans), (nameof(Mono), Mono), (nameof(Display), Display)];
}

internal sealed record StudioThemeShape(string? RadiusSm, string? Radius, string? RadiusMd, string? RadiusLg, string? RadiusXl)
{
    public IEnumerable<(string Name, string? Value)> Values() =>
        [(nameof(RadiusSm), RadiusSm), (nameof(Radius), Radius), (nameof(RadiusMd), RadiusMd), (nameof(RadiusLg), RadiusLg), (nameof(RadiusXl), RadiusXl)];
}
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

// Persistence is intentionally not a response DTO: source enrichment and projected defaults never become managed authority.
internal sealed record ManagedThemeState(StudioThemeDefinition[] Themes, string DefaultThemeId, string[]? DisabledBuiltInThemeIds = null)
{
    // Keep the existing on-disk empty-assets shape; asset metadata/URLs are derived from the owned directory.
    public ThemeStoreAsset[] Assets => [];
}
internal sealed record ThemeReadSnapshot(ManagedThemeState Managed, ThemeStoreResponse View);
