using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Values;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal sealed class NativeStandaloneTriggerRuntimeTarget(IWorkflowExecutableStore executables,
    IWorkflowExecutionActorProvider actors, IRuntimeIngressDeliveryStore ingress, IWorkflowExecutableInputValidator inputs,
    IWorkflowExecutableStartPolicy policy, NativeIngressTargetEvidence evidence, IPersistenceAccessContextAccessor access,
    IRuntimePersistenceModeResolver? persistenceModes = null) : IStandaloneTriggerRuntimeTarget
{
    internal const string Protocol = "elsa.runtime.native-ingress-start";
    internal const string OriginMetadata = "runtime.ingress.origin.v1";
    public string ModelKey => "native";
    public bool Supports(WorkflowExecutable? nativeWorkflow, RuntimeModelMaterial? model) => nativeWorkflow is not null && model is null;

    public async ValueTask<RuntimeDispatchSeal> PrepareDispatchAsync(RuntimePostCommitOutboxItem source,
        RuntimeSubscriptionRecipient recipient, CancellationToken cancellationToken = default)
    {
        NativeIngressTargetEvidence.ValidateSelection(source, recipient);
        access.Current.EnsureScope(recipient.StartIdentity.Scope);
        if (recipient.ModelProtocol is not null || recipient.Partition.Value != recipient.StartIdentity.Scope.Value)
            throw new TriggerOperationException("unavailable", "The selected target does not support this native ingress scope.");
        var executable = await executables.FindAsync(recipient.Material.ArtifactId, cancellationToken)
            ?? throw new TriggerOperationException("unavailable", "The accepted native material is unavailable.");
        if (!WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(executable.Identity, recipient.Material) || executable.InputContract is not { } contract ||
            RuntimeIngressAdmission.ComputeInputContractFingerprint(contract) != recipient.InputContractFingerprint)
            throw new TriggerOperationException("conflict", "The native material or declared-input contract differs from its accepted capture.");
        var validated = inputs.Validate(contract, recipient.Inputs);
        if (!validated.IsValid || !NativeIngressTargetEvidence.Equal(validated.NormalizedInputs, recipient.Inputs))
            throw new TriggerOperationException("validation", "The accepted native input bag is invalid.");
        await CheckPolicyAsync(recipient, cancellationToken);
        var origin = new RuntimeIngressCommandOrigin(source.OutboxItemId, source.SourceFingerprint,
            source.SubscriptionDelivery!.SelectionFingerprint, recipient.StartIdentity.Scope, recipient.RecipientId);
        var variables = WorkflowExecutionStartCommandPayload.ToJsonValues(new RuntimeVariableDeclarationProjector()
            .ProjectDeclaredVariableDefaultsByName(executable.WorkflowVariables));
        var payload = new WorkflowExecutionStartCommandPayload(recipient.Material, recipient.Material.ArtifactId, variables, validated.NormalizedInputs,
            null, null, WorkflowRunKind.PublishedRun, recipient.SourceProvenance,
            null, null, recipient.TenantId, recipient.Partition, recipient.Authority, WorkflowExecutableStartAuthority.FromStandaloneTrigger(origin), 0);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["runtime.dispatcher"] = nameof(NativeStandaloneTriggerRuntimeTarget),
            ["runtime.artifactId"] = recipient.Material.ArtifactId,
            ["runtime.artifactVersion"] = recipient.SourceProvenance.ArtifactVersion,
            ["runtime.artifactHash"] = recipient.Material.ArtifactHash,
            [RuntimeMetadataKeys.WorkflowExecutionOrigin] = WorkflowExecutableReferenceScope.Published.ToString(),
            [RuntimeMetadataKeys.SourceReferenceId] = recipient.SourceProvenance.SourceReferenceId!,
            [OriginMetadata] = JsonSerializer.Serialize(origin)
        };
        if (persistenceModes is not null) metadata[RuntimeMetadataKeys.PersistenceMode] = persistenceModes.Resolve(executable).ToString();
        var command = new WorkflowExecutionCommand(recipient.StartIdentity.CommandId, recipient.StartIdentity.WorkflowExecutionId,
            WorkflowExecutionCommandKind.Start, source.RecordedAt, JsonSerializer.SerializeToElement(payload), metadata);
        var envelope = new WorkflowExecutionCommandEnvelope(recipient.StartIdentity.EnvelopeId, recipient.StartIdentity.WorkflowExecutionId,
            command, recipient.StartIdentity.IdempotencyKey, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, source.RecordedAt,
            metadata: metadata, partition: recipient.Partition);
        return new(1, source.OutboxItemId, source.SourceFingerprint, source.SubscriptionDelivery.SelectionFingerprint, recipient.RecipientId,
            recipient.StartIdentity, source.RecordedAt, Protocol, 1, JsonSerializer.SerializeToElement(envelope));
    }

    public async ValueTask<RuntimeTargetAcceptanceReceipt?> ReconcileAsync(RuntimePostCommitOutboxItem source,
        RuntimeSubscriptionRecipient recipient, RuntimeDispatchSeal seal, CancellationToken cancellationToken = default)
    {
        var actual = await ingress.ReadCommittedRecipientAsync(NativeIngressTargetEvidence.Origin(source, recipient), cancellationToken);
        NativeIngressTargetEvidence.ValidateSeal(actual, seal);
        return await evidence.ReconcileAsync(actual.Item, actual.Recipient, actual.Seal, cancellationToken);
    }

    public async ValueTask DispatchAsync(RuntimePostCommitOutboxItem source, RuntimeSubscriptionRecipient recipient,
        RuntimeDispatchSeal seal, CancellationToken cancellationToken = default)
    {
        var actual = await ingress.ReadCommittedRecipientAsync(NativeIngressTargetEvidence.Origin(source, recipient), cancellationToken);
        NativeIngressTargetEvidence.ValidateSeal(actual, seal);
        if (await evidence.ReconcileAsync(actual.Item, actual.Recipient, actual.Seal, cancellationToken) is not null) return;
        await CheckPolicyAsync(actual.Recipient, cancellationToken);
        var envelope = NativeIngressTargetEvidence.ReadEnvelope(actual.Seal);
        var actor = await actors.GetAgentAsync(new(envelope.WorkflowExecutionId, WorkflowExecutionActorActivationReason.Start,
            envelope.EnqueuedAt, actual.Recipient.Authority.SystemIdentity, WorkflowExecutionActorCapabilities.None,
            envelope.Metadata, envelope.Partition), cancellationToken);
        var result = await actor.EnqueueAsync(envelope, cancellationToken);
        if (result.Status == WorkflowExecutionCommandDispatchStatus.Rejected || result.IsShed)
            throw new TriggerOperationException("unavailable", "The target has not accepted its committed native dispatch.");
        // Accepted/Deferred/AcceptedButFaulted are observations only. The caller must reconcile physical evidence.
    }

    private async ValueTask CheckPolicyAsync(RuntimeSubscriptionRecipient recipient, CancellationToken ct)
    {
        var decision = await policy.EvaluateAsync(new(recipient.Material, WorkflowExecutableStartAuthorityKind.StandaloneTrigger,
            recipient.Authority.SystemIdentity, recipient.Authority, WorkflowRunKind.PublishedRun, recipient.TenantId, recipient.Partition), ct);
        if (!decision.IsAllowed) throw new TriggerOperationException("target-start-policy-denied", "The current host start policy blocks this accepted recipient.");
    }
}
