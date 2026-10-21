using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Preparation;

namespace Elsa.Etl.Tests;

public sealed class EtlFilePreparationTests
{
    [Fact]
    public async Task Snapshots_the_admitted_revision_after_the_current_alias_moves_forward()
    {
        using var files = new TestFiles();
        var sourceV1 = files.Write("source-v1.csv", "id,name\n1,old\n");
        var lookupV1 = files.Write("lookup-v1.csv", "id,label\n1,old item\n");
        var admitted = CreateBindings(
        [
            Input(files.Scope, "source", EtlFileRole.Source, "v1", sourceV1, current: true),
            Input(files.Scope, "lookup", EtlFileRole.Lookup, "v1", lookupV1, current: true)
        ]);
        var sourcePin = admitted.Resolve(files.Scope, "source", EtlFileRole.Source);
        var lookupPin = admitted.Resolve(files.Scope, "lookup", EtlFileRole.Lookup);

        var sourceV2 = files.Write("source-v2.csv", "id,name\n1,new\n");
        var lookupV2 = files.Write("lookup-v2.csv", "id,label\n1,new item\n");
        var current = CreateBindings(
        [
            Input(files.Scope, "source", EtlFileRole.Source, "v1", sourceV1, current: false),
            Input(files.Scope, "source", EtlFileRole.Source, "v2", sourceV2, current: true),
            Input(files.Scope, "lookup", EtlFileRole.Lookup, "v1", lookupV1, current: false),
            Input(files.Scope, "lookup", EtlFileRole.Lookup, "v2", lookupV2, current: true)
        ]);
        var preparer = new EtlFilePreparer(current, files.SnapshotRoot);

        var result = await preparer.CreateSnapshotsAsync(files.Scope, "run-pinned", Pipeline(), sourcePin, lookupPin);

        Assert.True(result.Succeeded, result.Diagnostic?.Code);
        Assert.Equal("id,name\n1,old\n", await File.ReadAllTextAsync(Path.Combine(files.SnapshotRoot, result.Snapshots!.SourceChunk.Key)));
        Assert.Equal("id,label\n1,old item\n", await File.ReadAllTextAsync(Path.Combine(files.SnapshotRoot, result.Snapshots.LookupChunk.Key)));
    }

    [Fact]
    public async Task Refuses_a_removed_admitted_revision_instead_of_falling_back_to_current()
    {
        using var files = new TestFiles();
        var sourceV1 = files.Write("source-v1.csv", "id,name\n1,old\n");
        var lookupV1 = files.Write("lookup-v1.csv", "id,label\n1,old item\n");
        var admitted = CreateBindings(
        [
            Input(files.Scope, "source", EtlFileRole.Source, "v1", sourceV1, current: true),
            Input(files.Scope, "lookup", EtlFileRole.Lookup, "v1", lookupV1, current: true)
        ]);
        var sourcePin = admitted.Resolve(files.Scope, "source", EtlFileRole.Source);
        var lookupPin = admitted.Resolve(files.Scope, "lookup", EtlFileRole.Lookup);
        var sourceV2 = files.Write("source-v2.csv", "id,name\n1,new\n");
        var lookupV2 = files.Write("lookup-v2.csv", "id,label\n1,new item\n");
        var current = CreateBindings(
        [
            Input(files.Scope, "source", EtlFileRole.Source, "v2", sourceV2, current: true),
            Input(files.Scope, "lookup", EtlFileRole.Lookup, "v2", lookupV2, current: true)
        ]);

        var result = await new EtlFilePreparer(current, files.SnapshotRoot)
            .CreateSnapshotsAsync(files.Scope, "run-removed", Pipeline(), sourcePin, lookupPin);

        Assert.False(result.Succeeded);
        Assert.Equal("etl.binding-unavailable", result.Diagnostic!.Code);
        Assert.Empty(GetFiles(files.SnapshotRoot, "*.csv"));
    }

    [Fact]
    public async Task Rejects_changed_host_bytes_before_publishing_a_snapshot()
    {
        using var files = new TestFiles();
        var inputs = files.BindInputs("id,name\n1,old\n", "id,label\n1,item\n");
        var bindings = inputs.Bindings;
        await File.WriteAllTextAsync(files.SourcePath, "id,name\n1,changed\n");

        var result = await new EtlFilePreparer(bindings, files.SnapshotRoot)
            .CreateSnapshotsAsync(files.Scope, "run-digest", Pipeline(), inputs.SourcePin, inputs.LookupPin);

        Assert.False(result.Succeeded);
        Assert.Equal("etl.snapshot-digest-mismatch", result.Diagnostic!.Code);
        Assert.Equal(EtlPreparationDiagnosticKind.BusinessError, result.Diagnostic.Kind);
        Assert.Empty(GetFiles(files.SnapshotRoot, "*.csv"));
    }

    [Fact]
    public async Task Enforces_snapshot_and_lookup_byte_limits_before_retaining_files()
    {
        using var files = new TestFiles();
        var inputs = files.BindInputs("id,name\n1,item\n", "id,label\n1,item\n");
        var bindings = inputs.Bindings;

        var sourceTooLarge = await new EtlFilePreparer(bindings, files.SnapshotRoot)
            .CreateSnapshotsAsync(files.Scope, "run-source-bound", Pipeline(maxSnapshotBytes: 4), inputs.SourcePin, inputs.LookupPin);
        Assert.Equal("etl.snapshot-too-large", sourceTooLarge.Diagnostic!.Code);
        Assert.Empty(GetFiles(files.SnapshotRoot, "*.csv"));

        var lookupTooLarge = await new EtlFilePreparer(bindings, files.SnapshotRoot)
            .CreateSnapshotsAsync(files.Scope, "run-lookup-bound", Pipeline(maxLookupBytes: 4), inputs.SourcePin, inputs.LookupPin);
        Assert.Equal("etl.snapshot-too-large", lookupTooLarge.Diagnostic!.Code);
        Assert.Single(Directory.EnumerateFiles(files.SnapshotRoot, "*.csv"));
    }

    [Fact]
    public async Task Prepares_reordered_headers_lookup_values_and_two_balanced_partitions()
    {
        using var files = new TestFiles();
        var sourceText = "name,id\nA,1\n\"B\nB\",2\nC,3\nD,4\nE,5";
        var lookupText = "label,id\nitem one,1\nitem two,2\nitem three,3\nitem four,4\nitem five,5\n";
        var snapshots = await files.SnapshotAsync(sourceText, lookupText);
        var preparer = new EtlFilePreparer(files.Bindings, files.SnapshotRoot);

        var result = await preparer.PrepareAsync(Pipeline(), snapshots);

        Assert.True(result.Succeeded, result.Diagnostic?.Code);
        var prepared = result.Prepared!;
        Assert.Equal(1, prepared.SourceHeader.GetFieldIndex("id"));
        Assert.Equal(0, prepared.SourceHeader.GetFieldIndex("name"));
        Assert.Equal(1, prepared.LookupHeader.GetFieldIndex("id"));
        Assert.Equal(0, prepared.LookupHeader.GetFieldIndex("label"));
        Assert.Equal(5L, prepared.SourceRecordCount);
        Assert.Equal(5, prepared.LookupRows.Count);
        Assert.Equal(new EtlPartitionRange(1, 4), prepared.Partitions[0]);
        Assert.Equal(new EtlPartitionRange(4, 6), prepared.Partitions[1]);

        var indexBytes = await File.ReadAllBytesAsync(Path.Combine(files.SnapshotRoot, prepared.SourceIndex.Key));
        Assert.Equal(6 * 24, indexBytes.Length);
        var records = ParseIndex(indexBytes);
        var expected = Encoding.UTF8.GetBytes(sourceText);
        long headerEnd = Encoding.UTF8.GetByteCount("name,id\n");
        var firstDataEnd = headerEnd + Encoding.UTF8.GetByteCount("A,1\n");
        var multilineDataEnd = firstDataEnd + Encoding.UTF8.GetByteCount("\"B\nB\",2\n");
        Assert.Equal((0L, 0L, headerEnd), records[0]);
        Assert.Equal((1L, headerEnd, firstDataEnd), records[1]);
        Assert.Equal((2L, firstDataEnd, multilineDataEnd), records[2]);
        Assert.Equal(expected.LongLength, records[^1].End);
    }

    [Fact]
    public async Task Duplicate_typed_lookup_keys_fail_before_source_index_publication()
    {
        using var files = new TestFiles();
        var snapshots = await files.SnapshotAsync("id,name\n1,item\n2,item\n", "id,label\n01,first\n1,duplicate\n");

        var result = await new EtlFilePreparer(files.Bindings, files.SnapshotRoot).PrepareAsync(Pipeline(), snapshots);

        Assert.False(result.Succeeded);
        Assert.Equal("etl.lookup-key-duplicate", result.Diagnostic!.Code);
        Assert.Equal(2, result.Diagnostic.RecordOrdinal);
        Assert.Equal("id", result.Diagnostic.Field);
        Assert.Empty(Directory.EnumerateFiles(files.SnapshotRoot, "*.index"));
    }

    [Fact]
    public async Task Lookup_record_limit_is_enforced_before_adding_an_extra_row()
    {
        using var files = new TestFiles();
        var snapshots = await files.SnapshotAsync("id,name\n1,item\n2,item\n", "id,label\n1,first\n2,second\n");

        var result = await new EtlFilePreparer(files.Bindings, files.SnapshotRoot)
            .PrepareAsync(Pipeline(maxLookupRecords: 1), snapshots);

        Assert.Equal("etl.lookup-too-many-records", result.Diagnostic!.Code);
        Assert.Empty(Directory.EnumerateFiles(files.SnapshotRoot, "*.index"));
    }

    [Fact]
    public async Task Invalid_typed_lookup_value_fails_preparation_before_index_publication()
    {
        using var files = new TestFiles();
        var snapshots = await files.SnapshotAsync("id,name\n1,item\n", "id,label\nnot-an-integer,item\n");

        var result = await new EtlFilePreparer(files.Bindings, files.SnapshotRoot).PrepareAsync(Pipeline(), snapshots);

        Assert.Equal("etl.invalid-value", result.Diagnostic!.Code);
        Assert.Equal("id", result.Diagnostic.Field);
        Assert.Empty(Directory.EnumerateFiles(files.SnapshotRoot, "*.index"));
    }

    [Fact]
    public async Task Malformed_source_tail_produces_no_runnable_partitions_or_index_chunk()
    {
        using var files = new TestFiles();
        var snapshots = await files.SnapshotAsync("id,name\n1,item\n2,\"unfinished", "id,label\n1,first\n");

        var result = await new EtlFilePreparer(files.Bindings, files.SnapshotRoot).PrepareAsync(Pipeline(), snapshots);

        Assert.False(result.Succeeded);
        Assert.Equal("csv.invalid-quoting", result.Diagnostic!.Code);
        Assert.Equal(EtlPreparationDiagnosticKind.BusinessError, result.Diagnostic.Kind);
        Assert.Equal(2, result.Diagnostic.RecordOrdinal);
        Assert.Empty(Directory.EnumerateFiles(files.SnapshotRoot, "*.index"));
    }

    [Fact]
    public async Task Empty_source_has_a_safe_missing_header_result_and_no_index()
    {
        using var files = new TestFiles();
        var snapshots = await files.SnapshotAsync("", "id,label\n1,first\n");

        var result = await new EtlFilePreparer(files.Bindings, files.SnapshotRoot).PrepareAsync(Pipeline(), snapshots);

        Assert.Equal("etl.source-header-missing", result.Diagnostic!.Code);
        Assert.Empty(Directory.EnumerateFiles(files.SnapshotRoot, "*.index"));
    }

    [Fact]
    public async Task Header_only_source_has_zero_actual_partitions()
    {
        using var files = new TestFiles();
        var snapshots = await files.SnapshotAsync("id,name\n", "id,label\n1,first\n");

        var result = await new EtlFilePreparer(files.Bindings, files.SnapshotRoot).PrepareAsync(Pipeline(), snapshots);

        Assert.True(result.Succeeded, result.Diagnostic?.Code);
        Assert.Empty(result.Prepared!.Partitions);
    }

    [Fact]
    public async Task Preparation_reopens_retained_snapshots_and_verifies_them_again()
    {
        using var files = new TestFiles();
        var snapshots = await files.SnapshotAsync("id,name\n1,item\n", "id,label\n1,one\n");
        await File.WriteAllTextAsync(files.SourcePath, "id,name\n1,changed original\n");
        var restartedPreparer = new EtlFilePreparer(new HostFileBindings([]), files.SnapshotRoot);

        var valid = await restartedPreparer.PrepareAsync(Pipeline(), snapshots);
        Assert.True(valid.Succeeded, valid.Diagnostic?.Code);

        await File.WriteAllTextAsync(Path.Combine(files.SnapshotRoot, snapshots.SourceChunk.Key), "id,name\n1,tampered\n");
        var corrupted = await restartedPreparer.PrepareAsync(Pipeline(), snapshots);

        Assert.False(corrupted.Succeeded);
        Assert.Equal("etl.snapshot-integrity-failed", corrupted.Diagnostic!.Code);
    }

    [Fact]
    public async Task Preparation_rejects_a_corrupted_lookup_snapshot_before_index_publication()
    {
        using var files = new TestFiles();
        var snapshots = await files.SnapshotAsync("id,name\n1,item\n", "id,label\n1,one\n");
        await File.WriteAllTextAsync(Path.Combine(files.SnapshotRoot, snapshots.LookupChunk.Key), "id,label\n1,changed\n");
        var restartedPreparer = new EtlFilePreparer(new HostFileBindings([]), files.SnapshotRoot);

        var result = await restartedPreparer.PrepareAsync(Pipeline(), snapshots);

        Assert.False(result.Succeeded);
        Assert.Equal("etl.snapshot-integrity-failed", result.Diagnostic!.Code);
        Assert.Empty(Directory.EnumerateFiles(files.SnapshotRoot, "*.index"));
    }

    [Fact]
    public async Task Defers_source_value_conversion_errors_until_row_execution()
    {
        using var files = new TestFiles();
        var snapshots = await files.SnapshotAsync("id,name\nnot-an-integer,item\n", "id,label\n1,one\n");

        var result = await new EtlFilePreparer(files.Bindings, files.SnapshotRoot).PrepareAsync(Pipeline(), snapshots);

        Assert.True(result.Succeeded, result.Diagnostic?.Code);
        Assert.Equal(1L, result.Prepared!.SourceRecordCount);
        Assert.Equal(new EtlPartitionRange(1, 2), Assert.Single(result.Prepared.Partitions));
    }

    [Fact]
    public async Task Reports_binding_read_failures_as_faults_without_exposing_paths()
    {
        using var files = new TestFiles();
        var inputs = files.BindInputs("id,name\n1,item\n", "id,label\n1,one\n");
        File.Delete(files.SourcePath);

        var result = await new EtlFilePreparer(inputs.Bindings, files.SnapshotRoot)
            .CreateSnapshotsAsync(files.Scope, "run-read-fault", Pipeline(), inputs.SourcePin, inputs.LookupPin);

        Assert.False(result.Succeeded);
        Assert.Equal("etl.binding-read-failed", result.Diagnostic!.Code);
        Assert.Equal(EtlPreparationDiagnosticKind.Fault, result.Diagnostic.Kind);
        Assert.NotNull(result.Diagnostic.Cause);
        Assert.DoesNotContain(files.SourcePath, result.Diagnostic.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_index_storage_failure_as_a_fault()
    {
        using var files = new TestFiles();
        var inputs = files.BindInputs("id,name\n1,item\n", "id,label\n1,one\n");
        var snapshotRootFile = files.Write("not-a-directory", "file");

        var result = await new EtlFilePreparer(inputs.Bindings, snapshotRootFile)
            .CreateSnapshotsAsync(files.Scope, "run-storage-fault", Pipeline(), inputs.SourcePin, inputs.LookupPin);

        Assert.False(result.Succeeded);
        Assert.Equal("etl.output-write-failed", result.Diagnostic!.Code);
        Assert.Equal(EtlPreparationDiagnosticKind.Fault, result.Diagnostic.Kind);
        Assert.NotNull(result.Diagnostic.Cause);
    }

    private static EtlPipeline Pipeline(
        long maxSnapshotBytes = 1024 * 1024,
        long maxLookupBytes = 1024 * 1024,
        long maxLookupRecords = 100,
        int partitions = 2) => new(
        new EtlFileBindings("source", "lookup", "output", "errors"),
        [new("id", EtlColumnType.Int64, Required: true), new("name", EtlColumnType.String, Required: true)],
        [new("id", EtlColumnType.Int64, Required: true), new("label", EtlColumnType.String, Required: true)],
        "id",
        "id",
        [new("id", EtlFieldOrigin.Source, "id"), new("label", EtlFieldOrigin.Lookup, "label")],
        Array.Empty<EtlFilter>(),
        new EtlLimits
        {
            PartitionCount = partitions,
            MaxRecordBytes = 4096,
            MaxFields = 8,
            MaxLookupRecords = maxLookupRecords,
            MaxLookupBytes = maxLookupBytes,
            MaxSnapshotBytes = maxSnapshotBytes
        });

    private static HostFileBinding Input(
        EtlScope scope,
        string alias,
        EtlFileRole role,
        string revision,
        string path,
        bool current) => new(scope, alias, role, revision, path, current,
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), ImmutableInput: true);

    private static HostFileBindings CreateBindings(IReadOnlyList<HostFileBinding> bindings) => new(bindings);

    private static IEnumerable<string> GetFiles(string root, string pattern) =>
        Directory.Exists(root) ? Directory.EnumerateFiles(root, pattern) : Enumerable.Empty<string>();

    private static List<(long Ordinal, long Start, long End)> ParseIndex(byte[] bytes)
    {
        var result = new List<(long, long, long)>();
        for (var offset = 0; offset < bytes.Length; offset += 24)
        {
            result.Add((
                BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset, 8)),
                BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset + 8, 8)),
                BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset + 16, 8))));
        }

        return result;
    }

    private sealed class TestFiles : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"elsa-etl-preparation-{Guid.NewGuid():N}");
        private IReadOnlyList<HostFileBinding>? _bindings;

        public EtlScope Scope { get; } = new("tenant", "partition");
        public string SnapshotRoot => Path.Combine(_root, "snapshots");
        public string SourcePath { get; private set; } = "";
        public HostFileBindings Bindings => new(_bindings ?? throw new InvalidOperationException("Input files have not been prepared."));

        public (HostFileBindings Bindings, PinnedFileBinding SourcePin, PinnedFileBinding LookupPin) BindInputs(
            string source,
            string lookup)
        {
            SourcePath = Write("source.csv", source);
            var lookupPath = Write("lookup.csv", lookup);
            _bindings =
            [
                Input(Scope, "source", EtlFileRole.Source, "v1", SourcePath, current: true),
                Input(Scope, "lookup", EtlFileRole.Lookup, "v1", lookupPath, current: true)
            ];
            var bindings = Bindings;
            return (
                bindings,
                bindings.Resolve(Scope, "source", EtlFileRole.Source),
                bindings.Resolve(Scope, "lookup", EtlFileRole.Lookup));
        }

        public string Write(string name, string contents)
        {
            Directory.CreateDirectory(_root);
            var path = Path.Combine(_root, name);
            File.WriteAllText(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }

        public async Task<EtlInputSnapshots> SnapshotAsync(string source, string lookup)
        {
            var inputs = BindInputs(source, lookup);
            var bindings = inputs.Bindings;
            var result = await new EtlFilePreparer(bindings, SnapshotRoot).CreateSnapshotsAsync(
                Scope, "run-1", Pipeline(),
                inputs.SourcePin,
                inputs.LookupPin);
            Assert.True(result.Succeeded, result.Diagnostic?.Code);
            return result.Snapshots!;
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); }
            catch (DirectoryNotFoundException) { }
        }
    }
}
