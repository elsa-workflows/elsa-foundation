using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using RuntimeActivityTransitionKind = Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind;
using RuntimeTriggerDeduplicationPolicy = Elsa.Workflows.Runtime.Core.Models.ActivityTriggerDeduplicationPolicy;
using RuntimeTriggerRegistration = Elsa.Workflows.Runtime.Core.Models.ActivityTriggerRegistration;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Projects one closed stateful suspension transition into the durable invocation document shared by
/// initial and resumed attempts. Checkpoint construction remains the owning handler's responsibility.
/// </summary>
internal static class StatefulActivitySuspensionProjector
{
    public const string SuspendedSubStatus = "TriggerWaiting";

    public static ActivityExecutionState Project(
        ActivityExecutionState state,
        ActivityAttempt attempt,
        IStatefulActivitySuspensionTransition transition,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(transition);

        if (!StringComparer.Ordinal.Equals(attempt.InvocationId, state.InvocationId))
            throw new InvalidOperationException($"Activity attempt '{attempt.AttemptId}' does not belong to invocation '{state.InvocationId}'.");

        if (attempt.EndedAt is not null)
            throw new InvalidOperationException($"Activity attempt '{attempt.AttemptId}' has already ended.");

        var registrations = transition.Registrations
            .Select((registration, index) => new RuntimeTriggerRegistration(
                registrationId: $"{attempt.AttemptId}:trigger:{index + 1}",
                invocationId: state.InvocationId,
                resumeTargetKey: registration.ResumeTargetKey,
                payloadType: registration.PayloadType,
                stimulusType: registration.StimulusType,
                stimulusHash: registration.StimulusHash,
                deduplicationPolicy: registration.DeduplicationMode switch
                {
                    ActivityTriggerDeduplicationMode.Once => RuntimeTriggerDeduplicationPolicy.Once,
                    ActivityTriggerDeduplicationMode.IdempotencyKey => RuntimeTriggerDeduplicationPolicy.IdempotencyKey,
                    _ => throw new InvalidOperationException($"Unsupported activity trigger deduplication mode '{registration.DeduplicationMode}'.")
                },
                metadata: registration.Metadata))
            .ToArray();

        if (registrations.Length == 0)
            throw new InvalidOperationException("A stateful suspension must carry at least one typed trigger registration.");

        var endedAttempt = new ActivityAttempt(
            attempt.AttemptId,
            attempt.InvocationId,
            attempt.Ordinal,
            attempt.Reason,
            attempt.StartedAt,
            occurredAt,
            attempt.TriggerDeliveryId,
            RuntimeActivityTransitionKind.Suspend);
        var priorAttempts = state.Attempts?.Where(candidate => candidate.AttemptId != endedAttempt.AttemptId) ?? [];
        var privateState = new ActivityPrivateState(
            state.InvocationId,
            transition.StateValueType.SchemaVersion ?? 1,
            ValueEnvelope.Inline(
                transition.StateValueType,
                transition.StatePayload,
                ValueProtectionPolicy.InstanceInline),
            endedAttempt.AttemptId,
            occurredAt);

        return state with
        {
            Status = ActivityExecutionStatus.Suspended,
            SubStatus = SuspendedSubStatus,
            CompletedAt = null,
            Attempts = priorAttempts.Append(endedAttempt).ToArray(),
            PrivateState = privateState,
            Completion = null,
            Fault = null,
            TriggerRegistrations = registrations
        };
    }
}
