using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Documents.Store;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public class GroundworkActivityExecutionHierarchyStoreTests
{
    [Fact]
    public async Task Store_Persists_Records_And_Pages_At_A_Stable_Watermark()
    {
        var store = Store();
        await store.SaveAsync(Record("outer", "outer", null, 1, boundary: true));
        await store.SaveAsync(Record("child-a", "outer", "outer", 2));
        await store.SaveAsync(Record("child-b", "outer", "child-a", 3));

        var first = await store.ReadPageAsync(Query(limit: 1));
        Assert.Equal("child-a", Assert.Single(first!.Items).ActivityExecutionId);
        Assert.Equal(1, first.Items[0].RelativeDepth);
        await store.SaveAsync(Record("late", "outer", "outer", 10));

        var second = await store.ReadPageAsync(Query(limit: 1, cursor: first.NextCursor));
        Assert.Equal("child-b", Assert.Single(second!.Items).ActivityExecutionId);
        Assert.Equal(2, second.Items[0].RelativeDepth);
        Assert.Equal(3, second.CommittedThroughSequence);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task Store_Pages_Descendants_When_The_Completed_Root_Has_The_Latest_Sequence()
    {
        var store = Store();
        await store.SaveAsync(Record("child-a", "outer", "outer", 2));
        await store.SaveAsync(Record("child-b", "outer", "child-a", 3));
        await store.SaveAsync(Record("outer", "outer", null, 4, boundary: true));

        var page = await store.ReadPageAsync(Query(limit: 10));

        Assert.Equal(["child-a", "child-b"], page!.Items.Select(x => x.ActivityExecutionId));
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task SaveAsync_RejectsProviderVersionChangeBetweenLoadAndWrite()
    {
        var documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var seedStore = Store(documentStore);
        await seedStore.SaveAsync(Record("outer", "outer", null, 1, boundary: true));
        await seedStore.SaveAsync(Record("child-a", "outer", "outer", 2));

        var competingStore = Store(documentStore);
        var interceptingStore = new InterceptingDocumentStore(documentStore)
        {
            OnBeforeSave = async request =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind, request.DocumentKind);
                Assert.Equal(DocumentId.Compose("wf", "child-a"), request.Id);
                Assert.Equal(1, request.ExpectedVersion);
                await competingStore.SaveAsync(Record("child-a", "outer", "outer", 3));
            }
        };
        var store = Store(interceptingStore);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(Record("child-a", "outer", "outer", 4)).AsTask());

        Assert.Contains("ConcurrencyConflict", exception.Message, StringComparison.Ordinal);
        var page = await seedStore.ReadPageAsync(Query(limit: 10));
        var winner = Assert.Single(page!.Items, item => item.ActivityExecutionId == "child-a");
        Assert.Equal(3, winner.ExecutionSequence);
    }

    [Fact]
    public async Task Nested_Boundary_Aggregate_Is_Rebuilt_From_Durable_Relations()
    {
        var store = Store();
        await store.SaveAsync(Record("outer", "outer", null, 1, boundary: true));
        await store.SaveAsync(Record("nested", "outer", "outer", 2, boundary: true));
        await store.SaveAsync(Record("leaf", "nested", "nested", 3));

        var boundary = await store.FindBoundaryAsync("wf", "nested");
        Assert.NotNull(boundary);
        Assert.Equal(1, boundary.CommittedDescendantCount);
        Assert.Equal(ActivityExecutionHierarchyAggregateStatus.Completed, boundary.Aggregate.Status);
    }

    [Fact]
    public async Task Attempt_navigation_traverses_the_bounded_workflow_route()
    {
        var store = Store();
        var first = WithAttempt(Record("attempt-1", "attempt-1", null, 1), 1, "attempt-1", null);
        var second = WithAttempt(Record("attempt-2", "attempt-2", null, 2), 2, "attempt-1", "attempt-1");
        var third = WithAttempt(Record("attempt-3", "attempt-3", null, 3), 3, "attempt-1", "attempt-2");
        await store.SaveAsync(first);
        await store.SaveAsync(second);
        await store.SaveAsync(third);

        var navigation = await store.FindAttemptNavigationAsync("wf", "attempt-2");

        Assert.NotNull(navigation);
        Assert.Equal("attempt-1", navigation!.Lineage.PreviousAttemptActivityExecutionId);
        Assert.Equal("attempt-3", navigation.NextAttemptActivityExecutionId);
        Assert.Equal(3, navigation.TotalAttempts);
    }

    private static ActivityExecutionHierarchyRecord WithAttempt(
        ActivityExecutionHierarchyRecord record,
        int number,
        string firstAttemptActivityExecutionId,
        string? previousAttemptActivityExecutionId) =>
        record with
        {
            Item = record.Item with
            {
                Attempt = new(number, firstAttemptActivityExecutionId, previousAttemptActivityExecutionId)
            }
        };

    private static GroundworkActivityExecutionHierarchyStore Store(IDocumentStore? documents = null)
    {
        documents ??= new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        return new(documents, GroundworkTestSerialization.Serializer,
            new HmacActivityExecutionHierarchyCursorCodec(Options.Create(new ActivityExecutionHierarchyCursorOptions
            {
                SigningKey = "groundwork-hierarchy-key-that-is-at-least-thirty-two-bytes"
            })));
    }

    private static ActivityExecutionHierarchyQuery Query(int limit, string? cursor = null) =>
        new("wf", "outer", cursor, limit, new HashSet<ActivityExecutionHierarchyInclude>(), "structure", "tenant:a");

    private static ActivityExecutionHierarchyRecord Record(string id, string scope, string? parent, long sequence, bool boundary = false)
    {
        var metadata = boundary
            ? new Dictionary<string, string>
            {
                ["activity.definitionId"] = $"def-{id}",
                ["activity.definitionVersionId"] = $"ver-{id}",
                ["activity.version"] = "1.0.0",
                ["activity.templateHash"] = $"hash-{id}"
            }
            : new Dictionary<string, string>();
        var projection = new ActivityExecutionInspectionProjection(
            id, "wf", $"node-{id}", $"authored-{id}", boundary ? "elsa.graph-activity" : "elsa.test", "1",
            ActivityExecutionStatus.Completed, null, sequence, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, "first", "last", DateTimeOffset.UnixEpoch,
            ActivitySchedulingProvenance.From("wf", parent, parent, null, null, null, scope, "test"),
            ["Done"], [], [], [], metadata, scope, null);
        return ActivityExecutionHierarchyProjector.FromInspection(projection);
    }
}
