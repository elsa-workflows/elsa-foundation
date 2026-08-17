using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Exceptions;
using Elsa.Workflows.Runtime.Reconciliation.Core.Options;
using Elsa.Workflows.Runtime.Reconciliation.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// Framework §2.23.2 branch coverage for <see cref="JsonWorkflowArtifactReconciliationSource"/>: every one of the
/// three configured shapes (single file, explicitly ordered files, scanned folder), the ordering guarantees each
/// one promises, and the deliberate missing-folder / empty-folder asymmetry.
/// </summary>
/// <remarks>
/// The asymmetry is the behaviour most worth pinning: a folder that does not exist aborts the pass, while a folder
/// that exists and holds nothing is a no-op. Collapsing the two would make an unmounted volume indistinguishable
/// from a healthy empty mount, and the runtime would keep serving stale activations without saying so.
/// </remarks>
public sealed class JsonWorkflowArtifactReconciliationSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "elsa-artifact-source-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [Fact]
    public void Source_identifies_itself_from_its_options()
    {
        var reader = new RecordingReader();
        var source = CreateSource(reader, options => options.FilePath = "closure.json");

        Assert.Equal("mounted-artifacts", source.SourceId);
        Assert.Equal("Json", source.SourceKind);
        Assert.Equal(JsonWorkflowArtifactReconciliationSource.Kind, source.SourceKind);
    }

    [Fact]
    public async Task Single_file_path_reads_exactly_that_file()
    {
        var reader = new RecordingReader();
        var source = CreateSource(reader, options => options.FilePath = "/mnt/artifacts/closure.json");

        var files = await ReadAllAsync(source);

        Assert.Equal(["/mnt/artifacts/closure.json"], reader.Paths);
        var file = Assert.Single(files);
        Assert.Equal("/mnt/artifacts/closure.json", file.Origin);
    }

    [Fact]
    public async Task Explicit_files_are_read_in_ascending_order()
    {
        var reader = new RecordingReader();
        var source = CreateSource(reader, options => options.Files =
        [
            new JsonWorkflowArtifactReconciliationFileOption(3, "third.json"),
            new JsonWorkflowArtifactReconciliationFileOption(1, "first.json"),
            new JsonWorkflowArtifactReconciliationFileOption(2, "second.json"),
        ]);

        await ReadAllAsync(source);

        Assert.Equal(["first.json", "second.json", "third.json"], reader.Paths);
    }

    [Fact]
    public async Task Explicit_files_with_equal_order_fall_back_to_ordinal_file_name()
    {
        // Ties must still be deterministic: two files an operator gave the same Order must not swap between
        // passes, because the second pass would then activate them in a different sequence.
        var reader = new RecordingReader();
        var source = CreateSource(reader, options => options.Files =
        [
            new JsonWorkflowArtifactReconciliationFileOption(1, "b.json"),
            new JsonWorkflowArtifactReconciliationFileOption(1, "a.json"),
            new JsonWorkflowArtifactReconciliationFileOption(1, "B.json"),
        ]);

        await ReadAllAsync(source);

        // Ordinal, not culture-aware: uppercase sorts before lowercase.
        Assert.Equal(["B.json", "a.json", "b.json"], reader.Paths);
    }

    [Fact]
    public async Task Explicit_files_win_over_a_single_file_path()
    {
        // The feature refuses this combination at registration, but the source's own branch order is what makes
        // that gate the single point of enforcement rather than one of two disagreeing rules.
        var reader = new RecordingReader();
        var source = CreateSource(reader, options =>
        {
            options.FilePath = "ignored.json";
            options.Files = [new JsonWorkflowArtifactReconciliationFileOption(1, "explicit.json")];
        });

        await ReadAllAsync(source);

        Assert.Equal(["explicit.json"], reader.Paths);
    }

    [Fact]
    public async Task No_configured_shape_reads_nothing()
    {
        var reader = new RecordingReader();
        var source = CreateSource(reader, _ => { });

        var files = await ReadAllAsync(source);

        Assert.Empty(files);
        Assert.Empty(reader.Paths);
    }

    [Fact]
    public async Task Folder_scan_reads_top_level_json_in_ordinal_file_name_order()
    {
        var folder = CreateFolder("mount");
        WriteFile(folder, "b-second.json");
        WriteFile(folder, "a-first.json");
        WriteFile(folder, "C-uppercase.json");

        var reader = new RecordingReader();
        var source = CreateSource(reader, options => options.FolderPath = folder);

        await ReadAllAsync(source);

        Assert.Equal(
            ["C-uppercase.json", "a-first.json", "b-second.json"],
            reader.Paths.Select(path => Path.GetFileName(path)!).ToArray());
    }

    [Fact]
    public async Task Folder_scan_ignores_non_json_files_and_sub_directories()
    {
        // Non-recursive on purpose: a Kubernetes ConfigMap mount exposes its content twice, once through the
        // `..data` symlink tree, so a recursive scan would import every closure a second time.
        var folder = CreateFolder("mount");
        WriteFile(folder, "closure.json");
        WriteFile(folder, "notes.txt");
        WriteFile(folder, "closure.json.bak");
        var nested = Directory.CreateDirectory(Path.Combine(folder, "nested"));
        WriteFile(nested.FullName, "nested.json");

        var reader = new RecordingReader();
        var source = CreateSource(reader, options => options.FolderPath = folder);

        await ReadAllAsync(source);

        Assert.Equal(["closure.json"], reader.Paths.Select(path => Path.GetFileName(path)!).ToArray());
    }

    [Fact]
    public async Task Missing_folder_aborts_the_pass()
    {
        var missing = Path.Combine(_root, "not-mounted");
        var reader = new RecordingReader();
        var source = CreateSource(reader, options => options.FolderPath = missing);

        var exception = await Assert.ThrowsAsync<WorkflowArtifactReconciliationException>(() => ReadAllAsync(source));

        Assert.Equal("mounted-artifacts", exception.SourceId);
        Assert.Contains(missing, exception.Message, StringComparison.Ordinal);
        Assert.Contains("does not exist", exception.Reason, StringComparison.Ordinal);
        Assert.Empty(reader.Paths);
    }

    [Fact]
    public async Task Empty_folder_is_a_no_op()
    {
        var folder = CreateFolder("empty-mount");

        var reader = new RecordingReader();
        var source = CreateSource(reader, options => options.FolderPath = folder);

        var files = await ReadAllAsync(source);

        Assert.Empty(files);
        Assert.Empty(reader.Paths);
    }

    [Fact]
    public async Task Folder_holding_no_json_matches_is_a_no_op_rather_than_an_abort()
    {
        var folder = CreateFolder("mount");
        WriteFile(folder, "readme.md");

        var reader = new RecordingReader();
        var source = CreateSource(reader, options => options.FolderPath = folder);

        var files = await ReadAllAsync(source);

        Assert.Empty(files);
    }

    [Fact]
    public async Task Read_stamps_the_configured_tenant_on_every_file()
    {
        var reader = new RecordingReader();
        var source = CreateSource(reader, options =>
        {
            options.TenantId = "tenant-a";
            options.Files =
            [
                new JsonWorkflowArtifactReconciliationFileOption(1, "one.json"),
                new JsonWorkflowArtifactReconciliationFileOption(2, "two.json"),
            ];
        });

        var files = await ReadAllAsync(source);

        Assert.Equal(2, files.Count);
        Assert.All(files, file => Assert.Equal("tenant-a", file.TenantId));
    }

    [Fact]
    public async Task Read_defaults_to_the_untenanted_engine()
    {
        var reader = new RecordingReader();
        var source = CreateSource(reader, options => options.FilePath = "closure.json");

        var file = Assert.Single(await ReadAllAsync(source));

        Assert.Null(file.TenantId);
    }

    [Fact]
    public async Task A_file_level_read_failure_surfaces_as_the_readers_own_exception()
    {
        // §2.23.5: the source does not swallow or re-wrap a per-file failure — the reconciler distinguishes it
        // from a pass-aborting one by type, so the type must survive the iterator.
        var reader = new ThrowingReader(new InvalidWorkflowArtifactClosureException("closure.json", "the file deserialized to null."));
        var source = CreateSource(reader, options => options.FilePath = "closure.json");

        var exception = await Assert.ThrowsAsync<InvalidWorkflowArtifactClosureException>(() => ReadAllAsync(source));

        Assert.Equal("closure.json", exception.Origin);
    }

    [Fact]
    public async Task Read_observes_cancellation_between_files()
    {
        var reader = new RecordingReader();
        var source = CreateSource(reader, options => options.Files =
        [
            new JsonWorkflowArtifactReconciliationFileOption(1, "one.json"),
            new JsonWorkflowArtifactReconciliationFileOption(2, "two.json"),
        ]);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadAllAsync(source, cancellation.Token));
        Assert.Empty(reader.Paths);
    }

    private string CreateFolder(string name)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void WriteFile(string folder, string fileName) =>
        File.WriteAllText(Path.Combine(folder, fileName), "{}");

    private static JsonWorkflowArtifactReconciliationSource CreateSource(
        IWorkflowArtifactClosureReader reader,
        Action<JsonWorkflowArtifactReconciliationOptions> configure)
    {
        var options = new JsonWorkflowArtifactReconciliationOptions { SourceId = "mounted-artifacts" };
        configure(options);
        return new JsonWorkflowArtifactReconciliationSource(
            reader,
            MicrosoftOptions.Create(options),
            NullLogger<JsonWorkflowArtifactReconciliationSource>.Instance);
    }

    private static async Task<List<WorkflowArtifactClosureFile>> ReadAllAsync(
        IWorkflowArtifactReconciliationSource source,
        CancellationToken cancellationToken = default)
    {
        var files = new List<WorkflowArtifactClosureFile>();
        await foreach (var file in source.ReadAsync(cancellationToken))
            files.Add(file);
        return files;
    }

    private sealed class RecordingReader : IWorkflowArtifactClosureReader
    {
        public List<string> Paths { get; } = [];

        public WorkflowArtifactClosure Read(string filePath, CancellationToken cancellationToken = default)
        {
            Paths.Add(filePath);
            return new WorkflowArtifactClosure(WorkflowArtifactClosureFormat.CurrentVersion, "artifact-1", [], [], []);
        }
    }

    private sealed class ThrowingReader(Exception exception) : IWorkflowArtifactClosureReader
    {
        public WorkflowArtifactClosure Read(string filePath, CancellationToken cancellationToken = default) => throw exception;
    }
}
