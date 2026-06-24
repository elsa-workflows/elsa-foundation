using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.IO.Compression;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Modularity.Nuplane.Services;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nuplane.Admin;

namespace Elsa.Server;

internal static class ElsaDemoApi
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static IEndpointRouteBuilder MapElsaDemoApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/_demo");

        group.MapGet("/state", GetStateAsync);
        group.MapGet("/packages", GetPackagesAsync);
        group.MapGet("/packages/events", GetPackageEvents);
        group.MapDelete("/packages/drop-folder", ClearPackageDropFolderAsync);
        group.MapPost("/packages/reconcile", TriggerPackageReconcileAsync);
        group.MapPost("/packages/upload", UploadPackageAsync)
            .Accepts<IFormFile>("multipart/form-data")
            .DisableAntiforgery();
        group.MapGet("/features", ListFeaturesAsync);
        group.MapPost("/reset", ResetDemoAsync);
        group.MapGet("/workflows/executables", ListWorkflowExecutablesAsync);
        group.MapGet("/shells/default", GetDefaultShellAsync);
        group.MapPost("/shells/reload", ReloadShellsAsync);
        group.MapPut("/shells/default", SaveShellDocumentAsync);
        group.MapPut("/shells/default/features/{featureName}", UpdateFeatureAsync);

        return endpoints;
    }

    private static async Task<IResult> GetStateAsync(INuplaneAdminOperations nuplaneAdmin, DemoPackageEventStore events, IWebHostEnvironment environment, CancellationToken cancellationToken)
    {
        var packages = await nuplaneAdmin.GetPackagesAsync(cancellationToken);
        return Results.Ok(new DemoStateResponse(
            PackageDropFolder: GetPackageDropFolder(environment),
            ShellsJsonPath: GetShellsJsonPath(environment),
            Packages: packages.Packages.Select(DemoPackageSummary.FromActivePackage).ToArray(),
            PackageEvents: events.GetEvents()));
    }

    private static async Task<IResult> GetPackagesAsync(INuplaneAdminOperations nuplaneAdmin, CancellationToken cancellationToken)
    {
        var packages = await nuplaneAdmin.GetPackagesAsync(cancellationToken);
        return Results.Ok(new DemoPackagesResponse(
            packages.SnapshotAtUtc,
            packages.PersistedAtUtc,
            packages.CorrelationId,
            packages.Packages.Select(DemoPackageSummary.FromActivePackage).ToArray()));
    }

    private static IResult GetPackageEvents(DemoPackageEventStore events, long? afterSequence) =>
        Results.Ok(events.GetEvents(afterSequence));

    private static async Task<IResult> ReloadShellsAsync(IRuntimeFeatureCatalogAccessor runtimeFeatureCatalog, IShellRegistry shellRegistry, CancellationToken cancellationToken)
    {
        var snapshot = await runtimeFeatureCatalog.RefreshAsync(cancellationToken);
        var featureDescriptorCount = snapshot.FeatureDescriptors.Count;
        var reloadResults = await shellRegistry.ReloadActiveAsync(null, cancellationToken);
        Console.WriteLine($"Demo shells reloaded after refreshing {featureDescriptorCount} feature descriptor(s) and reloading {reloadResults.Count} active shell(s).");

        return Results.Ok(new DemoReloadShellsResponse(featureDescriptorCount, reloadResults.Count));
    }

    private static async Task<IResult> TriggerPackageReconcileAsync(INuplaneAdminOperations nuplaneAdmin, CancellationToken cancellationToken)
    {
        var outcome = await nuplaneAdmin.TriggerReconcileAsync(cancellationToken);
        return Results.Ok(new DemoReconcileResponse(
            outcome.OutcomeCode.ToString(),
            outcome.CorrelationId,
            outcome.ReasonCode,
            outcome.RunResult?.Skipped,
            outcome.RunResult?.IsDegraded,
            outcome.RunResult?.FailedPackages ?? [],
            outcome.RunResult?.ChangeSet.Added.Select(DemoPackageSummary.FromResolvedPackage).ToArray() ?? [],
            outcome.RunResult?.ChangeSet.Updated.Select(DemoPackageSummary.FromResolvedPackage).ToArray() ?? [],
            outcome.RunResult?.ChangeSet.Removed.ToArray() ?? []));
    }

    private static async Task<IResult> ListFeaturesAsync(
        IRuntimeFeatureCatalogAccessor runtimeFeatureCatalog,
        INuplaneAdminOperations nuplaneAdmin,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var shellJson = await ReadShellDocumentAsync(environment, cancellationToken);
        if (shellJson.Error is not null)
            return shellJson.Error;

        var shellFeatures = GetDefaultFeatures(shellJson.Document!);
        var items = new Dictionary<string, DemoFeatureCatalogItemBuilder>(StringComparer.OrdinalIgnoreCase);

        foreach (var feature in shellFeatures)
        {
            UpsertFeature(items, feature.Key, feature.Value?.ToJsonString(IndentedJsonOptions) ?? "{}", builder =>
            {
                builder.Enabled = true;
                builder.SourceKind ??= "shell";
                builder.DisplayName ??= feature.Key;
            });
        }

        var snapshot = await runtimeFeatureCatalog.RefreshAsync(cancellationToken);
        foreach (var descriptor in snapshot.FeatureDescriptors)
        {
            var featureName = descriptor.Id;
            if (string.IsNullOrWhiteSpace(featureName))
                continue;

            UpsertFeature(items, featureName, "{}", builder =>
            {
                builder.SourceKind = builder.SourceKind == "manifest" ? builder.SourceKind : "runtime";
                builder.DisplayName = GetDescriptorMetadata(descriptor, "DisplayName") ?? builder.DisplayName ?? featureName;
                builder.Description = GetDescriptorMetadata(descriptor, "Description") ?? builder.Description;
            });
        }

        var packages = await nuplaneAdmin.GetPackagesAsync(cancellationToken);
        foreach (var package in packages.Packages)
        {
            var summary = DemoPackageSummary.FromActivePackage(package);
            var packageManifest = ReadPackageManifest(summary, environment);
            if (packageManifest.Manifest is null)
            {
                UpsertFeature(items, $"{summary.Id}:{summary.Version}", "{}", builder =>
                {
                    builder.SourceKind = "manifest-error";
                    builder.DisplayName = summary.Id;
                    builder.PackageId = summary.Id;
                    builder.PackageVersion = summary.Version;
                    builder.ReadError = packageManifest.ReadError ?? "Package manifest not found.";
                });
                continue;
            }

            var manifest = packageManifest.Manifest;
            foreach (var feature in manifest.Features ?? [])
            {
                var featureName = GetStringExtension(feature.Extensions, "cshellsFeatureName") ?? feature.Id;
                if (string.IsNullOrWhiteSpace(featureName))
                    continue;

                UpsertFeature(items, featureName, "{}", builder =>
                {
                    builder.SourceKind = "manifest";
                    builder.DisplayName = string.IsNullOrWhiteSpace(feature.DisplayName) ? featureName : feature.DisplayName;
                    builder.Description = feature.Description ?? builder.Description;
                    builder.Categories = MergeCategories(feature.Category, feature.Categories);
                    builder.PackageId = manifest.Package?.Id ?? summary.Id;
                    builder.PackageVersion = manifest.Package?.Version ?? summary.Version;
                    builder.Advanced = feature.Advanced;
                    builder.Experimental = feature.Experimental;
                    builder.Settings = MapManifestSettings(feature.Settings);
                    builder.ManifestPath = packageManifest.Path;
                    builder.ManifestHash = packageManifest.Hash;
                });
            }
        }

        var response = items.Values
            .Select(x => x.ToResponse())
            .OrderByDescending(x => x.Enabled)
            .ThenBy(x => x.DisplayName ?? x.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Results.Ok(response);
    }

    private static async Task<IResult> UploadPackageAsync(HttpRequest request, IWebHostEnvironment environment, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
            return Results.BadRequest(new DemoErrorResponse("Expected multipart form data."));

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("package") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
            return Results.BadRequest(new DemoErrorResponse("Choose a non-empty .nupkg file."));

        var fileName = Path.GetFileName(file.FileName);
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(fileName), ".nupkg"))
            return Results.BadRequest(new DemoErrorResponse("Only .nupkg files can be uploaded."));

        var dropFolder = GetPackageDropFolder(environment);
        Directory.CreateDirectory(dropFolder);
        var destination = Path.Combine(dropFolder, fileName);

        await using var output = File.Create(destination);
        await file.CopyToAsync(output, cancellationToken);

        Console.WriteLine($"Demo package uploaded to '{destination}'.");
        return Results.Ok(new DemoUploadPackageResponse(fileName, destination, file.Length));
    }

    private static IResult ClearPackageDropFolderAsync(IWebHostEnvironment environment) =>
        Results.Ok(ClearPackageDropFolder(environment));

    private static async Task<IResult> ResetDemoAsync(
        IRuntimeFeatureCatalogAccessor runtimeFeatureCatalog,
        IShellRegistry shellRegistry,
        INuplaneAdminOperations nuplaneAdmin,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var shellRestore = await RestoreShellDocumentFromBaselineAsync(environment, cancellationToken);
        if (shellRestore.Error is not null)
            return shellRestore.Error;

        DemoResetWorkflowCounts workflows;
        DemoResetActivityCounts activities;
        var shell = await shellRegistry.GetOrActivateAsync("default", cancellationToken);
        await using (var shellScope = shell.BeginScope())
        {
            workflows = await ClearWorkflowsAsync(shellScope.ServiceProvider, cancellationToken);
            activities = await ClearActivitiesAsync(shellScope.ServiceProvider, cancellationToken);
        }

        var packages = ClearPackageDropFolder(environment);
        var reconcile = await nuplaneAdmin.TriggerReconcileAsync(cancellationToken);
        var snapshot = await runtimeFeatureCatalog.RefreshAsync(cancellationToken);
        var featureDescriptorCount = snapshot.FeatureDescriptors.Count;
        var reloadResults = await shellRegistry.ReloadActiveAsync(null, cancellationToken);

        Console.WriteLine(
            $"Demo reset completed: {workflows.TotalDeleted} workflow row(s), {activities.TotalDeleted} activity row(s), " +
            $"{packages.DeletedFiles} package file(s), {packages.DeletedDirectories} package folder(s) deleted, " +
            $"shells.json restored from baseline, Nuplane reconcile {reconcile.OutcomeCode} ({reconcile.CorrelationId}), " +
            $"{reloadResults.Count} active shell(s) reloaded.");

        return Results.Ok(new DemoResetResponse(
            workflows,
            activities,
            packages,
            shellRestore.Response!,
            new DemoResetReconcileResponse(reconcile.OutcomeCode.ToString(), reconcile.CorrelationId, reconcile.ReasonCode),
            new DemoReloadShellsResponse(featureDescriptorCount, reloadResults.Count)));
    }

    private static async Task<IResult> ListWorkflowExecutablesAsync(IShellRegistry shellRegistry, CancellationToken cancellationToken)
    {
        var shell = await shellRegistry.GetOrActivateAsync("default", cancellationToken);
        await using var shellScope = shell.BeginScope();
        var store = shellScope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
        var executables = await store.ListAsync(cancellationToken: cancellationToken);

        var response = executables
            .Select(DemoWorkflowExecutableResponse.FromExecutable)
            .OrderByDescending(x => x.PublishedAt ?? x.CreatedAt)
            .ThenBy(x => x.ArtifactId)
            .ToArray();

        return Results.Ok(response);
    }

    private static async Task<DemoResetWorkflowCounts> ClearWorkflowsAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var factory = serviceProvider.GetRequiredService<IDbContextFactory<WorkflowsDesignDbContext>>();
        await using var dbContext = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var draftValidations = await dbContext.WorkflowDefinitionDraftValidations.ExecuteDeleteAsync(cancellationToken);
        var draftLayouts = await dbContext.WorkflowDefinitionDraftLayouts.ExecuteDeleteAsync(cancellationToken);
        var versionLayouts = await dbContext.WorkflowDefinitionVersionLayouts.ExecuteDeleteAsync(cancellationToken);
        var drafts = await dbContext.WorkflowDefinitionDrafts.ExecuteDeleteAsync(cancellationToken);
        var versions = await dbContext.WorkflowDefinitionVersions.ExecuteDeleteAsync(cancellationToken);
        var definitions = await dbContext.WorkflowDefinitions.ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new DemoResetWorkflowCounts(definitions, versions, drafts, versionLayouts, draftLayouts, draftValidations);
    }

    private static async Task<DemoResetActivityCounts> ClearActivitiesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var factory = serviceProvider.GetRequiredService<IDbContextFactory<ActivitiesDesignDbContext>>();
        await using var dbContext = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var versions = await dbContext.ActivityDefinitionVersions.ExecuteDeleteAsync(cancellationToken);
        var definitions = await dbContext.ActivityDefinitions.ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new DemoResetActivityCounts(definitions, versions);
    }

    private static DemoClearPackageDropFolderResponse ClearPackageDropFolder(IWebHostEnvironment environment)
    {
        var dropFolder = GetPackageDropFolder(environment);
        Directory.CreateDirectory(dropFolder);

        var deletedFiles = 0;
        var deletedDirectories = 0;

        foreach (var file in Directory.EnumerateFiles(dropFolder))
        {
            File.Delete(file);
            deletedFiles++;
        }

        foreach (var directory in Directory.EnumerateDirectories(dropFolder))
        {
            Directory.Delete(directory, recursive: true);
            deletedDirectories++;
        }

        Console.WriteLine($"Demo package drop folder cleared: {deletedFiles} file(s), {deletedDirectories} folder(s) deleted from '{dropFolder}'.");
        return new DemoClearPackageDropFolderResponse(dropFolder, deletedFiles, deletedDirectories);
    }

    private static async Task<IResult> GetDefaultShellAsync(IWebHostEnvironment environment, CancellationToken cancellationToken)
    {
        var shellJson = await ReadShellDocumentAsync(environment, cancellationToken);
        if (shellJson.Error is not null)
            return shellJson.Error;

        var features = GetDefaultFeatures(shellJson.Document!)
            .Select(x => new DemoShellFeatureResponse(x.Key, x.Value?.ToJsonString(IndentedJsonOptions) ?? "{}"))
            .OrderBy(x => x.Name)
            .ToArray();

        return Results.Ok(new DemoShellResponse(shellJson.Path, shellJson.Json, features));
    }

    private static async Task<IResult> SaveShellDocumentAsync(JsonElement body, IWebHostEnvironment environment, CancellationToken cancellationToken)
    {
        var document = ParseShellDocument(body.GetRawText());
        if (document is null)
            return Results.BadRequest(new DemoErrorResponse("The supplied shell JSON is not valid."));

        if (GetDefaultFeatures(document).Count == 0)
            return Results.BadRequest(new DemoErrorResponse("The supplied JSON must contain CShells.Shells.default.Features."));

        var path = GetShellsJsonPath(environment);
        var json = document.ToJsonString(IndentedJsonOptions);
        await File.WriteAllTextAsync(path, json + Environment.NewLine, cancellationToken);
        Console.WriteLine("Demo shells.json saved.");

        return Results.Ok(new DemoSaveShellResponse(path, json));
    }

    private static async Task<DemoShellRestoreResult> RestoreShellDocumentFromBaselineAsync(IWebHostEnvironment environment, CancellationToken cancellationToken)
    {
        var baselinePath = GetShellsBaselineJsonPath(environment);
        var path = GetShellsJsonPath(environment);

        if (!File.Exists(baselinePath))
            return new(null, Results.NotFound(new DemoErrorResponse($"Could not find shells baseline at '{baselinePath}'.")));

        var baselineJson = await File.ReadAllTextAsync(baselinePath, cancellationToken);
        var document = ParseShellDocument(baselineJson);
        if (document is null)
            return new(null, Results.BadRequest(new DemoErrorResponse("shells.baseline.json is not valid JSON.")));

        if (GetDefaultFeatures(document).Count == 0)
            return new(null, Results.BadRequest(new DemoErrorResponse("shells.baseline.json must contain CShells.Shells.default.Features.")));

        var json = document.ToJsonString(IndentedJsonOptions);
        await File.WriteAllTextAsync(path, json + Environment.NewLine, cancellationToken);
        Console.WriteLine($"Demo shells.json restored from '{baselinePath}'.");

        return new(new DemoShellRestoreResponse(path, baselinePath, json), null);
    }

    private static async Task<IResult> UpdateFeatureAsync(string featureName, DemoFeatureUpdateRequest body, IWebHostEnvironment environment, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(featureName))
            return Results.BadRequest(new DemoErrorResponse("Feature name is required."));

        var path = GetShellsJsonPath(environment);
        if (!File.Exists(path))
            return Results.NotFound(new DemoErrorResponse($"Could not find shells.json at '{path}'."));

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var document = ParseShellDocument(json);
        if (document is null)
            return Results.BadRequest(new DemoErrorResponse("shells.json is not valid JSON."));

        var features = GetDefaultFeatures(document);
        if (body.Enabled)
        {
            features[featureName] = body.Configuration.ValueKind is JsonValueKind.Object
                ? JsonNode.Parse(body.Configuration.GetRawText()) ?? new JsonObject()
                : new JsonObject();
        }
        else
        {
            features.Remove(featureName);
        }

        var updated = document.ToJsonString(IndentedJsonOptions);
        await File.WriteAllTextAsync(path, updated + Environment.NewLine, cancellationToken);
        Console.WriteLine($"Demo shell feature '{featureName}' {(body.Enabled ? "enabled" : "disabled")}.");

        return Results.Ok(new DemoSaveShellResponse(path, updated));
    }

    private static JsonNode? ParseShellDocument(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<DemoShellDocumentReadResult> ReadShellDocumentAsync(IWebHostEnvironment environment, CancellationToken cancellationToken)
    {
        var path = GetShellsJsonPath(environment);
        if (!File.Exists(path))
            return new(path, "", null, Results.NotFound(new DemoErrorResponse($"Could not find shells.json at '{path}'.")));

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var document = ParseShellDocument(json);
        return document is null
            ? new(path, json, null, Results.BadRequest(new DemoErrorResponse("shells.json is not valid JSON.")))
            : new(path, json, document, null);
    }

    private static JsonObject GetDefaultFeatures(JsonNode document)
    {
        var features = document["CShells"]?["Shells"]?["default"]?["Features"] as JsonObject;
        return features ?? [];
    }

    private static string? GetDescriptorMetadata(ShellFeatureDescriptor descriptor, string key)
    {
        return descriptor.Metadata.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static void UpsertFeature(
        IDictionary<string, DemoFeatureCatalogItemBuilder> items,
        string featureId,
        string defaultConfigurationJson,
        Action<DemoFeatureCatalogItemBuilder> configure)
    {
        if (!items.TryGetValue(featureId, out var builder))
        {
            builder = new DemoFeatureCatalogItemBuilder
            {
                Id = featureId,
                ConfigurationJson = defaultConfigurationJson
            };
            items[featureId] = builder;
        }

        configure(builder);
    }

    private static IReadOnlyList<string> MergeCategories(string? category, IReadOnlyList<string>? categories)
    {
        var result = new List<string>();
        if (!string.IsNullOrWhiteSpace(category))
            result.Add(category);

        if (categories is not null)
            result.AddRange(categories.Where(x => !string.IsNullOrWhiteSpace(x)));

        return result.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static DemoPackageManifestReadResult ReadPackageManifest(DemoPackageSummary package, IWebHostEnvironment environment)
    {
        try
        {
            var archivePath = ResolvePackageArchivePath(package, environment);
            if (archivePath is null)
                return DemoPackageManifestReadResult.Missing("Could not locate the package archive.");

            using var stream = File.OpenRead(archivePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var entries = archive.Entries
                .Where(x => IsPackageManifestPath(x.FullName))
                .ToArray();
            var selected = entries.FirstOrDefault(x => IsRootPackageManifestPath(x.FullName))
                ?? entries.FirstOrDefault(x => IsFallbackPackageManifestPath(x.FullName));

            if (selected is null)
                return DemoPackageManifestReadResult.Missing("Package manifest not found.");

            using var manifestStream = selected.Open();
            using var memory = new MemoryStream();
            manifestStream.CopyTo(memory);
            var bytes = memory.ToArray();
            var json = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
            var manifest = JsonSerializer.Deserialize<DemoPackageManifest>(json, DemoPackageManifestJsonOptions);
            if (manifest is null)
                return DemoPackageManifestReadResult.Missing("Package manifest was empty.");

            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return DemoPackageManifestReadResult.Found(NormalizePackagePath(selected.FullName), hash, manifest);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            return DemoPackageManifestReadResult.Missing(ex.Message);
        }
    }

    private static string? ResolvePackageArchivePath(DemoPackageSummary package, IWebHostEnvironment environment)
    {
        if (File.Exists(package.InstallPath) && string.Equals(Path.GetExtension(package.InstallPath), ".nupkg", StringComparison.OrdinalIgnoreCase))
            return package.InstallPath;

        if (Directory.Exists(package.InstallPath))
        {
            var direct = Directory.EnumerateFiles(package.InstallPath, "*.nupkg", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (direct is not null)
                return direct;
        }

        var dropFolder = GetPackageDropFolder(environment);
        if (!Directory.Exists(dropFolder))
            return null;

        return Directory
            .EnumerateFiles(dropFolder, "*.nupkg", SearchOption.AllDirectories)
            .FirstOrDefault(path => string.Equals(Path.GetFileNameWithoutExtension(path), package.Id, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(path).StartsWith($"{package.Id}.", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPackageManifestPath(string path) =>
        IsRootPackageManifestPath(path) || IsFallbackPackageManifestPath(path);

    private static bool IsRootPackageManifestPath(string path) =>
        string.Equals(NormalizePackagePath(path), "elsa-package.json", StringComparison.OrdinalIgnoreCase);

    private static bool IsFallbackPackageManifestPath(string path) =>
        string.Equals(NormalizePackagePath(path), "build/elsa-package.json", StringComparison.OrdinalIgnoreCase);

    private static string NormalizePackagePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static IReadOnlyList<DemoFeatureSettingResponse> MapManifestSettings(IReadOnlyList<DemoPackageFeatureSettingManifest>? settings) =>
        settings?
            .Where(setting => !string.IsNullOrWhiteSpace(setting.Name))
            .Select(setting =>
            {
                var advanced = setting.Ui?.Advanced ?? false;
                var experimental = setting.Ui?.Experimental ?? false;
                var uiHint = setting.Ui?.Hint;
                var sensitive = setting.Sensitive || GetBoolExtension(setting.Extensions, "sensitive");
                var options = MapSettingOptions(setting.Ui?.Options, setting.Validation?.Enum);

                return new DemoFeatureSettingResponse(
                    Name: setting.Name!,
                    DisplayName: string.IsNullOrWhiteSpace(setting.DisplayName) ? setting.Name : setting.DisplayName,
                    Description: setting.Description,
                    Category: setting.Category,
                    Group: setting.Group,
                    ClrType: setting.ClrType,
                    JsonType: setting.JsonType,
                    Required: setting.Required,
                    DefaultValue: setting.DefaultValue,
                    Secret: setting.Secret,
                    Sensitive: sensitive,
                    RestartRequired: setting.RestartRequired,
                    Advanced: advanced,
                    Experimental: experimental,
                    UIHint: uiHint,
                    OptionsProvider: setting.Ui?.OptionsProvider,
                    Options: options);
            })
            .OrderBy(setting => setting.Category ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(setting => setting.DisplayName ?? setting.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? [];

    private static IReadOnlyList<DemoFeatureSettingOptionResponse> MapSettingOptions(
        IReadOnlyList<DemoPackageFeatureSettingOptionManifest>? uiOptions,
        IReadOnlyList<JsonElement>? validationOptions)
    {
        if (uiOptions is { Count: > 0 })
        {
            return uiOptions
                .Where(option => option.Value is not null)
                .Select(option => new DemoFeatureSettingOptionResponse(
                    option.Label ?? JsonElementToDisplayText(option.Value!.Value),
                    option.Value!.Value,
                    option.Description))
                .ToArray();
        }

        return validationOptions?
            .Select(option => new DemoFeatureSettingOptionResponse(JsonElementToDisplayText(option), option, null))
            .ToArray()
        ?? [];
    }

    private static string JsonElementToDisplayText(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            _ => value.GetRawText()
        };

    private static string? GetStringExtension(JsonObject? extensions, string name)
    {
        if (extensions is null ||
            !extensions.TryGetPropertyValue(name, out var node) ||
            node is not JsonValue value ||
            !value.TryGetValue<string>(out var text) ||
            string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text;
    }

    private static bool GetBoolExtension(JsonObject? extensions, string name)
    {
        if (extensions is null ||
            !extensions.TryGetPropertyValue(name, out var node) ||
            node is not JsonValue value ||
            !value.TryGetValue<bool>(out var result))
        {
            return false;
        }

        return result;
    }

    private static string GetPackageDropFolder(IWebHostEnvironment environment) =>
        Path.Combine(environment.ContentRootPath, "packages");

    private static string GetShellsJsonPath(IWebHostEnvironment environment) =>
        Path.Combine(environment.ContentRootPath, "shells.json");

    private static string GetShellsBaselineJsonPath(IWebHostEnvironment environment) =>
        Path.Combine(environment.ContentRootPath, "shells.baseline.json");

    private static readonly JsonSerializerOptions DemoPackageManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
}

internal sealed record DemoStateResponse(
    string PackageDropFolder,
    string ShellsJsonPath,
    IReadOnlyList<DemoPackageSummary> Packages,
    IReadOnlyList<DemoPackageEvent> PackageEvents);

internal sealed record DemoPackagesResponse(
    DateTimeOffset SnapshotAtUtc,
    DateTimeOffset PersistedAtUtc,
    string CorrelationId,
    IReadOnlyList<DemoPackageSummary> Packages);

internal sealed record DemoFeatureCatalogItemResponse(
    string Id,
    string? DisplayName,
    string? Description,
    IReadOnlyList<string> Categories,
    string SourceKind,
    string? PackageId,
    string? PackageVersion,
    bool Enabled,
    string ConfigurationJson,
    bool Advanced,
    bool Experimental,
    string? ManifestPath,
    string? ManifestHash,
    string? ReadError,
    IReadOnlyList<DemoFeatureSettingResponse> Settings);

internal sealed record DemoFeatureSettingResponse(
    string Name,
    string? DisplayName,
    string? Description,
    string? Category,
    string? Group,
    string? ClrType,
    string? JsonType,
    bool Required,
    JsonElement? DefaultValue,
    bool Secret,
    bool Sensitive,
    bool RestartRequired,
    bool Advanced,
    bool Experimental,
    string? UIHint,
    string? OptionsProvider,
    IReadOnlyList<DemoFeatureSettingOptionResponse> Options);

internal sealed record DemoFeatureSettingOptionResponse(string Label, JsonElement Value, string? Description);

internal sealed record DemoReconcileResponse(
    string Outcome,
    string CorrelationId,
    string? ReasonCode,
    bool? Skipped,
    bool? IsDegraded,
    IReadOnlyList<string> FailedPackages,
    IReadOnlyList<DemoPackageSummary> Added,
    IReadOnlyList<DemoPackageSummary> Updated,
    IReadOnlyList<string> Removed);

internal sealed record DemoUploadPackageResponse(string FileName, string Path, long Length);

internal sealed record DemoClearPackageDropFolderResponse(string Path, int DeletedFiles, int DeletedDirectories);

internal sealed record DemoShellRestoreResponse(string Path, string BaselinePath, string Json);

internal sealed record DemoShellRestoreResult(DemoShellRestoreResponse? Response, IResult? Error);

internal sealed record DemoResetResponse(
    DemoResetWorkflowCounts Workflows,
    DemoResetActivityCounts Activities,
    DemoClearPackageDropFolderResponse Packages,
    DemoShellRestoreResponse Shells,
    DemoResetReconcileResponse Reconcile,
    DemoReloadShellsResponse Reload);

internal sealed record DemoResetReconcileResponse(string Outcome, string CorrelationId, string? ReasonCode);

internal sealed record DemoResetWorkflowCounts(
    int Definitions,
    int Versions,
    int Drafts,
    int VersionLayouts,
    int DraftLayouts,
    int DraftValidations)
{
    public int TotalDeleted => Definitions + Versions + Drafts + VersionLayouts + DraftLayouts + DraftValidations;
}

internal sealed record DemoResetActivityCounts(int Definitions, int Versions)
{
    public int TotalDeleted => Definitions + Versions;
}

internal sealed record DemoWorkflowExecutableResponse(
    string ArtifactId,
    string ArtifactVersion,
    string ArtifactHash,
    string DefinitionId,
    string DefinitionVersionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    string? SourceKind,
    string? SourceId,
    string? SourceVersion,
    string RootActivityType,
    string RootActivityVersion,
    int NodeCount,
    int ResumeTargetCount)
{
    public static DemoWorkflowExecutableResponse FromExecutable(Elsa.Workflows.Runtime.Core.Models.WorkflowExecutable executable) =>
        new(
            executable.Identity.ArtifactId,
            executable.Identity.ArtifactVersion,
            executable.Identity.ArtifactHash,
            executable.Identity.DefinitionId,
            executable.Identity.DefinitionVersionId,
            executable.CreatedAt,
            executable.PublishedAt,
            executable.Identity.Source?.SourceKind,
            executable.Identity.Source?.SourceId,
            executable.Identity.Source?.SourceVersion,
            executable.RootActivity.ActivityType,
            executable.RootActivity.ActivityTypeVersion,
            executable.Nodes.Count,
            executable.ResumeTargets.Count);
}

internal sealed record DemoReloadShellsResponse(int FeatureDescriptorCount, int ReloadedShellCount);

internal sealed record DemoShellResponse(
    string Path,
    string Json,
    IReadOnlyList<DemoShellFeatureResponse> Features);

internal sealed record DemoShellFeatureResponse(string Name, string ConfigurationJson);

internal sealed record DemoFeatureUpdateRequest(bool Enabled, JsonElement Configuration);

internal sealed record DemoSaveShellResponse(string Path, string Json);

internal sealed record DemoErrorResponse(string Error);

internal sealed record DemoShellDocumentReadResult(string Path, string Json, JsonNode? Document, IResult? Error);

internal sealed class DemoFeatureCatalogItemBuilder
{
    public string Id { get; init; } = "";
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public IReadOnlyList<string> Categories { get; set; } = [];
    public string? SourceKind { get; set; }
    public string? PackageId { get; set; }
    public string? PackageVersion { get; set; }
    public bool Enabled { get; set; }
    public string ConfigurationJson { get; set; } = "{}";
    public bool Advanced { get; set; }
    public bool Experimental { get; set; }
    public string? ManifestPath { get; set; }
    public string? ManifestHash { get; set; }
    public string? ReadError { get; set; }
    public IReadOnlyList<DemoFeatureSettingResponse> Settings { get; set; } = [];

    public DemoFeatureCatalogItemResponse ToResponse() =>
        new(
            Id,
            DisplayName ?? Id,
            Description,
            Categories,
            SourceKind ?? "shell",
            PackageId,
            PackageVersion,
            Enabled,
            ConfigurationJson,
            Advanced,
            Experimental,
            ManifestPath,
            ManifestHash,
            ReadError,
            Settings);
}

internal sealed record DemoPackageManifestReadResult(string? Path, string? Hash, DemoPackageManifest? Manifest, string? ReadError)
{
    public static DemoPackageManifestReadResult Found(string path, string hash, DemoPackageManifest manifest) =>
        new(path, hash, manifest, null);

    public static DemoPackageManifestReadResult Missing(string readError) =>
        new(null, null, null, readError);
}

internal sealed record DemoPackageManifest(DemoPackageIdentity? Package, IReadOnlyList<DemoPackageFeatureManifest>? Features);

internal sealed record DemoPackageIdentity(string? Id, string? Version);

internal sealed record DemoPackageFeatureManifest(
    string? Id,
    string? DisplayName,
    string? Description,
    string? Category,
    IReadOnlyList<string>? Categories,
    bool Advanced,
    bool Experimental,
    IReadOnlyList<DemoPackageFeatureSettingManifest>? Settings,
    JsonObject? Extensions);

internal sealed record DemoPackageFeatureSettingManifest(
    string? Name,
    string? ClrType,
    string? JsonType,
    bool Required,
    JsonElement? DefaultValue,
    string? DisplayName,
    string? Description,
    string? Category,
    string? Group,
    DemoPackageFeatureSettingValidationManifest? Validation,
    bool Secret,
    bool Sensitive,
    bool RestartRequired,
    DemoPackageFeatureSettingUiManifest? Ui,
    JsonObject? Extensions);

internal sealed record DemoPackageFeatureSettingValidationManifest(
    [property: JsonPropertyName("enum")] IReadOnlyList<JsonElement>? Enum);

internal sealed record DemoPackageFeatureSettingUiManifest(
    string? Hint,
    bool Advanced,
    bool Experimental,
    IReadOnlyList<DemoPackageFeatureSettingOptionManifest>? Options,
    string? OptionsProvider);

internal sealed record DemoPackageFeatureSettingOptionManifest(
    string? Label,
    JsonElement? Value,
    string? Description);
