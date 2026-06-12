using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CShells.Lifecycle;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
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
        group.MapPost("/reset", ResetDemoAsync);
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

    private static async Task<IResult> ReloadShellsAsync(IServiceProvider serviceProvider, IShellRegistry shellRegistry, CancellationToken cancellationToken)
    {
        var featureDescriptorCount = await RefreshFeatureCatalogAsync(serviceProvider, cancellationToken);
        var reloadResults = await shellRegistry.ReloadActiveAsync(null, cancellationToken);
        Console.WriteLine($"Demo shells reloaded after refreshing {featureDescriptorCount} feature descriptor(s) and reloading {reloadResults.Count} active shell(s).");

        return Results.Ok(new DemoReloadShellsResponse(featureDescriptorCount, reloadResults.Count));
    }

    private static async Task<int> RefreshFeatureCatalogAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        // CShells does not currently expose feature-catalog refresh through a public contract.
        // The demo reload endpoint needs newly loaded Nuplane feature assemblies to be discoverable before shell reload.
        var catalogType = AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(assembly => assembly.GetType("CShells.Features.RuntimeFeatureCatalog", throwOnError: false))
            .FirstOrDefault(type => type is not null);

        if (catalogType is null)
            throw new InvalidOperationException("Could not find the CShells runtime feature catalog type.");

        var catalog = serviceProvider.GetRequiredService(catalogType);
        var refreshMethod = catalogType.GetMethod("RefreshAsync", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Could not find RuntimeFeatureCatalog.RefreshAsync.");

        var refreshTask = refreshMethod.Invoke(catalog, [cancellationToken]) as Task
            ?? throw new InvalidOperationException("RuntimeFeatureCatalog.RefreshAsync did not return a task.");

        await refreshTask.ConfigureAwait(false);

        var snapshot = refreshTask.GetType().GetProperty("Result")?.GetValue(refreshTask)
            ?? throw new InvalidOperationException("RuntimeFeatureCatalog.RefreshAsync did not return a snapshot.");

        var featureDescriptors = snapshot.GetType().GetProperty("FeatureDescriptors")?.GetValue(snapshot)
            ?? throw new InvalidOperationException("The runtime feature catalog snapshot did not expose feature descriptors.");

        return Convert.ToInt32(featureDescriptors.GetType().GetProperty("Count")?.GetValue(featureDescriptors));
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
        IShellRegistry shellRegistry,
        INuplaneAdminOperations nuplaneAdmin,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var shell = await shellRegistry.GetOrActivateAsync("default", cancellationToken);
        await using var shellScope = shell.BeginScope();

        var workflows = await ClearWorkflowsAsync(shellScope.ServiceProvider, cancellationToken);
        var activities = await ClearActivitiesAsync(shellScope.ServiceProvider, cancellationToken);
        var packages = ClearPackageDropFolder(environment);
        var reconcile = await nuplaneAdmin.TriggerReconcileAsync(cancellationToken);

        Console.WriteLine(
            $"Demo reset completed: {workflows.TotalDeleted} workflow row(s), {activities.TotalDeleted} activity row(s), " +
            $"{packages.DeletedFiles} package file(s), {packages.DeletedDirectories} package folder(s) deleted, " +
            $"Nuplane reconcile {reconcile.OutcomeCode} ({reconcile.CorrelationId}).");

        return Results.Ok(new DemoResetResponse(
            workflows,
            activities,
            packages,
            new DemoResetReconcileResponse(reconcile.OutcomeCode.ToString(), reconcile.CorrelationId, reconcile.ReasonCode)));
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
        var path = GetShellsJsonPath(environment);
        if (!File.Exists(path))
            return Results.NotFound(new DemoErrorResponse($"Could not find shells.json at '{path}'."));

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var document = ParseShellDocument(json);
        if (document is null)
            return Results.BadRequest(new DemoErrorResponse("shells.json is not valid JSON."));

        var features = GetDefaultFeatures(document)
            .Select(x => new DemoShellFeatureResponse(x.Key, x.Value?.ToJsonString(IndentedJsonOptions) ?? "{}"))
            .OrderBy(x => x.Name)
            .ToArray();

        return Results.Ok(new DemoShellResponse(path, json, features));
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

    private static JsonObject GetDefaultFeatures(JsonNode document)
    {
        var features = document["CShells"]?["Shells"]?["default"]?["Features"] as JsonObject;
        return features ?? [];
    }

    private static string GetPackageDropFolder(IWebHostEnvironment environment) =>
        Path.Combine(environment.ContentRootPath, "packages");

    private static string GetShellsJsonPath(IWebHostEnvironment environment) =>
        Path.Combine(environment.ContentRootPath, "shells.json");
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

internal sealed record DemoResetResponse(
    DemoResetWorkflowCounts Workflows,
    DemoResetActivityCounts Activities,
    DemoClearPackageDropFolderResponse Packages,
    DemoResetReconcileResponse Reconcile);

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

internal sealed record DemoReloadShellsResponse(int FeatureDescriptorCount, int ReloadedShellCount);

internal sealed record DemoShellResponse(
    string Path,
    string Json,
    IReadOnlyList<DemoShellFeatureResponse> Features);

internal sealed record DemoShellFeatureResponse(string Name, string ConfigurationJson);

internal sealed record DemoFeatureUpdateRequest(bool Enabled, JsonElement Configuration);

internal sealed record DemoSaveShellResponse(string Path, string Json);

internal sealed record DemoErrorResponse(string Error);
