using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeDurableValueStateStoreTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InMemoryDurableValueStateStore_SavesFindsPagesAndDeletesByWorkflowExecution()
    {
        var store = new InMemoryDurableValueStateStore();
        var first = NewDurableValue("durable-1", "wfexec-1", "customer");
        var second = NewDurableValue("durable-2", "wfexec-1", "order");
        var otherWorkflow = NewDurableValue("durable-1", "wfexec-2", "customer");

        await store.SaveAsync(first);
        await store.SaveAsync(second);
        await store.SaveAsync(otherWorkflow);

        Assert.Equal(first, await store.FindAsync("wfexec-1", "durable-1"));
        var firstPage = await store.ListPageAsync(new DurableValueStatePageQuery("wfexec-1", limit: 1));
        var secondPage = await store.ListPageAsync(new DurableValueStatePageQuery("wfexec-1", limit: 1, firstPage.NextContinuationToken));
        Assert.Equal("durable-1", Assert.Single(firstPage.Items).DurableValueId);
        Assert.Equal("durable-2", Assert.Single(secondPage.Items).DurableValueId);
        Assert.Null(secondPage.NextContinuationToken);
        Assert.Equal(2, (await store.ListAllDurableValueStatesAsync("wfexec-1")).Count);

        Assert.True(await store.DeleteAsync("wfexec-1", "durable-1"));
        Assert.Null(await store.FindAsync("wfexec-1", "durable-1"));
        Assert.NotNull(await store.FindAsync("wfexec-2", "durable-1"));
        Assert.False(await store.DeleteAsync("wfexec-1", "durable-1"));
    }

    private DurableValueState NewDurableValue(string durableValueId, string workflowExecutionId, string valueId) =>
        new(
            durableValueId: durableValueId,
            workflowExecutionId: workflowExecutionId,
            valueId: valueId,
            type: new RuntimeValueTypeDescriptor("reference", "crm.customer", null),
            lifecycle: DurableValueLifecycle.Instance,
            storage: DurableValueStorage.Inline,
            inlineValue: Json("""{"id":"customer-1"}"""),
            externalReference: null,
            sourceActivityExecutionId: "actexec-1",
            capturedAt: _now,
            metadata: new Dictionary<string, string>());

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
