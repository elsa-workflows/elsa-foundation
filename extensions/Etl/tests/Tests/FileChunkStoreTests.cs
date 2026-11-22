using System.Text;
using System.Security.Cryptography;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Core.Runtime;

namespace Elsa.Etl.Tests;

public sealed class FileChunkStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "elsa-etl-chunks-" + Guid.NewGuid().ToString("N"));
    private readonly FileChunkStore _store = new();

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
