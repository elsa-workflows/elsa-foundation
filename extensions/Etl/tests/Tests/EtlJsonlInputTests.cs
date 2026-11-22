using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Execution;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Runtime.Preparation;

namespace Elsa.Etl.Tests;

/// <summary>Spec 1190: JSON Lines framing, preparation and batch building over retained snapshots.</summary>
public sealed class EtlJsonlInputTests
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    public static TheoryData<string, byte[], long, long[]> FramingCases => new()
    {
        { "empty", [], 0, [] },
        { "blank lines only", Utf8("\n \t\r\n\r\n"), 0, [] },
        { "unterminated single record", Utf8("{}"), 0, [0, 2] },
        { "terminated single record", Utf8("{}\n"), 0, [0, 3] },
        { "crlf then unterminated", Utf8("{}\r\n{}"), 0, [0, 4, 4, 6] },
        { "bom and blank gap", [.. Bom, .. Utf8("{}\n\n{}\n")], 3, [3, 6, 7, 10] },
        { "bom only", Bom, 3, [] },
        { "leading and trailing blanks", Utf8(" \t\n{}\n \n"), 0, [3, 6] },
        { "partial bom is content", [0xEF, 0xBB], 0, [0, 2] },
        { "whitespace inside a record", Utf8("  {} \r\n"), 0, [0, 7] }
    };

    [Theory]
    [MemberData(nameof(FramingCases))]
    public async Task Line_indexer_frames_records_and_skips_blank_lines(string name, byte[] input, long bomLength, long[] spans)
    {
        using var destination = new MemoryStream();
        var count = await EtlJsonlLineIndexer.WriteIndexAsync(new MemoryStream(input), destination, int.MaxValue, CancellationToken.None);

        var entries = ReadEntries(destination.ToArray());
        Assert.True(spans.Length / 2 == count, name);
        Assert.Equal((0L, 0L, bomLength), entries[0]);
        Assert.Equal(spans.Chunk(2).Select((span, index) => (index + 1L, span[0], span[1])).ToArray(), entries[1..]);
    }

    [Fact]
    public async Task Line_indexer_scans_lines_longer_than_its_buffer()
    {
        var line = "{\"v\":\"" + new string('x', 10_000) + "\"}\n";
        using var destination = new MemoryStream();
        var count = await EtlJsonlLineIndexer.WriteIndexAsync(new MemoryStream(Utf8(line + line)), destination, int.MaxValue,
            CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal((2L, (long)line.Length, 2L * line.Length), ReadEntries(destination.ToArray())[2]);
    }

    [Theory]
    [InlineData("", 0, 24)]
    [InlineData("bom", 0, 24)]
    [InlineData("{\"id\":1,\"name\":\"Ada\"}\n\n{\"id\":2,\"name\":\"Bram\"}", 2, 72)]
    public async Task Preparation_retains_a_jsonl_snapshot_and_sentinel_index(string content, long records, long indexBytes)
    {
        using var files = new TestRoot();
        var source = files.WriteBytes("source.jsonl", content == "bom" ? Bom : Utf8(content));
        var pipeline = JsonlPipeline();
        var result = await new EtlConnectorPreparer(Bindings(files.Scope, source), files.SnapshotRoot)
            .PrepareAsync(Admission(files.Scope, pipeline, source));

        Assert.True(result.Succeeded, result.Diagnostic?.Code);
        var file = result.Prepared!.File!;
        Assert.EndsWith(".jsonl", file.Source.Key);
        Assert.Equal(records, file.RecordCount);
        Assert.Equal(indexBytes, file.Index.ByteLength);
        Assert.Equal(await File.ReadAllBytesAsync(source), await File.ReadAllBytesAsync(Path.Combine(files.SnapshotRoot, file.Source.Key)));
    }

    [Theory]
    [InlineData("unavailable", "etl.binding-read-failed")]
    [InlineData("digest", "etl.snapshot-digest-mismatch")]
    [InlineData("too-large", "etl.snapshot-too-large")]
    [InlineData("shards", "etl.pipeline-invalid")]
    [InlineData("no-fields", "etl.pipeline-invalid")]
    [InlineData("too-many-records", "etl.source-too-many-records")]
    public async Task Preparation_refuses_invalid_jsonl_inputs_before_any_partition_exists(string variant, string code)
    {
        using var files = new TestRoot();
        var source = files.WriteBytes("source.jsonl", Utf8("{\"id\":1,\"name\":\"Ada\"}\n{\"id\":2,\"name\":\"Bram\"}\n"));
        var pipeline = JsonlPipeline();
        if (variant == "too-large")
            pipeline = pipeline with { Limits = pipeline.Limits with { MaxSnapshotBytes = 8, MaxLookupBytes = 8 } };
        if (variant == "no-fields")
            pipeline = pipeline with { Source = new EtlJsonlSource("source", []) };
        var bindings = Bindings(files.Scope, source);
        var admission = Admission(files.Scope, pipeline, source);
        if (variant == "shards")
            admission = admission with { Bindings = admission.Bindings with { HttpShardIds = ["shard-a"] } };
        if (variant == "digest")
            await File.AppendAllTextAsync(source, "{\"id\":3,\"name\":\"Cleo\"}\n");
        if (variant == "unavailable")
            File.Delete(source);
        var preparer = variant == "too-many-records"
            ? new EtlConnectorPreparer(bindings, files.SnapshotRoot, new FileChunkPublisherFactory(), maxSourceRecords: 1)
            : new EtlConnectorPreparer(bindings, files.SnapshotRoot);

        var result = await preparer.PrepareAsync(admission);

        Assert.False(result.Succeeded);
        Assert.Equal(code, result.Diagnostic!.Code);
    }

    [Fact]
    public async Task Batch_routes_each_bad_line_as_a_record_error_and_parses_the_rest()
    {
        using var files = new TestRoot();
        var exact = "{\"id\":10,\"name\":\"" + new string('e', 64 - 19) + "\"}";
        Assert.Equal(64, exact.Length);
        var lines = new List<byte[]>
        {
            Utf8("{\"id\":1,\"name\":\"Ada\",\"extra\":{\"nested\":[1,2]}}\n"),
            Utf8("{\"id\":\n"),
            Utf8("[1,2]\n"),
            Utf8("{\"id\":\"x\",\"name\":\"B\"}\n"),
            Utf8("{\"name\":\"C\"}\n"),
            Utf8("{\"id\":6,\"id\":7,\"name\":\"D\"}\n"),
            Utf8("{\"id\":7,\"name\":\"E\",\"deep\":[[[[[1]]]]]}\n"),
            Utf8("{\"id\":8,\"name\":\"").Append((byte)0xFF).Concat(Utf8("\"}\n")).ToArray(),
            Utf8("{\"id\":9,\"name\":\"" + new string('o', 64) + "\"}\n"),
            Utf8(exact + "\r\n"),
            Utf8("\n  \n"),
            Utf8("{\"id\":11,\"name\":\"Last\"}")
        };
        var source = files.WriteBytes("source.jsonl", lines.SelectMany(line => line).ToArray());
        var baseline = JsonlPipeline(maxBatchRecords: 100);
        var pipeline = baseline with
        {
            Limits = baseline.Limits with { MaxRecordBytes = 64 },
            Http = baseline.Http with { MaxJsonDepth = 4 }
        };
        var admission = Admission(files.Scope, pipeline, source);
        var prepared = await PrepareAsync(files, pipeline, source, admission);
        await using var builder = await EtlConnectorInputBatchBuilder.OpenAsync(files.SnapshotRoot, admission, prepared, 0);

        var batch = (await builder.BuildNextAsync(new EtlConnectorFilePosition(1, 12, 1))).Batch!;

        Assert.Equal(new EtlConnectorFileBatchRange(1, 12), batch.Descriptor.Range);
        Assert.Equal(Enumerable.Range(1, 11).Select(index => (long)index), batch.Records.Select(record => record.Index));
        Assert.Equal(new string?[]
            {
                null, "etl.jsonl.record-invalid", "etl.jsonl.record-not-object", "etl.invalid-value", "etl.required-value-missing",
                "etl.jsonl.record-invalid", "etl.jsonl.record-invalid", "etl.jsonl.record-invalid", "etl.record-too-large", null, null
            },
            batch.Records.Select(record => record.Error?.Code));
        Assert.Equal("id", batch.Records[3].Error!.Field);
        Assert.Equal(5, batch.Records[4].Error!.RecordOrdinal);
        Assert.Equal("Ada", batch.Records[0].Row!.Values["name"].Value);
        Assert.Equal(10L, batch.Records[9].Row!.Values["id"].Value);
        Assert.Equal(11, batch.Records[10].Row!.RecordOrdinal);
        // Raw input bytes are the full span; the gap of blank lines belongs to no record.
        Assert.Equal(lines.Take(10).Select(line => (long)line.Length).Append(lines[11].Length), batch.Records.Select(record => record.RawInputBytes));
    }

    [Fact]
    public async Task Batch_classifies_boundary_values_nested_duplicates_and_cr_only_files()
    {
        using var files = new TestRoot();
        var atLimit = "{\"id\":1,\"name\":\"" + new string('a', 64 - 18) + "\"}";
        var overLimit = "{\"id\":2,\"name\":\"" + new string('b', 64 - 17) + "\"}";
        Assert.Equal((64, 65), (atLimit.Length, overLimit.Length));
        var lines = new[]
        {
            atLimit,
            overLimit,
            "{\"id\":9223372036854775808,\"name\":\"big\"}",
            "{\"id\":3,\"name\":{\"first\":\"Ada\"}}",
            "{\"id\":4,\"name\":[\"Ada\"]}",
            "{\"id\":5,\"name\":\"N\",\"meta\":{\"k\":1,\"k\":2}}",
            "{\"id\":6,\"name\":\"Dec\",\"amount\":1.00000000000000000000000000001}",
            "{\"id\":7,\"name\":\"T\",}",
            "{\"id\":8 /*c*/,\"name\":\"C\"}"
        };
        var source = files.WriteBytes("source.jsonl", Utf8(string.Join("\n", lines) + "\n"));
        var baseline = JsonlPipeline(maxBatchRecords: 100);
        var pipeline = baseline with
        {
            Source = new EtlJsonlSource("source", [new EtlJsonField("id", "/id", EtlColumnType.Int64, true),
                new EtlJsonField("name", "/name", EtlColumnType.String, true),
                new EtlJsonField("amount", "/amount", EtlColumnType.Decimal, false)]),
            Limits = baseline.Limits with { MaxRecordBytes = 64 }
        };
        var admission = Admission(files.Scope, pipeline, source);
        var prepared = await PrepareAsync(files, pipeline, source, admission);
        await using var builder = await EtlConnectorInputBatchBuilder.OpenAsync(files.SnapshotRoot, admission, prepared, 0);

        var batch = (await builder.BuildNextAsync(new EtlConnectorFilePosition(1, 10, 1))).Batch!;

        // Trailing commas and comments are not JSON, so those lines are invalid records too.
        Assert.Equal(new string?[]
            {
                null, "etl.record-too-large", "etl.invalid-value", "etl.invalid-value", "etl.invalid-value",
                "etl.jsonl.record-invalid", "etl.invalid-value", "etl.jsonl.record-invalid", "etl.jsonl.record-invalid"
            },
            batch.Records.Select(record => record.Error?.Code));
        Assert.Equal(new string?[] { null, null, "id", "name", "name", null, "amount", null, null }, batch.Records.Select(record => record.Error?.Field));

        // CR-only line ends are not record terminators: the whole file is one line and one invalid record.
        using var crFiles = new TestRoot();
        var crSource = crFiles.WriteBytes("source.jsonl", Utf8("{\"id\":1,\"name\":\"A\"}\r{\"id\":2,\"name\":\"B\"}\r"));
        var crAdmission = Admission(crFiles.Scope, JsonlPipeline(), crSource);
        var crPrepared = await PrepareAsync(crFiles, JsonlPipeline(), crSource, crAdmission);
        Assert.Equal(1, crPrepared.File!.RecordCount);
        await using var crBuilder = await EtlConnectorInputBatchBuilder.OpenAsync(crFiles.SnapshotRoot, crAdmission, crPrepared, 0);
        var crBatch = (await crBuilder.BuildNextAsync(new EtlConnectorFilePosition(1, 2, 1))).Batch!;
        Assert.Equal("etl.jsonl.record-invalid", Assert.Single(crBatch.Records).Error!.Code);
    }

    [Fact]
    public async Task Preparation_accepts_a_record_count_exactly_at_its_ceiling()
    {
        using var files = new TestRoot();
        var source = files.WriteBytes("source.jsonl", Utf8("{\"id\":1,\"name\":\"Ada\"}\n{\"id\":2,\"name\":\"Bram\"}\n"));
        var result = await new EtlConnectorPreparer(Bindings(files.Scope, source), files.SnapshotRoot,
            new FileChunkPublisherFactory(), maxSourceRecords: 2).PrepareAsync(Admission(files.Scope, JsonlPipeline(), source));

        Assert.True(result.Succeeded, result.Diagnostic?.Code);
        Assert.Equal(2, result.Prepared!.File!.RecordCount);
    }

    [Fact]
    public async Task Two_partitions_rebuild_the_same_identity_and_ignore_a_changed_host_file()
    {
        using var files = new TestRoot();
        var source = files.WriteBytes("source.jsonl",
            Utf8("{\"id\":1,\"name\":\"A\"}\n\n{\"id\":2,\"name\":\"B\"}\n{\"id\":3,\"name\":\"C\"}\n \n{\"id\":4,\"name\":\"D\"}\n{\"id\":5,\"name\":\"E\"}\n"));
        var baseline = JsonlPipeline(maxBatchRecords: 2);
        var pipeline = baseline with { Limits = baseline.Limits with { FilePartitionCount = 2 } };
        var admission = Admission(files.Scope, pipeline, source);
        var prepared = await PrepareAsync(files, pipeline, source, admission);
        Assert.Equal(5, prepared.File!.RecordCount);
        await File.WriteAllTextAsync(source, "{\"id\":99,\"name\":\"changed\"}\n");

        await using var second = await EtlConnectorInputBatchBuilder.OpenAsync(files.SnapshotRoot, admission, prepared, 1);
        var position = new EtlConnectorFilePosition(4, 6, 4);
        var built = (await second.BuildNextAsync(position)).Batch!;
        var rebuilt = (await second.RebuildAsync(built.Descriptor, position)).Batch!;

        Assert.Equal(new EtlConnectorFileBatchRange(4, 6), built.Descriptor.Range);
        Assert.Equal(new[] { "D", "E" }, built.Records.Select(record => record.Row!.Values["name"].Value));
        Assert.Equal(built.Descriptor, rebuilt.Descriptor);
        await using var first = await EtlConnectorInputBatchBuilder.OpenAsync(files.SnapshotRoot, admission, prepared, 0);
        var firstBatch = (await first.BuildNextAsync(new EtlConnectorFilePosition(1, 4, 1))).Batch!;
        Assert.Equal(new[] { "A", "B" }, firstBatch.Records.Select(record => record.Row!.Values["name"].Value));
        Assert.NotEqual(built.Descriptor.BatchId, firstBatch.Descriptor.BatchId);
    }

    [Fact]
    public async Task Opening_refuses_a_tampered_jsonl_snapshot()
    {
        using var files = new TestRoot();
        var source = files.WriteBytes("source.jsonl", Utf8("{\"id\":1,\"name\":\"A\"}\n"));
        var pipeline = JsonlPipeline();
        var admission = Admission(files.Scope, pipeline, source);
        var prepared = await PrepareAsync(files, pipeline, source, admission);
        var snapshot = Path.Combine(files.SnapshotRoot, prepared.File!.Source.Key);
        var bytes = await File.ReadAllBytesAsync(snapshot);
        bytes[7] = (byte)'2';
        File.SetAttributes(snapshot, FileAttributes.Normal);
        await File.WriteAllBytesAsync(snapshot, bytes);

        var error = await Assert.ThrowsAsync<EtlConnectorInputBatchBuilderException>(() =>
            EtlConnectorInputBatchBuilder.OpenAsync(files.SnapshotRoot, admission, prepared, 0).AsTask());

        Assert.Equal("etl.snapshot-integrity-failed", error.Diagnostic.Code);
    }

    /// <summary>Each forged index carries its real hash, so only the framing re-verification can refuse it.</summary>
    [Theory]
    [InlineData("span-without-line-feed")]
    [InlineData("line-feed-inside-span")]
    [InlineData("content-in-gap")]
    [InlineData("blank-span")]
    [InlineData("mid-line-start")]
    public async Task Batch_building_refuses_a_hash_valid_index_whose_framing_is_wrong(string variant)
    {
        using var files = new TestRoot();
        // Spans: record 1 = [0, 9), blank line = [9, 10), record 2 = [10, 19).
        var source = files.WriteBytes("source.jsonl", Utf8("{\"id\":1}\n\n{\"id\":2}\n"));
        var pipeline = JsonlPipeline() with
        {
            Source = new EtlJsonlSource("source", [new EtlJsonField("id", "/id", EtlColumnType.Int64, true)]),
            Mapping = [new EtlOutputField("id", EtlFieldOrigin.Source, "id")],
            Filters = []
        };
        var admission = Admission(files.Scope, pipeline, source);
        var prepared = await PrepareAsync(files, pipeline, source, admission);
        (long Start, long End)[] spans = variant switch
        {
            "span-without-line-feed" => [(0, 5), (10, 19)],
            "line-feed-inside-span" => [(0, 19)],
            "content-in-gap" => [(10, 19)],
            "blank-span" => [(0, 9), (9, 10)],
            "mid-line-start" => [(0, 9), (10, 14), (14, 19)],
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
        var forged = prepared with { File = prepared.File! with { Index = WriteIndex(files.SnapshotRoot, spans), RecordCount = spans.Length } };
        await using var builder = await EtlConnectorInputBatchBuilder.OpenAsync(files.SnapshotRoot, admission, forged, 0);

        // The mid-line case starts at a record whose predecessor this batch did not read, as a partition's first batch does.
        var next = variant == "mid-line-start" ? 3 : 1;
        var result = await builder.BuildNextAsync(new EtlConnectorFilePosition(1, spans.Length + 1, next));

        Assert.Null(result.Batch);
        Assert.Equal("etl.source-index-invalid", result.Diagnostic!.Code);
    }

    [Fact]
    public async Task Opening_refuses_a_hash_valid_index_whose_sentinel_is_not_a_byte_order_mark()
    {
        using var files = new TestRoot();
        var source = files.WriteBytes("source.jsonl", Utf8("{\"id\":1,\"name\":\"A\"}\n"));
        var pipeline = JsonlPipeline();
        var admission = Admission(files.Scope, pipeline, source);
        var prepared = await PrepareAsync(files, pipeline, source, admission);
        var forged = prepared with { File = prepared.File! with { Index = WriteIndex(files.SnapshotRoot, [(3, 21)], sentinelEnd: 3) } };

        var error = await Assert.ThrowsAsync<EtlConnectorInputBatchBuilderException>(() =>
            EtlConnectorInputBatchBuilder.OpenAsync(files.SnapshotRoot, admission, forged, 0).AsTask());

        Assert.Equal("etl.source-index-invalid", error.Diagnostic.Code);
    }

    private static async Task<EtlConnectorPreparedRun> PrepareAsync(TestRoot files, EtlPipelineV2 pipeline, string source,
        EtlConnectorRunAdmission admission)
    {
        var result = await new EtlConnectorPreparer(Bindings(files.Scope, source), files.SnapshotRoot).PrepareAsync(admission);
        Assert.True(result.Succeeded, result.Diagnostic?.Code);
        return result.Prepared!;
    }

    private static EtlObjectReference WriteIndex(string root, IReadOnlyList<(long Start, long End)> spans, long sentinelEnd = 0)
    {
        var bytes = new byte[(spans.Count + 1) * EtlConnectorCsvSupport.IndexEntrySize];
        EtlConnectorCsvSupport.WriteIndexEntry(bytes.AsSpan(0, EtlConnectorCsvSupport.IndexEntrySize), 0, 0, sentinelEnd);
        for (var index = 0; index < spans.Count; index++)
            EtlConnectorCsvSupport.WriteIndexEntry(bytes.AsSpan((index + 1) * EtlConnectorCsvSupport.IndexEntrySize,
                EtlConnectorCsvSupport.IndexEntrySize), index + 1, spans[index].Start, spans[index].End);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        File.WriteAllBytes(Path.Combine(root, hash + ".index"), bytes);
        return new EtlObjectReference(hash + ".index", hash, bytes.Length);
    }

    private static (long Ordinal, long Start, long End)[] ReadEntries(byte[] index) =>
        index.Chunk(EtlConnectorCsvSupport.IndexEntrySize).Select(entry => (
            BinaryPrimitives.ReadInt64LittleEndian(entry.AsSpan(0, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(entry.AsSpan(8, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(entry.AsSpan(16, 8)))).ToArray();

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    internal static EtlPipelineV2 JsonlPipeline(int maxBatchRecords = 10)
    {
        var baseline = EtlV2TestPipelines.Create();
        return baseline with
        {
            Source = new EtlJsonlSource("source", [new EtlJsonField("id", "/id", EtlColumnType.Int64, true),
                new EtlJsonField("name", "/name", EtlColumnType.String, true)]),
            Routes = new EtlRoutes(new EtlJsonlDestination("output"), new EtlJsonlDestination("errors")),
            Limits = baseline.Limits with { FilePartitionCount = 1, MaxBatchRecords = maxBatchRecords }
        };
    }

    private static EtlConnectorRunAdmission Admission(EtlScope scope, EtlPipelineV2 pipeline, string sourcePath)
    {
        var pin = Bindings(scope, sourcePath).Resolve(scope, "source", EtlFileRole.Source);
        var document = EtlArtifactV2Json.Write(pipeline);
        var hash = EtlArtifactJson.Hash(document);
        var artifact = new EtlArtifact(EtlArtifactV2Json.FormatVersion,
            EtlArtifactJson.CreateArtifactId(EtlArtifactV2Json.FormatVersion, "orders", 1, hash), hash, "orders", 1, document);
        var invocation = EtlRuntimeFixtures.Admission("input-batch-jsonl", scope).Invocation with { Artifact = artifact };
        return new EtlConnectorRunAdmission(invocation, new EtlConnectorRunBindings(
            new EtlConnectorBindingRevision(EtlConnectorKind.Jsonl,
                new EtlBindingRevision(pin.Alias, pin.Revision, pin.Handle, pin.ExpectedSha256)),
            null,
            new EtlConnectorBindingRevision(EtlConnectorKind.Jsonl, new EtlBindingRevision("output", "v1", "output-handle", null)),
            new EtlConnectorBindingRevision(EtlConnectorKind.Jsonl, new EtlBindingRevision("errors", "v1", "errors-handle", null)), []));
    }

    private static HostFileBindings Bindings(EtlScope scope, string sourcePath) => new([
        new HostFileBinding(scope, "source", EtlFileRole.Source, "v1", sourcePath, true,
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(sourcePath))), ImmutableInput: true)
    ]);

    private sealed class TestRoot : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"elsa-etl-jsonl-{Guid.NewGuid():N}");
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
            if (!Directory.Exists(_root))
                return;
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_root, recursive: true);
        }
    }
}
