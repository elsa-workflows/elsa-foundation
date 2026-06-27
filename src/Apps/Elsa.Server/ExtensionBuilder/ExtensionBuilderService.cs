using Elsa.Modularity.Core.Contracts;
using Microsoft.Extensions.Logging;
using Nuplane.Admin;

namespace Elsa.Server.ExtensionBuilder;

internal interface IExtensionBuilderService
{
    ExtensionBuilderCapabilities GetCapabilities(ExtensionBuilderCaller caller);
    Task<IReadOnlyList<ExtensionTemplate>> ListTemplatesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExtensionRepositorySummary>> ListRepositoriesAsync(ExtensionBuilderCaller caller, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExtensionWorkspace>> ListWorkspacesAsync(ExtensionBuilderCaller caller, CancellationToken cancellationToken = default);
    Task<ExtensionWorkspace> CreateWorkspaceAsync(ExtensionBuilderCaller caller, CreateWorkspaceRequest request, CancellationToken cancellationToken = default);
    Task<ExtensionWorkspace> AttachServerLocalRepositoryAsync(ExtensionBuilderCaller caller, AttachServerLocalRepositoryRequest request, CancellationToken cancellationToken = default);
    Task<ExtensionWorkspace> CloneRepositoryAsync(ExtensionBuilderCaller caller, CloneRepositoryRequest request, CancellationToken cancellationToken = default);
    Task<ExtensionWorkspace?> GetWorkspaceAsync(ExtensionBuilderCaller caller, string workspaceId, CancellationToken cancellationToken = default);
    Task<bool> DeleteWorkspaceAsync(ExtensionBuilderCaller caller, string workspaceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExtensionWorkingCopySummary>?> ListWorkingCopiesAsync(ExtensionBuilderCaller caller, string workspaceId, string? sessionId, CancellationToken cancellationToken = default);
    Task<ExtensionWorkingCopySummary?> SelectWorkingCopyAsync(ExtensionBuilderCaller caller, string workspaceId, SelectWorkingCopyRequest request, CancellationToken cancellationToken = default);
    Task<RepositoryTree?> GetRepositoryTreeAsync(ExtensionBuilderCaller caller, string workspaceId, string? selectedSolutionPath, CancellationToken cancellationToken = default);
    Task<RepositoryFile?> ReadRepositoryFileAsync(ExtensionBuilderCaller caller, string workspaceId, string path, CancellationToken cancellationToken = default);
    Task<RepositoryFile?> WriteRepositoryFileAsync(ExtensionBuilderCaller caller, string workspaceId, string path, WriteProjectFileRequest request, CancellationToken cancellationToken = default);
    Task<AppliedRepositoryTemplate?> ApplyRepositoryTemplateAsync(ExtensionBuilderCaller caller, string workspaceId, ApplyRepositoryTemplateRequest request, CancellationToken cancellationToken = default);
    Task<RepositoryFile?> MoveRepositoryFileAsync(ExtensionBuilderCaller caller, string workspaceId, MoveRepositoryFileRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteRepositoryFileAsync(ExtensionBuilderCaller caller, string workspaceId, string path, CancellationToken cancellationToken = default);
    Task<SourceControlStatus?> GetSourceControlStatusAsync(ExtensionBuilderCaller caller, string workspaceId, CancellationToken cancellationToken = default);
    Task<SourceControlDiff?> GetSourceControlDiffAsync(ExtensionBuilderCaller caller, string workspaceId, string path, bool staged, CancellationToken cancellationToken = default);
    Task<SourceControlStatus?> StageRepositoryFileAsync(ExtensionBuilderCaller caller, string workspaceId, SourceControlPathRequest request, CancellationToken cancellationToken = default);
    Task<SourceControlStatus?> UnstageRepositoryFileAsync(ExtensionBuilderCaller caller, string workspaceId, SourceControlPathRequest request, CancellationToken cancellationToken = default);
    Task<SourceControlStatus?> StageAllRepositoryChangesAsync(ExtensionBuilderCaller caller, string workspaceId, CancellationToken cancellationToken = default);
    Task<SourceControlCommitResult?> CommitRepositoryChangesAsync(ExtensionBuilderCaller caller, string workspaceId, SourceControlCommitRequest request, CancellationToken cancellationToken = default);
    Task<ExtensionProject> CreateProjectAsync(ExtensionBuilderCaller caller, string workspaceId, CreateProjectRequest request, CancellationToken cancellationToken = default);
    Task<ExtensionProject?> GetProjectAsync(ExtensionBuilderCaller caller, string projectId, CancellationToken cancellationToken = default);
    Task<bool> DeleteProjectAsync(ExtensionBuilderCaller caller, string projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectFileSummary>?> ListProjectFilesAsync(ExtensionBuilderCaller caller, string projectId, CancellationToken cancellationToken = default);
    Task<ProjectFile?> ReadProjectFileAsync(ExtensionBuilderCaller caller, string projectId, string path, CancellationToken cancellationToken = default);
    Task<ProjectFile?> WriteProjectFileAsync(ExtensionBuilderCaller caller, string projectId, string path, WriteProjectFileRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteProjectFileAsync(ExtensionBuilderCaller caller, string projectId, string path, CancellationToken cancellationToken = default);
    Task<BuildResult?> SubmitBuildAsync(ExtensionBuilderCaller caller, string projectId, CancellationToken cancellationToken = default);
    Task<BuildResult?> GetBuildAsync(ExtensionBuilderCaller caller, string buildId, CancellationToken cancellationToken = default);
    Task<string?> GetBuildLogAsync(ExtensionBuilderCaller caller, string buildId, CancellationToken cancellationToken = default);
    Task<BuildArtifact?> GetBuildArtifactAsync(ExtensionBuilderCaller caller, string buildId, CancellationToken cancellationToken = default);
    Task<PackagePromotionResult?> PromoteBuildAsync(ExtensionBuilderCaller caller, string buildId, CancellationToken cancellationToken = default);
    Task<ExtensionRuntimeStatus?> GetRuntimeStatusAsync(ExtensionBuilderCaller caller, string projectId, CancellationToken cancellationToken = default);
    Task<PackagePromotionResult?> RollbackPackageAsync(ExtensionBuilderCaller caller, string projectId, RollbackPackageRequest request, CancellationToken cancellationToken = default);
    Task<ExtensionBuilderOperationResponse?> RetryReconciliationAsync(ExtensionBuilderCaller caller, string projectId, CancellationToken cancellationToken = default);
}

internal sealed class ExtensionBuilderService(
    IExtensionBuilderStorage storage,
    IExtensionBuilderTemplateCatalog templates,
    IExtensionBuilderBuildQueue buildQueue,
    IExtensionBuilderPromotionService promotionService,
    INuplaneAdminOperations nuplaneAdmin,
    IFeatureManagementService featureManagement,
    ILogger<ExtensionBuilderService> logger) : IExtensionBuilderService
{
    public ExtensionBuilderCapabilities GetCapabilities(ExtensionBuilderCaller caller) =>
        ExtensionBuilderCapabilities.FromTrust(caller.HasManagementAccess && caller.IsTrusted);

    public Task<IReadOnlyList<ExtensionTemplate>> ListTemplatesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(templates.List());

    public Task<IReadOnlyList<ExtensionRepositorySummary>> ListRepositoriesAsync(ExtensionBuilderCaller caller, CancellationToken cancellationToken = default) =>
        storage.ListRepositoriesAsync(caller.OwnerId, cancellationToken);

    public Task<IReadOnlyList<ExtensionWorkspace>> ListWorkspacesAsync(ExtensionBuilderCaller caller, CancellationToken cancellationToken = default) =>
        storage.ListWorkspacesAsync(caller.OwnerId, cancellationToken);

    public Task<ExtensionWorkspace> CreateWorkspaceAsync(ExtensionBuilderCaller caller, CreateWorkspaceRequest request, CancellationToken cancellationToken = default) =>
        storage.CreateWorkspaceAsync(caller.OwnerId, caller.DisplayName, request.DisplayName, cancellationToken);

    public Task<ExtensionWorkspace> AttachServerLocalRepositoryAsync(ExtensionBuilderCaller caller, AttachServerLocalRepositoryRequest request, CancellationToken cancellationToken = default)
    {
        if (!caller.HasManagementAccess || !caller.IsTrusted)
            throw new UnauthorizedAccessException("Server-local repository attach requires an administrative caller.");

        return storage.AttachServerLocalRepositoryAsync(caller.OwnerId, caller.DisplayName, request, cancellationToken);
    }

    public Task<ExtensionWorkspace> CloneRepositoryAsync(ExtensionBuilderCaller caller, CloneRepositoryRequest request, CancellationToken cancellationToken = default) =>
        storage.CloneRepositoryAsync(caller.OwnerId, caller.DisplayName, request, cancellationToken);

    public Task<ExtensionWorkspace?> GetWorkspaceAsync(ExtensionBuilderCaller caller, string workspaceId, CancellationToken cancellationToken = default) =>
        storage.GetWorkspaceAsync(workspaceId, caller.OwnerId, cancellationToken);

    public Task<bool> DeleteWorkspaceAsync(ExtensionBuilderCaller caller, string workspaceId, CancellationToken cancellationToken = default) =>
        storage.DeleteWorkspaceAsync(workspaceId, caller.OwnerId, cancellationToken);

    public Task<IReadOnlyList<ExtensionWorkingCopySummary>?> ListWorkingCopiesAsync(ExtensionBuilderCaller caller, string workspaceId, string? sessionId, CancellationToken cancellationToken = default) =>
        storage.ListWorkingCopiesAsync(workspaceId, caller.OwnerId, sessionId, cancellationToken);

    public Task<ExtensionWorkingCopySummary?> SelectWorkingCopyAsync(ExtensionBuilderCaller caller, string workspaceId, SelectWorkingCopyRequest request, CancellationToken cancellationToken = default) =>
        storage.SelectWorkingCopyAsync(workspaceId, caller.OwnerId, request, cancellationToken);

    public Task<RepositoryTree?> GetRepositoryTreeAsync(ExtensionBuilderCaller caller, string workspaceId, string? selectedSolutionPath, CancellationToken cancellationToken = default) =>
        storage.GetRepositoryTreeAsync(workspaceId, caller.OwnerId, selectedSolutionPath, cancellationToken);

    public Task<RepositoryFile?> ReadRepositoryFileAsync(ExtensionBuilderCaller caller, string workspaceId, string path, CancellationToken cancellationToken = default) =>
        storage.ReadRepositoryFileAsync(workspaceId, caller.OwnerId, path, cancellationToken);

    public Task<RepositoryFile?> WriteRepositoryFileAsync(ExtensionBuilderCaller caller, string workspaceId, string path, WriteProjectFileRequest request, CancellationToken cancellationToken = default) =>
        storage.WriteRepositoryFileAsync(workspaceId, caller.OwnerId, path, request.Content, cancellationToken);

    public Task<AppliedRepositoryTemplate?> ApplyRepositoryTemplateAsync(ExtensionBuilderCaller caller, string workspaceId, ApplyRepositoryTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var template = templates.Find(request.TemplateId) ?? throw new ArgumentException($"Trusted template '{request.TemplateId}' was not found.", nameof(request));
        return storage.ApplyRepositoryTemplateAsync(workspaceId, caller.OwnerId, template, request, cancellationToken);
    }

    public Task<RepositoryFile?> MoveRepositoryFileAsync(ExtensionBuilderCaller caller, string workspaceId, MoveRepositoryFileRequest request, CancellationToken cancellationToken = default) =>
        storage.MoveRepositoryFileAsync(workspaceId, caller.OwnerId, request, cancellationToken);

    public Task<bool> DeleteRepositoryFileAsync(ExtensionBuilderCaller caller, string workspaceId, string path, CancellationToken cancellationToken = default) =>
        storage.DeleteRepositoryFileAsync(workspaceId, caller.OwnerId, path, cancellationToken);

    public Task<SourceControlStatus?> GetSourceControlStatusAsync(ExtensionBuilderCaller caller, string workspaceId, CancellationToken cancellationToken = default) =>
        storage.GetSourceControlStatusAsync(workspaceId, caller.OwnerId, cancellationToken);

    public Task<SourceControlDiff?> GetSourceControlDiffAsync(ExtensionBuilderCaller caller, string workspaceId, string path, bool staged, CancellationToken cancellationToken = default) =>
        storage.GetSourceControlDiffAsync(workspaceId, caller.OwnerId, path, staged, cancellationToken);

    public Task<SourceControlStatus?> StageRepositoryFileAsync(ExtensionBuilderCaller caller, string workspaceId, SourceControlPathRequest request, CancellationToken cancellationToken = default) =>
        storage.StageRepositoryFileAsync(workspaceId, caller.OwnerId, request.Path, cancellationToken);

    public Task<SourceControlStatus?> UnstageRepositoryFileAsync(ExtensionBuilderCaller caller, string workspaceId, SourceControlPathRequest request, CancellationToken cancellationToken = default) =>
        storage.UnstageRepositoryFileAsync(workspaceId, caller.OwnerId, request.Path, cancellationToken);

    public Task<SourceControlStatus?> StageAllRepositoryChangesAsync(ExtensionBuilderCaller caller, string workspaceId, CancellationToken cancellationToken = default) =>
        storage.StageAllRepositoryChangesAsync(workspaceId, caller.OwnerId, cancellationToken);

    public Task<SourceControlCommitResult?> CommitRepositoryChangesAsync(ExtensionBuilderCaller caller, string workspaceId, SourceControlCommitRequest request, CancellationToken cancellationToken = default) =>
        storage.CommitRepositoryChangesAsync(workspaceId, caller.OwnerId, request, cancellationToken);

    public async Task<ExtensionProject> CreateProjectAsync(ExtensionBuilderCaller caller, string workspaceId, CreateProjectRequest request, CancellationToken cancellationToken = default)
    {
        var template = templates.Find(request.TemplateId) ?? throw new ArgumentException($"Template '{request.TemplateId}' was not found.", nameof(request));
        if (template.Scope is not ExtensionTemplateScope.Project)
            throw new ArgumentException($"Template '{request.TemplateId}' is not a project template.", nameof(request));
        return await storage.CreateProjectAsync(workspaceId, caller.OwnerId, template, request, cancellationToken);
    }

    public Task<ExtensionProject?> GetProjectAsync(ExtensionBuilderCaller caller, string projectId, CancellationToken cancellationToken = default) =>
        storage.GetProjectAsync(projectId, caller.OwnerId, cancellationToken);

    public Task<bool> DeleteProjectAsync(ExtensionBuilderCaller caller, string projectId, CancellationToken cancellationToken = default) =>
        storage.DeleteProjectAsync(projectId, caller.OwnerId, cancellationToken);

    public Task<IReadOnlyList<ProjectFileSummary>?> ListProjectFilesAsync(ExtensionBuilderCaller caller, string projectId, CancellationToken cancellationToken = default) =>
        storage.ListFilesAsync(projectId, caller.OwnerId, cancellationToken);

    public Task<ProjectFile?> ReadProjectFileAsync(ExtensionBuilderCaller caller, string projectId, string path, CancellationToken cancellationToken = default) =>
        storage.ReadFileAsync(projectId, caller.OwnerId, path, cancellationToken);

    public Task<ProjectFile?> WriteProjectFileAsync(ExtensionBuilderCaller caller, string projectId, string path, WriteProjectFileRequest request, CancellationToken cancellationToken = default) =>
        storage.WriteFileAsync(projectId, caller.OwnerId, path, request.Content, cancellationToken);

    public Task<bool> DeleteProjectFileAsync(ExtensionBuilderCaller caller, string projectId, string path, CancellationToken cancellationToken = default) =>
        storage.DeleteFileAsync(projectId, caller.OwnerId, path, cancellationToken);

    public async Task<BuildResult?> SubmitBuildAsync(ExtensionBuilderCaller caller, string projectId, CancellationToken cancellationToken = default)
    {
        var project = await storage.GetProjectAsync(projectId, caller.OwnerId, cancellationToken);
        if (project is null)
            return null;

        var snapshot = await storage.CreateSourceSnapshotAsync(project.Id, caller.OwnerId, cancellationToken);
        if (snapshot is null)
            return null;

        var buildId = $"build_{Guid.NewGuid():N}";
        var created = new BuildResult(
            buildId,
            project.Id,
            project.WorkspaceId,
            snapshot.Id,
            BuildStatus.Pending,
            [],
            null,
            storage.GetBuildLogPath(buildId),
            DateTimeOffset.UtcNow,
            null,
            null);
        if (!await storage.SaveBuildAsync(created, cancellationToken))
        {
            DeleteBuildDirectory(runningLogPath: created.LogPath);
            return null;
        }

        var running = created with { Status = BuildStatus.Running, StartedAt = DateTimeOffset.UtcNow };
        if (!await storage.SaveBuildAsync(running, CancellationToken.None))
        {
            DeleteBuildDirectory(running.LogPath);
            return null;
        }

        await buildQueue.EnqueueAsync(new(project, snapshot, buildId, running.LogPath, storage.GetBuildArtifactsPath(buildId)), CancellationToken.None);
        return await storage.GetBuildAsync(buildId, caller.OwnerId, CancellationToken.None) ?? running;
    }

    public Task<BuildResult?> GetBuildAsync(ExtensionBuilderCaller caller, string buildId, CancellationToken cancellationToken = default) =>
        storage.GetBuildAsync(buildId, caller.OwnerId, cancellationToken);

    public async Task<string?> GetBuildLogAsync(ExtensionBuilderCaller caller, string buildId, CancellationToken cancellationToken = default)
    {
        var build = await storage.GetBuildAsync(buildId, caller.OwnerId, cancellationToken);
        if (build is null || !File.Exists(build.LogPath))
            return null;

        return await File.ReadAllTextAsync(build.LogPath, cancellationToken);
    }

    public async Task<BuildArtifact?> GetBuildArtifactAsync(ExtensionBuilderCaller caller, string buildId, CancellationToken cancellationToken = default)
    {
        var build = await storage.GetBuildAsync(buildId, caller.OwnerId, cancellationToken);
        return build?.Artifact is { } artifact && File.Exists(artifact.Path) ? artifact : null;
    }

    public async Task<PackagePromotionResult?> PromoteBuildAsync(ExtensionBuilderCaller caller, string buildId, CancellationToken cancellationToken = default)
    {
        var build = await storage.GetBuildAsync(buildId, caller.OwnerId, cancellationToken);
        if (build is null)
            return null;
        if (!await storage.ProjectExistsAsync(build.ProjectId, cancellationToken))
            return null;

        var result = await promotionService.PromoteAsync(build, cancellationToken);
        if (result is { Status: PromotionStatus.Accepted, PublishedPackage: not null, ReconcileOutcome: not null } && build.Artifact is not null)
        {
            var recorded = await storage.AddPromotionAsync(build.ProjectId, new(
                result.PublishedPackage.PackageId,
                result.PublishedPackage.Version,
                build.Artifact.Path,
                result.PublishedPackage.Path,
                DateTimeOffset.UtcNow,
                result.ReconcileOutcome,
                result.RequiresReload,
                result.RequiresRestart,
                DateTimeOffset.UtcNow), CancellationToken.None);
            if (!recorded)
            {
                DeleteFileIfExists(result.PublishedPackage.Path);
                return null;
            }
        }

        return result;
    }

    public async Task<ExtensionRuntimeStatus?> GetRuntimeStatusAsync(ExtensionBuilderCaller caller, string projectId, CancellationToken cancellationToken = default)
    {
        var project = await storage.GetProjectAsync(projectId, caller.OwnerId, cancellationToken);
        if (project is null)
            return null;

        var promotions = await storage.ListPromotionsAsync(project.Id, cancellationToken);
        var activeVersion = await storage.GetActiveVersionAsync(project.Id, cancellationToken);
        var activePackages = await nuplaneAdmin.GetPackagesAsync(cancellationToken);
        var catalog = await featureManagement.GetCatalogAsync(cancellationToken);
        var runtimePackages = promotions
            .Select(promotion =>
            {
                var loaded = activePackages.Packages.Any(package =>
                    string.Equals(package.PackageId, promotion.PackageId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(package.Version, promotion.Version, StringComparison.OrdinalIgnoreCase));
                var failed = ReconcileFailedForPackage(promotion.ReconcileOutcome, promotion.PackageId);
                var state = failed
                    ? ExtensionPackageRuntimeState.FailedReconciliation
                    : loaded
                        ? ExtensionPackageRuntimeState.Loaded
                        : ExtensionPackageRuntimeState.PendingRestart;
                var contributions = catalog.Features
                    .Where(feature =>
                        string.Equals(feature.PackageId, promotion.PackageId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(feature.PackageVersion, promotion.Version, StringComparison.OrdinalIgnoreCase))
                    .Select(feature => new ExtensionRuntimeContribution("feature", feature.Id, feature.DisplayName, feature.Enabled ? "active" : "available"))
                    .ToArray();

                return new ExtensionRuntimePackageStatus(
                    promotion.PackageId,
                    promotion.Version,
                    state,
                    contributions,
                    promotion.RequiresReload,
                    promotion.RequiresRestart || state is ExtensionPackageRuntimeState.PendingRestart,
                    state is ExtensionPackageRuntimeState.FailedReconciliation ? promotion.ReconcileOutcome.Reason : null);
            })
            .OrderBy(x => x.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var lastReconcile = promotions
            .OrderByDescending(x => x.LastReconciledAt ?? x.PromotedAt)
            .FirstOrDefault()
            ?.ReconcileOutcome;
        return new(
            project.Id,
            project.PackageId,
            activeVersion,
            runtimePackages,
            promotions
                .Where(x => File.Exists(x.FeedPath))
                .Select(x => x.Version)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            lastReconcile,
            lastReconcile?.Reason);
    }

    public async Task<PackagePromotionResult?> RollbackPackageAsync(ExtensionBuilderCaller caller, string projectId, RollbackPackageRequest request, CancellationToken cancellationToken = default)
    {
        var project = await storage.GetProjectAsync(projectId, caller.OwnerId, cancellationToken);
        if (project is null)
            return null;

        var target = (await storage.ListPromotionsAsync(project.Id, cancellationToken))
            .FirstOrDefault(x => string.Equals(x.Version, request.Version, StringComparison.OrdinalIgnoreCase));
        if (target is null)
            return new PackagePromotionResult(PromotionStatus.Rejected, PromotionRejectionReason.InvalidManifest, null, null, false, false);

        var result = await promotionService.RollbackAsync(project, target, await storage.ListPromotionsAsync(project.Id, cancellationToken), cancellationToken);
        if (result is { Status: PromotionStatus.Accepted, ReconcileOutcome: not null })
        {
            var recorded = await storage.UpdatePromotionLifecycleAsync(
                project.Id,
                request.Version,
                result.ReconcileOutcome,
                result.RequiresReload,
                result.RequiresRestart,
                DateTimeOffset.UtcNow,
                CancellationToken.None);
            if (!recorded)
                return null;

            await storage.SetActiveVersionAsync(project.Id, request.Version, CancellationToken.None);
        }

        return result;
    }

    public async Task<ExtensionBuilderOperationResponse?> RetryReconciliationAsync(ExtensionBuilderCaller caller, string projectId, CancellationToken cancellationToken = default)
    {
        var project = await storage.GetProjectAsync(projectId, caller.OwnerId, cancellationToken);
        if (project is null)
            return null;

        var outcome = await promotionService.RetryReconciliationAsync(cancellationToken);
        await storage.UpdatePromotionReconcileOutcomeAsync(project.Id, outcome, CancellationToken.None);
        logger.LogInformation("Extension Builder reconciliation retry completed for project {ProjectId} with {Outcome}.", project.Id, outcome.Outcome);
        return new("retry-reconcile", outcome, RequiresReload: true, RequiresRestart: false, "Reconciliation retry completed.");
    }

    private static bool ReconcileFailedForPackage(ExtensionBuilderReconcileOutcome outcome, string packageId)
    {
        if (!outcome.IsDegraded)
            return false;

        return outcome.FailedPackages.Count is 0 ||
            outcome.FailedPackages.Any(x => string.Equals(x, packageId, StringComparison.OrdinalIgnoreCase));
    }

    private static void DeleteBuildDirectory(string runningLogPath)
    {
        var buildDirectory = Path.GetDirectoryName(runningLogPath);
        if (!string.IsNullOrWhiteSpace(buildDirectory) && Directory.Exists(buildDirectory))
            Directory.Delete(buildDirectory, recursive: true);
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
