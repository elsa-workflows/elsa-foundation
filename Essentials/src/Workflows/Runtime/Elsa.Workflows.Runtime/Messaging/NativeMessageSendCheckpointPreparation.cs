using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Workflows.Runtime.Messaging;

/// <summary>Message-owned pure assembly service used by the actual native suspension checkpoint owner.</summary>
/// <remarks>Produces proposals only. Actual providers must bind canonical sender control/outcome and Q4 before enabling.</remarks>
public sealed class NativeMessageSendCheckpointPreparation
{
    public NativeMessageSendCheckpointPlan Prepare(NativeMessageSendRequest request, RuntimeInvokeActivityCommandPayload invocation,
        WorkflowExecutable executable, ActivityExecutionState suspension, string suspensionCommitId, PersistenceScope scope)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(invocation); ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(suspension); ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(suspensionCommitId);
        var wait = NativeMessageSendStagingBuffer.Wait(request);
        var content = NativeMessageSendStagingBuffer.Validate(request.Submission);
        var contract = executable.NodesById.GetValueOrDefault(request.ExecutableNodeId)?.ActivityContract;
        var target = WorkflowExecutableResumeTarget.FindForNode(executable, request.ExecutableNodeId, NativeMessageSendCheckpointPlan.ResumeTargetKey);
        var registration = suspension.TriggerRegistrations?.SingleOrDefault();
        var privateState = suspension.PrivateState;
        var snapshot = suspension.InputSnapshot;
        var identity = suspension.ContractIdentity;
        var attempt = suspension.Attempts?.SingleOrDefault(item => item.AttemptId == request.AttemptId);
        if (scope != request.Scope || request.WorkflowExecutionId != suspension.Execution.WorkflowExecutionId ||
            request.InvocationId != invocation.ActivityExecutionId || request.InvocationId != suspension.InvocationId ||
            request.ExecutableNodeId != invocation.ExecutableNodeId || request.ExecutableNodeId != suspension.Execution.ExecutableNodeId ||
            request.OperationKey != suspension.OperationKey || request.TargetOperationKey != suspension.TargetOperationKey ||
            suspension.StartedAt != request.OriginatedAt || suspension.Status != ActivityExecutionStatus.Suspended ||
            executable.Identity.ArtifactId != invocation.PinnedExecutable.ArtifactId || executable.Identity.ArtifactHash != invocation.PinnedExecutable.ArtifactHash ||
            contract is null || identity is null || identity.SchemaFingerprint != contract.SchemaFingerprint ||
            identity.ActivityTypeKey != contract.ActivityTypeKey || identity.ContractVersion != contract.ContractVersion ||
            snapshot is null || snapshot.ContractFingerprint != contract.SchemaFingerprint || snapshot.InvocationId != request.InvocationId ||
            attempt is not { EndedAt: not null, TransitionKind: ActivityTransitionKind.Suspend } ||
            privateState is null || privateState.InvocationId != request.InvocationId || privateState.ProducedByAttemptId != request.AttemptId ||
            privateState.Value.Type.Alias != TypeAliasConvention.CanonicalAlias(typeof(NativeMessageSendWaitState)) ||
            privateState.Value.Type.CollectionKind != CollectionKind.Single || privateState.Value.Type.SchemaVersion != 1 || privateState.StateVersion != 1 ||
            privateState.Value.InlineValue is not { } privatePayload || target is null || target.ExecutableNodeId != request.ExecutableNodeId ||
            registration is null || registration.InvocationId != request.InvocationId || registration.ResumeTargetKey != target.ResumeTargetId ||
            registration.StimulusType != NativeMessageSendCheckpointPlan.WaitStimulusType || registration.StimulusHash != wait.StimulusHash ||
            registration.PayloadType.Alias != TypeAliasConvention.CanonicalAlias(typeof(NativeMessageSendResult)) ||
            registration.PayloadType.CollectionKind != CollectionKind.Single || registration.PayloadType.SchemaVersion != 1 ||
            registration.DeduplicationPolicy != ActivityTriggerDeduplicationPolicy.Once)
            throw Invalid();
        var captured = privatePayload.Deserialize<NativeMessageSendWaitState>() ?? throw Invalid();
        if (captured.IntentId != wait.IntentId || captured.BookmarkId != wait.BookmarkId || captured.StimulusHash != wait.StimulusHash) throw Invalid();
        // Reconstruct from the complete pinned contract/snapshot rather than trusting an activity-supplied body.
        var expected = new[] { "MessageName", "BusinessKey", "Payload", "ProducerMessageId", "LifetimeSeconds" };
        if (!contract.Inputs.Keys.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal)) ||
            !snapshot!.Values.Keys.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal))) throw Invalid();
        var values = snapshot.Values;
        var pinned = NativeMessageSendStagingBuffer.Validate(new MessageSubmission(
            Required(values, "MessageName").GetString()!, Required(values, "BusinessKey").GetString()!,
            Read(values, "Payload"), OptionalText(values, "ProducerMessageId"), OptionalInt(values, "LifetimeSeconds")));
        if (pinned.Fingerprint != content.Fingerprint) throw Invalid();
        var payload = JsonSerializer.SerializeToElement(new { version = 1, submission = request.Submission, authoredFingerprint = content.Fingerprint },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var intent = new RuntimePostCommitIntent(wait.IntentId, request.WorkflowExecutionId, NativeMessageSendCheckpointPlan.IntentKind,
            request.OriginatedAt, request.InvocationId, wait.IntentId, payload,
            dependsOnWaitRegistrationId: wait.BookmarkId, waitFailurePolicy: RuntimeWaitDependentIntentFailurePolicy.KeepWaitingManualIntervention);
        return new(1, request, invocation.PinnedExecutable, intent, wait.BookmarkId, wait.StimulusHash, target.ResumeTargetId,
            RuntimePostCommitOutboxItems.OutboxItemId(suspensionCommitId, intent), content.Fingerprint, RuntimeCheckpointCommitFingerprint.ComputeIntent(intent));
    }

    /// <summary>Default physical refusal, including plans stripped from declared or materialized native intents/private waits.</summary>
    internal static void RefuseUnqualified(RuntimeCheckpointCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (commit.NativeMessageSend is not null || commit.PostCommitIntents.Any(item => item.Kind == NativeMessageSendCheckpointPlan.IntentKind) ||
            commit.StateChanges.PostCommitOutbox.Any(item => item.State.Intent.Kind == NativeMessageSendCheckpointPlan.IntentKind) ||
            commit.StateChanges.Bookmarks.Any(item => item.State.StimulusType == NativeMessageSendCheckpointPlan.WaitStimulusType) ||
            commit.StateChanges.ActivityExecutions.Any(item => item.State.TriggerRegistrations?.Any(registration => registration.StimulusType == NativeMessageSendCheckpointPlan.WaitStimulusType) == true))
            throw new RuntimeSchedulerWorkUnavailableException("message.send.checkpoint-unavailable",
                "Native Message send requires a qualified source, private-wait, sender-control and cancellation checkpoint boundary.");
    }
    private static JsonElement Read(IReadOnlyDictionary<string, ValueEnvelope> values, string key, bool required = false)
    {
        if (!values.TryGetValue(key, out var value) || value.Presence == ValuePresence.Absent)
        { if (required) throw Invalid(); return JsonSerializer.SerializeToElement<object?>(null); }
        if (value.Presence == ValuePresence.ExplicitNull) return JsonSerializer.SerializeToElement<object?>(null);
        if (value.Presence != ValuePresence.Present || value.InlineValue is not { } inline) throw Invalid();
        return inline;
    }
    private static JsonElement Required(IReadOnlyDictionary<string, ValueEnvelope> values, string key)
    { var value = Read(values, key, true); if (value.ValueKind != JsonValueKind.String) throw Invalid(); return value; }
    private static string? OptionalText(IReadOnlyDictionary<string, ValueEnvelope> values, string key)
    { var value = Read(values, key); return value.ValueKind == JsonValueKind.Null ? null : value.ValueKind == JsonValueKind.String ? value.GetString() : throw Invalid(); }
    private static int? OptionalInt(IReadOnlyDictionary<string, ValueEnvelope> values, string key)
    { var value = Read(values, key); return value.ValueKind == JsonValueKind.Null ? null : value.TryGetInt32(out var integer) ? integer : throw Invalid(); }
    private static InvalidOperationException Invalid() => new("The native send proposal differs from its actual typed invoke suspension.");
}
