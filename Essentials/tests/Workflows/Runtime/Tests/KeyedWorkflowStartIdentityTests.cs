using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Pins the frozen keyed start identities: v1 (#2195) and the recurring-occurrence v2 with its occurrence key (#2198).
/// Execution ids derived this way are persisted, and a redelivery is recognized only by deriving the same id again, so
/// these literals must never change. A new derivation is a new version.
/// </summary>
public sealed class KeyedWorkflowStartIdentityTests
{
    // The idempotency key PublishStimulusStagingBuffer gives the first publish of activity execution "actexec-publish".
    private const string IdempotencyKey = "publish-stimulus:actexec-publish:0";
    private const string ArtifactId = "artifact-1";
    private const string Digest = "2755f6772df5c4c8cd01f7cddcc9df13db6423c88924244549340a7337ca65cb";

    // One occurrence of a Timer start in the default slot of workflow definition "orders-report". The stimulus hash carries
    // both characters the key escapes.
    private const string OccurrenceKey = "recurring:activation-slot%3A13%3Aorders-report%3A7%3Adefault:timer-start:hash%3A50%25:640291502400000000";
    private const string OccurrenceDigest = "282718766f7a4313bd0dcecf74935f49b2a7285dcff7d0bc398de4647c09f19a";
    private static readonly DateTimeOffset Occurrence = new(2030, 1, 2, 3, 4, 0, TimeSpan.Zero);

    [Fact]
    public void The_v1_ids_for_a_fixed_key_and_artifact_never_change()
    {
        var identity = KeyedWorkflowStartIdentity.For(IdempotencyKey, ArtifactId);

        Assert.Equal("v1", KeyedWorkflowStartIdentity.Version);
        Assert.Equal("publish-stimulus:actexec-publish:0:start:artifact-1", identity.StartKey);
        Assert.Equal($"wfexec:start:v1:{Digest}", identity.WorkflowExecutionId);
        Assert.Equal($"command:start:v1:{Digest}", identity.CommandId);
        Assert.Equal($"envelope:start:v1:{Digest}", identity.EnvelopeId);
    }

    [Fact]
    public void A_request_is_a_keyed_start_only_when_its_execution_id_is_the_one_derived_from_its_key()
    {
        var identity = KeyedWorkflowStartIdentity.For(IdempotencyKey, ArtifactId);

        var keyed = KeyedWorkflowStartIdentity.TryGet(Request(identity.WorkflowExecutionId, identity.StartKey));

        Assert.Equal(identity.WorkflowExecutionId, keyed?.WorkflowExecutionId);
        Assert.Null(KeyedWorkflowStartIdentity.TryGet(Request("wfexec-caller-named", identity.StartKey)));
        Assert.Null(KeyedWorkflowStartIdentity.TryGet(Request(identity.WorkflowExecutionId, idempotencyKey: null)));
        Assert.Null(KeyedWorkflowStartIdentity.TryGet(Request(workflowExecutionId: null, identity.StartKey)));
    }

    [Fact]
    public void One_key_names_a_distinct_start_per_artifact()
    {
        Assert.NotEqual(
            KeyedWorkflowStartIdentity.For(IdempotencyKey, "artifact-1").WorkflowExecutionId,
            KeyedWorkflowStartIdentity.For(IdempotencyKey, "artifact-2").WorkflowExecutionId);
    }

    [Fact]
    public void A_slot_scoped_occurrence_key_names_the_trigger_and_never_changes()
    {
        var schedule = OccurrenceSchedule("publication-a", "artifact-a");

        Assert.Equal(OccurrenceKey, schedule.BuildOccurrenceKey());
        // Another publication of the slot names the same occurrence alike: the key carries no activation and no artifact.
        Assert.Equal(OccurrenceKey, OccurrenceSchedule("publication-b", "artifact-b").BuildOccurrenceKey());
    }

    [Fact]
    public void A_schedule_without_a_slot_keeps_the_artifact_scoped_occurrence_key()
    {
        var schedule = new RecurringTriggerSchedule(
            RecurringTriggerSchedule.BuildId(ArtifactId, "timer-start"), ArtifactId, "timer-start", "Timer", "hash:50%",
            RecurringScheduleKind.Interval, "PT1M", Occurrence, Occurrence.AddHours(-1));

        Assert.Equal("recurring:artifact-1:timer-start:640291502400000000", schedule.BuildOccurrenceKey());
    }

    [Fact]
    public void The_v2_occurrence_ids_for_a_fixed_occurrence_key_never_change()
    {
        var identity = KeyedWorkflowStartIdentity.ForOccurrence(OccurrenceKey);

        Assert.Equal("v2", KeyedWorkflowStartIdentity.OccurrenceVersion);
        Assert.Equal($"{OccurrenceKey}:start", identity.StartKey);
        Assert.Equal($"wfexec:start:v2:{OccurrenceDigest}", identity.WorkflowExecutionId);
        Assert.Equal($"command:start:v2:{OccurrenceDigest}", identity.CommandId);
        Assert.Equal($"envelope:start:v2:{OccurrenceDigest}", identity.EnvelopeId);
        Assert.False(identity.IsArtifactScoped);
        Assert.True(KeyedWorkflowStartIdentity.For(IdempotencyKey, ArtifactId).IsArtifactScoped);
    }

    [Fact]
    public void A_request_carrying_a_v2_execution_id_is_an_occurrence_keyed_start()
    {
        var identity = KeyedWorkflowStartIdentity.ForOccurrence(OccurrenceKey);

        var keyed = KeyedWorkflowStartIdentity.TryGet(Request(identity.WorkflowExecutionId, identity.StartKey));

        Assert.Equal(identity.WorkflowExecutionId, keyed?.WorkflowExecutionId);
        Assert.Equal(KeyedWorkflowStartIdentity.OccurrenceVersion, keyed?.DerivationVersion);
        // The v1 id derived from the same start key is a different id, and a mismatched pairing is no keyed start at all.
        Assert.Null(KeyedWorkflowStartIdentity.TryGet(Request(identity.WorkflowExecutionId, $"{identity.StartKey}:other")));
    }

    private static RecurringTriggerSchedule OccurrenceSchedule(string activationId, string artifactId) =>
        new(RecurringTriggerSchedule.BuildId(activationId, artifactId, "timer-start"), artifactId, "timer-start", "Timer", "hash:50%",
            RecurringScheduleKind.Interval, "PT1M", Occurrence, Occurrence.AddHours(-1), activationId,
            WorkflowActivationSlotIdentity.Create("orders-report", "default"));

    private static WorkflowExecutionStartDispatchRequest Request(string? workflowExecutionId, string? idempotencyKey) =>
        new(ArtifactId, "runtime-test", workflowExecutionId: workflowExecutionId, idempotencyKey: idempotencyKey);
}
