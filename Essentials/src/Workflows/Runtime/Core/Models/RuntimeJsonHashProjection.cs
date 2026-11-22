namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>The Runtime owner's converter wire shapes for lossless hash projection; not a second serializer profile.</summary>
internal static class RuntimeJsonHashProjection
{
    internal static object? For(object value) => value switch
    {
        StimulusDeliverySnapshot item => StimulusDeliverySnapshotJsonConverter.HashProjection(item),
        StimulusStartRecipient item => StimulusStartRecipientJsonConverter.HashProjection(item),
        StimulusDeliveryScope item => StimulusDeliveryScopeJsonConverter.HashProjection(item),
        StimulusResumeRecipient item => StimulusResumeRecipientJsonConverter.HashProjection(item),
        StimulusResumeCapture item => StimulusResumeCaptureJsonConverter.HashProjection(item),
        RuntimeChildControlBatch item => RuntimeChildControlBatchJsonConverter.HashProjection(item),
        RuntimeChildControlInbox item => RuntimeChildControlInboxJsonConverter.HashProjection(item),
        WorkflowExecutableStimulusDeliveryAuthority item => WorkflowExecutableStimulusDeliveryAuthorityJsonConverter.HashProjection(item),
        WorkflowDeclaredInput input => new { input.Name, input.Type, input.IsRequired, HasDefault = input.DefaultValue.HasValue, input.DefaultValue },
        RuntimePostCommitIntent intent => new
        {
            intent.IntentId, intent.WorkflowExecutionId, intent.Kind, intent.RecordedAt,
            intent.ActivityExecutionId, intent.IdempotencyKey, HasPayload = intent.Payload.HasValue,
            intent.Payload, intent.Metadata, intent.NotBefore, intent.DependsOnWaitRegistrationId, intent.WaitFailurePolicy
        },
        _ => null
    };
}
