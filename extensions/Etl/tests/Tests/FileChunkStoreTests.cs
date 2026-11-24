using System.Text;
using System.Security.Cryptography;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Core.Runtime;

namespace Elsa.Etl.Tests;

public sealed class FileChunkStoreTests : IDisposable
{
    // Fixed digests independently generated from these literal canonical JSON values.
    [Theory]
    [InlineData("""{"OwnerKey":"René","n":9007199254740993,"rows":[1,null]}""", "4ad5669c9e2c2d62b13796dbc77609fbe0cbf90d7350123ad5d6409b94e1b91f")]
    [InlineData(""" { "rows": [1e0,null], "n":9007199254740993.0,"OwnerKey":"Ren\u00e9" } """, "4ad5669c9e2c2d62b13796dbc77609fbe0cbf90d7350123ad5d6409b94e1b91f")]
    [InlineData("""{"OwnerKey":"René","n":9007199254740993}""", "aaa637899be4a75616df0a78eb3c8c9798349e57095043d932e117ee654e8c4c")]
    [InlineData("""{"OwnerKey":"René","n":9007199254740993,"rows":null}""", "d27f0ae7fbcebdf5018ac5f421bcf1a7d9f1b006a4fd41df36d51e818fb828cc")]
    [InlineData("""{"OwnerKey":"René","n":9007199254740993,"rows":[null,1]}""", "7c885ecab394c5329fe59ec3d1f437035c7ab30dcb26ed2cb9a69fd1d4149ac7")]
    [InlineData("""{"OwnerKey":"René","n":9007199254740992,"rows":[1,null]}""", "999da9c38e8633d9bdc022381ed82ff9640e0b8d71de94c3f859ec6fb0b7fb9d")]
    public async Task Actual_json_descriptor_writer_and_reader_pin_canonical_hash_and_retained_bytes(string raw, string expected)
    {
        var store = new FileChunkStore(".json");
        var bytes = Encoding.UTF8.GetBytes(raw);
        var published = await store.PublishAsync(_root, "golden-json", 4096, (stream, ct) => stream.WriteAsync(bytes, ct));
        var found = await store.FindAsync(_root, "golden-json", 4096);
        Assert.True(published.ContentHash == expected && found?.ContentHash == expected,
            $"FileChunkStore JSON: First-stable hash freeze requires an explicit identity/migration decision. Expected {expected}; published {published.ContentHash}; read {found?.ContentHash}");
        Assert.True(bytes.Length == published.ByteLength, "FileChunkStore JSON physical length: First-stable hash freeze requires an explicit identity/migration decision.");
        Assert.True(bytes.SequenceEqual(await File.ReadAllBytesAsync(Path.Combine(_root, published.Key))),
            "FileChunkStore JSON retained bytes: First-stable hash freeze requires an explicit identity/migration decision.");
    }

    [Fact]
    public async Task Actual_json_descriptor_refuses_malformed_retained_data()
    {
        var store = new FileChunkStore(".json");
        var error = await Assert.ThrowsAsync<EtlEffectException>(() => store.PublishAsync(_root, "bad-json", 4096,
            (stream, ct) => stream.WriteAsync("{"u8.ToArray(), ct)).AsTask());
        Assert.True(error.Code == "etl.effect-conflict", "FileChunkStore JSON refusal: First-stable hash freeze requires an explicit identity/migration decision.");
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "elsa-etl-chunks-" + Guid.NewGuid().ToString("N"));
    private readonly FileChunkStore _store = new();

    [Fact]
    public async Task Json_hashes_values_but_immutable_publication_still_refuses_different_physical_bytes()
    {
        var store = new FileChunkStore(".json");
        var firstBytes = "{\"a\":1,\"b\":2}"u8.ToArray();
        var secondBytes = "{\"b\":2,\"a\":1}"u8.ToArray();
        var first = await store.PublishAsync(_root, "first", 1024, (stream, ct) => stream.WriteAsync(firstBytes, ct));
        var second = await store.PublishAsync(_root, "second", 1024, (stream, ct) => stream.WriteAsync(secondBytes, ct));
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(first.ByteLength, second.ByteLength);
        Assert.Equal(firstBytes, await File.ReadAllBytesAsync(Path.Combine(_root, first.Key)));
        Assert.Equal(secondBytes, await File.ReadAllBytesAsync(Path.Combine(_root, second.Key)));
        var error = await Assert.ThrowsAsync<EtlEffectException>(() => store.PublishAsync(_root, "first", 1024,
            (stream, ct) => stream.WriteAsync(secondBytes, ct)).AsTask());
        Assert.Equal("etl.effect-conflict", error.Code);
        Assert.Equal(firstBytes, await File.ReadAllBytesAsync(Path.Combine(_root, first.Key)));
    }

    [Fact]
    public async Task Connector_publication_key_matches_real_immutable_file_publication()
    {
        var effectId = Convert.ToHexStringLower(SHA256.HashData("connector-effect"u8));
        var bytes = "{\"value\":1}\n"u8.ToArray();
        var published = await _store.PublishAsync(_root, effectId, 1024, (stream, ct) => stream.WriteAsync(bytes, ct));
        Assert.Equal(EtlRuntimeIdentity.ConnectorFileOutputKey(effectId), published.Key);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_root, published.Key)));
    }

    [Fact]
    public async Task Publishes_complete_hashed_chunk_and_replays_without_overwriting()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"name\":\"雪\"}\n");
        var first = await WriteAsync(bytes);
        var originalTime = File.GetLastWriteTimeUtc(Path.Combine(_root, first.Key));
        var replay = await WriteAsync(bytes);

        Assert.Equal(first, replay);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_root, first.Key)));
        Assert.Equal(bytes.Length, first.ByteLength);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), first.ContentHash);
        Assert.Equal(originalTime, File.GetLastWriteTimeUtc(Path.Combine(_root, replay.Key)));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task A_conflicting_effect_cannot_replace_the_original_chunk()
    {
        var first = await WriteAsync("original\n"u8.ToArray());
        var error = await Assert.ThrowsAsync<EtlEffectException>(async () => await WriteAsync("different\n"u8.ToArray()));

        Assert.Equal("etl.effect-conflict", error.Code);
        Assert.Equal("original\n", await File.ReadAllTextAsync(Path.Combine(_root, first.Key)));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task Staged_data_is_invisible_until_writer_finishes_and_midwrite_failure_leaves_no_final_chunk()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new IOException("fixture failed after a partial write");
        var publish = _store.PublishAsync(_root, "run/partition/batch/main", 1024, async (stream, ct) =>
        {
            await stream.WriteAsync("partial"u8.ToArray(), ct);
            started.SetResult();
            await finish.Task.WaitAsync(ct);
            throw expected;
        }).AsTask();

        EtlEffectException? error = null;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Empty(Directory.GetFiles(_root, "*.jsonl"));
        }
        finally
        {
            finish.TrySetResult();
            error = await Assert.ThrowsAsync<EtlEffectException>(() => publish);
        }
        Assert.NotNull(error);
        Assert.Equal("etl.output-write-failed", error.Code);
        Assert.Same(expected, error.InnerException);
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task Checks_output_byte_budget_before_each_write_and_does_not_publish_partial_data()
    {
        var error = await Assert.ThrowsAsync<EtlEffectException>(async () =>
            await _store.PublishAsync(_root, "bounded", 3, async (stream, ct) =>
            {
                await stream.WriteAsync("ab"u8.ToArray(), ct);
                await stream.WriteAsync("cd"u8.ToArray(), ct);
            }));

        Assert.Equal("etl.output-too-large", error.Code);
        Assert.Empty(Directory.GetFiles(_root));
        var exact = await _store.PublishAsync(_root, "bounded", 3, (stream, ct) => stream.WriteAsync("abc"u8.ToArray(), ct));
        Assert.Equal(3, exact.ByteLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Concurrent_identical_writers_converge_and_conflicting_writers_have_one_winner(int round)
    {
        async Task<(EtlChunk? Chunk, EtlEffectException? Error)> Compete(string identity, string text, Barrier barrier)
        {
            try
            {
                var chunk = await _store.PublishAsync(_root, identity, 1024, async (stream, ct) =>
                {
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(text), ct);
                    Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30), ct));
                });
                return (chunk, null);
            }
            catch (EtlEffectException error)
            {
                return (null, error);
            }
        }

        using var same = new Barrier(2);
        var identical = await Task.WhenAll(Task.Run(() => Compete($"same-{round}", "value", same)), Task.Run(() => Compete($"same-{round}", "value", same)));
        Assert.All(identical, x => Assert.Null(x.Error));
        Assert.Equal(identical[0].Chunk, identical[1].Chunk);

        using var different = new Barrier(2);
        var conflict = await Task.WhenAll(Task.Run(() => Compete($"different-{round}", "one", different)), Task.Run(() => Compete($"different-{round}", "two", different)));
        Assert.Single(conflict, x => x.Chunk is not null);
        Assert.Equal("etl.effect-conflict", Assert.Single(conflict, x => x.Error is not null).Error!.Code);
        Assert.Equal(2, Directory.GetFiles(_root).Length);
    }

    [Fact]
    public async Task Cancellation_unwinds_its_writer_and_cleans_only_its_staging_file()
    {
        Directory.CreateDirectory(_root);
        var unrelated = Path.Combine(_root, ".another-attempt.staging");
        await File.WriteAllTextAsync(unrelated, "owned elsewhere");
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await _store.PublishAsync(_root, "cancelled", 1024, async (stream, ct) =>
            {
                await stream.WriteAsync("partial"u8.ToArray(), ct);
                cancellation.Cancel();
                ct.ThrowIfCancellationRequested();
            }, cancellation.Token));

        Assert.Equal(new[] { unrelated }, Directory.GetFiles(_root));
    }

    [Fact]
    public async Task Existing_oversized_or_corrupted_chunk_is_refused_without_reading_unbounded_content()
    {
        var first = await WriteAsync("ok"u8.ToArray());
        await File.WriteAllTextAsync(Path.Combine(_root, first.Key), "oversized physical effect");
        var error = await Assert.ThrowsAsync<EtlEffectException>(async () =>
            await _store.PublishAsync(_root, "effect", 2, (stream, ct) => stream.WriteAsync("ok"u8.ToArray(), ct)));
        Assert.Equal("etl.effect-conflict", error.Code);
    }

    private ValueTask<EtlChunk> WriteAsync(byte[] bytes) => _store.PublishAsync(_root, "effect", 1024,
        (stream, ct) => stream.WriteAsync(bytes, ct));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
