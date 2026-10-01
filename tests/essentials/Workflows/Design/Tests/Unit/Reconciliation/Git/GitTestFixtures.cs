using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Git;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Testing;
using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Elsa.Workflows.Design.Reconciliation.Git.Services;
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

/// <summary>
/// Base for the export tests: one bare remote and one catalog, shared the way the nodes of one authoring deployment share
/// them, and a factory for Writer nodes, each with a clone of its own.
/// </summary>
public abstract class GitExportTest : GitIntegrationTest
{
    protected readonly string _remote;
    protected readonly InMemoryDefinitionStore _definitions = new();
    protected readonly InMemoryVersionStore _versions = new();

    protected GitExportTest() => _remote = GitTestSupport.CreateRemoteWithMain(_git, _tempPaths);

    /// <summary>Adds a definition and its versions to the shared catalog.</summary>
    protected void Publish(string definitionId, string name, params string[] versions)
    {
        _definitions.With(new WorkflowDefinition { Id = definitionId, Name = name });
        foreach (var version in versions)
            _versions.With(new WorkflowDefinitionVersion(definitionId, version) { State = WorkflowDefinitionState.Empty });
    }

    /// <summary>
    /// A Writer node of the shared catalog and remote, with a clone of its own. A non-empty <paramref name="exportBranch"/>
    /// makes it export to that branch rather than the one it tracks.
    /// </summary>
    protected GitWriterNode Writer(GitPushMode pushMode = GitPushMode.Manual, string exportBranch = "")
    {
        var cachePath = GitTestSupport.NewCachePath();
        _tempPaths.Add(cachePath);
        var options = GitTestSupport.Options(new GitReconciliationOptions
        {
            RemoteUrl = _remote, Branch = "main", WorkflowsPath = "workflows",
            LocalCachePath = cachePath, Role = GitReconciliationRole.Writer,
            Export = new GitExportOptions { PushMode = pushMode, Branch = exportBranch, Tag = true },
        });
        var git = new InterceptingGitClient(_git);
        var workspaceLog = new RecordingLogger<GitWorkspace>();
        var workspace = new GitWorkspace(git, options, workspaceLog);
        var exportLog = new RecordingLogger<GitWorkflowExporter>();
        var serializer = new FakePayloadSerializer();
        return new GitWriterNode(
            cachePath,
            git,
            new GitWorkflowExporter(workspace, git, serializer, _definitions, _versions, options, exportLog),
            new GitWorkflowReconciliationSource(workspace, git, serializer, options, NullLogger<GitWorkflowReconciliationSource>.Instance),
            workspaceLog,
            exportLog);
    }

    /// <summary>
    /// Two nodes exporting the same catalog on the same parent make the same commit, hash included, when they do it within
    /// one second (a commit records whole seconds), and a push of an identical commit is no race at all. A test that needs
    /// two writers to differ lets the clock pass a second boundary between their commits.
    /// </summary>
    protected static Task LetCommitClockAdvanceAsync() => Task.Delay(TimeSpan.FromMilliseconds(1100));

    /// <summary>
    /// Another writer, or a person, pushes a commit to <paramref name="branch"/> of the remote (created from main when it does
    /// not exist yet). It rewrites <c>README.md</c>, so a clone with an uncommitted edit of it cannot move onto the commit.
    /// </summary>
    protected async Task AdvanceRemoteAsync(string branch)
    {
        var work = GitTestSupport.NewCachePath();
        _tempPaths.Add(work);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(work)!);
        await _git.RunAsync(System.IO.Path.GetDirectoryName(work)!, CancellationToken.None, "clone", _remote, work);
        var start = _git.RunOrDefault(work, "ls-remote", "--heads", "origin", branch).Length > 0 ? $"origin/{branch}" : "origin/main";
        await _git.RunAsync(work, CancellationToken.None, "checkout", "-B", branch, start);
        await File.WriteAllTextAsync(System.IO.Path.Join(work, "README.md"), Guid.NewGuid().ToString("N"));
        await _git.RunAsync(work, CancellationToken.None, "add", "--", "README.md");
        await _git.RunAsync(work, CancellationToken.None,
            "-c", "user.name=Other writer", "-c", "user.email=other@example.com", "commit", "-m", $"Other writer on {branch}");
        await _git.RunAsync(work, CancellationToken.None, "push", "origin", $"HEAD:{branch}");
    }

    protected IReadOnlyList<string> Subjects(string repository, string revision = "HEAD") =>
        _git.RunOrDefault(repository, "log", "--format=%s", revision).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    protected IReadOnlyList<string> RemoteSubjects() => Subjects(_remote, "main");

    protected string Head(string repository, string revision = "HEAD") => _git.RunOrDefault(repository, "rev-parse", revision);

    protected bool IsCommitted(string repository, string path) =>
        _git.RunOrDefault(repository, "ls-tree", "--name-only", "HEAD", "--", path) == path;
}

/// <summary>One Writer node: its clone, the git client it runs (which a test can intercept), its export and its import.</summary>
public sealed record GitWriterNode(
    string CachePath,
    InterceptingGitClient Git,
    GitWorkflowExporter Exporter,
    GitWorkflowReconciliationSource Source,
    RecordingLogger<GitWorkspace> WorkspaceLog,
    RecordingLogger<GitWorkflowExporter> ExportLog);

/// <summary>
/// Runs every git command through the real client, except that a test can act just before a chosen run of a command: fail
/// it, as a process that stops between two steps would, or let another node act in that moment. Commands are counted by
/// name, skipping the leading <c>-c key=value</c> pairs.
/// </summary>
public sealed class InterceptingGitClient(IGitClient inner) : IGitClient
{
    private readonly List<(string Command, int Occurrence, Func<Task> Action)> _interceptions = [];
    private readonly Dictionary<string, int> _runs = new(StringComparer.Ordinal);

    /// <summary>Fails the given run of <paramref name="command"/> before it starts, the way a failed git command fails.</summary>
    public void FailAt(string command, int occurrence = 1) =>
        Before(command, occurrence, () => throw new InvalidOperationException($"Simulated stop before git {command} (run {occurrence})."));

    public void Before(string command, int occurrence, Func<Task> action) => _interceptions.Add((command, occurrence, action));

    public async Task<string> RunAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
    {
        var command = CommandOf(arguments);
        var run = _runs[command] = _runs.GetValueOrDefault(command) + 1;
        foreach (var interception in _interceptions.Where(i => i.Command == command && i.Occurrence == run))
            await interception.Action();

        return await inner.RunAsync(workingDirectory, cancellationToken, arguments);
    }

    public string RunOrDefault(string workingDirectory, params string[] arguments) => inner.RunOrDefault(workingDirectory, arguments);

    public bool IsGitRepository(string repositoryPath) => inner.IsGitRepository(repositoryPath);

    private static string CommandOf(string[] arguments)
    {
        var index = 0;
        while (index < arguments.Length && arguments[index] == "-c")
            index += 2;
        return index < arguments.Length ? arguments[index] : "";
    }
}

internal static class GitTestSupport
{
    /// <summary>
    /// Isolates every git process this test assembly starts from the developer's global and system git config. A global
    /// <c>commit.gpgsign = true</c> would otherwise make each test commit ask a GPG agent to sign, which launches pinentry
    /// and fails whenever the agent is locked. No test relies on that config: the harness and the exporter set their
    /// commit identity explicitly. Git child processes inherit the variables, because <see cref="GitClient"/> builds its
    /// start info from the current environment.
    /// </summary>
    [ModuleInitializer]
    internal static void IsolateFromDeveloperGitConfig()
    {
        // Git for Windows maps /dev/null to nul, so the same value isolates the global config on every platform.
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", "/dev/null");
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
    }

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
public sealed class InMemoryDefinitionStore : IWorkflowDefinitionStore
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
public sealed class InMemoryVersionStore : IWorkflowDefinitionVersionStore
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
