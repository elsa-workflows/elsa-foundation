using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Canonical validation performed by the physical source owner, never by a caller-supplied witness.</summary>
internal static class SavedStimulusStartSeal
{
    internal static void Validate(PostCommitDeliveryContext source, WorkflowExecutableStimulusDeliveryAuthority authority,
        WorkflowExecutionCommandEnvelope envelope, IEnumerable<IStimulusDeliveryIntentAdapter> adapters,
        WorkflowExecutable material, IWorkflowExecutableInputValidator? inputValidator)
    {
        var cohort = source.Claim.Item.RecipientSnapshot ?? throw new InvalidDataException("A saved command needs its actual retained cohort.");
        var canonical = new StimulusDeliveryRequestProjector(adapters).Read(source);
        var recipient = cohort.Starts.SingleOrDefault(item => item.Binding.TriggerBindingId == authority.TriggerBindingId)
            ?? throw new InvalidDataException("No captured binding authorizes this command.");
        var identity = KeyedWorkflowStartIdentity.For(source.Intent.IdempotencyKey!, recipient.Executable.ArtifactId);
        var start = envelope.Command.Payload?.Deserialize<WorkflowExecutionStartCommandPayload>()
            ?? throw new InvalidDataException("The saved start command has no payload.");
        IReadOnlyDictionary<string, JsonElement> defaults = new Dictionary<string, JsonElement>();
        if (material.InputContract is { } contract)
        {
            var validation = (inputValidator ?? throw new NotSupportedException("Saved start admission needs the selected input validator.")).Validate(contract, []);
            if (!validation.IsValid) throw new InvalidDataException("The saved start's empty ordinary input bag violates its retained contract.");
            defaults = validation.NormalizedInputs;
        }
        var origin = start.StimulusDeliveryOrigin;
        var accepted = start.StartAuthority?.StimulusDelivery;
        if (envelope.WorkflowExecutionId != identity.WorkflowExecutionId || envelope.EnvelopeId != identity.EnvelopeId ||
            envelope.Command.CommandId != identity.CommandId || envelope.Command.Kind != WorkflowExecutionCommandKind.Start ||
            envelope.IdempotencyKey != identity.StartKey || envelope.Command.EnqueuedAt != envelope.EnqueuedAt ||
            envelope.DeliveryMode != WorkflowExecutionCommandDeliveryMode.AtLeastOnce || envelope.Sequence is not null ||
            !recipient.SourceProvenanceCaptured || !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(start.PinnedExecutable, recipient.Executable) ||
            start.RequestedArtifactId != recipient.Executable.ArtifactId || start.PinnedSource != recipient.SourceProvenance ||
            start.TriggerNodeId != recipient.Binding.ExecutableNodeId ||
            !WorkflowExecutionAuthoritySnapshot.MetadataEquals(start.TriggerMetadata, recipient.Binding.Metadata) ||
            start.TenantId != cohort.Scope.TenantId || start.Partition != cohort.Scope.Partition || envelope.Partition != start.Partition ||
            start.RunKind != WorkflowRunKind.PublishedRun || start.ParentWorkflowExecutionId is not null || start.CorrelationId is not null ||
            start.DispatchNestingDepth != 0 || start.TestScope is not null || start.Variables.Count != 0 || !NativeIngressTargetEvidence.Equal(start.Inputs, defaults) ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(material.Identity, recipient.Executable) ||
            !WorkflowExecutionAuthoritySnapshot.Matches(start.Authority, canonical.RequestedBy, canonical.RequestedBy) ||
            !NativeIngressTargetEvidence.Equal(start.StimulusInput, canonical.Input) ||
            origin is null || origin.Scope != source.Scope || origin.SourceOutboxItemId != source.SourceOutboxItemId ||
            origin.IntentFingerprint != source.IntentFingerprint || origin.TriggerBindingId != authority.TriggerBindingId ||
            accepted is null || !NativeIngressTargetEvidence.Equal(accepted, authority))
            throw new InvalidDataException("The saved command changed its actual source's canonical authority, inputs, target or scope.");
    }

    internal static WorkflowExecutionCommandEnvelope Read(RuntimePostCommitOutboxItem source, string executionId) =>
        (source.SavedStimulusStarts is { } seals && seals.TryGetValue(executionId, out var command)
            ? command.Deserialize<WorkflowExecutionCommandEnvelope>() ?? throw new InvalidDataException("The accepted command is invalid.") : null)
        ?? throw new InvalidDataException("The actual source has no accepted command for this deterministic target.");
}
