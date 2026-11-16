using System.Security.Cryptography;
using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Execution;
using Elsa.Etl.Runtime.Preparation;

namespace Elsa.Etl.Tests;

public sealed class EtlBatchBuilderTests
{
    [Fact]
    public async Task Builds_a_canonical_batch_with_exact_utf8_charge_and_row_identity()
    {
        const string firstRow = "{\"ordinal\":1,\"fields\":[\"A\",\"1\"]}";
        var expectedPayload = $"{{\"formatVersion\":1,\"records\":[{firstRow}]}}";
        var firstRowPayloadBytes = Encoding.UTF8.GetByteCount(expectedPayload);
        var pipeline = CreatePipeline(maxBatchRecords: 10, maxBatchBytes: firstRowPayloadBytes);
        using var files = new TestFiles();
        var prepared = await files.PrepareAsync(pipeline, "name,id\nA,1\nB,2\nC,3\n", "id,label\n1,one\n2,two\n3,three\n");

        await using var builder = await EtlInputBatchBuilder.OpenAsync(files.SnapshotRoot, pipeline, prepared);
        var batch = await builder.BuildNextAsync(partitionIndex: 0, nextRecordOrdinal: 1);

        Assert.True(batch.Succeeded, batch.Diagnostic?.ToString());
        Assert.NotNull(batch.Batch);
        Assert.Equal(1, batch.Batch.StartRecordOrdinal);
        Assert.Equal(2, batch.Batch.EndRecordOrdinalExclusive);
        Assert.Equal(1, batch.Batch.RecordCount);
        Assert.Equal(firstRowPayloadBytes, batch.Batch.CanonicalInputBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expectedPayload))).ToLowerInvariant(),
            batch.Batch.ContentHash);
        var record = Assert.Single(batch.Batch.Records);
        Assert.Equal(1, record.RecordOrdinal);
        Assert.Equal(new[] { "A", "1" }, record.Fields);

        var next = await builder.BuildNextAsync(partitionIndex: 0, nextRecordOrdinal: 2);
        Assert.True(next.Succeeded, next.Diagnostic?.ToString());
        Assert.Equal(2, Assert.Single(next.Batch!.Records).RecordOrdinal);
        Assert.Equal(2, next.Batch.StartRecordOrdinal);
        Assert.Equal(3, next.Batch.EndRecordOrdinalExclusive);
    }

    [Fact]
    public async Task Reads_a_nonzero_partition_from_global_offsets_after_bom_and_multiline_records()
    {
        const string source = "\uFEFFid,name\r\n1,\"A\nline\"\r\n2,B";
        var pipeline = CreatePipeline(maxBatchRecords: 10, maxBatchBytes: 4096);
        using var files = new TestFiles();
        var prepared = await files.PrepareAsync(pipeline, source, "id,label\n1,one\n2,two\n");
        var bytes = Encoding.UTF8.GetBytes(source);
        var secondRecordOffset = Encoding.UTF8.GetByteCount("\uFEFFid,name\r\n1,\"A\nline\"\r\n");

        await using var builder = await EtlInputBatchBuilder.OpenAsync(files.SnapshotRoot, pipeline, prepared);
        var result = await builder.BuildNextAsync(partitionIndex: 1, nextRecordOrdinal: 2);

        Assert.True(result.Succeeded, result.Diagnostic?.ToString());
        var record = Assert.Single(result.Batch!.Records);
        Assert.Equal(2, record.RecordOrdinal);
        Assert.Equal(secondRecordOffset, record.StartByteOffset);
        Assert.Equal(bytes.LongLength, record.EndByteOffset);
        Assert.Equal(new[] { "2", "B" }, record.Fields);
    }

    [Fact]
    public async Task Measures_json_escaping_in_the_canonical_byte_charge()
    {
        const string expectedPayload = "{\"formatVersion\":1,\"records\":[{\"ordinal\":1,\"fields\":[\"1\",\"\\u0001\"]}]}";
        var pipeline = CreatePipeline(maxBatchRecords: 10, maxBatchBytes: Encoding.UTF8.GetByteCount(expectedPayload));
        using var files = new TestFiles();
        var prepared = await files.PrepareAsync(pipeline, "id,name\n1,\u0001\n2,B\n3,C\n", "id,label\n1,one\n2,two\n3,three\n");

        await using var builder = await EtlInputBatchBuilder.OpenAsync(files.SnapshotRoot, pipeline, prepared);
        var result = await builder.BuildNextAsync(partitionIndex: 0, nextRecordOrdinal: 1);

        Assert.True(result.Succeeded, result.Diagnostic?.ToString());
        Assert.Equal(Encoding.UTF8.GetByteCount(expectedPayload), result.Batch!.CanonicalInputBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expectedPayload))).ToLowerInvariant(),
            result.Batch.ContentHash);
    }

    [Fact]
    public async Task Stops_at_the_record_limit_and_marks_the_partition_end_without_an_empty_batch()
    {
        var pipeline = CreatePipeline(maxBatchRecords: 1, maxBatchBytes: 4096);
        using var files = new TestFiles();
        var prepared = await files.PrepareAsync(pipeline, "id,name\n1,A\n2,B\n3,C\n", "id,label\n1,one\n2,two\n3,three\n");

        await using var builder = await EtlInputBatchBuilder.OpenAsync(files.SnapshotRoot, pipeline, prepared);
        var first = await builder.BuildNextAsync(partitionIndex: 0, nextRecordOrdinal: 1);
        var second = await builder.BuildNextAsync(partitionIndex: 0, nextRecordOrdinal: 2);
        var complete = await builder.BuildNextAsync(partitionIndex: 0, nextRecordOrdinal: 3);

        Assert.True(first.Succeeded, first.Diagnostic?.ToString());
        Assert.True(second.Succeeded, second.Diagnostic?.ToString());
        Assert.Equal(1, first.Batch!.StartRecordOrdinal);
        Assert.Equal(2, first.Batch.EndRecordOrdinalExclusive);
        Assert.Equal(2, second.Batch!.StartRecordOrdinal);
        Assert.Equal(3, second.Batch.EndRecordOrdinalExclusive);
        Assert.True(complete.Succeeded);
        Assert.Null(complete.Batch);
    }

    [Fact]
    public async Task Rejects_one_row_that_cannot_fit_the_canonical_input_limit_without_a_batch()
    {
        var pipeline = CreatePipeline(maxBatchRecords: 10, maxBatchBytes: 8);
        using var files = new TestFiles();
        var prepared = await files.PrepareAsync(pipeline, "id,name\n1,A\n", "id,label\n1,one\n");

        await using var builder = await EtlInputBatchBuilder.OpenAsync(files.SnapshotRoot, pipeline, prepared);
        var result = await builder.BuildNextAsync(partitionIndex: 0, nextRecordOrdinal: 1);

        Assert.False(result.Succeeded);
        Assert.Null(result.Batch);
        Assert.Equal("etl.batch-record-too-large", result.Diagnostic!.Code);
        Assert.Equal(1, result.Diagnostic.RecordOrdinal);
    }

    [Fact]
    public async Task Rebuilds_a_saved_range_with_the_same_measurement_and_refuses_a_changed_identity()
    {
        var pipeline = CreatePipeline(maxBatchRecords: 10, maxBatchBytes: 4096);
        using var files = new TestFiles();
        var prepared = await files.PrepareAsync(pipeline, "id,name\n1,A\n2,B\n3,C\n", "id,label\n1,one\n2,two\n3,three\n");
        EtlInputBatch saved;
        await using (var builder = await EtlInputBatchBuilder.OpenAsync(files.SnapshotRoot, pipeline, prepared))
        {
            var result = await builder.BuildNextAsync(0, 1);
            Assert.True(result.Succeeded, result.Diagnostic?.ToString());
            saved = result.Batch!;
        }

        await using var restarted = await EtlInputBatchBuilder.OpenAsync(files.SnapshotRoot, pipeline, prepared);
        var replay = await restarted.RebuildAsync(
            saved.PartitionIndex,
            saved.StartRecordOrdinal,
            saved.EndRecordOrdinalExclusive,
            saved.CanonicalInputBytes,
            saved.ContentHash);

        Assert.True(replay.Succeeded, replay.Diagnostic?.ToString());
        Assert.Equal(saved.StartRecordOrdinal, replay.Batch!.StartRecordOrdinal);
        Assert.Equal(saved.EndRecordOrdinalExclusive, replay.Batch.EndRecordOrdinalExclusive);
        Assert.Equal(saved.CanonicalInputBytes, replay.Batch.CanonicalInputBytes);
        Assert.Equal(saved.ContentHash, replay.Batch.ContentHash);
        Assert.Equal(saved.Records.Select(record => record.Fields), replay.Batch.Records.Select(record => record.Fields));

        var mismatch = await restarted.RebuildAsync(
            saved.PartitionIndex,
            saved.StartRecordOrdinal,
            saved.EndRecordOrdinalExclusive,
            saved.CanonicalInputBytes + 1,
            saved.ContentHash);
        Assert.Equal("etl.batch-reconstruction-mismatch", mismatch.Diagnostic!.Code);
        Assert.DoesNotContain("A", mismatch.Diagnostic.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Refuses_a_corrupted_source_or_index_before_returning_rows(bool corruptSource)
    {
        var pipeline = CreatePipeline(maxBatchRecords: 10, maxBatchBytes: 4096);
        using var files = new TestFiles();
        var prepared = await files.PrepareAsync(pipeline, "id,name\n1,A\n2,B\n", "id,label\n1,one\n2,two\n");
        var chunk = corruptSource ? prepared.Inputs.SourceChunk : prepared.SourceIndex;
        var bytes = await File.ReadAllBytesAsync(Path.Combine(files.SnapshotRoot, chunk.Key));
        bytes[0] ^= 0x01;
        await File.WriteAllBytesAsync(Path.Combine(files.SnapshotRoot, chunk.Key), bytes);

        var exception = await Assert.ThrowsAsync<EtlInputBatchBuilderException>(async () =>
            await EtlInputBatchBuilder.OpenAsync(files.SnapshotRoot, pipeline, prepared));

        Assert.Equal("etl.snapshot-integrity-failed", exception.Diagnostic.Code);
        Assert.DoesNotContain(files.SnapshotRoot, exception.Diagnostic.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_index_slots_with_inconsistent_offsets_even_if_the_index_hash_matches()
    {
        var pipeline = CreatePipeline(maxBatchRecords: 10, maxBatchBytes: 4096);
        using var files = new TestFiles();
        var prepared = await files.PrepareAsync(pipeline, "id,name\n1,A\n2,B\n", "id,label\n1,one\n2,two\n");
        var indexPath = Path.Combine(files.SnapshotRoot, prepared.SourceIndex.Key);
        var indexBytes = await File.ReadAllBytesAsync(indexPath);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(indexBytes.AsSpan(24 + 8, 8), 0);
        await File.WriteAllBytesAsync(indexPath, indexBytes);
        var descriptor = prepared with
        {
            SourceIndex = prepared.SourceIndex with
            {
                ContentHash = Convert.ToHexString(SHA256.HashData(indexBytes)).ToLowerInvariant()
            }
        };

        await using var builder = await EtlInputBatchBuilder.OpenAsync(files.SnapshotRoot, pipeline, descriptor);
        var result = await builder.BuildNextAsync(0, 1);

        Assert.False(result.Succeeded);
        Assert.Null(result.Batch);
        Assert.Equal("etl.source-index-invalid", result.Diagnostic!.Code);
        Assert.Equal(1, result.Diagnostic.RecordOrdinal);
    }

    private static EtlPipeline CreatePipeline(int maxBatchRecords, long maxBatchBytes) => new(
        new EtlFileBindings("source", "lookup", "main", "errors"),
        [new("id", EtlColumnType.Int64, Required: true), new("name", EtlColumnType.String, Required: true)],
        [new("id", EtlColumnType.Int64, Required: true), new("label", EtlColumnType.String, Required: true)],
        "id",
        "id",
        [new("id", EtlFieldOrigin.Source, "id")],
        Array.Empty<EtlFilter>(),
        new EtlLimits
        {
            PartitionCount = 2,
            MaxRecordBytes = 4096,
            MaxFields = 8,
            MaxBatchRecords = maxBatchRecords,
            MaxBatchBytes = maxBatchBytes,
            MaxLookupRecords = 100,
            MaxLookupBytes = 1024 * 1024,
            MaxSnapshotBytes = 1024 * 1024
        });

    private sealed class TestFiles : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"elsa-etl-batch-{Guid.NewGuid():N}");

        public string SnapshotRoot => Path.Combine(_root, "snapshots");

        public async Task<EtlPreparedDescriptor> PrepareAsync(EtlPipeline pipeline, string source, string lookup)
        {
            Directory.CreateDirectory(_root);
            var sourcePath = Write("source.csv", source);
            var lookupPath = Write("lookup.csv", lookup);
            var scope = new EtlScope("tenant", "partition");
            var bindings = new HostFileBindings([
                Input(scope, "source", EtlFileRole.Source, sourcePath),
                Input(scope, "lookup", EtlFileRole.Lookup, lookupPath)
            ]);
            var sourcePin = bindings.Resolve(scope, "source", EtlFileRole.Source);
            var lookupPin = bindings.Resolve(scope, "lookup", EtlFileRole.Lookup);
            var preparer = new EtlFilePreparer(bindings, SnapshotRoot);
            var snapshots = await preparer.CreateSnapshotsAsync(scope, "run-1", pipeline, sourcePin, lookupPin);
            Assert.True(snapshots.Succeeded, snapshots.Diagnostic?.ToString());
            var result = await preparer.PrepareAsync(pipeline, snapshots.Snapshots!);
            Assert.True(result.Succeeded, result.Diagnostic?.ToString());
            return result.Prepared!;
        }

        private string Write(string name, string content)
        {
            var path = Path.Combine(_root, name);
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }

        private static HostFileBinding Input(EtlScope scope, string alias, EtlFileRole role, string path) =>
            new(scope, alias, role, "v1", path, true,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), ImmutableInput: true);

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); }
            catch (DirectoryNotFoundException) { }
        }
    }
}
