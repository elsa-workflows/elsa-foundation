using System.Security.Cryptography;
using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Execution;
using Elsa.Etl.Runtime.Http;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Runtime.Preparation;

namespace Elsa.Etl.Tests;

public sealed class EtlConnectorInputBatchBuilderTests
{
    [Fact]
    public async Task Csv_batches_split_at_the_record_limit_and_read_a_bom_header_from_the_prepared_index()
    {
        using var files = new TestRoot();
        var sourceBytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("id,name\n1,Ada\n2,Bram\n3,Cleo\n")).ToArray();
        var sourcePath = files.WriteBytes("source.csv", sourceBytes);
        var pipeline = CsvPipeline(maxBatchRecords: 1);
        var admission = CsvAdmission(files.Scope, pipeline, sourcePath);
        var bindings = CreateHostFileBindings(files.Scope, sourcePath);

        var prepared = await new EtlConnectorPreparer(bindings, files.SnapshotRoot).PrepareAsync(admission);
        Assert.True(prepared.Succeeded, prepared.Diagnostic?.Code);
        await using var builder = await EtlConnectorInputBatchBuilder.OpenAsync(files.SnapshotRoot, admission,
            prepared.Prepared!, partitionOrdinal: 0);

        var position = new EtlConnectorFilePosition(1, 4, 1);
        var first = Assert.IsType<EtlConnectorInputBatch>((await builder.BuildNextAsync(position)).Batch);
        Assert.Equal(new EtlConnectorFileBatchRange(1, 2), first.Descriptor.Range);
        Assert.Equal("Ada", first.Records[0].Row!.Values["name"].Value);

        var second = Assert.IsType<EtlConnectorInputBatch>((await builder.BuildNextAsync(position with { NextOrdinal = 2 })).Batch);
        Assert.Equal(new EtlConnectorFileBatchRange(2, 3), second.Descriptor.Range);
        Assert.Equal("Bram", second.Records[0].Row!.Values["name"].Value);
    }

    [Fact]
    public async Task Reopening_reports_a_bounded_csv_header_diagnostic_without_leaking_open_streams()
    {
        using var files = new TestRoot();
        var sourcePath = files.WriteBytes("source.csv", Encoding.UTF8.GetBytes("id,name\n1,Ada\n"));
        var pipeline = CsvPipeline(maxBatchRecords: 1);
        var bindings = CreateHostFileBindings(files.Scope, sourcePath);
        var prepared = await new EtlConnectorPreparer(bindings, files.SnapshotRoot)
            .PrepareAsync(CsvAdmission(files.Scope, pipeline, sourcePath));
        Assert.True(prepared.Succeeded, prepared.Diagnostic?.Code);

        var strictPipeline = pipeline with { Limits = pipeline.Limits with { MaxRecordBytes = 4 } };
        var strictAdmission = CsvAdmission(files.Scope, strictPipeline, sourcePath);
        var error = await Assert.ThrowsAsync<EtlConnectorInputBatchBuilderException>(() =>
            EtlConnectorInputBatchBuilder.OpenAsync(files.SnapshotRoot, strictAdmission, prepared.Prepared!, 0).AsTask());

        Assert.Equal("csv.record-too-large", error.Diagnostic.Code);
        Assert.Equal(EtlPreparationDiagnosticKind.BusinessError, error.Diagnostic.Kind);
    }

    [Fact]
    public async Task Reopening_rejects_a_corrupt_retained_lookup_as_snapshot_integrity_failure()
    {
        using var files = new TestRoot();
        var sourcePath = files.WriteBytes("source.csv", Encoding.UTF8.GetBytes("id,name\n1,Ada\n"));
        var lookupPath = files.WriteBytes("lookup.csv", Encoding.UTF8.GetBytes("id,label\n1,person\n"));
        var pipeline = CsvPipeline(maxBatchRecords: 1) with
        {
            Lookup = new EtlCsvLookup("lookup", [new EtlColumn("id", EtlColumnType.Int64, true),
                new EtlColumn("label", EtlColumnType.String, true)], "id", "id")
        };
        var bindings = CreateHostFileBindings(files.Scope, sourcePath, lookupPath);
        var admission = CsvAdmission(files.Scope, pipeline, sourcePath, lookupPath);
        var prepared = await new EtlConnectorPreparer(bindings, files.SnapshotRoot).PrepareAsync(admission);
        Assert.True(prepared.Succeeded, prepared.Diagnostic?.Code);

        var lookup = Assert.IsType<EtlObjectReference>(prepared.Prepared!.Lookup);
        var lookupPathInCache = Path.Combine(files.SnapshotRoot, lookup.Key);
        var altered = await File.ReadAllBytesAsync(lookupPathInCache);
        altered[^2] ^= 1;
        await File.WriteAllBytesAsync(lookupPathInCache, altered);

        var error = await Assert.ThrowsAsync<EtlConnectorInputBatchBuilderException>(() =>
            EtlConnectorInputBatchBuilder.OpenAsync(files.SnapshotRoot, admission, prepared.Prepared!, 0).AsTask());

        Assert.Equal("etl.snapshot-integrity-failed", error.Diagnostic.Code);
    }

    [Fact]
    public async Task Http_page_splits_by_record_and_canonical_byte_limits_and_rebuilds_the_exact_range()
    {
        using var files = new TestRoot();
        var initialPipeline = HttpPipeline(maxBatchRecords: 1);
        var initialAdmission = HttpAdmission(files.Scope, initialPipeline);
        var page = await CapturePageAsync(files.SnapshotRoot, initialAdmission);
        await using var initialBuilder = await EtlConnectorInputBatchBuilder.OpenAsync(files.SnapshotRoot,
            initialAdmission, new EtlConnectorPreparedRun(null, null), partitionOrdinal: 0);
        var position = new EtlConnectorHttpPosition(0, 0, new EtlHttpRequest("shard-a", null), page.Descriptor, false);

        var first = Assert.IsType<EtlConnectorInputBatch>((await initialBuilder.BuildNextAsync(position, page)).Batch);
        Assert.Equal(new EtlConnectorHttpBatchRange(0, page.Descriptor.RequestHash, page.Descriptor.Body.ContentHash, 0, 1),
            first.Descriptor.Range);
        var rebuilt = Assert.IsType<EtlConnectorInputBatch>(
            (await initialBuilder.RebuildAsync(first.Descriptor, position, page)).Batch);
        Assert.Equal(first.Descriptor, rebuilt.Descriptor);

        var byteLimitedPipeline = initialPipeline with
        {
            Limits = initialPipeline.Limits with
            {
                MaxBatchRecords = 8,
                MaxBatchBytes = first.Descriptor.InputBytes,
                MaxRecordBytes = checked((int)first.Descriptor.InputBytes)
            }
        };
        var byteLimitedAdmission = HttpAdmission(files.Scope, byteLimitedPipeline);
        var byteLimitedPage = await CapturePageAsync(files.SnapshotRoot, byteLimitedAdmission);
        await using var byteLimitedBuilder = await EtlConnectorInputBatchBuilder.OpenAsync(files.SnapshotRoot,
            byteLimitedAdmission, new EtlConnectorPreparedRun(null, null), partitionOrdinal: 0);
        var byteLimitedPosition = new EtlConnectorHttpPosition(0, 0, new EtlHttpRequest("shard-a", null),
            byteLimitedPage.Descriptor, false);

        var boundedFirst = Assert.IsType<EtlConnectorInputBatch>(
            (await byteLimitedBuilder.BuildNextAsync(byteLimitedPosition, byteLimitedPage)).Batch);
        Assert.Equal(first.Descriptor.InputBytes, boundedFirst.Descriptor.InputBytes);
        Assert.Equal(1, ((EtlConnectorHttpBatchRange)boundedFirst.Descriptor.Range).EndItemExclusive);
        var boundedSecond = Assert.IsType<EtlConnectorInputBatch>(
            (await byteLimitedBuilder.BuildNextAsync(byteLimitedPosition with { NextItem = 1 }, byteLimitedPage)).Batch);
        Assert.Equal(1, ((EtlConnectorHttpBatchRange)boundedSecond.Descriptor.Range).StartItem);
        Assert.Equal(2, ((EtlConnectorHttpBatchRange)boundedSecond.Descriptor.Range).EndItemExclusive);
    }

    [Fact]
    public async Task Opening_a_builder_snapshots_http_shard_ids_from_the_admission()
    {
        using var files = new TestRoot();
        var pipeline = HttpPipeline(maxBatchRecords: 1);
        var shardIds = new[] { "shard-a" };
        var admission = HttpAdmission(files.Scope, pipeline, shardIds);
        var page = await CapturePageAsync(files.SnapshotRoot, admission);
        await using var builder = await EtlConnectorInputBatchBuilder.OpenAsync(files.SnapshotRoot, admission,
            new EtlConnectorPreparedRun(null, null), partitionOrdinal: 0);
        var position = new EtlConnectorHttpPosition(0, 0, new EtlHttpRequest("shard-a", null), page.Descriptor, false);
        var original = Assert.IsType<EtlConnectorInputBatch>((await builder.BuildNextAsync(position, page)).Batch);
        shardIds[0] = "changed-after-open";

        var replay = Assert.IsType<EtlConnectorInputBatch>((await builder.BuildNextAsync(position, page)).Batch);

        Assert.Equal(original.Descriptor, replay.Descriptor);
    }

    private static async Task<EtlCachedHttpPage> CapturePageAsync(string root, EtlConnectorRunAdmission admission)
    {
        var body = Encoding.UTF8.GetBytes("{\"records\":[{\"id\":1,\"name\":\"Ada\"},{\"id\":2,\"name\":\"Amy\"}],\"next\":null}");
        return await new EtlHttpPageCache(root, admission, 0).PublishAsync(0,
            new EtlHttpRequest("shard-a", null), Guid.NewGuid(), 1, DateTimeOffset.UtcNow, body);
    }

    private static EtlPipelineV2 HttpPipeline(int maxBatchRecords)
    {
        var baseline = EtlV2TestPipelines.Create();
        return baseline with
        {
            Source = ((EtlHttpJsonSource)baseline.Source) with { Pagination = new EtlSinglePagePagination() },
            Limits = baseline.Limits with { MaxBatchRecords = maxBatchRecords }
        };
    }

    private static EtlPipelineV2 CsvPipeline(int maxBatchRecords)
    {
        var baseline = HttpPipeline(maxBatchRecords);
        return baseline with
        {
            Source = new EtlCsvSource("source", [new EtlColumn("id", EtlColumnType.Int64, true),
                new EtlColumn("name", EtlColumnType.String, true)]),
            Routes = new EtlRoutes(new EtlJsonlDestination("output"), new EtlJsonlDestination("errors")),
            Limits = baseline.Limits with { FilePartitionCount = 1 }
        };
    }

    private static EtlConnectorRunAdmission HttpAdmission(EtlScope scope, EtlPipelineV2 pipeline,
        IReadOnlyList<string>? shardIds = null)
    {
        var artifact = CreateArtifact(pipeline);
        var invocation = EtlRuntimeFixtures.Admission("input-batch", scope).Invocation with { Artifact = artifact };
        return new EtlConnectorRunAdmission(invocation, new EtlConnectorRunBindings(
            Binding(EtlConnectorKind.Http, "source", "source-handle"), null,
            Binding(EtlConnectorKind.Http, "output", "output-handle"),
            Binding(EtlConnectorKind.Http, "errors", "errors-handle"), shardIds ?? ["shard-a"]));
    }

    private static EtlConnectorRunAdmission CsvAdmission(EtlScope scope, EtlPipelineV2 pipeline, string sourcePath,
        string? lookupPath = null)
    {
        var bindings = CreateHostFileBindings(scope, sourcePath, lookupPath);
        var pin = bindings.Resolve(scope, "source", EtlFileRole.Source);
        var lookupPin = pipeline.Lookup is null ? null : bindings.Resolve(scope, pipeline.Lookup.Binding, EtlFileRole.Lookup);
        var artifact = CreateArtifact(pipeline);
        var invocation = EtlRuntimeFixtures.Admission("input-batch-csv", scope).Invocation with { Artifact = artifact };
        return new EtlConnectorRunAdmission(invocation, new EtlConnectorRunBindings(
            new EtlConnectorBindingRevision(EtlConnectorKind.Csv,
                new EtlBindingRevision(pin.Alias, pin.Revision, pin.Handle, pin.ExpectedSha256)),
            lookupPin is null ? null : new EtlConnectorBindingRevision(EtlConnectorKind.Csv,
                new EtlBindingRevision(lookupPin.Alias, lookupPin.Revision, lookupPin.Handle, lookupPin.ExpectedSha256)),
            Binding(EtlConnectorKind.Jsonl, "output", "output-handle"),
            Binding(EtlConnectorKind.Jsonl, "errors", "errors-handle"), []));
    }

    private static HostFileBindings CreateHostFileBindings(EtlScope scope, string sourcePath, string? lookupPath = null)
    {
        var inputs = new List<HostFileBinding>
        {
            FileBinding(scope, "source", EtlFileRole.Source, sourcePath)
        };
        if (lookupPath is not null)
            inputs.Add(FileBinding(scope, "lookup", EtlFileRole.Lookup, lookupPath));
        return new HostFileBindings(inputs);
    }

    private static HostFileBinding FileBinding(EtlScope scope, string alias, EtlFileRole role, string path) =>
        new(scope, alias, role, "v1", path, true,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), ImmutableInput: true);

    private static EtlArtifact CreateArtifact(EtlPipelineV2 pipeline)
    {
        var document = EtlArtifactV2Json.Write(pipeline);
        var hash = EtlArtifactJson.Hash(document);
        return new EtlArtifact(EtlArtifactV2Json.FormatVersion,
            EtlArtifactJson.CreateArtifactId(EtlArtifactV2Json.FormatVersion, "orders", 1, hash), hash,
            "orders", 1, document);
    }

    private static EtlConnectorBindingRevision Binding(EtlConnectorKind kind, string alias, string handle) =>
        new(kind, new EtlBindingRevision(alias, "v1", handle, null));

    private sealed class TestRoot : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"elsa-etl-input-batches-{Guid.NewGuid():N}");
        public EtlScope Scope { get; } = new("tenant", "partition");
        public string SnapshotRoot => Path.Combine(_root, "snapshots");

        public string WriteBytes(string name, byte[] bytes)
        {
            Directory.CreateDirectory(_root);
            var path = Path.Combine(_root, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}
