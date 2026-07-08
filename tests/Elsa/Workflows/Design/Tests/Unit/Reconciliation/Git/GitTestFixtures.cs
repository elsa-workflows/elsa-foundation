using System.Text.Json;
using Elsa.Git;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation.Git;

/// <summary>
/// A throwaway on-disk git repository for the GitOps integration tests. Uses the real
/// <see cref="GitClient"/> (git must be on PATH). Deletes its working tree on dispose.
/// </summary>
internal sealed class GitTestRepo : IDisposable
{
    public string Path { get; }
    private readonly IGitClient _git;

    private GitTestRepo(string path, IGitClient git)
    {
        Path = path;
        _git = git;
    }

    public static GitTestRepo InitBare(IGitClient git)
    {
        var path = NewTempDir();
        git.RunAsync(path, CancellationToken.None, "init", "--bare", "-b", "main").GetAwaiter().GetResult();
        return new GitTestRepo(path, git);
    }

    public static GitTestRepo InitWorking(IGitClient git)
    {
        var path = NewTempDir();
        git.RunAsync(path, CancellationToken.None, "init", "-b", "main").GetAwaiter().GetResult();
        git.RunAsync(path, CancellationToken.None, "config", "user.email", "test@elsa.local").GetAwaiter().GetResult();
        git.RunAsync(path, CancellationToken.None, "config", "user.name", "Test").GetAwaiter().GetResult();
        return new GitTestRepo(path, git);
    }

    public void WriteFile(string relativePath, string content)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public void CommitAll(string message)
    {
        _git.RunAsync(Path, CancellationToken.None, "add", "-A").GetAwaiter().GetResult();
        _git.RunAsync(Path, CancellationToken.None,
            "-c", "user.email=test@elsa.local", "-c", "user.name=Test", "commit", "-m", message).GetAwaiter().GetResult();
    }

    public string RunGit(params string[] args) => _git.RunOrDefault(Path, args);

    private static string NewTempDir()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "elsa-gitops-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}

/// <summary>
/// Base for the GitOps integration tests: owns a real <see cref="IGitClient"/> and a list of temp
/// directories deleted on dispose, so each test class stays free of repeated setup/teardown.
/// </summary>
public abstract class GitIntegrationTest : IDisposable
{
    protected readonly IGitClient _git = GitTestSupport.NewGitClient();
    protected readonly List<string> _tempPaths = new();

    public void Dispose()
    {
        foreach (var path in _tempPaths)
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* best-effort temp cleanup */ }
    }
}

internal static class GitTestSupport
{
    public static IGitClient NewGitClient() => new GitClient("git", NullLogger<GitClient>.Instance);

    public static IOptions<GitReconciliationOptions> Options(GitReconciliationOptions options) =>
        Microsoft.Extensions.Options.Options.Create(options);

    public static string NewCachePath() =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "elsa-gitops-tests", "cache-" + Guid.NewGuid().ToString("N"));

    /// <summary>The canonical (compact) empty-state JSON written into version fixtures.</summary>
    public static string EmptyStateJson => JsonSerializer.Serialize(WorkflowDefinitionState.Empty);

    /// <summary>
    /// Creates a bare remote whose <c>main</c> branch has one initial commit (so a Writer clone can
    /// check out and integrate). Returns the bare repo path; adds every temp dir to <paramref name="tempPaths"/>.
    /// </summary>
    public static string CreateRemoteWithMain(IGitClient git, List<string> tempPaths)
    {
        var bare = GitTestRepo.InitBare(git);
        tempPaths.Add(bare.Path);

        var seed = GitTestRepo.InitWorking(git);
        tempPaths.Add(seed.Path);
        seed.WriteFile("README.md", "workflows");
        seed.CommitAll("init");
        seed.RunGit("remote", "add", "origin", bare.Path);
        git.RunAsync(seed.Path, CancellationToken.None, "push", "origin", "main").GetAwaiter().GetResult();

        return bare.Path;
    }
}

/// <summary>In-memory <see cref="IWorkflowDefinitionStore"/> for exporter tests.</summary>
internal sealed class InMemoryDefinitionStore : IWorkflowDefinitionStore
{
    private readonly List<WorkflowDefinition> _items = new();
    public InMemoryDefinitionStore With(WorkflowDefinition item) { _items.Add(item); return this; }
    public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<WorkflowDefinition>>(_items);
    public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult(_items.FirstOrDefault(x => x.Id == id));
    public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult(_items.First(x => x.Id == id));
}

/// <summary>In-memory <see cref="IWorkflowDefinitionVersionStore"/> for exporter tests (State pre-hydrated).</summary>
internal sealed class InMemoryVersionStore : IWorkflowDefinitionVersionStore
{
    private readonly List<WorkflowDefinitionVersion> _items = new();
    public InMemoryVersionStore With(WorkflowDefinitionVersion item) { _items.Add(item); return this; }
    public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>(_items.Where(x => x.DefinitionId == definitionId).ToList());
    public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default)
        => Task.FromResult(_items.Any(x => x.DefinitionId == definitionId && x.SemVerSortKey == semVerSortKey));
    public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default)
        => Task.FromResult(_items.Where(x => x.DefinitionId == definitionId).OrderByDescending(x => x.SemVerSortKey, StringComparer.Ordinal).FirstOrDefault());

    private const string Unused = "Not exercised by git exporter tests.";
    public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
    public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
    public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Unused);
}

/// <summary>
/// Test <see cref="IPayloadSerializer"/>: real JSON out (so exported files and hashes are honest), and
/// a fixed <see cref="WorkflowDefinitionState.Empty"/> back (the git tests assert on identity/metadata,
/// not authored content, so state fidelity is unnecessary and STJ polymorphism is avoided).
/// </summary>
internal sealed class FakePayloadSerializer : IPayloadSerializer
{
    public string Serialize(object payload) => JsonSerializer.Serialize(payload);
    public T Deserialize<T>(string serializedData) => (T)(object)WorkflowDefinitionState.Empty;
    public JsonElement SerializeToElement(object payload) => throw new NotSupportedException();
    public object Deserialize(string serializedData) => throw new NotSupportedException();
    public object Deserialize(string serializedData, Type type) => throw new NotSupportedException();
    public object Deserialize(JsonElement serializedData) => throw new NotSupportedException();
    public T Deserialize<T>(JsonElement serializedData) => throw new NotSupportedException();
    public JsonSerializerOptions GetOptions() => throw new NotSupportedException();
}
