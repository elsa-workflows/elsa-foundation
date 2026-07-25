using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkBoundedDocumentMutationStoreRouterTests
{
    [Fact]
    public async Task Execute_routes_the_exact_mutation_to_its_document_kind_runtime()
    {
        var expected = new RecordingMutationStore(BoundedMutationResult.Completed(3));
        var other = new RecordingMutationStore(BoundedMutationResult.Completed(1));
        var router = new GroundworkBoundedDocumentMutationStoreRouter(
        [
            KeyValuePair.Create<string, IBoundedDocumentMutationStore>("expected", expected),
            KeyValuePair.Create<string, IBoundedDocumentMutationStore>("other", other)
        ]);
        var mutation = Mutation("expected");

        var result = await router.ExecuteAsync(mutation);

        Assert.Same(mutation, expected.ObservedMutation);
        Assert.Null(other.ObservedMutation);
        Assert.Equal(BoundedMutationStatus.Completed, result.Status);
        Assert.Equal(3, result.AffectedCount);
    }

    [Fact]
    public async Task Lazy_router_materializes_only_the_selected_document_kind_once()
    {
        var expectedCreated = 0;
        var otherCreated = 0;
        var expected = new RecordingMutationStore(BoundedMutationResult.Completed(1));
        var router = GroundworkBoundedDocumentMutationStoreRouter.CreateLazy(
        [
            KeyValuePair.Create<string, Func<IBoundedDocumentMutationStore>>("expected", () =>
            {
                expectedCreated++;
                return expected;
            }),
            KeyValuePair.Create<string, Func<IBoundedDocumentMutationStore>>("other", () =>
            {
                otherCreated++;
                return new RecordingMutationStore(BoundedMutationResult.Completed(1));
            })
        ]);

        await router.ExecuteAsync(Mutation("expected"));
        await router.ExecuteAsync(Mutation("expected"));

        Assert.Equal(1, expectedCreated);
        Assert.Equal(0, otherCreated);
    }

    [Fact]
    public async Task Execute_fails_closed_for_an_unknown_document_kind()
    {
        var router = new GroundworkBoundedDocumentMutationStoreRouter([]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            router.ExecuteAsync(Mutation("unknown")));

        Assert.Contains("unknown", exception.Message, StringComparison.Ordinal);
    }

    private static DocumentMutation Mutation(string documentKind) => new(
        documentKind,
        "revoke",
        "operation-1",
        [DocumentQueryClause.Of(DocumentQueryComparison.Equal("subject", "alice"))]);

    private sealed class RecordingMutationStore(BoundedMutationResult result) : IBoundedDocumentMutationStore
    {
        public DocumentMutation? ObservedMutation { get; private set; }

        public Task<BoundedMutationResult> ExecuteAsync(
            DocumentMutation mutation,
            CancellationToken cancellationToken = default)
        {
            ObservedMutation = mutation;
            return Task.FromResult(result);
        }
    }
}
