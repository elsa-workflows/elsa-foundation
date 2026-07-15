using Groundwork.Documents.Store;
using Elsa.Persistence.Groundwork.Scoping;
using Groundwork.Documents.Scoping;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkStoreSessionSourceTests
{
    [Fact]
    public async Task Concurrent_publication_selects_one_complete_state_and_disposes_every_owner_once()
    {
        var source = new GroundworkStoreSessionSource();
        var candidates = Enumerable.Range(0, 32)
            .Select(_ => new Candidate(
                new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create()),
                new GroundworkBoundedDocumentStoreRouter([]),
                new RecordingHandle()))
            .ToArray();
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = candidates.Select(async candidate =>
        {
            await start.Task;
            var won = source.TrySet(
                (access, _) => ValueTask.FromResult(new GroundworkStoreSessionResources(
                    candidate.Store,
                    candidate.BoundedStore)),
                candidate.Handle);
            if (!won)
                await candidate.Handle.DisposeAsync();
            return (Candidate: candidate, Won: won);
        }).ToArray();

        start.SetResult(true);
        var results = await Task.WhenAll(attempts);
        var winner = Assert.Single(results.Where(result => result.Won)).Candidate;

        var opened = await source.OpenAsync(DocumentStoreAccess.Global);
        Assert.Same(winner.Store, opened.DocumentStore);
        Assert.Same(winner.BoundedStore, opened.BoundedDocumentStore);
        Assert.Equal(0, winner.Handle.DisposeCount);
        Assert.All(results.Where(result => !result.Won), result =>
            Assert.Equal(1, result.Candidate.Handle.DisposeCount));

        await source.DisposeAsync();
        await source.DisposeAsync();

        Assert.All(candidates, candidate => Assert.Equal(1, candidate.Handle.DisposeCount));
    }

    [Fact]
    public async Task Disposal_waits_for_an_inflight_open_and_rejects_every_later_open()
    {
        var source = new GroundworkStoreSessionSource();
        var handle = new RecordingHandle();
        var openStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var boundedStore = new GroundworkBoundedDocumentStoreRouter([]);
        Assert.True(source.TrySet(
            async (_, _) =>
            {
                openStarted.SetResult();
                await releaseOpen.Task;
                return new GroundworkStoreSessionResources(store, boundedStore);
            },
            handle));

        var opening = source.OpenAsync(DocumentStoreAccess.Global).AsTask();
        await openStarted.Task;

        var disposing = source.DisposeAsync().AsTask();
        Assert.False(disposing.IsCompleted);
        Assert.Equal(0, handle.DisposeCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            source.OpenAsync(DocumentStoreAccess.Global).AsTask());

        releaseOpen.SetResult();
        var opened = await opening;
        await disposing;

        Assert.Same(store, opened.DocumentStore);
        Assert.Equal(1, handle.DisposeCount);
        await source.DisposeAsync();
        Assert.Equal(1, handle.DisposeCount);
    }

    [Fact]
    public async Task Disposed_uninitialized_source_rejects_publication_and_open()
    {
        var source = new GroundworkStoreSessionSource();
        await source.DisposeAsync();

        Assert.False(source.TrySet((_, _) => throw new InvalidOperationException("must not run")));
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            source.OpenAsync(DocumentStoreAccess.Global).AsTask());
    }

    private sealed record Candidate(
        IDocumentStore Store,
        IBoundedDocumentStore BoundedStore,
        RecordingHandle Handle);

    private sealed class RecordingHandle : IAsyncDisposable
    {
        private int disposeCount;

        public int DisposeCount => Volatile.Read(ref disposeCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref disposeCount);
            return ValueTask.CompletedTask;
        }
    }
}
