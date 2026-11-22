using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Canonical ordinary resume wire format for an accepted external source's exact saved wait.</summary>
/// <remarks>These pure data checks grant no authority. Targets must read the actual immutable source seal first.</remarks>
public static class RuntimeIngressWaitCommand
{
    public const string Protocol = "elsa.runtime.native-ingress-resume";
    public const int ProtocolVersion = 1;
    public const string AcceptanceSealMetadataKey = "runtime.ingress.wait.acceptance-seal.v1";

    public static WorkflowExecutionCommandEnvelope CreateEnvelope(RuntimePostCommitOutboxItem source, RuntimeIngressWaitRecipient recipient)
    {
        var delivery = RequireSelection(source, recipient);
        var expected = recipient.Expected; var capture = expected.RegistrationCapture!; var id = recipient.Identity;
        var candidate = delivery.Candidate;
        var metadata = candidate.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        metadata["runtime.dispatcher"] = "BookmarkResumeDispatcher";
        metadata["runtime.bookmarkId"] = expected.BookmarkId;
        metadata["runtime.activityExecutionId"] = expected.ActivityExecutionId;
        metadata["runtime.executableNodeId"] = expected.ExecutableNodeId;
        metadata["runtime.resumeTargetId"] = expected.ResumeTargetId;
        metadata["runtime.stimulusType"] = candidate.StimulusType;
        metadata["runtime.stimulusHash"] = candidate.StimulusHash;
        metadata["runtime.artifactId"] = capture.Executable.ArtifactId;
        metadata["runtime.artifactVersion"] = capture.Executable.ArtifactVersion;
        metadata["runtime.artifactHash"] = capture.Executable.ArtifactHash;
        var typed = candidate.PayloadType is null ? null : new RuntimeTypedTriggerDeliveryMetadata(id.CommandId,
            candidate.PayloadType, candidate.ProviderId!, source.RecordedAt, id.IdempotencyKey);
        var payload = new RuntimeResumeBookmarkCommandPayload(capture.Executable, expected.BookmarkId,
            expected.ActivityExecutionId, expected.ExecutableNodeId, expected.ResumeTargetId, candidate.StimulusType,
            candidate.StimulusHash, candidate.Input, RuntimeResumeBookmarkCommandPayload.StimulusMatchedReason, typed)
        { ExpectedBookmarkResume = expected, IngressWaitOrigin = delivery.Origin(recipient) };
        var command = new WorkflowExecutionCommand(id.CommandId, id.WorkflowExecutionId, WorkflowExecutionCommandKind.ResumeBookmark,
            source.RecordedAt, JsonSerializer.SerializeToElement(payload), metadata);
        return new(id.EnvelopeId, id.WorkflowExecutionId, command, id.IdempotencyKey, WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            source.RecordedAt, metadata: metadata, partition: capture.Scope.Partition ?? new(WorkflowExecutionPartition.DefaultValue));
    }

    public static RuntimeIngressWaitDispatchSeal CreateSeal(RuntimePostCommitOutboxItem source, RuntimeIngressWaitRecipient recipient)
    {
        var delivery = RequireSelection(source, recipient);
        return new(delivery.Origin(recipient), recipient.Identity, source.RecordedAt, Protocol, ProtocolVersion,
            JsonSerializer.SerializeToElement(CreateEnvelope(source, recipient)));
    }

    public static RuntimeIngressWaitDelivery RequireSelection(RuntimePostCommitOutboxItem source, RuntimeIngressWaitRecipient recipient)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(recipient);
        if (source.StandaloneIngress is not { } external || source.SubscriptionDelivery?.WaitDelivery is not { } delivery || delivery.SourceOutboxItemId != source.OutboxItemId ||
            delivery.SourceFingerprint != source.SourceFingerprint || delivery.Scope != recipient.Identity.Scope ||
            delivery.Recipients.SingleOrDefault(item => item.WaitId == recipient.WaitId)?.IsEquivalentTo(recipient) != true ||
            recipient.Expected.RegistrationCapture is not { } capture || capture.Scope.TenantId != external.TenantId ||
            (capture.Scope.Partition ?? new(WorkflowExecutionPartition.DefaultValue)) != external.Partition || capture.Scope.TestScope is not null)
            throw Invalid();
        return delivery;
    }

    public static WorkflowExecutionCommandEnvelope ReadEnvelope(RuntimeIngressWaitDispatchSeal seal)
    {
        ArgumentNullException.ThrowIfNull(seal);
        if (seal.CommandProtocol != Protocol || seal.CommandProtocolVersion != ProtocolVersion)
            throw new NotSupportedException("The saved wait command protocol is unsupported.");
        return seal.Command.Deserialize<WorkflowExecutionCommandEnvelope>() ?? throw Invalid();
    }

    public static void ValidateEnvelope(RuntimeCommittedIngressWait committed, WorkflowExecutionCommandEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(committed); ArgumentNullException.ThrowIfNull(envelope);
        var delivery = RequireSelection(committed.Item, committed.Recipient);
        if (committed.Seal.Origin != delivery.Origin(committed.Recipient) ||
            !committed.Seal.Identity.IsEquivalentTo(committed.Recipient.Identity) || committed.Seal.CommittedAt != committed.Item.RecordedAt ||
            committed.Seal.CommandProtocol != Protocol || committed.Seal.CommandProtocolVersion != ProtocolVersion ||
            !Equal(envelope, CreateEnvelope(committed.Item, committed.Recipient)) ||
            !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(envelope), committed.Seal.Command)) throw Invalid();
    }

    public static void ValidateWorkItem(RuntimeCommittedIngressWait committed, RuntimeSchedulerWorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var envelope = ReadEnvelope(committed.Seal); ValidateEnvelope(committed, envelope);
        if (item.WorkItemId != envelope.EnvelopeId || item.WorkflowExecutionId != envelope.WorkflowExecutionId ||
            item.CommandId != envelope.Command.CommandId || item.CommandKind != WorkflowExecutionCommandKind.ResumeBookmark ||
            item.EnvelopeId != envelope.EnvelopeId || item.IdempotencyKey != envelope.IdempotencyKey ||
            item.EnqueuedAt != envelope.EnqueuedAt || item.Sequence != envelope.Sequence || item.ExecutionScopeId is not null || item.Attempt is not null ||
            !Equal(item.Payload, envelope.Command.Payload) || !Equal(item.CommandMetadata, envelope.Command.Metadata) ||
            !Equal(item.EnvelopeMetadata, envelope.Metadata)) throw Invalid();
    }
    private static bool Equal<T>(T left, T right) => JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left), JsonSerializer.SerializeToElement(right));
    private static InvalidDataException Invalid() => new("The saved wait command differs from its exact physical source commitment.");
}
