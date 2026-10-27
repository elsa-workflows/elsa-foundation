using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Studio.Api.Authorization;
using Elsa.Studio.Api.Contracts;
using global::Nuplane.Admin;
using global::Nuplane.Abstractions.PackageStoreProtection;

namespace Elsa.Studio.Hosting.Nuplane;

internal static class ModuleManagementEndpoints
{
    internal const string FeedPrefix = "Nuplane:Setup:Feeds";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/_elsa/module-management");
        group.MapGet("/registry", RegistryAsync).RequireAuthorization(StudioBridgeAuthorization.ModuleManagementReadPolicyName);
        group.MapPost("/packages/upload", UploadAsync).RequireAuthorization(StudioBridgeAuthorization.ModuleManagementManagePolicyName)
            .Accepts<IFormFile>("multipart/form-data").DisableAntiforgery();
        group.MapDelete("/packages/drop-folder/{fileName}", DeleteAsync).RequireAuthorization(StudioBridgeAuthorization.ModuleManagementManagePolicyName);
        group.MapPost("/reconcile", ReconcileAsync).RequireAuthorization(StudioBridgeAuthorization.ModuleManagementManagePolicyName);
        group.MapPost("/feeds", AddFeedAsync).RequireAuthorization(StudioBridgeAuthorization.ModuleManagementManagePolicyName);
        group.MapPut("/feeds/{name}", UpdateFeedAsync).RequireAuthorization(StudioBridgeAuthorization.ModuleManagementManagePolicyName);
        group.MapDelete("/feeds/{name}", DeleteFeedAsync).RequireAuthorization(StudioBridgeAuthorization.ModuleManagementManagePolicyName);
    }

    private static async Task<IResult> RegistryAsync(IStudioModuleManifestProvider manifests, PackageContentReader reader,
        StudioNuplaneSettings settings, ManagementConfigurationStore management, IConfiguration configuration,
        IWebHostEnvironment environment, HttpContext context)
    {
        try
        {
            // Hosting's root adapter borrows the selected shell across the asynchronous manifest query.
            var modules = await manifests.GetModuleRegistry(context.RequestAborted);
            var snapshot = await reader.GetPackagesAsync(context.RequestAborted);
            var diagnostics = new List<ModuleManagementDiagnostic>();
            var packages = new List<ModuleManagementPackage>();
            foreach (var package in snapshot.Packages)
                packages.Add(await PackageRegistryReader.ReadAsync(package, reader, diagnostics, context.RequestAborted));
            var policy = management.ReadRetention();
            return Results.Ok(new ModuleManagementRegistryResponse(
                new("studio", "Studio", environment.ApplicationName, environment.ContentRootPath), DateTimeOffset.UtcNow,
                modules.Modules.Select(module => ModuleManagementModule.FromStudioModule(module, environment.ApplicationName)).ToArray(),
                packages, DropFolderPackages(settings.DropFolder), ReadFeeds(configuration),
                new(policy.KeepNewestInactiveVersionsPerPackage, policy.Status),
                new(true, management.CanWrite(FeedPrefix), true, true, true,
                    management.CanWrite(ManagementConfigurationStore.RetentionPrefix)), diagnostics));
        }
        catch (PackageStoreAdmissionException error)
        {
            context.Response.Headers.RetryAfter = "1";
            return Results.Json(new { Error = "The package registry is temporarily unavailable.", Reason = error.Reason.ToString() }, statusCode: 503);
        }
    }

    private static async Task<IResult> UploadAsync(HttpRequest request, StudioNuplaneSettings settings,
        IServiceScopeFactory scopes, IHostApplicationLifetime lifetime, ILoggerFactory logs, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType) return Error("Expected multipart form data.");
        IFormCollection form;
        try { form = await request.ReadFormAsync(cancellationToken); }
        catch (InvalidDataException) { return Error("The multipart form is invalid."); }
        var file = form.Files.GetFile("package") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0) return Error("Choose a non-empty .nupkg file.");
        if (FileNameError(file.FileName) is { } invalid) return Error(invalid);
        Directory.CreateDirectory(settings.DropFolder);
        var destination = Path.Combine(settings.DropFolder, file.FileName);
        var temporary = Path.Combine(settings.DropFolder, $".{Guid.NewGuid():N}.upload");
        try
        {
            await using (var output = File.Create(temporary)) await file.CopyToAsync(output, cancellationToken);
            try
            {
                using var archive = ZipFile.OpenRead(temporary);
                _ = archive.Entries.Count;
            }
            catch (Exception error) when (error is InvalidDataException or IOException)
            {
                return Error("The uploaded file is not a valid .nupkg package.");
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        QueueReconciliation(scopes, lifetime, logs, "package upload");
        return Results.Ok(new ModuleManagementUploadResponse(file.FileName, destination, file.Length,
            ModuleManagementReconcileResponse.Deferred("Package uploaded; Nuplane reconciliation is scheduled. Discovery and activation depend on the configured sources and loading policy."), true, false));
    }

    private static Task<IResult> DeleteAsync(string fileName, StudioNuplaneSettings settings, IServiceScopeFactory scopes,
        IHostApplicationLifetime lifetime, ILoggerFactory logs)
    {
        if (FileNameError(fileName) is { } invalid) return Task.FromResult(Error(invalid));
        var path = Path.Combine(settings.DropFolder, fileName);
        if (!File.Exists(path)) return Task.FromResult<IResult>(Results.NotFound(new ModuleManagementErrorResponse("Package was not found in the installed drop folder.")));
        File.Delete(path);
        QueueReconciliation(scopes, lifetime, logs, "package deletion");
        return Task.FromResult<IResult>(Results.Ok(new ModuleManagementDeleteResponse(fileName, path,
            ModuleManagementReconcileResponse.Deferred("Package deleted from the drop folder; Nuplane reconciliation is scheduled."), true, false)));
    }

    private static async Task<IResult> ReconcileAsync(INuplaneAdminOperations admin, CancellationToken cancellationToken)
    {
        var outcome = await admin.TriggerReconcileAsync(cancellationToken);
        return Results.Ok(new ModuleManagementOperationResponse("reconcile", ModuleManagementReconcileResponse.FromOutcome(outcome), true, false,
            "Nuplane reconciliation returned the reported outcome."));
    }

    private static void QueueReconciliation(IServiceScopeFactory scopes, IHostApplicationLifetime lifetime, ILoggerFactory logs, string operation)
    {
        var stopping = lifetime.ApplicationStopping;
        var logger = logs.CreateLogger("Elsa.Studio.Hosting.Nuplane.ModuleManagement");
        _ = Task.Run(async () =>
        {
            if (stopping.IsCancellationRequested) return;
            try
            {
                using var scope = scopes.CreateScope();
                var outcome = await scope.ServiceProvider.GetRequiredService<INuplaneAdminOperations>().TriggerReconcileAsync(stopping);
                logger.LogInformation("Nuplane reconciliation after {Operation}: {Outcome}, correlation {CorrelationId}, reason {ReasonCode}.",
                    operation, outcome.OutcomeCode, outcome.CorrelationId, outcome.ReasonCode);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (stopping.IsCancellationRequested) { }
            catch (Exception error) { logger.LogError(error, "Nuplane reconciliation failed after {Operation}.", operation); }
        });
    }

    private static Task<IResult> AddFeedAsync(ModuleManagementFeed feed, ManagementConfigurationStore store, CancellationToken token)
    {
        if (FeedError(feed) is { } invalid) return Task.FromResult(Error(invalid));
        return MutateFeedsAsync(store, token, feeds =>
        {
            if (IndexOf(feeds, feed.Name) >= 0) return Results.Conflict(new ModuleManagementErrorResponse($"Feed '{feed.Name}' already exists."));
            feeds.Add(FeedJson(feed, feed.Name));
            return Results.Ok(new ModuleManagementConfigurationWriteResponse(true, "Feed added. Restart the host to activate registration changes."));
        });
    }

    private static Task<IResult> UpdateFeedAsync(string name, ModuleManagementFeed feed, ManagementConfigurationStore store, CancellationToken token)
    {
        if (FeedError(feed with { Name = name }) is { } invalid) return Task.FromResult(Error(invalid));
        return MutateFeedsAsync(store, token, feeds =>
        {
            var index = IndexOf(feeds, name);
            if (index < 0) return Results.NotFound(new ModuleManagementErrorResponse($"Feed '{name}' was not found."));
            feeds[index] = FeedJson(feed, name, feeds[index] as JsonObject);
            return Results.Ok(new ModuleManagementConfigurationWriteResponse(true, "Feed updated. Restart the host to activate registration changes."));
        });
    }

    private static Task<IResult> DeleteFeedAsync(string name, ManagementConfigurationStore store, CancellationToken token) =>
        MutateFeedsAsync(store, token, feeds =>
        {
            var index = IndexOf(feeds, name);
            if (index < 0) return Results.NotFound(new ModuleManagementErrorResponse($"Feed '{name}' was not found."));
            feeds.RemoveAt(index);
            return Results.Ok(new ModuleManagementConfigurationWriteResponse(true, "Feed deleted. Restart the host to activate registration changes."));
        });

    private static async Task<IResult> MutateFeedsAsync(ManagementConfigurationStore store, CancellationToken token, Func<JsonArray, IResult> mutate)
    {
        IResult result = Results.BadRequest();
        try
        {
            await store.MutateIfAsync(FeedPrefix, document =>
            {
                var setup = ManagementConfigurationStore.Object(ManagementConfigurationStore.Object(document, "Nuplane"), "Setup");
                var feeds = setup["Feeds"] as JsonArray;
                if (feeds is null) setup["Feeds"] = feeds = new JsonArray();
                result = mutate(feeds);
                return result is IStatusCodeHttpResult { StatusCode: >= 200 and < 300 };
            }, token);
            return result;
        }
        catch (ConfigurationReadOnlyException error) { return Results.Conflict(new { Error = error.Message, Code = "configuration-read-only" }); }
    }

    private static IReadOnlyList<ModuleManagementFeed> ReadFeeds(IConfiguration configuration) =>
        configuration.GetSection(FeedPrefix).GetChildren().Select(feed => new ModuleManagementFeed(
            feed["Name"] ?? "", feed["ServiceIndex"], feed["DirectoryPath"], feed["Credentials"], feed.GetValue<bool>("IncludeAll"),
            feed.GetSection("IncludePatterns").GetChildren().Select(item => item.Value).OfType<string>().ToArray(),
            new(feed.GetValue<bool?>("Directory:Watch") ?? true, feed["Directory:DebounceWindow"] ?? "00:00:01")))
        .Where(feed => !string.IsNullOrWhiteSpace(feed.Name)).OrderBy(feed => feed.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    private static JsonObject FeedJson(ModuleManagementFeed feed, string name, JsonObject? previous = null)
    {
        var result = previous?.DeepClone().AsObject() ?? new JsonObject();
        result["Name"] = name;
        result["ServiceIndex"] = feed.ServiceIndex;
        result["DirectoryPath"] = feed.DirectoryPath;
        result["Credentials"] = feed.Credentials;
        result["IncludeAll"] = feed.IncludeAll;
        result["IncludePatterns"] = JsonSerializer.SerializeToNode(feed.IncludePatterns ?? [], JsonOptions);
        var directory = ManagementConfigurationStore.Object(result, "Directory");
        directory["Watch"] = feed.Directory?.Watch ?? true;
        directory["DebounceWindow"] = feed.Directory?.DebounceWindow ?? "00:00:01";
        return result;
    }

    private static int IndexOf(JsonArray feeds, string name)
    {
        for (var index = 0; index < feeds.Count; index++)
            if (string.Equals((string?)feeds[index]?["Name"], name, StringComparison.OrdinalIgnoreCase)) return index;
        return -1;
    }

    private static string? FeedError(ModuleManagementFeed feed)
    {
        if (string.IsNullOrWhiteSpace(feed.Name)) return "Feed name is required.";
        if (feed.Name != feed.Name.Trim()) return "Feed names cannot start or end with whitespace.";
        if (!string.IsNullOrWhiteSpace(feed.ServiceIndex) && !string.IsNullOrWhiteSpace(feed.DirectoryPath)) return "Choose a service index or a directory path, not both.";
        if (string.IsNullOrWhiteSpace(feed.ServiceIndex) && string.IsNullOrWhiteSpace(feed.DirectoryPath)) return "A service index or directory path is required.";
        if (!string.IsNullOrWhiteSpace(feed.ServiceIndex) && (!Uri.TryCreate(feed.ServiceIndex, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))) return "The service index must be an absolute HTTP or HTTPS URL.";
        if (feed.DirectoryPath?.Contains('\0') == true) return "The directory path is invalid.";
        if (feed.IncludePatterns?.Any(string.IsNullOrWhiteSpace) == true) return "Include patterns must be non-blank strings.";
        if (feed.Directory is { } directory && (!TimeSpan.TryParse(directory.DebounceWindow, out var delay) || delay < TimeSpan.Zero)) return "Directory debounce must be a nonnegative time span.";
        return null;
    }

    private static string? FileNameError(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\', ':', '\0']) >= 0 || Path.GetFileName(name) != name
            ? "A bare .nupkg file name is required."
            : !string.Equals(Path.GetExtension(name), ".nupkg", StringComparison.OrdinalIgnoreCase) ? "Only .nupkg files can be uploaded." : null;

    private static IReadOnlyList<ModuleManagementDropFolderPackage> DropFolderPackages(string path) => !Directory.Exists(path) ? [] :
        Directory.EnumerateFiles(path, "*.nupkg", SearchOption.TopDirectoryOnly).Select(path => new FileInfo(path))
            .Select(file => new ModuleManagementDropFolderPackage(file.Name, file.FullName, file.Length, file.LastWriteTimeUtc))
            .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase).ToArray();

    private static IResult Error(string message) => Results.BadRequest(new ModuleManagementErrorResponse(message));
}
