using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// RT-11: the CompleteActivity payload is parsed once per work item and reused across every inspection point (routing
/// selector, both handlers' CanHandle, and the winning handler's body) instead of being deserialized up to four times.
/// </summary>
public sealed class RuntimeCompleteActivityPayloadMemoTests : RuntimePipelineTestSupport
{
    [Fact]
    public void Deserialize_ReturnsTheSameInstance_ForRepeatedReadsOfTheSameWorkItem()
    {
        var workItem = NewCompleteActivityWorkItem();

        var first = RuntimeCompleteActivityPayloadMemo.Deserialize(workItem);
        var second = RuntimeCompleteActivityPayloadMemo.Deserialize(workItem);

        Assert.NotNull(first);
        // Same instance => the second read reused the memo rather than deserializing the payload again.
        Assert.Same(first, second);
    }

    [Fact]
    public void Deserialize_ReturnsDistinctInstances_ForDistinctWorkItems()
    {
        var a = NewCompleteActivityWorkItem(index: 1);
        var b = NewCompleteActivityWorkItem(index: 2);

        var parsedA = RuntimeCompleteActivityPayloadMemo.Deserialize(a);
        var parsedB = RuntimeCompleteActivityPayloadMemo.Deserialize(b);

        Assert.NotNull(parsedA);
        Assert.NotNull(parsedB);
        Assert.NotSame(parsedA, parsedB);
    }

    [Fact]
    public void Deserialize_ReturnsNull_WhenTheWorkItemCarriesNoPayload()
    {
        var workItem = NewWorkItem(WorkflowExecutionCommandKind.CompleteActivity, payload: JsonDocument.Parse("null").RootElement.Clone());

        // A JSON null payload deserializes to null and is not cached; the caller applies its own null semantics.
        Assert.Null(RuntimeCompleteActivityPayloadMemo.Deserialize(workItem));
    }

    [Fact]
    public void Deserialize_PropagatesTheParseException_ForAMalformedPayload()
    {
        // A payload shaped like the completion command but missing required members fails the record's validation; the
        // memo lets that throw (never caching it) so each call site keeps its own catch semantics.
        var malformed = JsonSerializer.SerializeToElement(new { completionKind = SchedulerCompletionKind.ParentCompletionEvaluation });
        var workItem = NewWorkItem(WorkflowExecutionCommandKind.CompleteActivity, payload: malformed);

        Assert.ThrowsAny<Exception>(() => RuntimeCompleteActivityPayloadMemo.Deserialize(workItem));
    }

    private RuntimeSchedulerWorkItem NewCompleteActivityWorkItem(int index = 1)
    {
        var payload = new RuntimeCompleteActivityCommandPayload(
            pinnedExecutable: NewIdentity(),
            executableNodeId: "node-start",
            activityExecutionId: "actexec-parent",
            parentActivityExecutionId: null,
            branchId: null,
            outcomeNames: ["Done"],
            reason: RuntimeCompleteActivityCommandPayload.ParentCompletionEvaluationReason,
            completionKind: SchedulerCompletionKind.ParentCompletionEvaluation,
            completedChildActivityExecutionId: "actexec-child");

        return NewWorkItem(WorkflowExecutionCommandKind.CompleteActivity, index: index, payload: JsonSerializer.SerializeToElement(payload));
    }
}
