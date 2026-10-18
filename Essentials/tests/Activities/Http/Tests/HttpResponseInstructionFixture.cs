using System.Text.Json;
using Elsa.Activities.Http.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;

namespace Elsa.Activities.Http.Tests;

internal static class HttpResponseInstructionFixture
{
    internal static async Task<InMemoryActivityExecutionStateStore> StoreWith(HttpResponseInstruction instruction)
    {
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        var invocationId = "activity-write-response";
        var valueType = new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(HttpResponseInstruction)));
        var result = ValueEnvelope.Inline(
            valueType,
            JsonSerializer.SerializeToElement(instruction, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ValueProtectionPolicy.InstanceInline);
        var completion = new ActivityCompletion(invocationId, "attempt-1", result, "Done", now, "sha256:test");
        return await StoreWithCompletion(completion);
    }

    internal static async Task<InMemoryActivityExecutionStateStore> StoreWithCompletion(ActivityCompletion completion)
    {
        var now = completion.CompletedAt;
        var invocationId = completion.InvocationId;
        var execution = new ActivityExecution(invocationId, "wf-1", "node-write-response", "authored-write-response", "Elsa.WriteHttpResponse", "1.0.0");
        var state = new ActivityExecutionState(
            execution,
            ActivityExecutionStatus.Completed,
            SubStatus: null,
            ScheduledAt: now,
            StartedAt: now,
            CompletedAt: now,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            CallStackDepth: null,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>())
        {
            Completion = completion
        };

        var store = new InMemoryActivityExecutionStateStore();
        await store.SaveAsync(state);
        return store;
    }

}
