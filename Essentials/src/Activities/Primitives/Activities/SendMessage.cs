using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>Hidden native Message authoring surface. Stages one send and waits for durable acceptance only.</summary>
/// <remarks>No publisher is called in the uncommitted activity. The checkpoint backend currently refuses this
/// proposal until source/control/cancellation and guarded outcome delivery participate in its physical boundary.</remarks>
[Activity(Icon = "Send", IsDiscoverable = false)]
[RefusesSecretBinding(nameof(MessageName), SecretBindingRefusalReason.PersistedByActivity)]
[RefusesSecretBinding(nameof(BusinessKey), SecretBindingRefusalReason.PersistedByActivity)]
[RefusesSecretBinding(nameof(Payload), SecretBindingRefusalReason.PersistedByActivity)]
[RefusesSecretBinding(nameof(ProducerMessageId), SecretBindingRefusalReason.PersistedByActivity)]
[RefusesSecretBinding(nameof(LifetimeSeconds), SecretBindingRefusalReason.PersistedByActivity)]
public sealed class SendMessage(INativeMessageSendStager stager, IPersistenceAccessContextAccessor access,
    IActivityExecutionStateStore activities) : StatefulActivity<MessageReceipt, NativeMessageSendWaitState, NativeMessageSendResult>
{
    public const string ActivityType = NativeMessageSendCheckpointPlan.ActivityType;
    public const string ResumeTargetId = NativeMessageSendCheckpointPlan.ResumeTargetKey;
    [ActivityInput(Key = nameof(MessageName)), Required]
    public string MessageName { get; set; } = null!;
    [ActivityInput(Key = nameof(BusinessKey)), Required]
    public string BusinessKey { get; set; } = null!;
    [ActivityInput(Key = nameof(Payload), DefaultValue = "null", DefaultSyntax = "Object")]
    public JsonElement? Payload { get; set; }
    [ActivityInput(Key = nameof(ProducerMessageId))]
    public string? ProducerMessageId { get; set; }
    [ActivityInput(Key = nameof(LifetimeSeconds))]
    public int? LifetimeSeconds { get; set; }

    protected override async ValueTask<ActivityTransition<MessageReceipt, NativeMessageSendWaitState>> ExecuteAsync(ActivityExecutionContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var state = await activities.FindAsync(context.WorkflowExecutionId, context.InvocationId, context.CancellationToken)
            ?? throw new InvalidOperationException("The native send invocation is unavailable.");
        var attempt = state.Attempts?.SingleOrDefault(item => item.AttemptId == context.AttemptId);
        if (state.Status != ActivityExecutionStatus.Running || state.Execution.WorkflowExecutionId != context.WorkflowExecutionId || state.Execution.ExecutableNodeId != context.ExecutableNodeId ||
            state.OperationKey != context.OperationKey || state.TargetOperationKey != context.TargetOperationKey ||
            state.StartedAt is not { } origin || attempt is null || attempt.EndedAt is not null ||
            !state.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaim, out var claim) || claim != context.AttemptId)
            throw new InvalidOperationException("The native send does not match its durable invoke attempt.");
        var scope = access.Current.RequireScope();
        var submission = new MessageSubmission(MessageName, BusinessKey, Payload ?? JsonSerializer.SerializeToElement<object?>(null), ProducerMessageId, LifetimeSeconds);
        var wait = stager.Stage(new(scope, context.WorkflowExecutionId, context.InvocationId, context.AttemptId,
            context.ExecutableNodeId, context.OperationKey, context.TargetOperationKey, origin, submission));
        return Suspend(wait, [new ActivityTriggerRegistration<NativeMessageSendResult>(ResumeTargetId,
            NativeMessageSendCheckpointPlan.WaitStimulusType, wait.StimulusHash)]);
    }

    [ResumeTarget(ResumeTargetId)]
    protected override ValueTask<ActivityTransition<MessageReceipt, NativeMessageSendWaitState>> ResumeAsync(
        ActivityResumeContext<NativeMessageSendWaitState, NativeMessageSendResult> context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        // A typed payload/constructed SDK receipt is data, never SourceOutcome or semantic consumption evidence.
        throw new RuntimeSchedulerWorkUnavailableException("message.send.receipt-unavailable",
            "Native Message send requires an authoritative source outcome and guarded receipt checkpoint.");
    }
}
