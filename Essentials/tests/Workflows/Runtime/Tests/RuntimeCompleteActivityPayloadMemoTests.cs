using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
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

    [Fact]
    public void ParentOutcomePayload_RoundTripsVersionedFaultAndOrderedNotificationBatch()
    {
        var payload = NewParentOutcomePayload();

        var json = JsonSerializer.SerializeToElement(payload);
        var deserialized = json.Deserialize<RuntimeCompleteActivityCommandPayload>();

        Assert.NotNull(deserialized);
        Assert.Equal(SchedulerCompletionKind.ParentOutcomeEvaluation, deserialized.CompletionKind);
        Assert.Equal(RuntimeParentOutcome.CurrentVersion, deserialized.ParentOutcome?.Version);
        Assert.Equal("incident-1", deserialized.ParentOutcome?.IncidentId);
        Assert.Equal("masked fault message", deserialized.ParentOutcome?.Fault.Message);
        Assert.Equal(new[] { "first", "second" }, deserialized.ParentOutcome?.Notifications.Select(notification => notification.Code));
    }

    [Fact]
    public void LegacyPayload_OmitsNullParentOutcomeAndKeepsTheOriginalConstructorSignature()
    {
        var payload = new RuntimeCompleteActivityCommandPayload(
            NewIdentity(),
            "node-start",
            "actexec-parent",
            null,
            null,
            ["Done"],
            RuntimeCompleteActivityCommandPayload.ActivityInvocationCompletedReason);

        var json = JsonSerializer.SerializeToElement(payload);
        var constructor = typeof(RuntimeCompleteActivityCommandPayload).GetConstructor([
            typeof(WorkflowExecutableIdentity),
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(IReadOnlyCollection<string>),
            typeof(string),
            typeof(SchedulerCompletionKind),
            typeof(string)
        ]);

        Assert.NotNull(constructor);
        Assert.False(json.TryGetProperty(nameof(RuntimeCompleteActivityCommandPayload.ParentOutcome), out _));
        Assert.Null(json.Deserialize<RuntimeCompleteActivityCommandPayload>()?.ParentOutcome);
    }

    [Fact]
    public void ParentOutcome_RejectsUnsupportedVersionAndEmptyNotificationBatch()
    {
        var fault = new ActivityFault("child-fault", "masked fault message");
        var notification = new ActivityChildNotifiedContext("actexec-child", "node-child", "signal");

        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeParentOutcome(2, fault, "incident-1", [notification]));
        Assert.Throws<ArgumentException>(() => new RuntimeParentOutcome(RuntimeParentOutcome.CurrentVersion, fault, "incident-1", []));
    }

    [Fact]
    public void ParentOutcomePayload_RejectsNotificationsFromAnotherChild()
    {
        var outcome = new RuntimeParentOutcome(
            RuntimeParentOutcome.CurrentVersion,
            new ActivityFault("child-fault", "masked fault message"),
            "incident-1",
            [new ActivityChildNotifiedContext("actexec-other", "node-other", "signal")]);

        Assert.Throws<ArgumentException>(() => new RuntimeCompleteActivityCommandPayload(
            NewIdentity(),
            "node-parent",
            "actexec-parent",
            "actexec-grandparent",
            "branch-a",
            ["Done"],
            RuntimeCompleteActivityCommandPayload.ParentOutcomeEvaluationReason,
            SchedulerCompletionKind.ParentOutcomeEvaluation,
            "actexec-child",
            outcome));
    }

    [Fact]
    public void ParentOutcomePayload_RejectsUnknownVersionDuringDeserialization()
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(NewParentOutcomePayload()))!.AsObject();
        json[nameof(RuntimeCompleteActivityCommandPayload.ParentOutcome)]![nameof(RuntimeParentOutcome.Version)] = RuntimeParentOutcome.CurrentVersion + 1;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => json.Deserialize<RuntimeCompleteActivityCommandPayload>());
        Assert.Equal("version", exception.ParamName);
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

    private RuntimeCompleteActivityCommandPayload NewParentOutcomePayload() => new(
        NewIdentity(),
        "node-parent",
        "actexec-parent",
        "actexec-grandparent",
        "branch-a",
        ["Done"],
        RuntimeCompleteActivityCommandPayload.ParentOutcomeEvaluationReason,
        SchedulerCompletionKind.ParentOutcomeEvaluation,
        "actexec-child",
        new RuntimeParentOutcome(
            RuntimeParentOutcome.CurrentVersion,
            new ActivityFault("child-fault", "masked fault message"),
            "incident-1",
            [
                new ActivityChildNotifiedContext("actexec-child", "node-child", "first"),
                new ActivityChildNotifiedContext("actexec-child", "node-child", "second")
            ]));
}
