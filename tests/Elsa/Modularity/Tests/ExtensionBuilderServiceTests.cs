using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;
using Elsa.Server.ExtensionBuilder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nuplane.Abstractions;
using Nuplane.Admin;
using Nuplane.Operational;
using Nuplane.Reconciliation;
using Nuplane.Reconciliation.Models;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class ExtensionBuilderServiceTests : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"elsa-extension-builder-{Guid.NewGuid():N}");
    private readonly ExtensionBuilderCaller _caller = new("owner-1", "Owner One", true, true);

    public ExtensionBuilderServiceTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task WorkspacesAndProjectsArePersistedAndOwnerScoped()
    {
        var service = CreateService();
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("elsa-activity-module", "Elsa.Test.Activity", "1.0.0", "net10.0", null, null));

        var restarted = CreateService();
        var files = await restarted.ListProjectFilesAsync(_caller, project.Id);
        var otherOwnerProject = await restarted.GetProjectAsync(_caller with { OwnerId = "other" }, project.Id);

        Assert.NotNull(await restarted.GetWorkspaceAsync(_caller, workspace.Id));
        Assert.NotNull(await restarted.GetProjectAsync(_caller, project.Id));
        Assert.Contains(files!, x => x.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
        Assert.Null(otherOwnerProject);
    }

    [Fact]
    public async Task RepositorySummariesAreOwnerScopedAndExposeWorkbenchHealth()
    {
        var service = CreateService(buildRunner: new FakeBuildRunner(BuildStatus.Failed));
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Team Extensions"));
        await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.Repository", "1.0.0", "net10.0", null, null));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.Failed", "1.0.0", "net10.0", null, null));
        await service.SubmitBuildAsync(_caller, project.Id);

        var repositories = await service.ListRepositoriesAsync(_caller);
        var otherOwnerRepositories = await service.ListRepositoriesAsync(_caller with { OwnerId = "other" });

        var repository = Assert.Single(repositories);
        Assert.Equal(workspace.Id, repository.Id);
        Assert.Equal("Team Extensions", repository.Name);
        Assert.Equal(_caller.OwnerId, repository.OwnerId);
        Assert.Equal(2, repository.ProjectCount);
        Assert.Equal(BuildStatus.Failed, repository.LatestBuildStatus);
        Assert.Equal(1, repository.AttentionCount);
        Assert.Equal("not-connected", repository.RemoteState);
        Assert.False(repository.IsDirty);
        Assert.Empty(otherOwnerRepositories);
    }

    [Fact]
    public async Task CreateWorkspaceInitializesManagedRepositoryWithStarterCommit()
    {
        var service = CreateService();

        var workspace = await service.CreateWorkspaceAsync(_caller, new("Managed Extensions"));
        var repositoryPath = Path.Combine(_directory, "state", "workspaces", workspace.Id);
        var repositories = await service.ListRepositoriesAsync(_caller);

        var repository = Assert.Single(repositories);
        Assert.True(Directory.Exists(Path.Combine(repositoryPath, ".git")));
        Assert.Contains("Managed Extensions", await File.ReadAllTextAsync(Path.Combine(repositoryPath, "README.md")));
        Assert.Equal("1", await GitAsync(repositoryPath, "rev-list", "--count", "HEAD"));
        Assert.Equal("Elsa Extension Builder|extension-builder@elsa.local|Initial managed repository", await GitAsync(repositoryPath, "log", "-1", "--pretty=%an|%ae|%s"));
        Assert.Equal("main", repository.ActiveBranch);
        Assert.Equal("not-connected", repository.RemoteState);
        Assert.False(repository.IsDirty);
    }

    [Fact]
    public async Task CreateWorkspaceCleansUpWhenManagedRepositoryInitializationFails()
    {
        var service = CreateService(options: new ExtensionBuilderOptions
        {
            StoragePath = Path.Combine(_directory, "state"),
            GitExecutable = "missing-git-executable"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateWorkspaceAsync(_caller, new("Broken Repository")));

        Assert.Empty(await service.ListWorkspacesAsync(_caller));
        var workspacesPath = Path.Combine(_directory, "state", "workspaces");
        Assert.True(!Directory.Exists(workspacesPath) || !Directory.EnumerateDirectories(workspacesPath).Any());
    }

    [Fact]
    public async Task SelectWorkingCopyCreatesExplicitSessionBranch()
    {
        var service = CreateService();
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var repositoryPath = Path.Combine(_directory, "state", "workspaces", workspace.Id);

        var workingCopy = await service.SelectWorkingCopyAsync(_caller, workspace.Id, new("session-1", null, false));
        var repositories = await service.ListRepositoriesAsync(_caller);

        Assert.Equal("extension-builder/owner-1-session-1", workingCopy!.BranchName);
        Assert.True(workingCopy.IsActive);
        Assert.False(workingCopy.IsProtectedBranch);
        Assert.Equal(workingCopy.BranchName, Assert.Single(repositories).ActiveBranch);
        Assert.Equal(workingCopy.BranchName, await GitAsync(repositoryPath, "branch", "--show-current"));
    }

    [Fact]
    public async Task WorkingCopiesAreScopedByOwnerSessionAndBranch()
    {
        var service = CreateService();
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        await service.SelectWorkingCopyAsync(_caller, workspace.Id, new("session-1", "feature/one", false));
        await service.SelectWorkingCopyAsync(_caller, workspace.Id, new("session-2", "feature/two", false));
        await service.SelectWorkingCopyAsync(_caller, workspace.Id, new("session-1", "feature/three", false));

        var sessionOneCopies = await service.ListWorkingCopiesAsync(_caller, workspace.Id, "session-1");
        var sessionTwoCopies = await service.ListWorkingCopiesAsync(_caller, workspace.Id, "session-2");
        var sessionOne = sessionOneCopies!;

        Assert.Equal(new[] { "feature/three", "feature/one" }, sessionOne.Select(x => x.BranchName).ToArray());
        Assert.True(sessionOne.Single(x => x.BranchName == "feature/three").IsActive);
        Assert.False(sessionOne.Single(x => x.BranchName == "feature/one").IsActive);
        Assert.Equal("feature/two", Assert.Single(sessionTwoCopies!).BranchName);
        Assert.Null(await service.ListWorkingCopiesAsync(_caller with { OwnerId = "other" }, workspace.Id, null));
    }

    [Fact]
    public async Task WorkingCopySelectionReportsDirtyStateForActiveBranch()
    {
        var service = CreateService();
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var repositoryPath = Path.Combine(_directory, "state", "workspaces", workspace.Id);
        var workingCopy = await service.SelectWorkingCopyAsync(_caller, workspace.Id, new("session-1", "feature/dirty", false));
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "src", "Dirty.cs"), "namespace Dirty;");

        var copies = await service.ListWorkingCopiesAsync(_caller, workspace.Id, "session-1");
        var repositories = await service.ListRepositoriesAsync(_caller);

        Assert.True(Assert.Single(copies!).IsDirty);
        Assert.True(Assert.Single(repositories).IsDirty);
        Assert.Equal(workingCopy!.BranchName, Assert.Single(repositories).ActiveBranch);
    }

    [Fact]
    public async Task SelectWorkingCopyRequiresExplicitConfirmationForDefaultBranch()
    {
        var service = CreateService();
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));

        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SelectWorkingCopyAsync(_caller, workspace.Id, new("session-1", "main", false)));
        var allowed = await service.SelectWorkingCopyAsync(_caller, workspace.Id, new("session-1", "main", true));

        Assert.Contains("requires explicit confirmation", denied.Message);
        Assert.True(allowed!.IsProtectedBranch);
        Assert.Equal("main", allowed.BranchName);
    }

    [Fact]
    public async Task RepositoryTreeAutoSelectsSingleSolutionFromActiveWorkingCopy()
    {
        var service = CreateService();
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        await service.SelectWorkingCopyAsync(_caller, workspace.Id, new("session-1", "feature/files", false));
        await service.WriteRepositoryFileAsync(_caller, workspace.Id, "src/Hello/Hello.csproj", new("<Project />"));
        await service.WriteRepositoryFileAsync(_caller, workspace.Id, "Workspace.slnx", new("<Solution />"));

        var tree = await service.GetRepositoryTreeAsync(_caller, workspace.Id, null);

        var solution = Assert.Single(tree!.Solutions);
        Assert.Equal("Workspace.slnx", solution.Path);
        Assert.True(solution.IsSelected);
        Assert.Contains(tree.Entries, entry => entry is { Path: "src/Hello/Hello.csproj", Kind: RepositoryFileKind.Project, IsDirty: true });
        Assert.Contains(tree.Entries, entry => entry is { Path: "Workspace.slnx", Kind: RepositoryFileKind.Solution, IsDirty: true });
        Assert.True(tree.IsDirty);
        Assert.Equal("feature/files", tree.ActiveBranch);
    }

    [Fact]
    public async Task RepositoryTreeLeavesMultipleSolutionsForExplicitSelection()
    {
        var service = CreateService();
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        await service.WriteRepositoryFileAsync(_caller, workspace.Id, "A.sln", new(""));
        await service.WriteRepositoryFileAsync(_caller, workspace.Id, "nested/B.slnx", new(""));

        var unselected = await service.GetRepositoryTreeAsync(_caller, workspace.Id, null);
        var selected = await service.GetRepositoryTreeAsync(_caller, workspace.Id, "nested/B.slnx");

        Assert.Equal(new[] { "A.sln", "nested/B.slnx" }, unselected!.Solutions.Select(x => x.Path).ToArray());
        Assert.All(unselected.Solutions, solution => Assert.False(solution.IsSelected));
        Assert.False(selected!.Solutions.Single(x => x.Path == "A.sln").IsSelected);
        Assert.True(selected.Solutions.Single(x => x.Path == "nested/B.slnx").IsSelected);
    }

    [Fact]
    public async Task RepositoryFileMutationsUseNestedWorkingTreePaths()
    {
        var service = CreateService();
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));

        var created = await service.WriteRepositoryFileAsync(_caller, workspace.Id, "src/Nested/Activity.cs", new("namespace Before;"));
        var read = await service.ReadRepositoryFileAsync(_caller, workspace.Id, "src/Nested/Activity.cs");
        var saved = await service.WriteRepositoryFileAsync(_caller, workspace.Id, "src/Nested/Activity.cs", new("namespace After;"));
        var moved = await service.MoveRepositoryFileAsync(_caller, workspace.Id, new("src/Nested/Activity.cs", "src/Renamed/Activity.cs"));
        var deleted = await service.DeleteRepositoryFileAsync(_caller, workspace.Id, "src/Renamed/Activity.cs");
        var missing = await service.ReadRepositoryFileAsync(_caller, workspace.Id, "src/Renamed/Activity.cs");

        Assert.Equal("src/Nested/Activity.cs", created!.Path);
        Assert.Equal("namespace Before;", read!.Content);
        Assert.Equal("namespace After;", saved!.Content);
        Assert.Equal("src/Renamed/Activity.cs", moved!.Path);
        Assert.Equal("namespace After;", moved.Content);
        Assert.True(moved.IsDirty);
        Assert.True(deleted);
        Assert.Null(missing);
    }

    [Fact]
    public async Task RepositoryFileAccessRejectsUnsafePathsAndOtherOwners()
    {
        var service = CreateService();
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        await service.WriteRepositoryFileAsync(_caller, workspace.Id, "src/Safe.cs", new("namespace Safe;"));

        await Assert.ThrowsAsync<ArgumentException>(() => service.ReadRepositoryFileAsync(_caller, workspace.Id, "../outside.cs"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ReadRepositoryFileAsync(_caller, workspace.Id, ".git/config"));
        Assert.Null(await service.ReadRepositoryFileAsync(_caller with { OwnerId = "other" }, workspace.Id, "src/Safe.cs"));
        Assert.Null(await service.GetRepositoryTreeAsync(_caller with { OwnerId = "other" }, workspace.Id, null));
        Assert.False(await service.DeleteRepositoryFileAsync(_caller with { OwnerId = "other" }, workspace.Id, "src/Safe.cs"));
    }

    [Fact]
    public async Task SourceControlStagesUnstagesDiffsAndCommitsWorkingCopyChanges()
    {
        var service = CreateService();
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var repositoryPath = Path.Combine(_directory, "state", "workspaces", workspace.Id);
        await service.SelectWorkingCopyAsync(_caller, workspace.Id, new("session-1", "feature/source-control", false));
        await service.WriteRepositoryFileAsync(_caller, workspace.Id, "src/Status.cs", new("namespace Status;"));

        var dirty = await service.GetSourceControlStatusAsync(_caller, workspace.Id);
        var staged = await service.StageRepositoryFileAsync(_caller, workspace.Id, new("src/Status.cs"));
        var stagedDiff = await service.GetSourceControlDiffAsync(_caller, workspace.Id, "src/Status.cs", staged: true);
        var unstaged = await service.UnstageRepositoryFileAsync(_caller, workspace.Id, new("src/Status.cs"));
        var stageAll = await service.StageAllRepositoryChangesAsync(_caller, workspace.Id);
        var commit = await service.CommitRepositoryChangesAsync(_caller, workspace.Id, new("Add status source"));
        var repositories = await service.ListRepositoriesAsync(_caller);

        Assert.True(dirty!.IsDirty);
        Assert.Contains(dirty.UnstagedFiles, x => x is { Path: "src/Status.cs", IsUnstaged: true });
        Assert.Contains(staged!.StagedFiles, x => x is { Path: "src/Status.cs", IsStaged: true });
        Assert.Contains("+namespace Status;", stagedDiff!.Patch);
        Assert.Contains(unstaged!.UnstagedFiles, x => x.Path == "src/Status.cs");
        Assert.Contains(stageAll!.StagedFiles, x => x.Path == "src/Status.cs");
        Assert.False(commit!.Status.IsDirty);
        Assert.NotEmpty(commit.CommitId);
        Assert.Equal("Add status source", await GitAsync(repositoryPath, "log", "-1", "--pretty=%s"));
        Assert.False(Assert.Single(repositories).IsDirty);
    }

    [Fact]
    public async Task SourceControlRejectsEmptyCommitUnsafePathsAndOtherOwners()
    {
        var service = CreateService();
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        await service.WriteRepositoryFileAsync(_caller, workspace.Id, "src/Safe.cs", new("namespace Safe;"));

        var emptyCommit = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CommitRepositoryChangesAsync(_caller, workspace.Id, new("No staged changes")));

        await Assert.ThrowsAsync<ArgumentException>(() => service.StageRepositoryFileAsync(_caller, workspace.Id, new("../outside.cs")));
        Assert.Contains("Stage at least one change", emptyCommit.Message);
        Assert.Null(await service.GetSourceControlStatusAsync(_caller with { OwnerId = "other" }, workspace.Id));
        Assert.Null(await service.GetSourceControlDiffAsync(_caller with { OwnerId = "other" }, workspace.Id, "src/Safe.cs", staged: false));
        Assert.Null(await service.StageRepositoryFileAsync(_caller with { OwnerId = "other" }, workspace.Id, new("src/Safe.cs")));
        Assert.Null(await service.CommitRepositoryChangesAsync(_caller with { OwnerId = "other" }, workspace.Id, new("No access")));
    }

    [Fact]
    public async Task TemplateDiscoveryExposesTrustedScopesParametersAndCompatibility()
    {
        var templates = await CreateService().ListTemplatesAsync();

        Assert.Equal(
            [ExtensionTemplateScope.Repository, ExtensionTemplateScope.Solution, ExtensionTemplateScope.Project, ExtensionTemplateScope.Item],
            templates.Select(template => template.Scope).Distinct().OrderBy(scope => scope).ToArray());
        Assert.DoesNotContain(templates, template => template.Id.Contains("archive", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(templates, template => template is { Id: "generic-dotnet", Scope: ExtensionTemplateScope.Project } && template.CompatibleFileExtensions.Contains(".slnx"));
        Assert.Contains(templates.Single(template => template.Id == "csharp-class").Parameters, parameter => parameter is { Name: "namespace", Required: true });
    }

    [Fact]
    public async Task RepositoryTemplateApplicationSupportsTrustedScopesAndDirtyTree()
    {
        var service = CreateService();
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        await service.SelectWorkingCopyAsync(_caller, workspace.Id, new("session-1", "feature/templates", false));

        await service.ApplyRepositoryTemplateAsync(_caller, workspace.Id, new("repository-readme", ExtensionTemplateScope.Repository, "docs/templates", new Dictionary<string, string>
        {
            ["name"] = "ExtensionTemplates",
            ["description"] = "Trusted generated repository content."
        }));
        await service.ApplyRepositoryTemplateAsync(_caller, workspace.Id, new("solution-slnx", ExtensionTemplateScope.Solution, "solutions", new Dictionary<string, string>
        {
            ["name"] = "GeneratedExtensions"
        }));
        var projectResult = await service.ApplyRepositoryTemplateAsync(_caller, workspace.Id, new("generic-dotnet", ExtensionTemplateScope.Project, "src/GeneratedProject", new Dictionary<string, string>
        {
            ["packageId"] = "Elsa.Generated.Project",
            ["packageVersion"] = "2.1.0",
            ["targetFramework"] = "net10.0"
        }));
        var itemResult = await service.ApplyRepositoryTemplateAsync(_caller, workspace.Id, new("csharp-class", ExtensionTemplateScope.Item, "src/GeneratedProject/Generated", new Dictionary<string, string>
        {
            ["name"] = "CustomActivity",
            ["namespace"] = "Elsa.Generated.Project"
        }));
        var generatedItem = await service.ReadRepositoryFileAsync(_caller, workspace.Id, "src/GeneratedProject/Generated/CustomActivity.cs");
        var generatedProject = await service.ReadRepositoryFileAsync(_caller, workspace.Id, "src/GeneratedProject/GenericExtension.csproj");
        var status = await service.GetSourceControlStatusAsync(_caller, workspace.Id);

        Assert.Contains(projectResult!.Tree.Entries, entry => entry is { Path: "docs/templates/README.md", IsDirty: true });
        Assert.Contains(projectResult.Tree.Entries, entry => entry is { Path: "solutions/GeneratedExtensions.slnx", Kind: RepositoryFileKind.Solution, IsDirty: true });
        Assert.Contains(projectResult.Tree.Entries, entry => entry is { Path: "src/GeneratedProject/GenericExtension.csproj", Kind: RepositoryFileKind.Project, IsDirty: true });
        Assert.Contains(itemResult!.Files, file => file is { Path: "src/GeneratedProject/Generated/CustomActivity.cs", IsDirty: true });
        Assert.Contains("namespace Elsa.Generated.Project;", generatedItem!.Content);
        Assert.Contains("public sealed class CustomActivity", generatedItem.Content);
        Assert.Contains("<PackageId>Elsa.Generated.Project</PackageId>", generatedProject!.Content);
        Assert.Contains(status!.ChangedFiles, file => file.Path == "src/GeneratedProject/Generated/CustomActivity.cs");
        Assert.True(status.IsDirty);
    }

    [Fact]
    public async Task RepositoryTemplateApplicationRejectsUntrustedUnsafeOverwriteAndOtherOwners()
    {
        var service = CreateService();
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));

        await Assert.ThrowsAsync<ArgumentException>(() => service.ApplyRepositoryTemplateAsync(_caller, workspace.Id, new("uploaded-archive", null, null, null)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ApplyRepositoryTemplateAsync(_caller, workspace.Id, new("csharp-class", ExtensionTemplateScope.Item, "src", new Dictionary<string, string>
        {
            ["name"] = "../Bad",
            ["namespace"] = "Elsa.Generated"
        })));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ApplyRepositoryTemplateAsync(_caller, workspace.Id, new("csharp-class", ExtensionTemplateScope.Item, ".git/hooks", new Dictionary<string, string>
        {
            ["name"] = "SafeClass",
            ["namespace"] = "Elsa.Generated"
        })));
        await Assert.ThrowsAsync<IOException>(() => service.ApplyRepositoryTemplateAsync(_caller, workspace.Id, new("repository-readme", ExtensionTemplateScope.Repository, null, new Dictionary<string, string>
        {
            ["name"] = "AlreadyExists"
        })));
        Assert.Null(await service.ApplyRepositoryTemplateAsync(_caller with { OwnerId = "other" }, workspace.Id, new("csharp-class", ExtensionTemplateScope.Item, "src", new Dictionary<string, string>
        {
            ["name"] = "NoAccess",
            ["namespace"] = "Elsa.Generated"
        })));
    }

    [Fact]
    public async Task FileEditsPersistAndRejectUnsafePaths()
    {
        var service = CreateService();
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.Generic", "1.0.0", "net10.0", null, null));

        var written = await service.WriteProjectFileAsync(_caller, project.Id, "src/NewFile.cs", new("namespace Test;"));
        var read = await service.ReadProjectFileAsync(_caller, project.Id, "src/NewFile.cs");
        var deleted = await service.DeleteProjectFileAsync(_caller, project.Id, "src/NewFile.cs");

        Assert.Equal("namespace Test;", written!.Content);
        Assert.Equal("namespace Test;", read!.Content);
        Assert.True(deleted);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ReadProjectFileAsync(_caller, project.Id, "../outside.cs"));
    }

    [Fact]
    public async Task DeleteWorkspaceRemovesProjectFilesAndBuildArtifacts()
    {
        var service = CreateService();
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.Delete", "1.0.0", "net10.0", null, null));
        var build = await service.SubmitBuildAsync(_caller, project.Id);
        var projectPath = Path.Combine(_directory, "state", "projects", project.Id);
        var buildPath = Path.Combine(_directory, "state", "builds", build!.Id);

        var deleted = await service.DeleteWorkspaceAsync(_caller, workspace.Id);

        Assert.True(deleted);
        Assert.False(Directory.Exists(projectPath));
        Assert.False(Directory.Exists(buildPath));
        Assert.Null(await service.GetProjectAsync(_caller, project.Id));
        Assert.Null(await service.GetBuildAsync(_caller, build.Id));
    }

    [Fact]
    public async Task QueuedBuildDoesNotResurrectArtifactsAfterProjectDeletion()
    {
        var storage = CreateStorage();
        var queue = new CapturingBuildQueue();
        var service = CreateService(buildQueue: queue, storage: storage);
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.DeleteQueued", "1.0.0", "net10.0", null, null));
        var build = await service.SubmitBuildAsync(_caller, project.Id);
        var buildPath = Path.Combine(_directory, "state", "builds", build!.Id);

        Assert.True(await service.DeleteProjectAsync(_caller, project.Id));
        await new ExtensionBuilderBuildExecutor(storage, new FakeBuildRunner(BuildStatus.Succeeded), NullLogger<ExtensionBuilderBuildExecutor>.Instance)
            .ExecuteAsync(queue.WorkItem!);

        Assert.Null(await service.GetBuildAsync(_caller, build.Id));
        Assert.False(Directory.Exists(buildPath));
    }

    [Fact]
    public async Task StorageRejectsBuildAndPromotionWritesForDeletedProject()
    {
        var storage = CreateStorage();
        var service = CreateService(storage: storage);
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.DeletedWrites", "1.0.0", "net10.0", null, null));

        Assert.True(await service.DeleteProjectAsync(_caller, project.Id));
        var build = new BuildResult("build", project.Id, project.WorkspaceId, project.CurrentSourceRevisionId, BuildStatus.Running, [], null, Path.Combine(_directory, "state", "builds", "build", "build.log"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
        var promotion = new PackagePromotionRecord(project.PackageId, project.PackageVersion, "artifact.nupkg", "feed.nupkg", DateTimeOffset.UtcNow, new("completed", "corr", null, false, []), true, false);

        Assert.False(await storage.SaveBuildAsync(build));
        Assert.False(await storage.AddPromotionAsync(project.Id, promotion));
        Assert.Null(await service.GetBuildAsync(_caller, build.Id));
    }

    [Fact]
    public async Task SubmitBuildStoresSuccessfulArtifactAndLog()
    {
        var buildRunner = new FakeBuildRunner(BuildStatus.Succeeded);
        var service = CreateService(buildRunner: buildRunner);
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.Build", "1.0.0", "net10.0", null, null));

        var build = await service.SubmitBuildAsync(_caller, project.Id);
        var log = await service.GetBuildLogAsync(_caller, build!.Id);
        var artifact = await service.GetBuildArtifactAsync(_caller, build.Id);

        Assert.Equal(BuildStatus.Succeeded, build.Status);
        Assert.NotNull(artifact);
        Assert.Contains("fake build", log);
        Assert.Equal(project.Id, buildRunner.LastProjectId);
    }

    [Fact]
    public async Task SubmitBuildReturnsPollableRunningBuildWhenQueued()
    {
        var queue = new CapturingBuildQueue();
        var service = CreateService(buildQueue: queue);
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.Queued", "1.0.0", "net10.0", null, null));

        var build = await service.SubmitBuildAsync(_caller, project.Id);
        var persisted = await service.GetBuildAsync(_caller, build!.Id);

        Assert.Equal(BuildStatus.Running, build.Status);
        Assert.Equal(BuildStatus.Running, persisted!.Status);
        Assert.Equal(build.Id, queue.WorkItem!.BuildId);
    }

    [Fact]
    public async Task SubmitBuildStillEnqueuesAfterRequestCancellationDuringQueueWrite()
    {
        using var cancellation = new CancellationTokenSource();
        var queue = new CancelingEnqueueBuildQueue(cancellation);
        var service = CreateService(buildQueue: queue);
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.EnqueueCancel", "1.0.0", "net10.0", null, null));

        var build = await service.SubmitBuildAsync(_caller, project.Id, cancellation.Token);
        var persisted = await service.GetBuildAsync(_caller, build!.Id);

        Assert.Equal(BuildStatus.Running, build.Status);
        Assert.Equal(BuildStatus.Running, persisted!.Status);
        Assert.Equal(build.Id, queue.WorkItem!.BuildId);
    }

    [Fact]
    public async Task FailedBuildHasDiagnosticsAndNoArtifact()
    {
        var service = CreateService(buildRunner: new FakeBuildRunner(BuildStatus.Failed));
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.Fail", "1.0.0", "net10.0", null, null));

        var build = await service.SubmitBuildAsync(_caller, project.Id);

        Assert.Equal(BuildStatus.Failed, build!.Status);
        Assert.Null(build.Artifact);
        Assert.Contains(build.Diagnostics, x => x.Severity is BuildDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task CanceledBuildPersistsTerminalFailedState()
    {
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(buildRunner: new CancelingBuildRunner(cancellation));
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.Cancel", "1.0.0", "net10.0", null, null));

        var build = await service.SubmitBuildAsync(_caller, project.Id, cancellation.Token);
        var persisted = await service.GetBuildAsync(_caller, build!.Id);

        Assert.Equal(BuildStatus.Failed, build.Status);
        Assert.Equal(BuildStatus.Failed, persisted!.Status);
        Assert.Contains(build.Diagnostics, x => x.Message.Contains("canceled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildCompletionPersistsWhenRequestIsCanceledAfterRunnerReturns()
    {
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(buildRunner: new PostRunCancelingBuildRunner(cancellation));
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.PostCancel", "1.0.0", "net10.0", null, null));

        var build = await service.SubmitBuildAsync(_caller, project.Id, cancellation.Token);
        var persisted = await service.GetBuildAsync(_caller, build!.Id);

        Assert.Equal(BuildStatus.Succeeded, build.Status);
        Assert.Equal(BuildStatus.Succeeded, persisted!.Status);
    }

    [Fact]
    public async Task PromoteStoresPromotionAndRuntimeStatusUsesNuplaneCatalog()
    {
        var nuplane = new FakeNuplaneAdmin();
        var featureManagement = new FakeFeatureManagement();
        var promotion = new FakePromotionService();
        var service = CreateService(new FakeBuildRunner(BuildStatus.Succeeded), promotion: promotion, nuplane: nuplane, featureManagement: featureManagement);
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("elsa-activity-module", "Elsa.Test.Promote", "1.0.0", "net10.0", null, null));
        var build = await service.SubmitBuildAsync(_caller, project.Id);

        var result = await service.PromoteBuildAsync(_caller, build!.Id);
        nuplane.Packages =
        [
            new(result!.PublishedPackage!.PackageId, result.PublishedPackage.Version, "local", "drop", result.PublishedPackage.Path, DateTimeOffset.UtcNow, "corr", "graph", "gen", default, [], [], true)
        ];
        featureManagement.Features =
        [
            new("FeatureA", "Feature A", null, [], "manifest", result.PublishedPackage.PackageId, result.PublishedPackage.Version, true, Json("{}"), false, false, null, null, null, [])
        ];

        var status = await service.GetRuntimeStatusAsync(_caller, project.Id);

        Assert.Equal(PromotionStatus.Accepted, result.Status);
        var package = Assert.Single(status!.Packages);
        Assert.Equal(ExtensionPackageRuntimeState.Loaded, package.State);
        Assert.Contains(package.Contributions, x => x.Id == "FeatureA");
    }

    [Fact]
    public async Task PromoteRecordsStateWhenRequestIsCanceledAfterLiveMutation()
    {
        using var cancellation = new CancellationTokenSource();
        var promotion = new FakePromotionService { CancelDuringPromote = cancellation };
        var service = CreateService(new FakeBuildRunner(BuildStatus.Succeeded), promotion: promotion);
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("elsa-activity-module", "Elsa.Test.PromoteCancel", "1.0.0", "net10.0", null, null));
        var build = await service.SubmitBuildAsync(_caller, project.Id);

        var result = await service.PromoteBuildAsync(_caller, build!.Id, cancellation.Token);
        var status = await service.GetRuntimeStatusAsync(_caller, project.Id);

        Assert.Equal(PromotionStatus.Accepted, result!.Status);
        Assert.Single(status!.Packages);
        Assert.Equal("1.0.0", status.ActiveVersion);
    }

    [Fact]
    public async Task RuntimeStatusDoesNotFailPackageWhenDegradedOutcomeNamesAnotherPackage()
    {
        var promotion = new FakePromotionService
        {
            ReconcileOutcome = new("completed", "corr", "other-package-failed", true, ["Other.Package"])
        };
        var service = CreateService(new FakeBuildRunner(BuildStatus.Succeeded), promotion: promotion);
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("elsa-activity-module", "Elsa.Test.Promote", "1.0.0", "net10.0", null, null));
        var build = await service.SubmitBuildAsync(_caller, project.Id);

        await service.PromoteBuildAsync(_caller, build!.Id);

        var status = await service.GetRuntimeStatusAsync(_caller, project.Id);

        var package = Assert.Single(status!.Packages);
        Assert.Equal(ExtensionPackageRuntimeState.PendingRestart, package.State);
        Assert.Null(package.Reason);
    }

    [Fact]
    public async Task RuntimeStatusExcludesPrunedFeedPackagesFromRollbackVersions()
    {
        var service = CreateService(new FakeBuildRunner(BuildStatus.Succeeded));
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("elsa-activity-module", "Elsa.Test.RollbackStatus", "1.0.0", "net10.0", null, null));
        var build = await service.SubmitBuildAsync(_caller, project.Id);
        var promotion = await service.PromoteBuildAsync(_caller, build!.Id);
        File.Delete(promotion!.PublishedPackage!.Path);

        var status = await service.GetRuntimeStatusAsync(_caller, project.Id);

        Assert.Empty(status!.AvailableRollbackVersions);
        Assert.Single(status.Packages);
    }

    [Fact]
    public async Task RollbackMissingVersionIsRejectedAndRetryReturnsOutcome()
    {
        var service = CreateService();
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.Rollback", "1.0.0", "net10.0", null, null));

        var rollback = await service.RollbackPackageAsync(_caller, project.Id, new("0.9.0"));
        var retry = await service.RetryReconciliationAsync(_caller, project.Id);

        Assert.Equal(PromotionStatus.Rejected, rollback!.Status);
        Assert.Equal("completed", retry!.ReconcileOutcome.Outcome);
    }

    [Fact]
    public async Task RollbackRejectsVersionWhenFeedPackageWasPruned()
    {
        var service = new ExtensionBuilderPromotionService(
            new FakeEnvironment(_directory),
            Options.Create(new ExtensionBuilderOptions()),
            new FakeNuplaneAdmin());
        var artifactPath = Path.Combine(_directory, "artifact.nupkg");
        await File.WriteAllTextAsync(artifactPath, "artifact still exists");
        var project = new ExtensionProject("project", "workspace", "generic-dotnet", ExtensionTemplateKind.GenericDotNet, "Elsa.Test.Rollback", "1.0.0", "net10.0", new("Test", null, [], Json("{}")), "rev", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var target = new PackagePromotionRecord(project.PackageId, "1.0.0", artifactPath, Path.Combine(_directory, "feed", "missing.nupkg"), DateTimeOffset.UtcNow, new("completed", "corr", null, false, []), true, false);

        var rollback = await service.RollbackAsync(project, target, [target]);

        Assert.Equal(PromotionStatus.Rejected, rollback.Status);
        Assert.Equal(PromotionRejectionReason.InvalidManifest, rollback.RejectionReason);
    }

    [Fact]
    public async Task RollbackRemovesSupersededPackageVersionsFromFeedBeforeReconcile()
    {
        var feed = Path.Combine(_directory, "packages");
        Directory.CreateDirectory(feed);
        var oldPackage = Path.Combine(feed, "Elsa.Test.Rollback.1.0.0.nupkg");
        var newPackage = Path.Combine(feed, "Elsa.Test.Rollback.2.0.0.nupkg");
        CreatePackage(oldPackage, "Elsa.Test.Rollback", "1.0.0", "Safe.Package", includeManifest: true);
        CreatePackage(newPackage, "Elsa.Test.Rollback", "2.0.0", "Safe.Package", includeManifest: true);
        var project = new ExtensionProject("project", "workspace", "generic-dotnet", ExtensionTemplateKind.GenericDotNet, "Elsa.Test.Rollback", "2.0.0", "net10.0", new("Test", null, [], Json("{}")), "rev", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var target = new PackagePromotionRecord(project.PackageId, "1.0.0", oldPackage, oldPackage, DateTimeOffset.UtcNow.AddMinutes(-1), new("completed", "corr-1", null, false, []), true, false);
        var superseded = new PackagePromotionRecord(project.PackageId, "2.0.0", newPackage, newPackage, DateTimeOffset.UtcNow, new("completed", "corr-2", null, false, []), true, false);
        var service = new ExtensionBuilderPromotionService(
            new FakeEnvironment(_directory),
            Options.Create(new ExtensionBuilderOptions()),
            new FakeNuplaneAdmin());

        var rollback = await service.RollbackAsync(project, target, [target, superseded]);

        Assert.Equal(PromotionStatus.Accepted, rollback.Status);
        Assert.True(File.Exists(oldPackage));
        Assert.False(File.Exists(newPackage));
    }

    [Fact]
    public async Task RollbackUsesPromotedPackageIdentityWhenProjectMetadataIsStale()
    {
        var feed = Path.Combine(_directory, "packages");
        Directory.CreateDirectory(feed);
        var oldPackage = Path.Combine(feed, "Elsa.Test.Actual.1.0.0.nupkg");
        var newPackage = Path.Combine(feed, "Elsa.Test.Actual.2.0.0.nupkg");
        CreatePackage(oldPackage, "Elsa.Test.Actual", "1.0.0", "Safe.Package", includeManifest: true);
        CreatePackage(newPackage, "Elsa.Test.Actual", "2.0.0", "Safe.Package", includeManifest: true);
        var project = new ExtensionProject("project", "workspace", "generic-dotnet", ExtensionTemplateKind.GenericDotNet, "Elsa.Test.Metadata", "2.0.0", "net10.0", new("Test", null, [], Json("{}")), "rev", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var target = new PackagePromotionRecord("Elsa.Test.Actual", "1.0.0", oldPackage, oldPackage, DateTimeOffset.UtcNow.AddMinutes(-1), new("completed", "corr-1", null, false, []), true, false);
        var superseded = new PackagePromotionRecord("Elsa.Test.Actual", "2.0.0", newPackage, newPackage, DateTimeOffset.UtcNow, new("completed", "corr-2", null, false, []), true, false);
        var service = new ExtensionBuilderPromotionService(
            new FakeEnvironment(_directory),
            Options.Create(new ExtensionBuilderOptions()),
            new FakeNuplaneAdmin());

        var rollback = await service.RollbackAsync(project, target, [target, superseded]);

        Assert.Equal(PromotionStatus.Accepted, rollback.Status);
        Assert.Equal("Elsa.Test.Actual", rollback.PublishedPackage!.PackageId);
        Assert.True(File.Exists(oldPackage));
        Assert.False(File.Exists(newPackage));
    }

    [Fact]
    public async Task RollbackUpdatesRuntimeStatusWithLatestReconcileOutcome()
    {
        var storage = CreateStorage();
        var promotion = new FakePromotionService
        {
            RollbackOutcome = new("failed", "rollback-corr", "rollback failed", true, ["Elsa.Test.RollbackStatus"])
        };
        var service = CreateService(storage: storage, promotion: promotion);
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.RollbackStatus", "2.0.0", "net10.0", null, null));
        var promotedV1At = DateTimeOffset.UtcNow.AddMinutes(-10);
        await storage.AddPromotionAsync(project.Id, new(project.PackageId, "1.0.0", "artifact-1.nupkg", "feed-1.nupkg", promotedV1At, new("completed", "promote-1", null, false, []), true, false, promotedV1At));
        await storage.AddPromotionAsync(project.Id, new(project.PackageId, "2.0.0", "artifact-2.nupkg", "feed-2.nupkg", DateTimeOffset.UtcNow, new("completed", "promote-2", null, false, []), true, false, DateTimeOffset.UtcNow));

        var rollback = await service.RollbackPackageAsync(_caller, project.Id, new("1.0.0"));
        var status = await service.GetRuntimeStatusAsync(_caller, project.Id);

        Assert.Equal(PromotionStatus.Accepted, rollback!.Status);
        Assert.Equal("1.0.0", status!.ActiveVersion);
        Assert.Equal("rollback-corr", status.LastReconcileOutcome!.CorrelationId);
        var targetPackage = Assert.Single(status.Packages, x => x.Version == "1.0.0");
        Assert.Equal(ExtensionPackageRuntimeState.FailedReconciliation, targetPackage.State);
        Assert.Equal("rollback failed", targetPackage.Reason);
    }

    [Fact]
    public async Task RollbackRecordsStateWhenRequestIsCanceledAfterLiveMutation()
    {
        using var cancellation = new CancellationTokenSource();
        var storage = CreateStorage();
        var promotion = new FakePromotionService { CancelDuringRollback = cancellation };
        var service = CreateService(storage: storage, promotion: promotion);
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.RollbackCancel", "2.0.0", "net10.0", null, null));
        var promotedV1At = DateTimeOffset.UtcNow.AddMinutes(-10);
        await storage.AddPromotionAsync(project.Id, new(project.PackageId, "1.0.0", "artifact-1.nupkg", "feed-1.nupkg", promotedV1At, new("completed", "promote-1", null, false, []), true, false, promotedV1At));
        await storage.AddPromotionAsync(project.Id, new(project.PackageId, "2.0.0", "artifact-2.nupkg", "feed-2.nupkg", DateTimeOffset.UtcNow, new("completed", "promote-2", null, false, []), true, false, DateTimeOffset.UtcNow));

        var rollback = await service.RollbackPackageAsync(_caller, project.Id, new("1.0.0"), cancellation.Token);
        var status = await service.GetRuntimeStatusAsync(_caller, project.Id);

        Assert.Equal(PromotionStatus.Accepted, rollback!.Status);
        Assert.Equal("1.0.0", status!.ActiveVersion);
        Assert.Equal("rollback", status.LastReconcileOutcome!.CorrelationId);
    }

    [Fact]
    public async Task RetryReconciliationRecordsStateWhenRequestIsCanceledAfterReconcile()
    {
        using var cancellation = new CancellationTokenSource();
        var storage = CreateStorage();
        var promotion = new FakePromotionService
        {
            RetryOutcome = new("completed", "retry-corr", null, false, []),
            CancelDuringRetry = cancellation
        };
        var service = CreateService(storage: storage, promotion: promotion);
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.RetryCancel", "1.0.0", "net10.0", null, null));
        var promotedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        await storage.AddPromotionAsync(project.Id, new(project.PackageId, project.PackageVersion, "artifact.nupkg", "feed.nupkg", promotedAt, new("failed", "old-corr", "old failure", true, [project.PackageId]), true, false, promotedAt));

        var retry = await service.RetryReconciliationAsync(_caller, project.Id, cancellation.Token);
        var status = await service.GetRuntimeStatusAsync(_caller, project.Id);

        Assert.Equal("retry-corr", retry!.ReconcileOutcome.CorrelationId);
        Assert.Equal("retry-corr", status!.LastReconcileOutcome!.CorrelationId);
        Assert.Equal(ExtensionPackageRuntimeState.PendingRestart, Assert.Single(status.Packages).State);
    }

    [Fact]
    public async Task PromotionValidationRejectsMalformedMissingManifestAndDeniedDependencyPackages()
    {
        var malformed = Path.Combine(_directory, "bad.nupkg");
        await File.WriteAllTextAsync(malformed, "not a zip");
        var service = new ExtensionBuilderPromotionService(
            new FakeEnvironment(_directory),
            Options.Create(new ExtensionBuilderOptions { DeniedDependencyPatterns = ["Dangerous.*"] }),
            new FakeNuplaneAdmin());
        var denied = Path.Combine(_directory, "denied.nupkg");
        CreatePackage(denied, "Elsa.Test.Denied", "1.0.0", "Dangerous.Package", includeManifest: true);
        var missingManifest = Path.Combine(_directory, "missing-manifest.nupkg");
        CreatePackage(missingManifest, "Elsa.Test.MissingManifest", "1.0.0", "Safe.Package");
        var emptyManifest = Path.Combine(_directory, "empty-manifest.nupkg");
        CreatePackage(emptyManifest, "Elsa.Test.EmptyManifest", "1.0.0", "Safe.Package", includeManifest: true, manifestJson: "{}");
        var mismatchedManifest = Path.Combine(_directory, "mismatched-manifest.nupkg");
        CreatePackage(mismatchedManifest, "Elsa.Test.Mismatch", "1.0.0", "Safe.Package", includeManifest: true, manifestPackageId: "Elsa.Test.Other");
        var nonStringManifest = Path.Combine(_directory, "non-string-manifest.nupkg");
        CreatePackage(nonStringManifest, "Elsa.Test.NonString", "1.0.0", "Safe.Package", includeManifest: true, manifestJson: """
            {
              "package": {
                "id": 123,
                "version": "1.0.0"
              },
              "features": []
            }
            """);

        var malformedResult = await service.ValidatePackageAsync(malformed);
        var deniedResult = await service.ValidatePackageAsync(denied);
        var missingManifestResult = await service.ValidatePackageAsync(missingManifest);
        var emptyManifestResult = await service.ValidatePackageAsync(emptyManifest);
        var mismatchedManifestResult = await service.ValidatePackageAsync(mismatchedManifest);
        var nonStringManifestResult = await service.ValidatePackageAsync(nonStringManifest);

        Assert.Equal(PromotionRejectionReason.MalformedPackage, malformedResult.RejectionReason);
        Assert.Equal(PromotionRejectionReason.DependencyPolicy, deniedResult.RejectionReason);
        Assert.Equal(PromotionRejectionReason.InvalidManifest, missingManifestResult.RejectionReason);
        Assert.Equal(PromotionRejectionReason.InvalidManifest, emptyManifestResult.RejectionReason);
        Assert.Equal(PromotionRejectionReason.InvalidManifest, mismatchedManifestResult.RejectionReason);
        Assert.Equal(PromotionRejectionReason.InvalidManifest, nonStringManifestResult.RejectionReason);
    }

    [Fact]
    public async Task PromoteRejectsArtifactMetadataThatDoesNotMatchPackageIdentity()
    {
        var packagePath = Path.Combine(_directory, "valid.nupkg");
        CreatePackage(packagePath, "Elsa.Test.Real", "1.0.0", "Safe.Package", includeManifest: true);
        var build = new BuildResult(
            "build",
            "project",
            "workspace",
            "rev",
            BuildStatus.Succeeded,
            [],
            new("artifact", "build", "Elsa.Test.Stale", "1.0.0", Path.GetFileName(packagePath), packagePath, new FileInfo(packagePath).Length, DateTimeOffset.UtcNow),
            Path.Combine(_directory, "build.log"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        var service = new ExtensionBuilderPromotionService(
            new FakeEnvironment(_directory),
            Options.Create(new ExtensionBuilderOptions()),
            new FakeNuplaneAdmin());

        var result = await service.PromoteAsync(build);

        Assert.Equal(PromotionStatus.Rejected, result.Status);
        Assert.Equal(PromotionRejectionReason.InvalidManifest, result.RejectionReason);
        Assert.False(Directory.Exists(Path.Combine(_directory, "packages")));
    }

    [Fact]
    public async Task PromoteRejectsExistingFeedPackageWithSameIdentityAndDifferentFileName()
    {
        var feed = Path.Combine(_directory, "packages");
        Directory.CreateDirectory(feed);
        CreatePackage(Path.Combine(feed, "already-published.nupkg"), "Elsa.Test.Duplicate", "1.0.0", "Safe.Package", includeManifest: true);
        var packagePath = Path.Combine(_directory, "new-name.nupkg");
        CreatePackage(packagePath, "Elsa.Test.Duplicate", "1.0.0", "Safe.Package", includeManifest: true);
        var build = new BuildResult(
            "build",
            "project",
            "workspace",
            "rev",
            BuildStatus.Succeeded,
            [],
            new("artifact", "build", "Elsa.Test.Duplicate", "1.0.0", Path.GetFileName(packagePath), packagePath, new FileInfo(packagePath).Length, DateTimeOffset.UtcNow),
            Path.Combine(_directory, "build.log"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        var service = new ExtensionBuilderPromotionService(
            new FakeEnvironment(_directory),
            Options.Create(new ExtensionBuilderOptions()),
            new FakeNuplaneAdmin());

        var result = await service.PromoteAsync(build);

        Assert.Equal(PromotionStatus.Rejected, result.Status);
        Assert.Equal(PromotionRejectionReason.Duplicate, result.RejectionReason);
        Assert.False(File.Exists(Path.Combine(feed, "new-name.nupkg")));
    }

    [Fact]
    public void PublicJsonContractsSerializeEnumsAsStrings()
    {
        var json = JsonSerializer.Serialize(
            new BuildDiagnostic(BuildDiagnosticSeverity.Error, "compile failed", null, null, null, "CS1001"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"severity\":\"Error\"", json);
    }

    [Fact]
    public async Task SubmitBuildReturnsFailedBuildWhenRunnerCannotStart()
    {
        var service = CreateService(buildRunner: new ThrowingBuildRunner());
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.Throw", "1.0.0", "net10.0", null, null));

        var build = await service.SubmitBuildAsync(_caller, project.Id);

        Assert.Equal(BuildStatus.Failed, build!.Status);
        Assert.Contains(build.Diagnostics, x => x.Severity is BuildDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task SubmitBuildReturnsFailedBuildWhenRunnerFailsUnexpectedly()
    {
        var service = CreateService(buildRunner: new UnexpectedThrowingBuildRunner());
        var workspace = await service.CreateWorkspaceAsync(_caller, new("Workspace"));
        var project = await service.CreateProjectAsync(_caller, workspace.Id, new("generic-dotnet", "Elsa.Test.Unexpected", "1.0.0", "net10.0", null, null));

        var build = await service.SubmitBuildAsync(_caller, project.Id);
        var persisted = await service.GetBuildAsync(_caller, build!.Id);
        var log = await service.GetBuildLogAsync(_caller, build.Id);

        Assert.Equal(BuildStatus.Failed, build.Status);
        Assert.Equal(BuildStatus.Failed, persisted!.Status);
        Assert.Contains(build.Diagnostics, x => x.Severity is BuildDiagnosticSeverity.Error);
        Assert.Contains("disk unavailable", log);
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        return ValueTask.CompletedTask;
    }

    private ExtensionBuilderService CreateService(
        IExtensionBuilderBuildRunner? buildRunner = null,
        IExtensionBuilderBuildQueue? buildQueue = null,
        IExtensionBuilderPromotionService? promotion = null,
        FakeNuplaneAdmin? nuplane = null,
        IFeatureManagementService? featureManagement = null,
        ExtensionBuilderStorage? storage = null,
        ExtensionBuilderOptions? options = null)
    {
        nuplane ??= new FakeNuplaneAdmin();
        storage ??= CreateStorage(options);
        buildRunner ??= new FakeBuildRunner(BuildStatus.Succeeded);
        buildQueue ??= new ImmediateBuildQueue(new ExtensionBuilderBuildExecutor(storage, buildRunner, NullLogger<ExtensionBuilderBuildExecutor>.Instance));
        return new(
            storage,
            new ExtensionBuilderTemplateCatalog(),
            buildQueue,
            promotion ?? new FakePromotionService(),
            nuplane,
            featureManagement ?? new FakeFeatureManagement(),
            NullLogger<ExtensionBuilderService>.Instance);
    }

    private ExtensionBuilderStorage CreateStorage(ExtensionBuilderOptions? options = null) =>
        new(
            new FakeEnvironment(_directory),
            Options.Create(options ?? new ExtensionBuilderOptions { StoragePath = Path.Combine(_directory, "state") }));

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static async Task<string> GitAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(error);

        return output.Trim();
    }

    private static void CreatePackage(
        string path,
        string packageId,
        string version,
        string dependencyId,
        bool includeManifest = false,
        string? manifestPackageId = null,
        string? manifestPackageVersion = null,
        string? manifestJson = null)
    {
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var nuspec = archive.CreateEntry($"{packageId}.nuspec");
        using (var writer = new StreamWriter(nuspec.Open()))
        {
            writer.Write($"""
                <?xml version="1.0" encoding="utf-8"?>
                <package>
                  <metadata>
                    <id>{packageId}</id>
                    <version>{version}</version>
                    <dependencies>
                      <dependency id="{dependencyId}" version="1.0.0" />
                    </dependencies>
                  </metadata>
                </package>
                """);
        }

        if (includeManifest)
        {
            var manifest = archive.CreateEntry("elsa-package.json");
            using var writer = new StreamWriter(manifest.Open());
            writer.Write(manifestJson ?? $$"""
                {
                  "package": {
                    "id": "{{manifestPackageId ?? packageId}}",
                    "version": "{{manifestPackageVersion ?? version}}"
                  },
                  "features": []
                }
                """);
        }
    }

    private sealed class CancelingBuildRunner(CancellationTokenSource cancellation) : IExtensionBuilderBuildRunner
    {
        public Task<BuildResult> RunAsync(ExtensionProject project, SourceSnapshot snapshot, string buildId, string logPath, string artifactsPath, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private sealed class PostRunCancelingBuildRunner(CancellationTokenSource cancellation) : IExtensionBuilderBuildRunner
    {
        public async Task<BuildResult> RunAsync(ExtensionProject project, SourceSnapshot snapshot, string buildId, string logPath, string artifactsPath, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(artifactsPath);
            var artifactPath = Path.Combine(artifactsPath, $"{project.PackageId}.{project.PackageVersion}.nupkg");
            await File.WriteAllTextAsync(logPath, "fake build", CancellationToken.None);
            await File.WriteAllTextAsync(artifactPath, "fake package", CancellationToken.None);
            cancellation.Cancel();
            return new(buildId, project.Id, project.WorkspaceId, snapshot.Id, BuildStatus.Succeeded, [],
                new($"artifact_{Guid.NewGuid():N}", buildId, project.PackageId, project.PackageVersion, Path.GetFileName(artifactPath), artifactPath, new FileInfo(artifactPath).Length, DateTimeOffset.UtcNow),
                logPath, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        }
    }

    private sealed class ThrowingBuildRunner : IExtensionBuilderBuildRunner
    {
        public Task<BuildResult> RunAsync(ExtensionProject project, SourceSnapshot snapshot, string buildId, string logPath, string artifactsPath, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("dotnet executable was not found.");
    }

    private sealed class UnexpectedThrowingBuildRunner : IExtensionBuilderBuildRunner
    {
        public Task<BuildResult> RunAsync(ExtensionProject project, SourceSnapshot snapshot, string buildId, string logPath, string artifactsPath, CancellationToken cancellationToken = default) =>
            throw new IOException("disk unavailable");
    }

    private sealed class FakeBuildRunner(BuildStatus status) : IExtensionBuilderBuildRunner
    {
        public string? LastProjectId { get; private set; }

        public async Task<BuildResult> RunAsync(ExtensionProject project, SourceSnapshot snapshot, string buildId, string logPath, string artifactsPath, CancellationToken cancellationToken = default)
        {
            LastProjectId = project.Id;
            await File.WriteAllTextAsync(logPath, "fake build", cancellationToken);
            if (status is BuildStatus.Failed)
            {
                return new(buildId, project.Id, project.WorkspaceId, snapshot.Id, BuildStatus.Failed,
                    [new(BuildDiagnosticSeverity.Error, "compile failed", "Class1.cs", 1, 1, "CS1001")],
                    null, logPath, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            }

            Directory.CreateDirectory(artifactsPath);
            var artifactPath = Path.Combine(artifactsPath, $"{project.PackageId}.{project.PackageVersion}.nupkg");
            await File.WriteAllTextAsync(artifactPath, "fake package", cancellationToken);
            return new(buildId, project.Id, project.WorkspaceId, snapshot.Id, BuildStatus.Succeeded, [],
                new($"artifact_{Guid.NewGuid():N}", buildId, project.PackageId, project.PackageVersion, Path.GetFileName(artifactPath), artifactPath, new FileInfo(artifactPath).Length, DateTimeOffset.UtcNow),
                logPath, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        }
    }

    private sealed class FakePromotionService : IExtensionBuilderPromotionService
    {
        public ExtensionBuilderReconcileOutcome ReconcileOutcome { get; set; } = new("completed", "corr", null, false, []);
        public ExtensionBuilderReconcileOutcome RollbackOutcome { get; set; } = new("completed", "rollback", null, false, []);
        public ExtensionBuilderReconcileOutcome RetryOutcome { get; set; } = new("completed", "corr", null, false, []);
        public CancellationTokenSource? CancelDuringPromote { get; set; }
        public CancellationTokenSource? CancelDuringRollback { get; set; }
        public CancellationTokenSource? CancelDuringRetry { get; set; }

        public Task<PackagePromotionResult> PromoteAsync(BuildResult build, CancellationToken cancellationToken = default)
        {
            var artifact = build.Artifact!;
            CancelDuringPromote?.Cancel();
            return Task.FromResult(new PackagePromotionResult(
                PromotionStatus.Accepted,
                null,
                new(artifact.PackageId, artifact.Version, "local", artifact.Path),
                ReconcileOutcome,
                true,
                false));
        }

        public Task<PackagePromotionResult> RollbackAsync(ExtensionProject project, PackagePromotionRecord target, IReadOnlyList<PackagePromotionRecord> promotions, CancellationToken cancellationToken = default)
        {
            CancelDuringRollback?.Cancel();
            return Task.FromResult(new PackagePromotionResult(PromotionStatus.Accepted, null, new(project.PackageId, target.Version, "local", target.FeedPath), RollbackOutcome, true, false));
        }

        public Task<ExtensionBuilderReconcileOutcome> RetryReconciliationAsync(CancellationToken cancellationToken = default)
        {
            CancelDuringRetry?.Cancel();
            return Task.FromResult(RetryOutcome);
        }
    }

    private sealed class ImmediateBuildQueue(IExtensionBuilderBuildExecutor executor) : IExtensionBuilderBuildQueue
    {
        public Task EnqueueAsync(ExtensionBuilderBuildWorkItem workItem, CancellationToken cancellationToken = default) =>
            executor.ExecuteAsync(workItem, cancellationToken);
    }

    private sealed class CapturingBuildQueue : IExtensionBuilderBuildQueue
    {
        public ExtensionBuilderBuildWorkItem? WorkItem { get; private set; }

        public Task EnqueueAsync(ExtensionBuilderBuildWorkItem workItem, CancellationToken cancellationToken = default)
        {
            WorkItem = workItem;
            return Task.CompletedTask;
        }
    }

    private sealed class CancelingEnqueueBuildQueue(CancellationTokenSource cancellation) : IExtensionBuilderBuildQueue
    {
        public ExtensionBuilderBuildWorkItem? WorkItem { get; private set; }

        public Task EnqueueAsync(ExtensionBuilderBuildWorkItem workItem, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            WorkItem = workItem;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNuplaneAdmin : INuplaneAdminOperations
    {
        public IReadOnlyList<ActivePackage> Packages { get; set; } = [];

        public Task<ActivePackagesSnapshot> GetPackagesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ActivePackagesSnapshot(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Packages, "corr"));

        public Task<OperationalStateSnapshot> GetStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OperationalStateSnapshot(DateTimeOffset.UtcNow, new("corr", DateTimeOffset.UtcNow, false, false, []), default, [], "corr"));

        public Task<ManualReconcileOutcome> TriggerReconcileAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ManualReconcileOutcome(
                ManualReconcileOutcomeCode.Completed,
                "corr",
                new ReconciliationRunResult(false, new PackageChangeSet([], [], [], "corr", DateTimeOffset.UtcNow), [], false),
                null));
    }

    private sealed class FakeFeatureManagement : IFeatureManagementService
    {
        public IReadOnlyList<FeatureCatalogItem> Features { get; set; } = [];

        public Task<FeatureCatalogResponse> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new FeatureCatalogResponse("rev", Features));

        public Task<FeatureApplyResult> ApplyAsync(FeatureApplyRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
