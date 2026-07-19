using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class ActivityExecutionValueFlowDocumentTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly ValueTypeDescriptor StringType = new("String");
    private static readonly ValueTypeDescriptor StateType = new("OrderApprovalState", schemaVersion: 1);

    [Fact]
    public void Role_records_round_trip_as_one_activity_execution_document_fragment()
    {
        var inputSnapshot = new ActivityInputSnapshot(
            "invocation-1",
            "contract-sha256",
            "bindings-sha256",
            new Dictionary<string, ValueEnvelope>
            {
                ["message"] = Inline(StringType, "hello")
            },
            DateTimeOffset.Parse("2026-07-16T08:00:00Z"));
        var attempt = new ActivityAttempt(
            "attempt-1",
            "invocation-1",
            1,
            ActivityAttemptReason.Initial,
            DateTimeOffset.Parse("2026-07-16T08:00:01Z"),
            DateTimeOffset.Parse("2026-07-16T08:00:02Z"),
            null,
            ActivityTransitionKind.Suspend,
            null);
        var privateState = new ActivityPrivateState(
            "invocation-1",
            1,
            Inline(StateType, "waiting"),
            "attempt-1",
            DateTimeOffset.Parse("2026-07-16T08:00:02Z"));
        var completion = new ActivityCompletion(
            "invocation-1",
            "attempt-2",
            Inline(StringType, "approved"),
            "Done",
            DateTimeOffset.Parse("2026-07-16T08:05:00Z"),
            "contract-sha256");
        var fault = new NormalizedActivityFault(
            "activity.timeout",
            "TimeoutException",
            "The operation timed out.",
            "sanitized-stack",
            true,
            new Dictionary<string, string> { ["source"] = "activity" });
        var registration = new ActivityTriggerRegistration(
            "trigger-1",
            "invocation-1",
            "resume:approval",
            StringType,
            "approval",
            "stimulus-sha256",
            ActivityTriggerDeduplicationPolicy.Once);
        var delivery = new ActivityTriggerDelivery(
            "delivery-1",
            "trigger-1",
            StringType,
            Inline(StringType, "approved"),
            "provider-1",
            DateTimeOffset.Parse("2026-07-16T08:04:59Z"),
            "deduplication-1",
            ActivityTriggerDeliveryStatus.Consumed);
        var guard = ActivityExecutionValueFlowDocumentVersionGuard.Compatible(2, 3);
        var source = new DocumentFragment(inputSnapshot, attempt, privateState, completion, fault, registration, delivery, guard);

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var copy = JsonSerializer.Deserialize<DocumentFragment>(json, JsonOptions)!;

        Assert.Equal("hello", copy.InputSnapshot.Values["message"].InlineValue!.Value.GetString());
        Assert.Equal(ActivityTransitionKind.Suspend, copy.Attempt.TransitionKind);
        Assert.Equal("waiting", copy.PrivateState.Value.InlineValue!.Value.GetString());
        Assert.Equal("approved", copy.Completion.Result.InlineValue!.Value.GetString());
        Assert.Equal("activity.timeout", copy.Fault.Code);
        Assert.Equal("String", copy.TriggerRegistration.PayloadType.Alias);
        Assert.Equal(ActivityTriggerDeliveryStatus.Consumed, copy.TriggerDelivery.Status);
        Assert.True(copy.VersionGuard.IsCompatible);
    }

    [Fact]
    public void Input_snapshot_is_complete_immutable_and_preserves_absent_values()
    {
        var values = new Dictionary<string, ValueEnvelope> { ["message"] = Inline(StringType, "before") };
        var snapshot = new ActivityInputSnapshot(
            "invocation-1",
            "contract-sha256",
            "bindings-sha256",
            values,
            DateTimeOffset.UtcNow);

        values["message"] = Inline(StringType, "after");

        Assert.Equal("before", snapshot.Values["message"].InlineValue!.Value.GetString());
        var absentSnapshot = new ActivityInputSnapshot(
            "invocation-1",
            "contract-sha256",
            "bindings-sha256",
            new Dictionary<string, ValueEnvelope>
            {
                ["message"] = ValueEnvelope.Absent(StringType, ValueProtectionPolicy.InstanceInline)
            },
            DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(absentSnapshot, JsonOptions);
        var copy = JsonSerializer.Deserialize<ActivityInputSnapshot>(json, JsonOptions)!;

        Assert.Equal(ValuePresence.Absent, copy.Values["message"].Presence);
    }

    [Fact]
    public void Attempt_enforces_identity_order_and_closed_transition_invariants()
    {
        var startedAt = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentOutOfRangeException>(() => new ActivityAttempt(
            "attempt-1", "invocation-1", 0, ActivityAttemptReason.Initial, startedAt));
        Assert.Throws<ArgumentException>(() => new ActivityAttempt(
            "attempt-1", "invocation-1", 1, ActivityAttemptReason.Initial, startedAt,
            triggerDeliveryId: "delivery-1"));
        Assert.Throws<ArgumentException>(() => new ActivityAttempt(
            "attempt-1", "invocation-1", 1, ActivityAttemptReason.Initial, startedAt,
            endedAt: startedAt.AddSeconds(1)));
        Assert.Throws<ArgumentException>(() => new ActivityAttempt(
            "attempt-1", "invocation-1", 1, ActivityAttemptReason.Initial, startedAt,
            transitionKind: ActivityTransitionKind.Complete));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActivityAttempt(
            "attempt-1", "invocation-1", 1, ActivityAttemptReason.Initial, startedAt,
            endedAt: startedAt.AddSeconds(-1), transitionKind: ActivityTransitionKind.Fault));
    }

    [Fact]
    public void State_completion_fault_and_trigger_records_reject_incomplete_shapes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActivityPrivateState(
            "invocation-1", 0, Inline(StateType, "state"), "attempt-1", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => new ActivityPrivateState(
            "invocation-1", 1, ValueEnvelope.Absent(StateType, ValueProtectionPolicy.InstanceInline), "attempt-1", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => new ActivityCompletion(
            "invocation-1", "attempt-1", ValueEnvelope.Absent(StringType, ValueProtectionPolicy.InstanceInline), "Done", DateTimeOffset.UtcNow, "contract"));
        Assert.Throws<ArgumentException>(() => new NormalizedActivityFault(
            "", "InvalidOperationException", "failed", null, false));
        Assert.Throws<ArgumentException>(() => new ActivityTriggerRegistration(
            "trigger-1", "invocation-1", "resume", StringType, "", "hash", ActivityTriggerDeduplicationPolicy.Once));
        Assert.Throws<ArgumentException>(() => new ActivityTriggerDelivery(
            "delivery-1", "trigger-1", StringType,
            Inline(new ValueTypeDescriptor("Int32"), 42),
            "provider", DateTimeOffset.UtcNow, "dedup", ActivityTriggerDeliveryStatus.Received));
    }

    [Fact]
    public void Incompatible_document_version_guard_round_trips_and_fails_loudly()
    {
        var guard = ActivityExecutionValueFlowDocumentVersionGuard.Incompatible(
            1,
            3,
            "missing-input-snapshot",
            "A started legacy invocation cannot reconstruct its pinned input snapshot.");

        var json = JsonSerializer.Serialize(guard, JsonOptions);
        var copy = JsonSerializer.Deserialize<ActivityExecutionValueFlowDocumentVersionGuard>(json, JsonOptions)!;

        Assert.False(copy.IsCompatible);
        Assert.Equal("missing-input-snapshot", copy.IncompatibilityCode);
        var exception = Assert.Throws<InvalidOperationException>(() => copy.EnsureCompatible());
        Assert.Contains("missing-input-snapshot", exception.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new ActivityExecutionValueFlowDocumentVersionGuard(
            1, 3, false, null, null));
    }

    private static ValueEnvelope Inline(ValueTypeDescriptor type, object value) =>
        ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline);

    private sealed record DocumentFragment(
        ActivityInputSnapshot InputSnapshot,
        ActivityAttempt Attempt,
        ActivityPrivateState PrivateState,
        ActivityCompletion Completion,
        NormalizedActivityFault Fault,
        ActivityTriggerRegistration TriggerRegistration,
        ActivityTriggerDelivery TriggerDelivery,
        ActivityExecutionValueFlowDocumentVersionGuard VersionGuard);
}
