using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Tests.Fixtures;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class ReusableActivityRuntimeStoreTests
{
    [Fact]
    public void InvocationOrigin_Uses_LengthFramed_KindAware_FullSha256_Namespaces()
    {
        var left = new ActivityInvocationOrigin(
        [
            new(ActivityInvocationOriginSegmentKind.AuthoredNode, "ab"),
            new(ActivityInvocationOriginSegmentKind.NestedPlacement, "c")
        ]);
        var right = new ActivityInvocationOrigin(
        [
            new(ActivityInvocationOriginSegmentKind.AuthoredNode, "a"),
            new(ActivityInvocationOriginSegmentKind.NestedPlacement, "bc")
        ]);
        var otherKind = new ActivityInvocationOrigin(
        [
            new(ActivityInvocationOriginSegmentKind.WorkflowRoot, "ab"),
            new(ActivityInvocationOriginSegmentKind.NestedPlacement, "c")
        ]);

        var placementNamespace = left.ComputePlacementNamespace();

        Assert.Equal(64, placementNamespace.Length);
        Assert.Equal(placementNamespace.ToLowerInvariant(), placementNamespace);
        Assert.NotEqual(placementNamespace, right.ComputePlacementNamespace());
        Assert.NotEqual(placementNamespace, otherKind.ComputePlacementNamespace());
    }

    [Fact]
    public void ExecutableTemplate_Flattens_A_Deep_Graph_Iteratively()
    {
        const int depth = 2_000;
        var node = Node($"node-{depth - 1}");
        for (var index = depth - 2; index >= 0; index--)
            node = Node($"node-{index}", node);

        var template = Template("template-deep", "sha256:deep", node);

        Assert.Equal(depth, template.NodesById.Count);
        Assert.Equal($"node-{depth - 1}", template.NodesById[$"node-{depth - 1}"].ExecutableNodeId);
    }

    [Fact]
    public async Task TemplateStore_Indexes_Exact_Id_And_Hash_And_Rejects_Identity_Collisions()
    {
        var store = new InMemoryExecutableActivityTemplateStore();
        var template = Template("template-1", "sha256:one", Node("node-1"));

        await store.SaveAsync(template);

        Assert.Same(template, await store.FindAsync("template-1"));
        Assert.Same(template, await store.FindByHashAsync("sha256:one"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.SaveAsync(Template("template-1", "sha256:other", Node("node-2"))));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.SaveAsync(Template("template-other", "sha256:one", Node("node-3"))));
    }

    [Fact]
    public async Task TemplateStore_Allows_Exact_Resave_But_Rejects_Same_Identity_With_Different_Content()
    {
        var store = new InMemoryExecutableActivityTemplateStore();
        var template = Template("template-1", "sha256:one", Node("node-1"));

        await store.SaveAsync(template);
        await store.SaveAsync(Template(
            "template-1",
            "sha256:one",
            Node("node-1"),
            DateTimeOffset.UnixEpoch.AddMinutes(1)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.SaveAsync(Template("template-1", "sha256:one", Node("different-node"))));
        Assert.Contains("different content", exception.Message, StringComparison.Ordinal);
        Assert.Same(template, await store.FindAsync("template-1"));
    }

    [Fact]
    public async Task TemplateStore_Reads_Finite_Ordered_Pages()
    {
        var store = new InMemoryExecutableActivityTemplateStore();
        await store.SaveAsync(Template("template-c", "sha256:c", Node("node-c")));
        await store.SaveAsync(Template("template-a", "sha256:a", Node("node-a")));
        await store.SaveAsync(Template("template-b", "sha256:b", Node("node-b")));

        var first = await store.ListPageAsync(new RuntimeStorePageRequest(limit: 2));
        var second = await store.ListPageAsync(new RuntimeStorePageRequest(limit: 2, first.NextContinuationToken));

        Assert.Equal(["template-a", "template-b"], first.Items.Select(template => template.TemplateId));
        Assert.NotNull(first.NextContinuationToken);
        Assert.Equal(["template-c"], second.Items.Select(template => template.TemplateId));
        Assert.Null(second.NextContinuationToken);
    }

    [Fact]
    public async Task HierarchyStore_Pins_The_FirstPage_Watermark_Across_Continuation()
    {
        var store = new InMemoryActivityExecutionHierarchyStore();
        store.SaveRoot(new ActivityExecutionHierarchyRoot("wf-1", "scope-1", "scope-1", "version-1", "sha256:template"));
        await store.SaveAsync(Record(1));
        await store.SaveAsync(Record(2));
        await store.SaveAsync(Record(3));

        var first = await store.ReadPageAsync(Query(limit: 2));
        Assert.NotNull(first);
        Assert.Equal(3, first!.CommittedThroughSequence);
        Assert.Equal(["activity-1", "activity-2"], first.Items.Select(item => item.ActivityExecutionId));
        Assert.NotNull(first.NextCursor);

        await store.SaveAsync(Record(4));
        var continuation = await store.ReadPageAsync(Query(limit: 2, cursor: first.NextCursor));

        Assert.NotNull(continuation);
        Assert.Equal(3, continuation!.CommittedThroughSequence);
        Assert.Equal(["activity-3"], continuation.Items.Select(item => item.ActivityExecutionId));
        Assert.Null(continuation.NextCursor);

        var fresh = await store.ReadPageAsync(Query(limit: 10));
        Assert.Equal(4, fresh!.CommittedThroughSequence);
        Assert.Equal(4, fresh.Items.Count);
    }

    [Fact]
    public async Task HierarchyStore_Binds_Cursors_To_QueryShape_And_Reads_Pinned_Layout()
    {
        var store = new InMemoryActivityExecutionHierarchyStore();
        store.SaveRoot(new ActivityExecutionHierarchyRoot("wf-1", "scope-1", "scope-1", "version-1", "sha256:template"));
        await store.SaveAsync(Record(1));
        await store.SaveAsync(Record(2));
        var first = await store.ReadPageAsync(Query(limit: 1));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.ReadPageAsync(Query(limit: 1, cursor: first!.NextCursor, authorizationProfile: "values")));

        var layout = new ActivityExecutionLayout(
            "wf-1",
            "scope-1",
            "artifact-1",
            "source-reference-1",
            ActivityExecutionLayoutSelection.ExecutedReference,
            ActivityInvocationOrigin.Empty,
            "sha256:template",
            [],
            [],
            []);
        store.SaveLayout(layout);

        Assert.Same(layout, await store.FindLayoutAsync("wf-1", "scope-1"));
    }

    private static ActivityExecutionHierarchyQuery Query(
        int limit,
        string? cursor = null,
        string authorizationProfile = "structure") =>
        new(
            "wf-1",
            "scope-1",
            cursor,
            limit,
            new HashSet<ActivityExecutionHierarchyInclude> { ActivityExecutionHierarchyInclude.Outcomes },
            authorizationProfile);

    private static ActivityExecutionHierarchyRecord Record(long sequence)
    {
        var id = $"activity-{sequence}";
        var item = new ActivityExecutionHierarchyItem(
            id,
            "wf-1",
            $"node-{sequence}",
            $"authored-{sequence}",
            "test.activity",
            "1",
            ActivityExecutionStatus.Completed,
            null,
            sequence,
            DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            sequence == 1 ? "scope-1" : $"activity-{sequence - 1}",
            sequence == 1 ? "scope-1" : $"activity-{sequence - 1}",
            (int)sequence,
            null,
            null,
            ["Done"],
            0,
            0,
            0,
            null,
            null,
            new Dictionary<string, string>());
        return new ActivityExecutionHierarchyRecord("wf-1", "scope-1", id, item.ParentActivityExecutionId, sequence, item);
    }

    private static ExecutableActivityTemplate Template(
        string id,
        string hash,
        ExecutableNode root,
        DateTimeOffset? createdAt = null) => new(
        id,
        hash,
        root,
        new Dictionary<string, WorkflowExecutableResumeTarget>(),
        [],
        [],
        [new RuntimeRequirement("test.consumer", "1")],
        "provider/1",
        new Dictionary<string, string>(),
        createdAt ?? DateTimeOffset.UnixEpoch);

    private static ExecutableNode Node(string id, ExecutableNode? child = null) => new(
        id,
        $"authored-{id}",
        "test.activity",
        "1",
        new RuntimeActivityDescriptor("test.consumer", "1", JsonSerializer.SerializeToElement(new { id })),
        new Dictionary<string, RuntimeInputBinding>(),
        new Dictionary<string, RuntimeOutputCapture>(),
        new Dictionary<string, string>(),
        child is null ? [] : [new ExecutableChildSlot("Body", [child])]);
}
