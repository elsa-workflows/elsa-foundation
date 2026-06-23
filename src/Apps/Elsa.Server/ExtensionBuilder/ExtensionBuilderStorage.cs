using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Server.ExtensionBuilder;

internal interface IExtensionBuilderStorage
{
    string RootPath { get; }
    Task<IReadOnlyList<ExtensionWorkspace>> ListWorkspacesAsync(string ownerId, CancellationToken cancellationToken = default);
    Task<ExtensionWorkspace?> GetWorkspaceAsync(string workspaceId, string ownerId, CancellationToken cancellationToken = default);
    Task<ExtensionWorkspace> CreateWorkspaceAsync(string ownerId, string trustContext, string displayName, CancellationToken cancellationToken = default);
    Task<bool> DeleteWorkspaceAsync(string workspaceId, string ownerId, CancellationToken cancellationToken = default);
    Task<ExtensionProject?> GetProjectAsync(string projectId, string ownerId, CancellationToken cancellationToken = default);
    Task<bool> ProjectExistsAsync(string projectId, CancellationToken cancellationToken = default);
    Task<ExtensionProject> CreateProjectAsync(string workspaceId, string ownerId, ExtensionTemplate template, CreateProjectRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteProjectAsync(string projectId, string ownerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectFileSummary>?> ListFilesAsync(string projectId, string ownerId, CancellationToken cancellationToken = default);
    Task<ProjectFile?> ReadFileAsync(string projectId, string ownerId, string path, CancellationToken cancellationToken = default);
    Task<ProjectFile?> WriteFileAsync(string projectId, string ownerId, string path, string content, CancellationToken cancellationToken = default);
    Task<bool> DeleteFileAsync(string projectId, string ownerId, string path, CancellationToken cancellationToken = default);
    Task<SourceSnapshot?> CreateSourceSnapshotAsync(string projectId, string ownerId, CancellationToken cancellationToken = default);
    Task<BuildResult?> GetBuildAsync(string buildId, string ownerId, CancellationToken cancellationToken = default);
    Task<bool> SaveBuildAsync(BuildResult build, CancellationToken cancellationToken = default);
    Task<int> FailIncompleteBuildsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BuildResult>> ListProjectBuildsAsync(string projectId, CancellationToken cancellationToken = default);
    Task<bool> AddPromotionAsync(string projectId, PackagePromotionRecord record, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PackagePromotionRecord>> ListPromotionsAsync(string projectId, CancellationToken cancellationToken = default);
    Task UpdatePromotionReconcileOutcomeAsync(string projectId, ExtensionBuilderReconcileOutcome outcome, CancellationToken cancellationToken = default);
    Task<bool> UpdatePromotionLifecycleAsync(string projectId, string version, ExtensionBuilderReconcileOutcome outcome, bool requiresReload, bool requiresRestart, DateTimeOffset reconciledAt, CancellationToken cancellationToken = default);
    Task SetActiveVersionAsync(string projectId, string version, CancellationToken cancellationToken = default);
    Task<bool> TrySetActiveVersionAsync(string projectId, string version, string? expectedCurrentVersion, CancellationToken cancellationToken = default);
    Task<string?> GetActiveVersionAsync(string projectId, CancellationToken cancellationToken = default);
    string GetBuildLogPath(string buildId);
    string GetBuildArtifactsPath(string buildId);
}

internal sealed record SourceSnapshot(string Id, string ProjectId, string Path, DateTimeOffset CreatedAt);

internal sealed class ExtensionBuilderStorage : IExtensionBuilderStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _statePath;

    public ExtensionBuilderStorage(IWebHostEnvironment environment, IOptions<ExtensionBuilderOptions> options)
    {
        var configuredPath = options.Value.StoragePath;
        RootPath = Path.GetFullPath(Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath));
        _statePath = Path.Combine(RootPath, "state.json");
    }

    public string RootPath { get; }

    public async Task<IReadOnlyList<ExtensionWorkspace>> ListWorkspacesAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            return state.Workspaces.Values
                .Where(x => string.Equals(x.OwnerId, ownerId, StringComparison.Ordinal))
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ExtensionWorkspace?> GetWorkspaceAsync(string workspaceId, string ownerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            return TryGetOwnedWorkspace(state, workspaceId, ownerId, out var workspace) ? workspace : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ExtensionWorkspace> CreateWorkspaceAsync(string ownerId, string trustContext, string displayName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Workspace display name is required.", nameof(displayName));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var workspace = new ExtensionWorkspace(CreateId("ws"), ownerId, trustContext, displayName.Trim(), now, now, []);
            state.Workspaces[workspace.Id] = workspace;
            Directory.CreateDirectory(GetWorkspacePath(workspace.Id));
            await SaveStateAsync(state, cancellationToken);
            return workspace;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteWorkspaceAsync(string workspaceId, string ownerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedWorkspace(state, workspaceId, ownerId, out var workspace))
                return false;

            foreach (var projectId in workspace.ProjectIds)
                RemoveProjectAuthoringState(state, projectId);
            state.Workspaces.Remove(workspace.Id);
            DeleteDirectoryIfExists(GetWorkspacePath(workspace.Id));
            await SaveStateAsync(state, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ExtensionProject?> GetProjectAsync(string projectId, string ownerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            return TryGetOwnedProject(state, projectId, ownerId, out var project) ? project : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ProjectExistsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            return state.Projects.ContainsKey(projectId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ExtensionProject> CreateProjectAsync(string workspaceId, string ownerId, ExtensionTemplate template, CreateProjectRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PackageId))
            throw new ArgumentException("Package id is required.", nameof(request));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedWorkspace(state, workspaceId, ownerId, out var workspace))
                throw new KeyNotFoundException($"Workspace '{workspaceId}' was not found.");

            var now = DateTimeOffset.UtcNow;
            var projectId = CreateId("proj");
            var packageId = request.PackageId.Trim();
            var version = string.IsNullOrWhiteSpace(request.PackageVersion) ? template.DefaultPackageVersion : request.PackageVersion.Trim();
            var targetFramework = string.IsNullOrWhiteSpace(request.TargetFramework) ? template.DefaultTargetFramework : request.TargetFramework.Trim();
            var manifestContent = ExtensionBuilderTemplateCatalog.RewriteManifest(template.DefaultManifest.Content, packageId, version);
            var manifest = template.DefaultManifest with
            {
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? template.DefaultManifest.DisplayName : request.DisplayName.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? template.DefaultManifest.Description : request.Description.Trim(),
                Content = manifestContent
            };

            var project = new ExtensionProject(
                projectId,
                workspace.Id,
                template.Id,
                template.Kind,
                packageId,
                version,
                targetFramework,
                manifest,
                "",
                now,
                now);

            Directory.CreateDirectory(GetProjectFilesPath(project.Id));
            foreach (var file in template.Files)
            {
                var content = file.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                    ? ExtensionBuilderTemplateCatalog.ProjectFile(packageId, version, targetFramework)
                    : file.Path.Equals("elsa-package.json", StringComparison.OrdinalIgnoreCase)
                        ? manifest.Content.GetRawText()
                        : file.Content;
                await WriteFileCoreAsync(project.Id, file.Path, content, cancellationToken);
            }

            var snapshot = await CreateSourceSnapshotCoreAsync(project.Id, cancellationToken);
            project = project with { CurrentSourceRevisionId = snapshot.Id };
            state.Projects[project.Id] = project;
            state.Workspaces[workspace.Id] = workspace with
            {
                ProjectIds = workspace.ProjectIds.Concat([project.Id]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                UpdatedAt = now
            };
            await SaveStateAsync(state, cancellationToken);
            return project;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteProjectAsync(string projectId, string ownerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedProject(state, projectId, ownerId, out var project))
                return false;

            RemoveProjectAuthoringState(state, project.Id);
            if (state.Workspaces.TryGetValue(project.WorkspaceId, out var workspace))
            {
                state.Workspaces[workspace.Id] = workspace with
                {
                    ProjectIds = workspace.ProjectIds.Where(x => !string.Equals(x, project.Id, StringComparison.OrdinalIgnoreCase)).ToArray(),
                    UpdatedAt = DateTimeOffset.UtcNow
                };
            }

            DeleteDirectoryIfExists(GetProjectPath(project.Id));
            await SaveStateAsync(state, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ProjectFileSummary>?> ListFilesAsync(string projectId, string ownerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedProject(state, projectId, ownerId, out _))
                return null;

            var filesRoot = GetProjectFilesPath(projectId);
            if (!Directory.Exists(filesRoot))
                return [];

            return Directory.EnumerateFiles(filesRoot, "*", SearchOption.AllDirectories)
                .Select(path =>
                {
                    var info = new FileInfo(path);
                    var relative = Path.GetRelativePath(filesRoot, path).Replace(Path.DirectorySeparatorChar, '/');
                    return new ProjectFileSummary(relative, GetFileKind(relative), info.Length, info.LastWriteTimeUtc);
                })
                .OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ProjectFile?> ReadFileAsync(string projectId, string ownerId, string path, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedProject(state, projectId, ownerId, out _))
                return null;

            var filePath = ResolveProjectFilePath(projectId, path);
            if (!File.Exists(filePath))
                return null;

            return await ReadProjectFileAsync(filePath, NormalizeRelativePath(path), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ProjectFile?> WriteFileAsync(string projectId, string ownerId, string path, string content, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedProject(state, projectId, ownerId, out var project))
                return null;

            var normalizedPath = await WriteFileCoreAsync(projectId, path, content, cancellationToken);
            var snapshot = await CreateSourceSnapshotCoreAsync(projectId, CancellationToken.None);
            state.Projects[project.Id] = project with { CurrentSourceRevisionId = snapshot.Id, UpdatedAt = DateTimeOffset.UtcNow };
            await SaveStateAsync(state, CancellationToken.None);
            return await ReadProjectFileAsync(ResolveProjectFilePath(projectId, normalizedPath), normalizedPath, CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteFileAsync(string projectId, string ownerId, string path, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedProject(state, projectId, ownerId, out var project))
                return false;

            var filePath = ResolveProjectFilePath(projectId, path);
            if (!File.Exists(filePath))
                return false;

            File.Delete(filePath);
            var snapshot = await CreateSourceSnapshotCoreAsync(projectId, CancellationToken.None);
            state.Projects[project.Id] = project with { CurrentSourceRevisionId = snapshot.Id, UpdatedAt = DateTimeOffset.UtcNow };
            await SaveStateAsync(state, CancellationToken.None);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SourceSnapshot?> CreateSourceSnapshotAsync(string projectId, string ownerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedProject(state, projectId, ownerId, out var project))
                return null;

            var snapshot = await CreateSourceSnapshotCoreAsync(projectId, cancellationToken);
            state.Projects[project.Id] = project with { CurrentSourceRevisionId = snapshot.Id, UpdatedAt = DateTimeOffset.UtcNow };
            await SaveStateAsync(state, cancellationToken);
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BuildResult?> GetBuildAsync(string buildId, string ownerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!state.Builds.TryGetValue(buildId, out var build))
                return null;

            return TryGetOwnedProject(state, build.ProjectId, ownerId, out _) ? build : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> SaveBuildAsync(BuildResult build, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!state.Projects.ContainsKey(build.ProjectId))
                return false;

            state.Builds[build.Id] = build;
            await SaveStateAsync(state, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> FailIncompleteBuildsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var recovered = 0;
            foreach (var build in state.Builds.Values.Where(x => x.Status is BuildStatus.Pending or BuildStatus.Running).ToArray())
            {
                var diagnostic = new BuildDiagnostic(
                    BuildDiagnosticSeverity.Error,
                    "Build failed because the server restarted before the queued build completed.",
                    null,
                    null,
                    null,
                    null);
                state.Builds[build.Id] = build with
                {
                    Status = BuildStatus.Failed,
                    Diagnostics = [.. build.Diagnostics, diagnostic],
                    Artifact = null,
                    CompletedAt = now
                };
                Directory.CreateDirectory(Path.GetDirectoryName(build.LogPath)!);
                await File.AppendAllTextAsync(build.LogPath, $"{diagnostic.Message}{Environment.NewLine}", cancellationToken);
                recovered++;
            }

            if (recovered > 0)
                await SaveStateAsync(state, cancellationToken);

            return recovered;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<BuildResult>> ListProjectBuildsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            return state.Builds.Values.Where(x => string.Equals(x.ProjectId, projectId, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> AddPromotionAsync(string projectId, PackagePromotionRecord record, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!state.Projects.ContainsKey(projectId))
                return false;

            var promotions = state.Promotions.TryGetValue(projectId, out var existing) ? existing.ToList() : [];
            promotions.RemoveAll(x => string.Equals(x.Version, record.Version, StringComparison.OrdinalIgnoreCase));
            promotions.Add(record);
            state.Promotions[projectId] = promotions.OrderBy(x => x.Version, StringComparer.OrdinalIgnoreCase).ToArray();
            state.ActiveVersions[projectId] = record.Version;
            await SaveStateAsync(state, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<PackagePromotionRecord>> ListPromotionsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            return state.Promotions.TryGetValue(projectId, out var promotions) ? promotions : [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdatePromotionReconcileOutcomeAsync(string projectId, ExtensionBuilderReconcileOutcome outcome, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!state.Promotions.TryGetValue(projectId, out var promotions))
                return;

            state.Promotions[projectId] = promotions
                .Select(promotion => promotion with { ReconcileOutcome = outcome, LastReconciledAt = DateTimeOffset.UtcNow })
                .ToArray();
            await SaveStateAsync(state, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> UpdatePromotionLifecycleAsync(
        string projectId,
        string version,
        ExtensionBuilderReconcileOutcome outcome,
        bool requiresReload,
        bool requiresRestart,
        DateTimeOffset reconciledAt,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!state.Projects.ContainsKey(projectId) || !state.Promotions.TryGetValue(projectId, out var promotions))
                return false;

            var updated = false;
            state.Promotions[projectId] = promotions
                .Select(promotion =>
                {
                    if (!string.Equals(promotion.Version, version, StringComparison.OrdinalIgnoreCase))
                        return promotion;

                    updated = true;
                    return promotion with
                    {
                        ReconcileOutcome = outcome,
                        RequiresReload = requiresReload,
                        RequiresRestart = requiresRestart,
                        LastReconciledAt = reconciledAt
                    };
                })
                .ToArray();
            if (!updated)
                return false;

            await SaveStateAsync(state, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetActiveVersionAsync(string projectId, string version, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            state.ActiveVersions[projectId] = version;
            await SaveStateAsync(state, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> TrySetActiveVersionAsync(string projectId, string version, string? expectedCurrentVersion, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!state.Projects.ContainsKey(projectId))
                return false;
            state.ActiveVersions.TryGetValue(projectId, out var currentVersion);
            if (!string.Equals(currentVersion, expectedCurrentVersion, StringComparison.OrdinalIgnoreCase))
                return false;

            state.ActiveVersions[projectId] = version;
            await SaveStateAsync(state, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> GetActiveVersionAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            return state.ActiveVersions.TryGetValue(projectId, out var version) ? version : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public string GetBuildLogPath(string buildId)
    {
        var path = Path.Combine(RootPath, "builds", buildId, "build.log");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    public string GetBuildArtifactsPath(string buildId)
    {
        var path = Path.Combine(RootPath, "builds", buildId, "artifacts");
        Directory.CreateDirectory(path);
        return path;
    }

    private async Task<ExtensionBuilderState> LoadStateAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(RootPath);
        if (!File.Exists(_statePath))
            return new();

        await using var stream = File.OpenRead(_statePath);
        return await JsonSerializer.DeserializeAsync<ExtensionBuilderState>(stream, JsonOptions, cancellationToken) ?? new();
    }

    private async Task SaveStateAsync(ExtensionBuilderState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(RootPath);
        var tempPath = Path.Combine(RootPath, $".state-{Guid.NewGuid():N}.json");
        await using (var stream = File.Create(tempPath))
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
        File.Move(tempPath, _statePath, overwrite: true);
    }

    private bool TryGetOwnedWorkspace(ExtensionBuilderState state, string workspaceId, string ownerId, out ExtensionWorkspace workspace)
    {
        if (state.Workspaces.TryGetValue(workspaceId, out var found) &&
            string.Equals(found.OwnerId, ownerId, StringComparison.Ordinal))
        {
            workspace = found;
            return true;
        }

        workspace = null!;
        return false;
    }

    private bool TryGetOwnedProject(ExtensionBuilderState state, string projectId, string ownerId, out ExtensionProject project)
    {
        if (state.Projects.TryGetValue(projectId, out var found) &&
            state.Workspaces.TryGetValue(found.WorkspaceId, out var workspace) &&
            string.Equals(workspace.OwnerId, ownerId, StringComparison.Ordinal))
        {
            project = found;
            return true;
        }

        project = null!;
        return false;
    }

    private async Task<string> WriteFileCoreAsync(string projectId, string path, string content, CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizeRelativePath(path);
        var filePath = ResolveProjectFilePath(projectId, normalizedPath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, content, cancellationToken);
        return normalizedPath;
    }

    private async Task<SourceSnapshot> CreateSourceSnapshotCoreAsync(string projectId, CancellationToken cancellationToken)
    {
        var snapshot = new SourceSnapshot(CreateId("rev"), projectId, GetSnapshotPath(projectId, CreateId("revtmp")), DateTimeOffset.UtcNow);
        var snapshotPath = GetSnapshotPath(projectId, snapshot.Id);
        DeleteDirectoryIfExists(snapshotPath);
        Directory.CreateDirectory(snapshotPath);
        CopyDirectory(GetProjectFilesPath(projectId), snapshotPath, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(snapshotPath, ".source-revision"), snapshot.Id, cancellationToken);
        return snapshot with { Path = snapshotPath };
    }

    private static async Task<ProjectFile> ReadProjectFileAsync(string filePath, string relativePath, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var info = new FileInfo(filePath);
        return new(relativePath, content, GetFileKind(relativePath), info.Length, info.LastWriteTimeUtc);
    }

    private string ResolveProjectFilePath(string projectId, string relativePath)
    {
        var root = GetProjectFilesPath(projectId);
        var normalizedPath = NormalizeRelativePath(relativePath);
        var path = Path.GetFullPath(Path.Combine(root, normalizedPath));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The resolved project file path is outside the project root.");
        return path;
    }

    internal static string NormalizeRelativePath(string path)
    {
        var normalized = (path ?? "").Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized) ||
            Path.IsPathRooted(normalized) ||
            normalized.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new ArgumentException("A safe relative project file path is required.", nameof(path));
        return normalized;
    }

    private static ProjectFileKind GetFileKind(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
            return ProjectFileKind.Source;
        if (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            return ProjectFileKind.Project;
        if (Path.GetFileName(path).Equals("elsa-package.json", StringComparison.OrdinalIgnoreCase))
            return ProjectFileKind.Manifest;
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            return ProjectFileKind.Configuration;
        return ProjectFileKind.Other;
    }

    private string GetWorkspacePath(string workspaceId) => Path.Combine(RootPath, "workspaces", workspaceId);
    private string GetProjectPath(string projectId) => Path.Combine(RootPath, "projects", projectId);
    private string GetProjectFilesPath(string projectId) => Path.Combine(GetProjectPath(projectId), "files");
    private string GetSnapshotPath(string projectId, string snapshotId) => Path.Combine(GetProjectPath(projectId), "snapshots", snapshotId);
    private string GetBuildPath(string buildId) => Path.Combine(RootPath, "builds", buildId);

    private void RemoveProjectAuthoringState(ExtensionBuilderState state, string projectId)
    {
        state.Projects.Remove(projectId);
        state.Promotions.Remove(projectId);
        state.ActiveVersions.Remove(projectId);

        var buildIds = state.Builds.Values
            .Where(x => string.Equals(x.ProjectId, projectId, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Id)
            .ToArray();
        foreach (var buildId in buildIds)
        {
            state.Builds.Remove(buildId);
            DeleteDirectoryIfExists(GetBuildPath(buildId));
        }

        DeleteDirectoryIfExists(GetProjectPath(projectId));
    }

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(source))
            return;

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static string CreateId(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    private sealed class ExtensionBuilderState
    {
        public Dictionary<string, ExtensionWorkspace> Workspaces { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ExtensionProject> Projects { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, BuildResult> Builds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, PackagePromotionRecord[]> Promotions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> ActiveVersions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
