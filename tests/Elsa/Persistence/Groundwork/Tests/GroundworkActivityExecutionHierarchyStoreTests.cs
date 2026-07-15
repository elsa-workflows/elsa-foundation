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

    private static GroundworkActivityExecutionHierarchyStore Store()
    {
        var documents = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
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
