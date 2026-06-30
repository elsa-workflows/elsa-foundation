using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nuplane;
using Nuplane.Abstractions;
using Nuplane.Admin;
using Nuplane.Reconciliation;
using Nuplane.Store.Cleanup;

namespace Elsa.Server;

internal static class ElsaModuleManagementApi
{
    private const string ManagementFileName = "appsettings.json";
    private const string ManagementApiKeyConfigurationKey = "Elsa:ModuleManagement:ApiKey";
    private const string ManagementApiKeyHeaderName = "X-Elsa-Module-Management-Key";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static IEndpointRouteBuilder MapElsaModuleManagementApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/_elsa/module-management");
        group.AddEndpointFilter(RequireManagementApiKeyAsync);

        group.MapGet("/registry", GetRegistryAsync);
        group.MapPost("/packages/upload", UploadPackageAsync)
            .Accepts<IFormFile>("multipart/form-data")
            .DisableAntiforgery();
        group.MapDelete("/packages/drop-folder/{fileName}", DeleteDropFolderPackageAsync);
        group.MapPost("/reconcile", TriggerReconcileAsync);
        group.MapPost("/prune", PrunePackagesAsync);
        group.MapPost("/feeds", AddFeedAsync);
        group.MapPut("/feeds/{name}", UpdateFeedAsync);
        group.MapDelete("/feeds/{name}", DeleteFeedAsync);
        group.MapPut("/retention-policy", UpdateRetentionPolicyAsync);

        return endpoints;
    }

    private static async Task<IResult> GetRegistryAsync(
        [FromServices] IModuleRegistryService moduleRegistry,
        [FromServices] IWebHostEnvironment environment,
        [FromServices] IOptions<CleanupPolicyOptions> cleanupOptions,
        CancellationToken cancellationToken)
    {
        var modules = await moduleRegistry.ListModulesAsync(cancellationToken);
        var config = await ReadManagementConfigurationAsync(environment, cancellationToken);
        var dropFolder = ResolveDropFolder(environment, config);

        return Results.Ok(new ModuleManagementRegistryResponse(
            new("server", "Server", "Elsa.Server", environment.ContentRootPath),
            DateTimeOffset.UtcNow,
            modules,
            ListDropFolderPackages(dropFolder),
            ReadRegistryFeeds(config),
            ReadRetentionPolicy(config, cleanupOptions.Value),
            new(
                CanUploadPackages: true,
                CanManageFeeds: true,
                CanReconcile: true,
                CanPrunePackages: true,
                FeedChangesRequireRestart: true),
            []));
    }

    private static async Task<IResult> UploadPackageAsync(
        HttpRequest request,
        [FromServices] INuplaneAdminOperations nuplaneAdmin,
        [FromServices] IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
            return Results.BadRequest(new ModuleManagementErrorResponse("Expected multipart form data."));

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("package") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
            return Results.BadRequest(new ModuleManagementErrorResponse("Choose a non-empty .nupkg file."));

        var fileName = Path.GetFileName(file.FileName);
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(fileName), ".nupkg"))
            return Results.BadRequest(new ModuleManagementErrorResponse("Only .nupkg files can be uploaded."));

        var config = await ReadManagementConfigurationAsync(environment, cancellationToken);
        var dropFolder = ResolveDropFolder(environment, config);
        Directory.CreateDirectory(dropFolder);

        var destination = EnsureChildPath(dropFolder, fileName);
        var tempPath = Path.Combine(dropFolder, $".{Guid.NewGuid():N}.upload");
        await using (var output = File.Create(tempPath))
            await file.CopyToAsync(output, cancellationToken);

        File.Move(tempPath, destination, overwrite: true);
        var reconcile = await nuplaneAdmin.TriggerReconcileAsync(cancellationToken);

        return Results.Ok(new ModuleManagementUploadResponse(
            fileName,
            destination,
            file.Length,
            ModuleManagementReconcileResponse.FromOutcome(reconcile),
            RequiresReload: true,
            RequiresRestart: false));
    }

    private static async Task<IResult> DeleteDropFolderPackageAsync(
        string fileName,
        [FromServices] IServiceScopeFactory scopeFactory,
        [FromServices] IWebHostEnvironment environment,
        [FromServices] IHostApplicationLifetime applicationLifetime,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName) || !StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(safeFileName), ".nupkg"))
            return Results.BadRequest(new ModuleManagementErrorResponse("A .nupkg file name is required."));

        var config = await ReadManagementConfigurationAsync(environment, cancellationToken);
        var dropFolder = ResolveDropFolder(environment, config);
        var path = EnsureChildPath(dropFolder, safeFileName);
        if (!File.Exists(path))
            return Results.NotFound(new ModuleManagementErrorResponse($"Package '{safeFileName}' was not found in the drop folder."));

        File.Delete(path);
        QueueReconciliation(
            scopeFactory,
            applicationLifetime,
            loggerFactory.CreateLogger("Elsa.Server.ModuleManagementDelete"),
            "package deletion");

        return Results.Ok(new ModuleManagementDeleteResponse(
            safeFileName,
            path,
            ModuleManagementReconcileResponse.Deferred("Package was deleted from the drop folder. Nuplane reconciliation is running in the background."),
            RequiresReload: true,
            RequiresRestart: false));
    }

    private static void QueueReconciliation(
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime applicationLifetime,
        ILogger logger,
        string operation)
    {
        var stoppingToken = applicationLifetime.ApplicationStopping;

        _ = Task.Run(async () =>
        {
            if (stoppingToken.IsCancellationRequested)
                return;

            try
            {
                using var scope = scopeFactory.CreateScope();
                var nuplaneAdmin = scope.ServiceProvider.GetRequiredService<INuplaneAdminOperations>();
                var outcome = await nuplaneAdmin.TriggerReconcileAsync(stoppingToken);

                logger.LogInformation(
                    "Background Nuplane reconciliation completed after {Operation}. Outcome={Outcome}, CorrelationId={CorrelationId}, ReasonCode={ReasonCode}.",
                    operation,
                    outcome.OutcomeCode,
                    outcome.CorrelationId,
                    outcome.ReasonCode);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background Nuplane reconciliation failed after {Operation}.", operation);
            }
        });
    }

    private static async Task<IResult> TriggerReconcileAsync([FromServices] INuplaneAdminOperations nuplaneAdmin, CancellationToken cancellationToken)
    {
        var outcome = await nuplaneAdmin.TriggerReconcileAsync(cancellationToken);
        return Results.Ok(new ModuleManagementOperationResponse(
            "reconcile",
            ModuleManagementReconcileResponse.FromOutcome(outcome),
            RequiresReload: true,
            RequiresRestart: false,
            "Reconciliation completed for the Server host."));
    }

    private static async Task<IResult> PrunePackagesAsync(
        ModuleManagementPruneRequest request,
        [FromServices] INuplaneAdminOperations nuplaneAdmin,
        [FromServices] IWebHostEnvironment environment,
        [FromServices] IOptions<CleanupPolicyOptions> cleanupOptions,
        CancellationToken cancellationToken)
    {
        var packages = await nuplaneAdmin.GetPackagesAsync(cancellationToken);
        var config = await ReadManagementConfigurationAsync(environment, cancellationToken);
        var policy = ReadRetentionPolicy(config, cleanupOptions.Value).ToCleanupPolicyOptions();
        var decisions = PrunePackageInstallDirectories(packages.Packages, policy, request.DryRun, cancellationToken);

        return Results.Ok(new ModuleManagementPruneResponse(request.DryRun, decisions));
    }

    private static async Task<IResult> AddFeedAsync(
        ModuleManagementFeed feed,
        [FromServices] IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(feed.Name))
            return Results.BadRequest(new ModuleManagementErrorResponse("Feed name is required."));

        var config = await ReadManagementConfigurationAsync(environment, cancellationToken);
        var feeds = ReadFeedsArray(config);
        if (feeds.Any(node => string.Equals((string?)node?["Name"], feed.Name, StringComparison.OrdinalIgnoreCase)))
            return Results.Conflict(new ModuleManagementErrorResponse($"Feed '{feed.Name}' already exists."));

        feeds.Add(ToJson(feed));
        await WriteManagementConfigurationAsync(environment, config, cancellationToken);
        return Results.Ok(new ModuleManagementConfigurationWriteResponse(true, "Feed added. Restart the host to activate feed registration changes."));
    }

    private static async Task<IResult> UpdateFeedAsync(
        string name,
        ModuleManagementFeed feed,
        [FromServices] IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var config = await ReadManagementConfigurationAsync(environment, cancellationToken);
        var feeds = ReadFeedsArray(config);
        var index = FindFeedIndex(feeds, name);
        if (index < 0)
            return Results.NotFound(new ModuleManagementErrorResponse($"Feed '{name}' was not found."));

        feeds[index] = ToJson(feed with { Name = name });
        await WriteManagementConfigurationAsync(environment, config, cancellationToken);
        return Results.Ok(new ModuleManagementConfigurationWriteResponse(true, "Feed updated. Restart the host to activate feed registration changes."));
    }

    private static async Task<IResult> DeleteFeedAsync(
        string name,
        [FromServices] IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var config = await ReadManagementConfigurationAsync(environment, cancellationToken);
        var feeds = ReadFeedsArray(config);
        var index = FindFeedIndex(feeds, name);
        if (index < 0)
            return Results.NotFound(new ModuleManagementErrorResponse($"Feed '{name}' was not found."));

        feeds.RemoveAt(index);
        await WriteManagementConfigurationAsync(environment, config, cancellationToken);
        return Results.Ok(new ModuleManagementConfigurationWriteResponse(true, "Feed deleted. Restart the host to activate feed registration changes."));
    }

    private static async Task<IResult> UpdateRetentionPolicyAsync(
        ModuleManagementRetentionPolicy policy,
        [FromServices] IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var config = await ReadManagementConfigurationAsync(environment, cancellationToken);
        var nuplane = GetOrCreateObject(config.Document, "Nuplane");
        nuplane["CleanupPolicy"] = JsonSerializer.SerializeToNode(policy, JsonOptions);
        await WriteManagementConfigurationAsync(environment, config, cancellationToken);
        return Results.Ok(new ModuleManagementConfigurationWriteResponse(false, "Retention policy updated. It will be used by the next prune operation and after host restart by Nuplane cleanup."));
    }

    private static IReadOnlyList<ModuleManagementPruneDecision> PrunePackageInstallDirectories(
        IReadOnlyList<ActivePackage> activePackages,
        CleanupPolicyOptions policy,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var decisions = new List<ModuleManagementPruneDecision>();
        var activeByPackage = activePackages
            .Where(package => !string.IsNullOrWhiteSpace(package.InstallPath))
            .GroupBy(package => package.PackageId, StringComparer.OrdinalIgnoreCase);

        foreach (var group in activeByPackage)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var activeVersions = group.Select(package => package.Version).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var packageRoot = group
                .Select(package => Directory.GetParent(package.InstallPath)?.FullName)
                .FirstOrDefault(Directory.Exists);
            if (packageRoot is null)
                continue;

            var versions = Directory.EnumerateDirectories(packageRoot)
                .Select(path => new DirectoryInfo(path))
                .OrderByDescending(directory => directory.LastWriteTimeUtc)
                .ThenByDescending(directory => directory.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            for (var i = 0; i < versions.Length; i++)
            {
                var directory = versions[i];
                var ageDays = Math.Max(0, (int)(DateTimeOffset.UtcNow - directory.LastWriteTimeUtc).TotalDays);
                var isActive = activeVersions.Contains(directory.Name);
                var keepByPolicy = policy.IsRetainedByUnion(i + 1, ageDays);
                var action = isActive || keepByPolicy ? "kept" : dryRun ? "would-delete" : "deleted";
                var reason = isActive ? "active-version" : keepByPolicy ? "retained-policy" : "eligible-for-deletion";

                if (action == "deleted")
                    Directory.Delete(directory.FullName, recursive: true);

                decisions.Add(new(group.Key, directory.Name, directory.FullName, action, reason));
            }
        }

        return decisions;
    }

    private static IReadOnlyList<ModuleManagementDropFolderPackage> ListDropFolderPackages(string dropFolder)
    {
        if (!Directory.Exists(dropFolder))
            return [];

        return Directory.EnumerateFiles(dropFolder, "*.nupkg", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                var file = new FileInfo(path);
                return new ModuleManagementDropFolderPackage(file.Name, file.FullName, file.Length, file.LastWriteTimeUtc);
            })
            .OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ModuleManagementFeed[] ReadFeeds(ManagementConfiguration config) =>
        ReadFeedsArray(config)
            .OfType<JsonObject>()
            .Select(feed => new ModuleManagementFeed(
                (string?)feed["Name"] ?? "",
                (string?)feed["ServiceIndex"],
                (string?)feed["DirectoryPath"],
                (string?)feed["Credentials"],
                (bool?)feed["IncludeAll"] ?? false,
                feed["IncludePatterns"]?.AsArray().Select(x => (string?)x).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray() ?? [],
                new(
                    (bool?)feed["Directory"]?["Watch"] ?? true,
                    (string?)feed["Directory"]?["DebounceWindow"] ?? "00:00:01")))
            .Where(feed => !string.IsNullOrWhiteSpace(feed.Name))
            .OrderBy(feed => feed.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static ModuleManagementFeed[] ReadRegistryFeeds(ManagementConfiguration config) =>
        ReadFeeds(config)
            .Select(feed => feed with { Credentials = null })
            .ToArray();

    private static ModuleManagementRetentionPolicy ReadRetentionPolicy(ManagementConfiguration config, CleanupPolicyOptions fallback)
    {
        var node = config.Document["Nuplane"]?["CleanupPolicy"];
        return new(
            (int?)node?["RetainLastNVersions"] ?? fallback.RetainLastNVersions,
            (int?)node?["RetainYoungerThanDays"] ?? fallback.RetainYoungerThanDays,
            (string?)node?["Mode"] ?? fallback.Mode.ToString(),
            (bool?)node?["ProtectLastKnownGood"] ?? fallback.ProtectLastKnownGood);
    }

    private static string ResolveDropFolder(IWebHostEnvironment environment, ManagementConfiguration config)
    {
        var feed = ReadFeeds(config).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.DirectoryPath));
        var path = feed?.DirectoryPath ?? "packages";
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(environment.ContentRootPath, path));
    }

    private static async Task<ManagementConfiguration> ReadManagementConfigurationAsync(IWebHostEnvironment environment, CancellationToken cancellationToken)
    {
        var path = GetManagementFilePath(environment);
        if (!File.Exists(path))
            return new(path, new JsonObject { ["Nuplane"] = new JsonObject { ["Setup"] = new JsonObject { ["Feeds"] = new JsonArray() } } });

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return new(path, JsonNode.Parse(json)?.AsObject() ?? new JsonObject());
    }

    private static async Task WriteManagementConfigurationAsync(IWebHostEnvironment environment, ManagementConfiguration config, CancellationToken cancellationToken)
    {
        var path = GetManagementFilePath(environment);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(path, config.Document.ToJsonString(JsonOptions) + Environment.NewLine, cancellationToken);
    }

    private static string GetManagementFilePath(IWebHostEnvironment environment) =>
        Path.Combine(environment.ContentRootPath, ManagementFileName);

    private static JsonArray ReadFeedsArray(ManagementConfiguration config)
    {
        var nuplane = GetOrCreateObject(config.Document, "Nuplane");
        var setup = GetOrCreateObject(nuplane, "Setup");
        return GetOrCreateArray(setup, "Feeds");
    }

    private static JsonObject GetOrCreateObject(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject existing)
            return existing;

        var created = new JsonObject();
        parent[name] = created;
        return created;
    }

    private static JsonArray GetOrCreateArray(JsonObject parent, string name)
    {
        if (parent[name] is JsonArray existing)
            return existing;

        var created = new JsonArray();
        parent[name] = created;
        return created;
    }

    private static int FindFeedIndex(JsonArray feeds, string name)
    {
        for (var i = 0; i < feeds.Count; i++)
        {
            if (string.Equals((string?)feeds[i]?["Name"], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static JsonObject ToJson(ModuleManagementFeed feed)
    {
        var node = JsonSerializer.SerializeToNode(feed, JsonOptions)?.AsObject() ?? [];
        Rename(node, "name", "Name");
        Rename(node, "serviceIndex", "ServiceIndex");
        Rename(node, "directoryPath", "DirectoryPath");
        Rename(node, "credentials", "Credentials");
        Rename(node, "includeAll", "IncludeAll");
        Rename(node, "includePatterns", "IncludePatterns");
        if (node.Remove("directory", out var directory))
            node["Directory"] = directory;
        if (node["Directory"] is JsonObject dir)
        {
            Rename(dir, "watch", "Watch");
            Rename(dir, "debounceWindow", "DebounceWindow");
        }

        return node;
    }

    private static void Rename(JsonObject node, string from, string to)
    {
        if (node.Remove(from, out var value))
            node[to] = value;
    }

    private static string EnsureChildPath(string root, string fileName)
    {
        var rootPath = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(rootPath, fileName));
        if (!path.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The resolved path is outside the package drop folder.");

        return path;
    }

    private static ValueTask<object?> RequireManagementApiKeyAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var configuredApiKey = configuration[ManagementApiKeyConfigurationKey];
        if (string.IsNullOrWhiteSpace(configuredApiKey))
            return new ValueTask<object?>(Results.NotFound());

        if (!context.HttpContext.Request.Headers.TryGetValue(ManagementApiKeyHeaderName, out var providedApiKeys) ||
            providedApiKeys.Count != 1 ||
            string.IsNullOrWhiteSpace(providedApiKeys[0]) ||
            !ApiKeysEqual(configuredApiKey, providedApiKeys[0]!))
            return new ValueTask<object?>(Results.Unauthorized());

        return next(context);
    }

    private static bool ApiKeysEqual(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private sealed record ManagementConfiguration(string Path, JsonObject Document);
}

internal sealed record ModuleManagementRegistryResponse(
    ModuleManagementHost Host,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ModuleManagementModule> Modules,
    IReadOnlyList<ModuleManagementDropFolderPackage> DropFolderPackages,
    IReadOnlyList<ModuleManagementFeed> Feeds,
    ModuleManagementRetentionPolicy RetentionPolicy,
    ModuleManagementCapabilities Capabilities,
    IReadOnlyList<ModuleManagementDiagnostic> Diagnostics);

internal sealed record ModuleManagementHost(string Id, string DisplayName, string Runtime, string ContentRootPath);

internal interface IModuleRegistryService
{
    Task<IReadOnlyList<ModuleManagementModule>> ListModulesAsync(CancellationToken cancellationToken = default);
}

internal sealed class ModuleRegistryService(
    IFeatureManagementService featureManagement,
    INuplaneAdminOperations nuplaneAdmin) : IModuleRegistryService
{
    public async Task<IReadOnlyList<ModuleManagementModule>> ListModulesAsync(CancellationToken cancellationToken = default)
    {
        var features = await featureManagement.GetCatalogAsync(cancellationToken);
        var packages = await nuplaneAdmin.GetPackagesAsync(cancellationToken);
        return ModuleManagementRegistryBuilder.BuildModules(features.Features, packages.Packages);
    }
}

internal sealed record ModuleManagementModule(
    string Id,
    string DisplayName,
    string PackageId,
    string Version,
    string SourceKind,
    string FeedName,
    string SourceName,
    string InstallPath,
    string Status,
    string Compatibility,
    ModuleManagementPackageManifest? Manifest,
    IReadOnlyList<ModuleManagementPackageDependency> Dependencies,
    IReadOnlyList<ModuleManagementFeature> Features,
    IReadOnlyList<ModuleManagementDiagnostic> Diagnostics);

internal sealed record ModuleManagementFeature(
    string Id,
    string DisplayName,
    string? Description,
    IReadOnlyList<string> Categories,
    bool Enabled,
    string SourceKind,
    bool Advanced,
    bool Experimental,
    JsonElement Configuration,
    IReadOnlyList<FeatureSettingDescriptor> Settings,
    string? ManifestPath,
    string? ManifestHash,
    IReadOnlyList<ModuleManagementDiagnostic> Diagnostics);

internal static class ModuleManagementRegistryBuilder
{
    public static IReadOnlyList<ModuleManagementModule> BuildModules(
        IReadOnlyList<FeatureCatalogItem> features,
        IReadOnlyList<ActivePackage> packages)
    {
        var modules = new List<ModuleManagementModule>();
        var packageFeatures = features
            .Where(x => !string.IsNullOrWhiteSpace(x.PackageId) && !string.IsNullOrWhiteSpace(x.PackageVersion))
            .ToLookup(x => PackageKey(x.PackageId!, x.PackageVersion!), StringComparer.OrdinalIgnoreCase);

        foreach (var package in packages.OrderBy(x => x.PackageId, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Version, StringComparer.OrdinalIgnoreCase))
            modules.Add(BuildPackageModule(package, packageFeatures[PackageKey(package.PackageId, package.Version)].ToArray()));

        var serverFeatures = features
            .Where(x => string.IsNullOrWhiteSpace(x.PackageId) || string.IsNullOrWhiteSpace(x.PackageVersion))
            .Where(x => !IsManifestError(x))
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (serverFeatures.Length > 0)
            modules.Insert(0, BuildServerModule(serverFeatures));

        return modules;
    }

    private static ModuleManagementModule BuildPackageModule(ActivePackage package, IReadOnlyList<FeatureCatalogItem> features)
    {
        var manifest = ModuleManagementPackageManifest.Read(package.InstallPath);
        var dependencies = ModuleManagementPackageDependency.Read(package.InstallPath);
        var diagnostics = BuildPackageDiagnostics(package, manifest, features);
        var childFeatures = features
            .Where(x => !IsManifestError(x))
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(ToFeature)
            .ToArray();

        return new(
            PackageKey(package.PackageId, package.Version),
            package.PackageId,
            package.PackageId,
            package.Version,
            "package",
            package.FeedName ?? "",
            package.SourceName ?? "",
            package.InstallPath,
            diagnostics.Any(x => x.Status == "failed") ? "failed" : "loaded",
            diagnostics.Count > 0 ? "warning" : "compatible",
            manifest,
            dependencies,
            childFeatures,
            diagnostics);
    }

    private static ModuleManagementModule BuildServerModule(IReadOnlyList<FeatureCatalogItem> features) =>
        new(
            "Elsa.Server",
            "Elsa.Server",
            "Elsa.Server",
            "",
            "server",
            "",
            "",
            "",
            "loaded",
            "compatible",
            null,
            [],
            features.Select(ToFeature).ToArray(),
            []);

    private static IReadOnlyList<ModuleManagementDiagnostic> BuildPackageDiagnostics(
        ActivePackage package,
        ModuleManagementPackageManifest? manifest,
        IReadOnlyList<FeatureCatalogItem> features)
    {
        var diagnostics = new List<ModuleManagementDiagnostic>();

        foreach (var feature in features.Where(x => !string.IsNullOrWhiteSpace(x.ReadError)))
            diagnostics.Add(new(feature.Id, "failed", feature.ReadError!));

        if (manifest is null && diagnostics.Count == 0)
            diagnostics.Add(new(package.PackageId, "warning", "Package manifest not found."));
        else if (manifest?.Kind == "manifest-error")
            diagnostics.Add(new(package.PackageId, "failed", manifest.Text ?? "Package manifest could not be read."));

        var manifestIdentity = ReadManifestIdentity(manifest);
        if (manifestIdentity is not null)
        {
            if (!string.Equals(manifestIdentity.Value.PackageId, package.PackageId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(manifestIdentity.Value.Version, package.Version, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new(
                    "manifest",
                    "warning",
                    $"Manifest package identity '{manifestIdentity.Value.PackageId}@{manifestIdentity.Value.Version}' differs from active package identity '{package.PackageId}@{package.Version}'."));
            }
        }

        return diagnostics
            .GroupBy(x => (x.Source, x.Status, x.Reason))
            .Select(x => x.First())
            .ToArray();
    }

    private static ModuleManagementFeature ToFeature(FeatureCatalogItem feature)
    {
        var diagnostics = string.IsNullOrWhiteSpace(feature.ReadError)
            ? Array.Empty<ModuleManagementDiagnostic>()
            : [new ModuleManagementDiagnostic(feature.Id, "failed", feature.ReadError!)];

        return new(
            feature.Id,
            feature.DisplayName,
            feature.Description,
            feature.Categories,
            feature.Enabled,
            feature.SourceKind,
            feature.Advanced,
            feature.Experimental,
            feature.Configuration,
            feature.Settings,
            feature.ManifestPath,
            feature.ManifestHash,
            diagnostics);
    }

    private static (string PackageId, string Version)? ReadManifestIdentity(ModuleManagementPackageManifest? manifest)
    {
        if (manifest?.Content is not JsonObject content)
            return null;

        var package = content["package"] as JsonObject;
        if (package is null)
            return null;

        var packageId = ReadString(package, "id", "packageId");
        var version = ReadString(package, "version");
        return string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(version)
            ? null
            : (packageId, version);
    }

    private static string? ReadString(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj[name] is JsonValue value && value.TryGetValue<string>(out var text))
                return text;
        }

        return null;
    }

    private static bool IsManifestError(FeatureCatalogItem feature) =>
        string.Equals(feature.SourceKind, FeatureSourceKinds.ManifestError, StringComparison.OrdinalIgnoreCase);

    private static string PackageKey(string packageId, string version) => $"{packageId}@{version}";
}

internal sealed record ModuleManagementPackageManifest(string Kind, string Path, JsonNode? Content, string? Text)
{
    public static ModuleManagementPackageManifest? Read(string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
            return null;

        var manifestBytes = PackageContent.TryReadFile(installPath, "elsa-package.json")
            ?? PackageContent.TryReadFile(installPath, "build/elsa-package.json");
        if (manifestBytes is not null)
            return ReadJsonManifest(manifestBytes);

        var nuspec = PackageContent.TryFindByExtension(installPath, ".nuspec");
        return nuspec is null
            ? null
            : new("nuspec", nuspec.Name, null, Encoding.UTF8.GetString(nuspec.Content));
    }

    private static ModuleManagementPackageManifest ReadJsonManifest(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        try
        {
            return new("elsa-package", "elsa-package.json", JsonNode.Parse(text), null);
        }
        catch (JsonException)
        {
            return new("elsa-package", "elsa-package.json", null, text);
        }
    }
}

internal sealed record ModuleManagementPackageDependency(string Id, string VersionRange, string? Group, string Source)
{
    public static IReadOnlyList<ModuleManagementPackageDependency> Read(string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
            return [];

        var dependencies = new List<ModuleManagementPackageDependency>();

        var elsaManifest = PackageContent.TryReadFile(installPath, "elsa-package.json")
            ?? PackageContent.TryReadFile(installPath, "build/elsa-package.json");
        if (elsaManifest is not null)
            dependencies.AddRange(ReadElsaPackageDependencies(elsaManifest));

        var nuspec = PackageContent.TryFindByExtension(installPath, ".nuspec");
        if (nuspec is not null)
            dependencies.AddRange(ReadNuspecDependencies(nuspec.Content));

        return dependencies
            .GroupBy(x => (x.Id, x.VersionRange, x.Group, x.Source))
            .Select(x => x.First())
            .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.VersionRange, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<ModuleManagementPackageDependency> ReadElsaPackageDependencies(byte[] bytes)
    {
        try
        {
            var manifest = JsonNode.Parse(Encoding.UTF8.GetString(bytes))?.AsObject();
            if (manifest?["dependencies"] is not JsonArray dependencies)
                return [];

            return dependencies
                .Select(ParseElsaDependency)
                .Where(x => x is not null)
                .Cast<ModuleManagementPackageDependency>()
                .ToArray();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return [];
        }
    }

    private static ModuleManagementPackageDependency? ParseElsaDependency(JsonNode? dependency)
    {
        if (dependency is null)
            return null;

        if (dependency is JsonValue value && value.TryGetValue<string>(out var id) && !string.IsNullOrWhiteSpace(id))
            return new(id, "", null, "elsa-package");

        if (dependency is not JsonObject obj)
            return null;

        var packageId = ReadString(obj, "id", "packageId", "name");
        if (string.IsNullOrWhiteSpace(packageId))
            return null;

        return new(
            packageId,
            ReadString(obj, "versionRange", "version", "range") ?? "",
            ReadString(obj, "group", "targetFramework"),
            "elsa-package");
    }

    private static IReadOnlyList<ModuleManagementPackageDependency> ReadNuspecDependencies(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var document = XDocument.Load(stream);
            return document.Descendants()
                .Where(x => x.Name.LocalName == "dependency")
                .Select(dependency =>
                {
                    var id = dependency.Attribute("id")?.Value;
                    if (string.IsNullOrWhiteSpace(id))
                        return null;

                    var group = dependency.Ancestors().FirstOrDefault(x => x.Name.LocalName == "group")?.Attribute("targetFramework")?.Value;
                    return new ModuleManagementPackageDependency(id, dependency.Attribute("version")?.Value ?? "", group, "nuspec");
                })
                .Where(x => x is not null)
                .Cast<ModuleManagementPackageDependency>()
                .ToArray();
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }
    }

    private static string? ReadString(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj[name] is JsonValue value && value.TryGetValue<string>(out var text))
                return text;
        }

        return null;
    }
}

internal sealed record ModuleManagementDropFolderPackage(string FileName, string Path, long Size, DateTimeOffset LastWriteTimeUtc);

internal sealed record ModuleManagementFeed(
    string Name,
    string? ServiceIndex,
    string? DirectoryPath,
    string? Credentials,
    bool IncludeAll,
    IReadOnlyList<string> IncludePatterns,
    ModuleManagementDirectoryFeedSettings Directory);

internal sealed record ModuleManagementDirectoryFeedSettings(bool Watch, string DebounceWindow);

internal sealed record ModuleManagementRetentionPolicy(
    int? RetainLastNVersions,
    int? RetainYoungerThanDays,
    string Mode,
    bool ProtectLastKnownGood)
{
    public CleanupPolicyOptions ToCleanupPolicyOptions() =>
        new()
        {
            RetainLastNVersions = RetainLastNVersions,
            RetainYoungerThanDays = RetainYoungerThanDays,
            Mode = Enum.TryParse<CleanupExecutionMode>(Mode, ignoreCase: true, out var mode) ? mode : CleanupExecutionMode.Automatic,
            ProtectLastKnownGood = ProtectLastKnownGood
        };
}

internal sealed record ModuleManagementCapabilities(
    bool CanUploadPackages,
    bool CanManageFeeds,
    bool CanReconcile,
    bool CanPrunePackages,
    bool FeedChangesRequireRestart);

internal sealed record ModuleManagementDiagnostic(string Source, string Status, string Reason);

internal sealed record ModuleManagementErrorResponse(string Error);

internal sealed record ModuleManagementUploadResponse(
    string FileName,
    string Path,
    long Size,
    ModuleManagementReconcileResponse Reconcile,
    bool RequiresReload,
    bool RequiresRestart);

internal sealed record ModuleManagementDeleteResponse(
    string FileName,
    string Path,
    ModuleManagementReconcileResponse Reconcile,
    bool RequiresReload,
    bool RequiresRestart);

internal sealed record ModuleManagementOperationResponse(
    string Operation,
    ModuleManagementReconcileResponse Reconcile,
    bool RequiresReload,
    bool RequiresRestart,
    string Message);

internal sealed record ModuleManagementReconcileResponse(
    string Outcome,
    string CorrelationId,
    string? ReasonCode,
    bool? IsDegraded,
    IReadOnlyList<string> FailedPackages)
{
    public static ModuleManagementReconcileResponse FromOutcome(ManualReconcileOutcome outcome) =>
        new(
            outcome.OutcomeCode.ToString(),
            outcome.CorrelationId,
            outcome.ReasonCode,
            outcome.RunResult?.IsDegraded,
            outcome.RunResult?.FailedPackages ?? []);

    public static ModuleManagementReconcileResponse Deferred(string reason) =>
        new("Deferred", "", reason, null, []);
}

internal sealed record ModuleManagementPruneRequest(bool DryRun = true);

internal sealed record ModuleManagementPruneResponse(bool DryRun, IReadOnlyList<ModuleManagementPruneDecision> Decisions);

internal sealed record ModuleManagementPruneDecision(string PackageId, string Version, string Path, string Action, string Reason);

internal sealed record ModuleManagementConfigurationWriteResponse(bool RequiresRestart, string Message);
