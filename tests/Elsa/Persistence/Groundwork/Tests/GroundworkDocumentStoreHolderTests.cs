using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkDocumentStoreHolderTests
{
    [Fact]
    public async Task Concurrent_publication_selects_one_complete_state_and_disposes_every_owner_once()
    {
        var holder = new GroundworkDocumentStoreHolder();
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
            var won = holder.TrySet(candidate.Store, candidate.BoundedStore, candidate.Handle);
            if (!won)
                await candidate.Handle.DisposeAsync();
            return (Candidate: candidate, Won: won);
        }).ToArray();

        start.SetResult(true);
        var results = await Task.WhenAll(attempts);
        var winner = Assert.Single(results.Where(result => result.Won)).Candidate;

        Assert.Same(winner.Store, holder.Store);
        Assert.Same(winner.BoundedStore, holder.BoundedStore);
        Assert.Equal(0, winner.Handle.DisposeCount);
        Assert.All(results.Where(result => !result.Won), result =>
            Assert.Equal(1, result.Candidate.Handle.DisposeCount));

        await holder.DisposeAsync();
        await holder.DisposeAsync();

        Assert.All(candidates, candidate => Assert.Equal(1, candidate.Handle.DisposeCount));
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
