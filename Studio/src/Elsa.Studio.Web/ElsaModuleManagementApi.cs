using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Elsa.Studio.Api.Contracts;
using Elsa.Studio.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Nuplane.Abstractions;
using Nuplane.Admin;
using Nuplane.Reconciliation;
using Nuplane.Store.Cleanup;

namespace Elsa.Studio.Web;

internal static class ElsaModuleManagementApi
{
    private const string ManagementFileName = "nuplane-management.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static IEndpointRouteBuilder MapElsaModuleManagementApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/_elsa/module-management");

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
        [FromServices] IStudioModuleManifestProvider moduleManifestProvider,
        [FromServices] INuplaneAdminOperations nuplaneAdmin,
        [FromServices] IWebHostEnvironment environment,
        [FromServices] IOptions<CleanupPolicyOptions> cleanupOptions,
        CancellationToken cancellationToken)
    {
        var modules = await moduleManifestProvider.GetModuleRegistry(cancellationToken);
        var packages = await nuplaneAdmin.GetPackagesAsync(cancellationToken);
        var config = await ReadManagementConfigurationAsync(environment, cancellationToken);
        var dropFolder = ResolveDropFolder(environment, config);

        return Results.Ok(new ModuleManagementRegistryResponse(
            new("studio", "Studio", "Elsa.Studio.Web", environment.ContentRootPath),
            DateTimeOffset.UtcNow,
            modules.Modules.Select(ModuleManagementModule.FromStudioModule).ToArray(),
            packages.Packages.Select(ModuleManagementPackage.FromActivePackage).ToArray(),
            ListDropFolderPackages(dropFolder),
            ReadFeeds(config),
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
        [FromServices] INuplaneAdminOperations nuplaneAdmin,
        [FromServices] IWebHostEnvironment environment,
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
        var reconcile = await nuplaneAdmin.TriggerReconcileAsync(cancellationToken);

        return Results.Ok(new ModuleManagementDeleteResponse(
            safeFileName,
            path,
            ModuleManagementReconcileResponse.FromOutcome(reconcile),
            RequiresReload: true,
            RequiresRestart: false));
    }

    private static async Task<IResult> TriggerReconcileAsync([FromServices] INuplaneAdminOperations nuplaneAdmin, CancellationToken cancellationToken)
    {
        var outcome = await nuplaneAdmin.TriggerReconcileAsync(cancellationToken);
        return Results.Ok(new ModuleManagementOperationResponse(
            "reconcile",
            ModuleManagementReconcileResponse.FromOutcome(outcome),
            RequiresReload: true,
            RequiresRestart: false,
            "Reconciliation completed for the Studio host."));
    }

    private static async Task<IResult> PrunePackagesAsync(
        ModuleManagementPruneRequest request,
        [FromServices] INuplaneAdminOperations nuplaneAdmin,
        [FromServices] IOptions<CleanupPolicyOptions> cleanupOptions,
        CancellationToken cancellationToken)
    {
        var packages = await nuplaneAdmin.GetPackagesAsync(cancellationToken);
        var decisions = PrunePackageInstallDirectories(packages.Packages, cleanupOptions.Value, request.DryRun, cancellationToken);

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

    private sealed record ManagementConfiguration(string Path, JsonObject Document);
}

internal sealed record ModuleManagementRegistryResponse(
    ModuleManagementHost Host,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ModuleManagementModule> Modules,
    IReadOnlyList<ModuleManagementPackage> Packages,
    IReadOnlyList<ModuleManagementDropFolderPackage> DropFolderPackages,
    IReadOnlyList<ModuleManagementFeed> Feeds,
    ModuleManagementRetentionPolicy RetentionPolicy,
    ModuleManagementCapabilities Capabilities,
    IReadOnlyList<ModuleManagementDiagnostic> Diagnostics);

internal sealed record ModuleManagementHost(string Id, string DisplayName, string Runtime, string ContentRootPath);

internal sealed record ModuleManagementModule(
    string Id,
    string DisplayName,
    string Surface,
    string Runtime,
    string SourceKind,
    string Scope,
    string Version,
    string Status,
    string Compatibility,
    string? PackageId,
    string? PackageVersion,
    IReadOnlyList<StudioModuleContributionSummary> Contributions,
    IReadOnlyList<ModuleManagementDiagnostic> Diagnostics,
    ModuleManagementStudioManifest Manifest)
{
    public static ModuleManagementModule FromStudioModule(StudioModuleRegistryItem module) =>
        new(
            module.Id,
            module.DisplayName,
            "Studio",
            "Elsa.Studio.Web",
            module.SourceKind,
            module.Scope,
            module.Version,
            module.Status,
            module.Compatibility,
            null,
            null,
            module.Contributions.ToArray(),
            module.Diagnostics.Select(x => new ModuleManagementDiagnostic(x.ModuleId, x.Status, x.Reason)).ToArray(),
            new(module.Manifest.Entry, module.Manifest.Styles.ToArray(), module.Manifest.Capabilities.ToArray()));
}

internal sealed record ModuleManagementStudioManifest(
    string Entry,
    IReadOnlyList<string> Styles,
    IReadOnlyList<string> Capabilities);

internal sealed record ModuleManagementPackage(
    string Id,
    string Version,
    string FeedName,
    string SourceName,
    string InstallPath,
    ModuleManagementPackageManifest? Manifest,
    IReadOnlyList<ModuleManagementPackageDependency> Dependencies)
{
    public static ModuleManagementPackage FromActivePackage(ActivePackage package) =>
        new(
            package.PackageId,
            package.Version,
            package.FeedName ?? "",
            package.SourceName ?? "",
            package.InstallPath,
            ModuleManagementPackageManifest.Read(package.InstallPath),
            ModuleManagementPackageDependency.Read(package.InstallPath));
}

internal sealed record ModuleManagementPackageManifest(string Kind, string Path, JsonNode? Content, string? Text)
{
    public static ModuleManagementPackageManifest? Read(string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            return null;

        try
        {
            var elsaManifestPath = System.IO.Path.Combine(installPath, "elsa-package.json");
            if (File.Exists(elsaManifestPath))
            {
                var text = File.ReadAllText(elsaManifestPath);
                try
                {
                    return new("elsa-package", elsaManifestPath, JsonNode.Parse(text), null);
                }
                catch (JsonException)
                {
                    return new("elsa-package", elsaManifestPath, null, text);
                }
            }

            var nuspecPath = Directory.EnumerateFiles(installPath, "*.nuspec", SearchOption.TopDirectoryOnly).FirstOrDefault();
            return nuspecPath is null
                ? null
                : new("nuspec", nuspecPath, null, File.ReadAllText(nuspecPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new("manifest-error", installPath, null, ex.Message);
        }
    }
}

internal sealed record ModuleManagementPackageDependency(string Id, string VersionRange, string? Group, string Source)
{
    public static IReadOnlyList<ModuleManagementPackageDependency> Read(string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            return [];

        try
        {
            var dependencies = new List<ModuleManagementPackageDependency>();
            var elsaManifestPath = System.IO.Path.Combine(installPath, "elsa-package.json");
            if (File.Exists(elsaManifestPath))
                dependencies.AddRange(ReadElsaPackageDependencies(elsaManifestPath));

            var nuspecPath = Directory.EnumerateFiles(installPath, "*.nuspec", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (nuspecPath is not null)
                dependencies.AddRange(ReadNuspecDependencies(nuspecPath));

            return dependencies
                .GroupBy(x => (x.Id, x.VersionRange, x.Group, x.Source))
                .Select(x => x.First())
                .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.VersionRange, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IReadOnlyList<ModuleManagementPackageDependency> ReadElsaPackageDependencies(string path)
    {
        try
        {
            var manifest = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
            if (manifest?["dependencies"] is not JsonArray dependencies)
                return [];

            return dependencies
                .Select(ParseElsaDependency)
                .Where(x => x is not null)
                .Cast<ModuleManagementPackageDependency>()
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
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

    private static IReadOnlyList<ModuleManagementPackageDependency> ReadNuspecDependencies(string path)
    {
        try
        {
            var document = XDocument.Load(path);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
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
    bool ProtectLastKnownGood);

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
}

internal sealed record ModuleManagementPruneRequest(bool DryRun = true);

internal sealed record ModuleManagementPruneResponse(bool DryRun, IReadOnlyList<ModuleManagementPruneDecision> Decisions);

internal sealed record ModuleManagementPruneDecision(string PackageId, string Version, string Path, string Action, string Reason);

internal sealed record ModuleManagementConfigurationWriteResponse(bool RequiresRestart, string Message);
