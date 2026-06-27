using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Server.ExtensionBuilder;

internal interface IExtensionBuilderStorage
{
    string RootPath { get; }
    Task<IReadOnlyList<ExtensionRepositorySummary>> ListRepositoriesAsync(string ownerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExtensionWorkspace>> ListWorkspacesAsync(string ownerId, CancellationToken cancellationToken = default);
    Task<ExtensionWorkspace?> GetWorkspaceAsync(string workspaceId, string ownerId, CancellationToken cancellationToken = default);
    Task<ExtensionWorkspace> CreateWorkspaceAsync(string ownerId, string trustContext, string displayName, CancellationToken cancellationToken = default);
    Task<bool> DeleteWorkspaceAsync(string workspaceId, string ownerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExtensionWorkingCopySummary>?> ListWorkingCopiesAsync(string workspaceId, string ownerId, string? sessionId, CancellationToken cancellationToken = default);
    Task<ExtensionWorkingCopySummary?> SelectWorkingCopyAsync(string workspaceId, string ownerId, SelectWorkingCopyRequest request, CancellationToken cancellationToken = default);
    Task<RepositoryTree?> GetRepositoryTreeAsync(string workspaceId, string ownerId, string? selectedSolutionPath, CancellationToken cancellationToken = default);
    Task<RepositoryFile?> ReadRepositoryFileAsync(string workspaceId, string ownerId, string path, CancellationToken cancellationToken = default);
    Task<RepositoryFile?> WriteRepositoryFileAsync(string workspaceId, string ownerId, string path, string content, CancellationToken cancellationToken = default);
    Task<AppliedRepositoryTemplate?> ApplyRepositoryTemplateAsync(string workspaceId, string ownerId, ExtensionTemplate template, ApplyRepositoryTemplateRequest request, CancellationToken cancellationToken = default);
    Task<RepositoryFile?> MoveRepositoryFileAsync(string workspaceId, string ownerId, MoveRepositoryFileRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteRepositoryFileAsync(string workspaceId, string ownerId, string path, CancellationToken cancellationToken = default);
    Task<SourceControlStatus?> GetSourceControlStatusAsync(string workspaceId, string ownerId, CancellationToken cancellationToken = default);
    Task<SourceControlDiff?> GetSourceControlDiffAsync(string workspaceId, string ownerId, string path, bool staged, CancellationToken cancellationToken = default);
    Task<SourceControlStatus?> StageRepositoryFileAsync(string workspaceId, string ownerId, string path, CancellationToken cancellationToken = default);
    Task<SourceControlStatus?> UnstageRepositoryFileAsync(string workspaceId, string ownerId, string path, CancellationToken cancellationToken = default);
    Task<SourceControlStatus?> StageAllRepositoryChangesAsync(string workspaceId, string ownerId, CancellationToken cancellationToken = default);
    Task<SourceControlCommitResult?> CommitRepositoryChangesAsync(string workspaceId, string ownerId, SourceControlCommitRequest request, CancellationToken cancellationToken = default);
    Task<RemoteSyncResult?> PushRepositoryAsync(string workspaceId, string ownerId, CancellationToken cancellationToken = default);
    Task<RemoteSyncResult?> PullRepositoryAsync(string workspaceId, string ownerId, CancellationToken cancellationToken = default);
    Task<BuildResult?> SubmitRepositoryBuildAsync(string workspaceId, string ownerId, ExtensionProject project, SubmitRepositoryBuildRequest request, CancellationToken cancellationToken = default);
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
    Task<IReadOnlyList<BuildResult>> ListProjectBuildsAsync(string projectId, CancellationToken cancellationToken = default);
    Task<bool> AddPromotionAsync(string projectId, PackagePromotionRecord record, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PackagePromotionRecord>> ListPromotionsAsync(string projectId, CancellationToken cancellationToken = default);
    Task UpdatePromotionReconcileOutcomeAsync(string projectId, ExtensionBuilderReconcileOutcome outcome, CancellationToken cancellationToken = default);
    Task<bool> UpdatePromotionLifecycleAsync(string projectId, string version, ExtensionBuilderReconcileOutcome outcome, bool requiresReload, bool requiresRestart, DateTimeOffset reconciledAt, CancellationToken cancellationToken = default);
    Task SetActiveVersionAsync(string projectId, string version, CancellationToken cancellationToken = default);
    Task<string?> GetActiveVersionAsync(string projectId, CancellationToken cancellationToken = default);
    string GetBuildLogPath(string buildId);
    string GetBuildArtifactsPath(string buildId);
}

internal sealed record SourceSnapshot(string Id, string ProjectId, string Path, DateTimeOffset CreatedAt);
internal sealed record RenderedTemplateFile(string RelativePath, string PhysicalPath, string Content);
internal sealed record RemoteDivergence(bool RemoteBranchExists, int Ahead, int Behind);

internal sealed class ExtensionBuilderStorage : IExtensionBuilderStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dotNetExecutable;
    private readonly string _gitExecutable;
    private readonly string _statePath;

    public ExtensionBuilderStorage(IWebHostEnvironment environment, IOptions<ExtensionBuilderOptions> options)
    {
        var extensionBuilderOptions = options.Value;
        var configuredPath = extensionBuilderOptions.StoragePath;
        RootPath = Path.GetFullPath(Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath));
        _dotNetExecutable = string.IsNullOrWhiteSpace(extensionBuilderOptions.DotNetExecutable) ? "dotnet" : extensionBuilderOptions.DotNetExecutable;
        _gitExecutable = string.IsNullOrWhiteSpace(extensionBuilderOptions.GitExecutable) ? "git" : extensionBuilderOptions.GitExecutable;
        _statePath = Path.Combine(RootPath, "state.json");
    }

    public string RootPath { get; }

    public async Task<IReadOnlyList<ExtensionRepositorySummary>> ListRepositoriesAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            return state.Workspaces.Values
                .Where(x => string.Equals(x.OwnerId, ownerId, StringComparison.Ordinal))
                .Select(workspace => ToRepositorySummary(state, workspace))
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

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
            var workspacePath = GetWorkspacePath(workspace.Id);
            try
            {
                await InitializeManagedRepositoryAsync(workspacePath, workspace.DisplayName, cancellationToken);
                state.Workspaces[workspace.Id] = workspace;
                await SaveStateAsync(state, cancellationToken);
                return workspace;
            }
            catch
            {
                DeleteDirectoryIfExists(workspacePath);
                throw;
            }
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
            foreach (var workingCopyId in state.WorkingCopies.Values
                         .Where(x => string.Equals(x.WorkspaceId, workspace.Id, StringComparison.OrdinalIgnoreCase))
                         .Select(x => x.Id)
                         .ToArray())
            {
                state.WorkingCopies.Remove(workingCopyId);
            }
            DeleteDirectoryIfExists(GetWorkspacePath(workspace.Id));
            await SaveStateAsync(state, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ExtensionWorkingCopySummary>?> ListWorkingCopiesAsync(string workspaceId, string ownerId, string? sessionId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedWorkspace(state, workspaceId, ownerId, out _))
                return null;

            var repositoryState = GetRepositoryState(GetWorkspacePath(workspaceId));
            return state.WorkingCopies.Values
                .Where(x => string.Equals(x.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(x.OwnerId, ownerId, StringComparison.Ordinal) &&
                            (string.IsNullOrWhiteSpace(sessionId) || string.Equals(x.SessionId, sessionId, StringComparison.Ordinal)))
                .Select(x => ToWorkingCopySummary(x, repositoryState))
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.BranchName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ExtensionWorkingCopySummary?> SelectWorkingCopyAsync(string workspaceId, string ownerId, SelectWorkingCopyRequest request, CancellationToken cancellationToken = default)
    {
        var sessionId = NormalizeSessionId(request.SessionId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedWorkspace(state, workspaceId, ownerId, out var workspace))
                return null;

            var branchName = NormalizeBranchName(request.BranchName, ownerId, sessionId);
            var protectedBranch = IsProtectedBranch(branchName);
            if (protectedBranch && !request.AllowProtectedBranchEdit)
                throw new InvalidOperationException($"Editing '{branchName}' requires explicit confirmation because it is a protected or common default branch.");

            var repositoryPath = GetWorkspacePath(workspace.Id);
            await SwitchRepositoryBranchAsync(repositoryPath, branchName, cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var existing = state.WorkingCopies.Values.FirstOrDefault(x =>
                string.Equals(x.WorkspaceId, workspace.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.OwnerId, ownerId, StringComparison.Ordinal) &&
                string.Equals(x.SessionId, sessionId, StringComparison.Ordinal) &&
                string.Equals(x.BranchName, branchName, StringComparison.OrdinalIgnoreCase));
            var activeIds = state.WorkingCopies.Values
                .Where(x => string.Equals(x.WorkspaceId, workspace.Id, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(x.OwnerId, ownerId, StringComparison.Ordinal) &&
                            string.Equals(x.SessionId, sessionId, StringComparison.Ordinal))
                .Select(x => x.Id)
                .ToArray();
            foreach (var id in activeIds)
                state.WorkingCopies[id] = state.WorkingCopies[id] with { IsActive = false };

            var workingCopy = existing is null
                ? new WorkingCopyState(CreateId("wc"), workspace.Id, ownerId, sessionId, branchName, true, protectedBranch, now, now)
                : existing with { IsActive = true, IsProtectedBranch = protectedBranch, UpdatedAt = now };
            state.WorkingCopies[workingCopy.Id] = workingCopy;
            state.Workspaces[workspace.Id] = workspace with { UpdatedAt = now };
            await SaveStateAsync(state, cancellationToken);
            return ToWorkingCopySummary(workingCopy, GetRepositoryState(repositoryPath));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RepositoryTree?> GetRepositoryTreeAsync(string workspaceId, string ownerId, string? selectedSolutionPath, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedWorkspace(state, workspaceId, ownerId, out var workspace))
                return null;

            var repositoryPath = GetWorkspacePath(workspace.Id);
            return BuildRepositoryTree(workspace.Id, repositoryPath, selectedSolutionPath);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RepositoryFile?> ReadRepositoryFileAsync(string workspaceId, string ownerId, string path, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedWorkspace(state, workspaceId, ownerId, out var workspace))
                return null;

            var repositoryPath = GetWorkspacePath(workspace.Id);
            var normalizedPath = NormalizeRepositoryRelativePath(path);
            var filePath = ResolveRepositoryFilePath(repositoryPath, normalizedPath);
            if (!File.Exists(filePath))
                return null;

            return await ReadRepositoryFileCoreAsync(repositoryPath, filePath, normalizedPath, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AppliedRepositoryTemplate?> ApplyRepositoryTemplateAsync(string workspaceId, string ownerId, ExtensionTemplate template, ApplyRepositoryTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var scope = request.Scope ?? template.Scope;
        if (scope != template.Scope)
            throw new ArgumentException($"Template '{template.Id}' supports {template.Scope} scope, not {scope}.", nameof(request));

        var values = BuildTemplateParameterValues(template, request);
        var targetPath = NormalizeTemplateTargetPath(request.TargetPath, scope, values);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedWorkspace(state, workspaceId, ownerId, out var workspace))
                return null;

            var repositoryPath = GetWorkspacePath(workspace.Id);
            var renderedFiles = template.Files
                .Select(file =>
                {
                    var renderedPath = RenderTemplateText(file.Path, values);
                    var relativePath = NormalizeRepositoryRelativePath(CombineTemplatePath(targetPath, renderedPath));
                    var physicalPath = ResolveRepositoryFilePath(repositoryPath, relativePath);
                    var content = RenderTemplateContent(template, file, relativePath, values);
                    return new RenderedTemplateFile(relativePath, physicalPath, content);
                })
                .ToArray();

            foreach (var file in renderedFiles)
            {
                if (File.Exists(file.PhysicalPath))
                    throw new IOException($"Repository file '{file.RelativePath}' already exists.");
            }

            foreach (var file in renderedFiles)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file.PhysicalPath)!);
                await File.WriteAllTextAsync(file.PhysicalPath, file.Content, cancellationToken);
            }

            state.Workspaces[workspace.Id] = workspace with { UpdatedAt = DateTimeOffset.UtcNow };
            await SaveStateAsync(state, cancellationToken);

            var dirtyPaths = GetDirtyRepositoryPaths(repositoryPath);
            var summaries = renderedFiles
                .Select(file =>
                {
                    var info = new FileInfo(file.PhysicalPath);
                    return new RepositoryFileSummary(file.RelativePath, GetRepositoryFileKind(file.RelativePath), info.Length, IsPathDirty(file.RelativePath, dirtyPaths), info.LastWriteTimeUtc);
                })
                .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new(template.Id, scope, summaries, BuildRepositoryTree(workspace.Id, repositoryPath, null));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RepositoryFile?> WriteRepositoryFileAsync(string workspaceId, string ownerId, string path, string content, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedWorkspace(state, workspaceId, ownerId, out var workspace))
                return null;

            var repositoryPath = GetWorkspacePath(workspace.Id);
            var normalizedPath = NormalizeRepositoryRelativePath(path);
            var filePath = ResolveRepositoryFilePath(repositoryPath, normalizedPath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllTextAsync(filePath, content, cancellationToken);
            state.Workspaces[workspace.Id] = workspace with { UpdatedAt = DateTimeOffset.UtcNow };
            await SaveStateAsync(state, cancellationToken);
            return await ReadRepositoryFileCoreAsync(repositoryPath, filePath, normalizedPath, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RepositoryFile?> MoveRepositoryFileAsync(string workspaceId, string ownerId, MoveRepositoryFileRequest request, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedWorkspace(state, workspaceId, ownerId, out var workspace))
                return null;

            var repositoryPath = GetWorkspacePath(workspace.Id);
            var sourcePath = NormalizeRepositoryRelativePath(request.SourcePath);
            var destinationPath = NormalizeRepositoryRelativePath(request.DestinationPath);
            var sourceFilePath = ResolveRepositoryFilePath(repositoryPath, sourcePath);
            var destinationFilePath = ResolveRepositoryFilePath(repositoryPath, destinationPath);
            if (!File.Exists(sourceFilePath))
                return null;

            Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath)!);
            File.Move(sourceFilePath, destinationFilePath, overwrite: false);
            DeleteEmptyParentDirectories(repositoryPath, Path.GetDirectoryName(sourceFilePath));
            state.Workspaces[workspace.Id] = workspace with { UpdatedAt = DateTimeOffset.UtcNow };
            await SaveStateAsync(state, cancellationToken);
            return await ReadRepositoryFileCoreAsync(repositoryPath, destinationFilePath, destinationPath, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteRepositoryFileAsync(string workspaceId, string ownerId, string path, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedWorkspace(state, workspaceId, ownerId, out var workspace))
                return false;

            var repositoryPath = GetWorkspacePath(workspace.Id);
            var normalizedPath = NormalizeRepositoryRelativePath(path);
            var filePath = ResolveRepositoryFilePath(repositoryPath, normalizedPath);
            if (!File.Exists(filePath))
                return false;

            File.Delete(filePath);
            DeleteEmptyParentDirectories(repositoryPath, Path.GetDirectoryName(filePath));
            state.Workspaces[workspace.Id] = workspace with { UpdatedAt = DateTimeOffset.UtcNow };
            await SaveStateAsync(state, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SourceControlStatus?> GetSourceControlStatusAsync(string workspaceId, string ownerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            return TryGetOwnedWorkspace(state, workspaceId, ownerId, out var workspace)
                ? GetSourceControlStatus(workspace.Id)
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SourceControlDiff?> GetSourceControlDiffAsync(string workspaceId, string ownerId, string path, bool staged, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedWorkspace(state, workspaceId, ownerId, out var workspace))
                return null;

            var normalizedPath = NormalizeRepositoryRelativePath(path);
            var repositoryPath = GetWorkspacePath(workspace.Id);
            var arguments = staged
                ? new[] { "diff", "--cached", "--", normalizedPath }
                : ["diff", "--", normalizedPath];
            return new(normalizedPath, staged, RunGitOrDefault(repositoryPath, arguments));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SourceControlStatus?> StageRepositoryFileAsync(string workspaceId, string ownerId, string path, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedWorkspace(state, workspaceId, ownerId, out var workspace))
                return null;

            var repositoryPath = GetWorkspacePath(workspace.Id);
            await RunGitAsync(repositoryPath, cancellationToken, "add", "--", NormalizeRepositoryRelativePath(path));
            return GetSourceControlStatus(workspace.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SourceControlStatus?> UnstageRepositoryFileAsync(string workspaceId, string ownerId, string path, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedWorkspace(state, workspaceId, ownerId, out var workspace))
                return null;

            var repositoryPath = GetWorkspacePath(workspace.Id);
            await RunGitAsync(repositoryPath, cancellationToken, "restore", "--staged", "--", NormalizeRepositoryRelativePath(path));
            return GetSourceControlStatus(workspace.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SourceControlStatus?> StageAllRepositoryChangesAsync(string workspaceId, string ownerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedWorkspace(state, workspaceId, ownerId, out var workspace))
                return null;

            var repositoryPath = GetWorkspacePath(workspace.Id);
            await RunGitAsync(repositoryPath, cancellationToken, "add", "--all");
            return GetSourceControlStatus(workspace.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SourceControlCommitResult?> CommitRepositoryChangesAsync(string workspaceId, string ownerId, SourceControlCommitRequest request, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedWorkspace(state, workspaceId, ownerId, out var workspace))
                return null;

            var message = NormalizeCommitMessage(request.Message);
            var repositoryPath = GetWorkspacePath(workspace.Id);
            var status = GetSourceControlStatus(workspace.Id);
            if (status.StagedFiles.Count == 0)
                throw new InvalidOperationException("Stage at least one change before committing.");

            await RunGitAsync(
                repositoryPath,
                cancellationToken,
                "-c",
                "user.name=Elsa Extension Builder",
                "-c",
                "user.email=extension-builder@elsa.local",
                "commit",
                "-m",
                message);
            state.Workspaces[workspace.Id] = workspace with { UpdatedAt = DateTimeOffset.UtcNow };
            await SaveStateAsync(state, cancellationToken);
            var commitId = RunGitOrDefault(repositoryPath, "rev-parse", "HEAD");
            return new(commitId, message, GetSourceControlStatus(workspace.Id));
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

    public async Task<RemoteSyncResult?> PushRepositoryAsync(string workspaceId, string ownerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedWorkspace(state, workspaceId, ownerId, out var workspace))
                return null;

            var repositoryPath = GetWorkspacePath(workspace.Id);
            var remote = GetPrimaryRemote(repositoryPath);
            var branch = GetRequiredActiveBranch(repositoryPath);
            EnsureCleanWorkingCopy(workspace.Id, "Commit or discard working-copy changes before pushing.");
            var divergence = await FetchAndMeasureRemoteDivergenceAsync(repositoryPath, remote, branch, cancellationToken);
            if (divergence.Behind > 0)
                throw new InvalidOperationException("Push blocked because the remote branch contains commits that are not in the active working copy. Pull or resolve divergence outside Extension Builder first.");

            await RunGitAsync(repositoryPath, cancellationToken, "push", "-u", remote, branch);
            state.Workspaces[workspace.Id] = workspace with { UpdatedAt = DateTimeOffset.UtcNow };
            await SaveStateAsync(state, cancellationToken);
            return new(RemoteSyncOperation.Push, RemoteSyncState.Completed, $"Pushed {branch} to {remote}.", remote, branch, 0, 0, GetSourceControlStatus(workspace.Id));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RemoteSyncResult?> PullRepositoryAsync(string workspaceId, string ownerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedWorkspace(state, workspaceId, ownerId, out var workspace))
                return null;

            var repositoryPath = GetWorkspacePath(workspace.Id);
            var remote = GetPrimaryRemote(repositoryPath);
            var branch = GetRequiredActiveBranch(repositoryPath);
            EnsureCleanWorkingCopy(workspace.Id, "Pull blocked because the working copy has uncommitted changes. Commit or discard changes before pulling.");
            var divergence = await FetchAndMeasureRemoteDivergenceAsync(repositoryPath, remote, branch, cancellationToken);
            if (!divergence.RemoteBranchExists)
                throw new InvalidOperationException($"Pull blocked because remote branch '{remote}/{branch}' does not exist.");
            if (divergence.Ahead > 0 && divergence.Behind > 0)
                throw new InvalidOperationException("Pull blocked because the active branch has diverged from the remote branch. Resolve divergence outside Extension Builder before continuing.");
            if (divergence.Behind == 0)
                return new(RemoteSyncOperation.Pull, RemoteSyncState.Completed, $"Branch {branch} is already up to date with {remote}.", remote, branch, divergence.Ahead, divergence.Behind, GetSourceControlStatus(workspace.Id));

            await RunGitAsync(repositoryPath, cancellationToken, "pull", "--ff-only", remote, branch);
            state.Workspaces[workspace.Id] = workspace with { UpdatedAt = DateTimeOffset.UtcNow };
            await SaveStateAsync(state, cancellationToken);
            return new(RemoteSyncOperation.Pull, RemoteSyncState.Completed, $"Pulled {remote}/{branch}.", remote, branch, 0, 0, GetSourceControlStatus(workspace.Id));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BuildResult?> SubmitRepositoryBuildAsync(string workspaceId, string ownerId, ExtensionProject project, SubmitRepositoryBuildRequest request, CancellationToken cancellationToken = default)
    {
        string repositoryPath;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadStateAsync(cancellationToken);
            if (!TryGetOwnedWorkspace(state, workspaceId, ownerId, out var workspace) || !TryGetOwnedProject(state, project.Id, ownerId, out _))
                return null;

            repositoryPath = GetWorkspacePath(workspace.Id);
        }
        finally
        {
            _gate.Release();
        }

        var buildId = $"build_{Guid.NewGuid():N}";
        var logPath = GetBuildLogPath(buildId);
        var createdAt = DateTimeOffset.UtcNow;
        var sourceRevisionId = RunGitOrDefault(repositoryPath, "rev-parse", "HEAD");
        if (string.IsNullOrWhiteSpace(sourceRevisionId))
            sourceRevisionId = "working-tree";

        var created = new BuildResult(buildId, project.Id, workspaceId, sourceRevisionId, BuildStatus.Pending, [], null, logPath, createdAt, null, null);
        if (!await SaveBuildAsync(created, cancellationToken))
            return null;

        var startedAt = DateTimeOffset.UtcNow;
        var running = created with { Status = BuildStatus.Running, StartedAt = startedAt };
        if (!await SaveBuildAsync(running, CancellationToken.None))
            return null;

        var commandName = request.Command ?? RepositoryBuildCommand.Build;
        var command = commandName.ToString().ToLowerInvariant();
        var diagnostics = new List<BuildDiagnostic>();
        var targetPath = ResolveRepositoryBuildTarget(repositoryPath, request.TargetPath);
        if (targetPath is null)
        {
            diagnostics.Add(new(BuildDiagnosticSeverity.Error, "No solution or project file was found for the repository build.", null, null, null, null));
            await File.WriteAllLinesAsync(logPath, diagnostics.Select(diagnostic => diagnostic.Message), cancellationToken);
            var failed = running with { Status = BuildStatus.Failed, Diagnostics = diagnostics, CompletedAt = DateTimeOffset.UtcNow };
            await SaveBuildAsync(failed, CancellationToken.None);
            return failed;
        }

        var arguments = new[] { command, targetPath };
        ProcessResult process;
        try
        {
            process = await RunProcessAsync(_dotNetExecutable, repositoryPath, cancellationToken, arguments);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            diagnostics.Add(new(BuildDiagnosticSeverity.Error, $"{commandName} was canceled.", null, null, null, null));
            await File.WriteAllLinesAsync(logPath, diagnostics.Select(diagnostic => diagnostic.Message), CancellationToken.None);
            var canceled = running with { Status = BuildStatus.Failed, Diagnostics = diagnostics, CompletedAt = DateTimeOffset.UtcNow };
            await SaveBuildAsync(canceled, CancellationToken.None);
            return canceled;
        }

        var log = new[] { process.Output, process.Error }
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .SelectMany(text => text.Split(Environment.NewLine))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        await File.WriteAllLinesAsync(logPath, log, cancellationToken);
        diagnostics.AddRange(ExtensionBuilderBuildRunner.ParseDiagnostics(log));
        if (process.ExitCode != 0 && diagnostics.All(diagnostic => diagnostic.Severity is not BuildDiagnosticSeverity.Error))
            diagnostics.Add(new(BuildDiagnosticSeverity.Error, $"{commandName} failed.", null, null, null, null));

        var completed = running with
        {
            Status = process.ExitCode == 0 ? BuildStatus.Succeeded : BuildStatus.Failed,
            Diagnostics = diagnostics,
            CompletedAt = DateTimeOffset.UtcNow
        };
        await SaveBuildAsync(completed, CancellationToken.None);
        return completed;
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
            var snapshot = await CreateSourceSnapshotCoreAsync(projectId, cancellationToken);
            state.Projects[project.Id] = project with { CurrentSourceRevisionId = snapshot.Id, UpdatedAt = DateTimeOffset.UtcNow };
            await SaveStateAsync(state, cancellationToken);
            return await ReadProjectFileAsync(ResolveProjectFilePath(projectId, normalizedPath), normalizedPath, cancellationToken);
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
            var snapshot = await CreateSourceSnapshotCoreAsync(projectId, cancellationToken);
            state.Projects[project.Id] = project with { CurrentSourceRevisionId = snapshot.Id, UpdatedAt = DateTimeOffset.UtcNow };
            await SaveStateAsync(state, cancellationToken);
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

    private ExtensionRepositorySummary ToRepositorySummary(ExtensionBuilderState state, ExtensionWorkspace workspace)
    {
        var projectIds = workspace.ProjectIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var latestBuild = state.Builds.Values
            .Where(build => projectIds.Contains(build.ProjectId))
            .OrderByDescending(build => build.CreatedAt)
            .FirstOrDefault();
        var failedBuilds = state.Builds.Values.Count(build => projectIds.Contains(build.ProjectId) && build.Status is BuildStatus.Failed);
        var failedPromotions = projectIds
            .Where(projectId => state.Promotions.TryGetValue(projectId, out _))
            .Sum(projectId => state.Promotions[projectId].Count(promotion => promotion.ReconcileOutcome.IsDegraded));
        var repositoryState = GetRepositoryState(GetWorkspacePath(workspace.Id));

        return new(
            workspace.Id,
            workspace.DisplayName,
            workspace.OwnerId,
            repositoryState.ActiveBranch,
            repositoryState.IsDirty,
            repositoryState.RemoteState,
            latestBuild?.Status,
            failedBuilds + failedPromotions,
            workspace.ProjectIds.Count,
            workspace.UpdatedAt);
    }

    private static ExtensionWorkingCopySummary ToWorkingCopySummary(WorkingCopyState workingCopy, RepositoryState repositoryState) =>
        new(
            workingCopy.Id,
            workingCopy.WorkspaceId,
            workingCopy.OwnerId,
            workingCopy.SessionId,
            workingCopy.BranchName,
            workingCopy.IsActive,
            workingCopy.IsProtectedBranch,
            string.Equals(repositoryState.ActiveBranch, workingCopy.BranchName, StringComparison.OrdinalIgnoreCase) && repositoryState.IsDirty,
            workingCopy.CreatedAt,
            workingCopy.UpdatedAt);

    private async Task InitializeManagedRepositoryAsync(string repositoryPath, string displayName, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(repositoryPath);
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "README.md"), StarterReadme(displayName), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, ".gitignore"), StarterGitIgnore(), cancellationToken);
        Directory.CreateDirectory(Path.Combine(repositoryPath, "src"));
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "src", ".gitkeep"), "", cancellationToken);

        await RunGitAsync(repositoryPath, cancellationToken, "init", "-b", "main");
        await RunGitAsync(repositoryPath, cancellationToken, "add", ".");
        await RunGitAsync(
            repositoryPath,
            cancellationToken,
            "-c",
            "user.name=Elsa Extension Builder",
            "-c",
            "user.email=extension-builder@elsa.local",
            "commit",
            "-m",
            "Initial managed repository");
    }

    private RepositoryState GetRepositoryState(string repositoryPath)
    {
        if (!Directory.Exists(Path.Combine(repositoryPath, ".git")))
            return new(null, false, "not-connected");

        var activeBranch = RunGitOrDefault(repositoryPath, "branch", "--show-current");
        var status = RunGitOrDefault(repositoryPath, "status", "--porcelain");
        var remotes = RunGitOrDefault(repositoryPath, "remote");
        return new(
            string.IsNullOrWhiteSpace(activeBranch) ? null : activeBranch,
            !string.IsNullOrWhiteSpace(status),
            string.IsNullOrWhiteSpace(remotes) ? "not-connected" : "connected");
    }

    private string GetPrimaryRemote(string repositoryPath)
    {
        var remote = RunGitOrDefault(repositoryPath, "remote")
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(remote))
            throw new InvalidOperationException("Remote sync requires a configured Git remote.");
        return remote;
    }

    private string GetRequiredActiveBranch(string repositoryPath)
    {
        var branch = RunGitOrDefault(repositoryPath, "branch", "--show-current");
        if (string.IsNullOrWhiteSpace(branch))
            throw new InvalidOperationException("Remote sync requires an active named branch.");
        return branch;
    }

    private void EnsureCleanWorkingCopy(string workspaceId, string message)
    {
        if (GetSourceControlStatus(workspaceId).IsDirty)
            throw new InvalidOperationException(message);
    }

    private async Task<RemoteDivergence> FetchAndMeasureRemoteDivergenceAsync(string repositoryPath, string remote, string branch, CancellationToken cancellationToken)
    {
        if (!RemoteBranchExists(repositoryPath, remote, branch))
            return new(false, 0, 0);

        var remoteRef = $"refs/remotes/{remote}/{branch}";
        await RunGitAsync(repositoryPath, cancellationToken, "fetch", remote, $"{branch}:{remoteRef}");
        var counts = RunGitOrDefault(repositoryPath, "rev-list", "--left-right", "--count", $"HEAD...{remoteRef}")
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        return counts.Length == 2 && int.TryParse(counts[0], out var ahead) && int.TryParse(counts[1], out var behind)
            ? new(true, ahead, behind)
            : new(true, 0, 0);
    }

    private bool RemoteBranchExists(string repositoryPath, string remote, string branch) =>
        !string.IsNullOrWhiteSpace(RunGitOrDefault(repositoryPath, "ls-remote", "--heads", remote, branch));

    private string? ResolveRepositoryBuildTarget(string repositoryPath, string? targetPath)
    {
        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            var normalized = NormalizeRepositoryRelativePath(targetPath);
            var filePath = ResolveRepositoryFilePath(repositoryPath, normalized);
            if (!File.Exists(filePath))
                throw new ArgumentException($"Build target '{normalized}' was not found.", nameof(targetPath));
            return filePath;
        }

        return EnumerateRepositoryFiles(repositoryPath, "*.sln*")
            .Where(path => Path.GetExtension(path).Equals(".sln", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".slnx", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Concat(EnumerateRepositoryFiles(repositoryPath, "*.*proj").Order(StringComparer.OrdinalIgnoreCase))
            .FirstOrDefault();
    }

    private RepositoryTree BuildRepositoryTree(string workspaceId, string repositoryPath, string? selectedSolutionPath)
    {
        var repositoryState = GetRepositoryState(repositoryPath);
        var dirtyPaths = GetDirtyRepositoryPaths(repositoryPath);
        var solutionPaths = EnumerateRepositoryFiles(repositoryPath, "*.sln*")
            .Select(path => Path.GetRelativePath(repositoryPath, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => Path.GetExtension(path).Equals(".sln", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".slnx", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var selectedSolution = SelectSolution(solutionPaths, selectedSolutionPath);
        var solutions = solutionPaths
            .Select(path => new RepositorySolutionSummary(path, Path.GetFileNameWithoutExtension(path), string.Equals(path, selectedSolution, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var entries = EnumerateRepositoryEntries(repositoryPath, dirtyPaths);
        return new(workspaceId, repositoryState.ActiveBranch, repositoryState.IsDirty, solutions, entries);
    }

    private RepositoryFileSummary[] EnumerateRepositoryEntries(string repositoryPath, ISet<string> dirtyPaths)
    {
        if (!Directory.Exists(repositoryPath))
            return [];

        var directories = EnumerateRepositoryDirectories(repositoryPath)
            .Select(path =>
            {
                var relative = Path.GetRelativePath(repositoryPath, path).Replace(Path.DirectorySeparatorChar, '/');
                return new RepositoryFileSummary(relative, RepositoryFileKind.Folder, 0, false, Directory.GetLastWriteTimeUtc(path));
            });
        var files = EnumerateRepositoryFiles(repositoryPath, "*")
            .Select(path =>
            {
                var info = new FileInfo(path);
                var relative = Path.GetRelativePath(repositoryPath, path).Replace(Path.DirectorySeparatorChar, '/');
                return new RepositoryFileSummary(relative, GetRepositoryFileKind(relative), info.Length, IsPathDirty(relative, dirtyPaths), info.LastWriteTimeUtc);
            });

        return directories.Concat(files)
            .OrderBy(x => x.Kind is RepositoryFileKind.Folder ? 0 : 1)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<RepositoryFile> ReadRepositoryFileCoreAsync(string repositoryPath, string filePath, string relativePath, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var info = new FileInfo(filePath);
        return new(relativePath, content, GetRepositoryFileKind(relativePath), info.Length, IsPathDirty(relativePath, GetDirtyRepositoryPaths(repositoryPath)), info.LastWriteTimeUtc);
    }

    private SourceControlStatus GetSourceControlStatus(string workspaceId)
    {
        var repositoryPath = GetWorkspacePath(workspaceId);
        var repositoryState = GetRepositoryState(repositoryPath);
        var changedFiles = RunGitOrDefault(repositoryPath, "status", "--porcelain", "--untracked-files=all")
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseSourceControlStatus)
            .OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new(
            workspaceId,
            repositoryState.ActiveBranch,
            changedFiles.Length > 0,
            changedFiles,
            changedFiles.Where(x => x.IsStaged).ToArray(),
            changedFiles.Where(x => x.IsUnstaged).ToArray());
    }

    private static SourceControlFileStatus ParseSourceControlStatus(string line)
    {
        var indexStatus = line.Length > 0 ? line[0] : ' ';
        var workTreeStatus = line.Length > 1 ? line[1] : ' ';
        var path = line.Length > 3 ? line[3..].Trim().Trim('"').Replace('\\', '/') : "";
        var renameSeparator = path.IndexOf(" -> ", StringComparison.Ordinal);
        if (renameSeparator >= 0)
            path = path[(renameSeparator + 4)..];

        return new(
            path,
            $"{indexStatus}{workTreeStatus}".Trim(),
            indexStatus is not ' ' and not '?',
            workTreeStatus is not ' ' || indexStatus is '?');
    }

    private ISet<string> GetDirtyRepositoryPaths(string repositoryPath)
    {
        var status = RunGitOrDefault(repositoryPath, "status", "--porcelain", "--untracked-files=all");
        if (string.IsNullOrWhiteSpace(status))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return status.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(ParseDirtyStatusPaths)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ParseDirtyStatusPaths(string line)
    {
        if (line.Length < 4)
            yield break;

        var path = line[3..].Trim().Trim('"').Replace('\\', '/');
        var renameSeparator = path.IndexOf(" -> ", StringComparison.Ordinal);
        if (renameSeparator >= 0)
        {
            yield return path[(renameSeparator + 4)..];
            yield break;
        }

        yield return path;
    }

    private static bool IsPathDirty(string path, ISet<string> dirtyPaths) =>
        dirtyPaths.Contains(path) || dirtyPaths.Any(dirtyPath => dirtyPath.StartsWith(path.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));

    private static string? SelectSolution(IReadOnlyList<string> solutionPaths, string? selectedSolutionPath)
    {
        if (solutionPaths.Count == 0)
            return null;
        if (!string.IsNullOrWhiteSpace(selectedSolutionPath))
        {
            var normalized = NormalizeRepositoryRelativePath(selectedSolutionPath);
            if (solutionPaths.Any(path => string.Equals(path, normalized, StringComparison.OrdinalIgnoreCase)))
                return normalized;
        }

        return solutionPaths.Count == 1 ? solutionPaths[0] : null;
    }

    private static RepositoryFileKind GetRepositoryFileKind(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
            return RepositoryFileKind.Solution;
        if (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) || extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) || extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase))
            return RepositoryFileKind.Project;
        return RepositoryFileKind.File;
    }

    private string ResolveRepositoryFilePath(string repositoryPath, string relativePath)
    {
        var normalizedPath = NormalizeRepositoryRelativePath(relativePath);
        var path = Path.GetFullPath(Path.Combine(repositoryPath, normalizedPath));
        if (!path.StartsWith(repositoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The resolved repository file path is outside the repository root.");
        return path;
    }

    internal static string NormalizeRepositoryRelativePath(string path)
    {
        var normalized = (path ?? "").Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized) ||
            Path.IsPathRooted(normalized) ||
            normalized.Split('/').Any(segment => segment is "" or "." or ".." || segment.Equals(".git", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A safe relative repository file path is required.", nameof(path));
        return normalized;
    }

    private static IReadOnlyDictionary<string, string> BuildTemplateParameterValues(ExtensionTemplate template, ApplyRepositoryTemplateRequest request)
    {
        var provided = request.Parameters is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(request.Parameters, StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["packageId"] = template.DefaultPackageId,
            ["packageVersion"] = template.DefaultPackageVersion,
            ["targetFramework"] = template.DefaultTargetFramework
        };

        foreach (var parameter in template.Parameters)
        {
            provided.TryGetValue(parameter.Name, out var providedValue);
            var value = string.IsNullOrWhiteSpace(providedValue) ? parameter.DefaultValue : providedValue.Trim();
            if (parameter.Required && string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"Template parameter '{parameter.Name}' is required.", nameof(request));

            if (!string.IsNullOrWhiteSpace(value))
            {
                ValidateTemplateParameter(parameter.Name, value);
                values[parameter.Name] = value;
            }
        }

        return values;
    }

    private static void ValidateTemplateParameter(string name, string value)
    {
        if (name.Equals("name", StringComparison.OrdinalIgnoreCase) && !IsSafeTemplateIdentifier(value))
            throw new ArgumentException("Template parameter 'name' must start with a letter and contain only letters, numbers, dots, underscores, or hyphens.", nameof(value));
        if ((name.Equals("namespace", StringComparison.OrdinalIgnoreCase) || name.Equals("packageId", StringComparison.OrdinalIgnoreCase)) && !IsSafeTemplateIdentifier(value))
            throw new ArgumentException($"Template parameter '{name}' must start with a letter and contain only letters, numbers, dots, underscores, or hyphens.", nameof(value));
    }

    private static bool IsSafeTemplateIdentifier(string value) =>
        value.Length > 0 && char.IsLetter(value[0]) && value.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-');

    private static string NormalizeTemplateTargetPath(string? targetPath, ExtensionTemplateScope scope, IReadOnlyDictionary<string, string> values)
    {
        if (!string.IsNullOrWhiteSpace(targetPath))
            return NormalizeRepositoryRelativePath(targetPath);

        return scope switch
        {
            ExtensionTemplateScope.Project => values.TryGetValue("name", out var name) ? NormalizeRepositoryRelativePath(name) : "",
            ExtensionTemplateScope.Item => "src",
            _ => ""
        };
    }

    private static string CombineTemplatePath(string targetPath, string templatePath) =>
        string.IsNullOrWhiteSpace(targetPath)
            ? templatePath
            : $"{targetPath.TrimEnd('/')}/{templatePath.TrimStart('/')}";

    private static string RenderTemplateContent(ExtensionTemplate template, ProjectTemplateFile file, string renderedPath, IReadOnlyDictionary<string, string> values)
    {
        var packageId = values.TryGetValue("packageId", out var configuredPackageId) ? configuredPackageId : template.DefaultPackageId;
        var packageVersion = values.TryGetValue("packageVersion", out var configuredPackageVersion) ? configuredPackageVersion : template.DefaultPackageVersion;
        var targetFramework = values.TryGetValue("targetFramework", out var configuredTargetFramework) ? configuredTargetFramework : template.DefaultTargetFramework;
        if (renderedPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return ExtensionBuilderTemplateCatalog.ProjectFile(packageId, packageVersion, targetFramework);
        if (Path.GetFileName(renderedPath).Equals("elsa-package.json", StringComparison.OrdinalIgnoreCase))
            return ExtensionBuilderTemplateCatalog.RewriteManifest(template.DefaultManifest.Content, packageId, packageVersion).GetRawText();
        return RenderTemplateText(file.Content, values);
    }

    private static string RenderTemplateText(string text, IReadOnlyDictionary<string, string> values)
    {
        var rendered = text;
        foreach (var (key, value) in values)
            rendered = rendered.Replace("{{" + key + "}}", value, StringComparison.OrdinalIgnoreCase);
        return rendered;
    }

    private static string NormalizeCommitMessage(string message)
    {
        var normalized = (message ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("A commit message is required.", nameof(message));
        return normalized;
    }

    private static bool IsRepositoryPrivatePath(string repositoryPath, string path)
    {
        var relative = Path.GetRelativePath(repositoryPath, path).Replace(Path.DirectorySeparatorChar, '/');
        return relative.Split('/').Any(segment => segment.Equals(".git", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> EnumerateRepositoryDirectories(string repositoryPath)
    {
        foreach (var directory in EnumerateRepositoryDirectoriesCore(repositoryPath))
            yield return directory;
    }

    private static IEnumerable<string> EnumerateRepositoryFiles(string repositoryPath, string searchPattern)
    {
        foreach (var file in Directory.EnumerateFiles(repositoryPath, searchPattern, SearchOption.TopDirectoryOnly).Where(path => !IsRepositoryPrivatePath(repositoryPath, path)))
            yield return file;

        foreach (var directory in EnumerateRepositoryDirectoriesCore(repositoryPath))
        {
            foreach (var file in Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly))
                yield return file;
        }
    }

    private static IEnumerable<string> EnumerateRepositoryDirectoriesCore(string repositoryPath)
    {
        var pending = new Stack<string>(Directory.EnumerateDirectories(repositoryPath, "*", SearchOption.TopDirectoryOnly).Reverse());
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if (IsRepositoryPrivatePath(repositoryPath, directory))
                continue;

            yield return directory;
            foreach (var child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly).Reverse())
                pending.Push(child);
        }
    }

    private static void DeleteEmptyParentDirectories(string repositoryPath, string? startPath)
    {
        var root = Path.GetFullPath(repositoryPath);
        var current = string.IsNullOrWhiteSpace(startPath) ? null : Path.GetFullPath(startPath);
        while (!string.IsNullOrWhiteSpace(current) &&
               !string.Equals(current, root, StringComparison.OrdinalIgnoreCase) &&
               current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
               Directory.Exists(current) &&
               !Directory.EnumerateFileSystemEntries(current).Any())
        {
            Directory.Delete(current);
            current = Path.GetDirectoryName(current);
        }
    }

    private async Task SwitchRepositoryBranchAsync(string repositoryPath, string branchName, CancellationToken cancellationToken)
    {
        var existingBranches = RunGitOrDefault(repositoryPath, "branch", "--list", branchName);
        if (string.IsNullOrWhiteSpace(existingBranches))
            await RunGitAsync(repositoryPath, cancellationToken, "switch", "-c", branchName);
        else
            await RunGitAsync(repositoryPath, cancellationToken, "switch", branchName);
    }

    private static string NormalizeSessionId(string sessionId)
    {
        var normalized = NormalizeToken(sessionId, "session id");
        return normalized.Length > 80 ? normalized[..80] : normalized;
    }

    private static string NormalizeBranchName(string? branchName, string ownerId, string sessionId)
    {
        var normalized = string.IsNullOrWhiteSpace(branchName)
            ? $"extension-builder/{NormalizeToken(ownerId, "owner id")}-{sessionId}"
            : branchName.Trim();
        if (normalized.Length > 120 ||
            normalized.StartsWith("-", StringComparison.Ordinal) ||
            normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.EndsWith("/", StringComparison.Ordinal) ||
            normalized.Contains("..", StringComparison.Ordinal) ||
            normalized.Any(char.IsWhiteSpace) ||
            normalized.Any(c => char.IsControl(c) || c is '~' or '^' or ':' or '?' or '*' or '[' or '\\'))
            throw new ArgumentException("A safe Git branch name is required.", nameof(branchName));

        return normalized;
    }

    private static string NormalizeToken(string value, string name)
    {
        var normalized = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException($"A {name} is required.", name);

        var safe = new string(normalized
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-')
            .ToArray()).Trim('-', '.');
        if (string.IsNullOrWhiteSpace(safe))
            throw new ArgumentException($"A safe {name} is required.", name);

        return safe;
    }

    private static bool IsProtectedBranch(string branchName) =>
        branchName.Equals("main", StringComparison.OrdinalIgnoreCase) ||
        branchName.Equals("master", StringComparison.OrdinalIgnoreCase);

    private async Task RunGitAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
    {
        var result = await RunProcessAsync(_gitExecutable, workingDirectory, cancellationToken, arguments);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Git command failed: {string.Join(' ', arguments)}{Environment.NewLine}{result.Error}".TrimEnd());
    }

    private string RunGitOrDefault(string workingDirectory, params string[] arguments)
    {
        try
        {
            var result = RunProcess(_gitExecutable, workingDirectory, arguments);
            return result.ExitCode == 0 ? result.Output.Trim() : "";
        }
        catch
        {
            return "";
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
    {
        try
        {
            using var process = StartProcess(fileName, workingDirectory, arguments);
            try
            {
                var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                return new(process.ExitCode, await outputTask, await errorTask);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);

                throw;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Could not start '{fileName}'.", ex);
        }
    }

    private static ProcessResult RunProcess(string fileName, string workingDirectory, params string[] arguments)
    {
        using var process = StartProcess(fileName, workingDirectory, arguments);
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new(process.ExitCode, output, error);
    }

    private static Process StartProcess(string fileName, string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{fileName}'.");
    }

    private static string StarterReadme(string displayName) =>
        $$"""
        # {{displayName}}

        Managed Extension Builder repository.
        """;

    private static string StarterGitIgnore() =>
        """
        bin/
        obj/
        *.user
        """;

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

    private sealed record RepositoryState(string? ActiveBranch, bool IsDirty, string RemoteState);

    private sealed record WorkingCopyState(
        string Id,
        string WorkspaceId,
        string OwnerId,
        string SessionId,
        string BranchName,
        bool IsActive,
        bool IsProtectedBranch,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record ProcessResult(int ExitCode, string Output, string Error);

    private sealed class ExtensionBuilderState
    {
        public Dictionary<string, ExtensionWorkspace> Workspaces { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ExtensionProject> Projects { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, BuildResult> Builds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, PackagePromotionRecord[]> Promotions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> ActiveVersions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, WorkingCopyState> WorkingCopies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
