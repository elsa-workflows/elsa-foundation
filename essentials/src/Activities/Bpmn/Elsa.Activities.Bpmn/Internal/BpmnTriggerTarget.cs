using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Pure installed-model metadata; discovery does not construct an interpreter or delivery adapter.</summary>
internal sealed class BpmnTriggerModelSource(BpmnModelHostingOptions options) : ITriggerTargetModelSource
{
    internal static readonly string[] Profiles = [BpmnModelProtocol.Profile, BpmnModelProtocol.NativeWorkProfile,
        BpmnModelProtocol.ClaimsProfile, BpmnModelProtocol.InputsProfile];
    public IEnumerable<TriggerTargetModelDescriptor> GetModels() =>
        [new("bpmn", BpmnModelProtocol.Key, BpmnModelProtocol.Version, Array.AsReadOnly(Profiles), false, AcceptsNewStarts: options.AdmitNewExecutions)];
}

/// <summary>Captured standalone subscriptions enter the ordinary model root through the immutable physical dispatch seal.</summary>
internal sealed class BpmnTriggerTarget(IWorkflowExecutableStore materials,
    IWorkflowSchedulerWorkQueue selectedQueue, IRuntimeIngressDeliveryStore ingress, IWorkflowExecutionActorProvider actors,
    IWorkflowExecutableInputValidator inputs, BpmnDirectExecutionDriver driver, IPersistenceAccessContextAccessor access,
    TimeProvider clock, IRuntimeModelExecutionReader? models = null,
    CoalescingInner<IWorkflowSchedulerWorkQueue>? physicalQueue = null) : IStandaloneTriggerRuntimeTarget
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private IWorkflowSchedulerWorkQueue Queue => selectedQueue.GetType() == typeof(CoalescingWorkflowSchedulerWorkQueue)
        ? physicalQueue?.Value ?? throw new NotSupportedException("The actual model queue is unavailable.") : selectedQueue;
    public string ModelKey => "bpmn";
    public bool Supports(WorkflowExecutable? nativeWorkflow, RuntimeModelMaterial? model) => driver.AdmitNewExecutions && MatchesMaterial(nativeWorkflow, model);
    private static bool MatchesMaterial(WorkflowExecutable? nativeWorkflow, RuntimeModelMaterial? model) => nativeWorkflow is null &&
        model is not null && model.Protocol == new RuntimeExecutionProtocol(BpmnModelProtocol.Key, BpmnModelProtocol.Version) &&
        BpmnTriggerModelSource.Profiles.Contains(model.Profile, StringComparer.Ordinal);

    public async ValueTask<RuntimeDispatchSeal> PrepareDispatchAsync(RuntimePostCommitOutboxItem source,
        RuntimeSubscriptionRecipient recipient, CancellationToken cancellationToken = default)
    {
        var selection = Check(source, recipient);
        var material = await materials.FindMaterialAsync(recipient.Material.ArtifactId, cancellationToken);
        if (material is null || !MatchesMaterial(material.NativeWorkflow, material.Model) || material.IncarnationId != recipient.MaterialIncarnation ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(material.Identity, recipient.Material) ||
            material.Model!.Protocol != recipient.ModelProtocol || material.Model.Profile != recipient.ModelProfile ||
            RuntimeIngressAdmission.ComputeInputContractFingerprint(material.Model.InputContract) != recipient.InputContractFingerprint)
            throw new BpmnModelValidationException("The captured BPMN model material or input contract is unavailable.");
        var validation = inputs.Validate(material.Model.InputContract, recipient.Inputs);
        BpmnInputSupport.Check(validation);
        if (!BpmnDirectExecutionDriver.SameInputs(recipient.Inputs, validation.NormalizedInputs))
            throw new BpmnModelValidationException("The captured model inputs are not the normalized admission snapshot.");
        await driver.ValidateMaterialAsync(material.Model, cancellationToken);
        var command = Command(source, selection, recipient);
        return new(1, source.OutboxItemId, source.SourceFingerprint, selection.SelectionFingerprint, recipient.RecipientId,
            recipient.StartIdentity, clock.GetUtcNow(), BpmnModelProtocol.Key, BpmnModelProtocol.Version, JsonSerializer.SerializeToElement(command, Web));
    }

    public async ValueTask<RuntimeTargetAcceptanceReceipt?> ReconcileAsync(RuntimePostCommitOutboxItem source,
        RuntimeSubscriptionRecipient recipient, RuntimeDispatchSeal seal, CancellationToken cancellationToken = default)
    {
        var command = await RequireSealAsync(source, recipient, seal, cancellationToken);
        var query = new RuntimeSchedulerWorkQuery(recipient.StartIdentity.WorkflowExecutionId);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var page = await Queue.ListAsync(query, cancellationToken);
            foreach (var work in page.Items.Where(item => item.WorkItemId == RuntimeModelExecutionCommand.CreateSchedulerWorkItemId(command.InputId)))
            {
                var queued = work.Payload is { } payload ? ReadCommand(payload, source, recipient, "queued") : null;
                if (work.CommandKind != WorkflowExecutionCommandKind.ExecuteModel || queued is null || work.CommandId != command.InputId ||
                    RuntimeCheckpointCommitFingerprint.ComputeModelInput(queued) != RuntimeCheckpointCommitFingerprint.ComputeModelInput(command))
                    throw new RuntimeCheckpointReplayConflictException(command.InputId);
                return Receipt(RuntimeTargetAcceptanceKind.SchedulerCommand, work.WorkItemId);
            }
            if (page.NextContinuationToken is null) break;
            if (!seen.Add(page.NextContinuationToken)) throw new BpmnModelValidationException("The model queue page did not advance.");
            query = new(query.WorkflowExecutionId, continuationToken: page.NextContinuationToken);
        }
        var reader = models ?? throw new NotSupportedException("The actual model state/input receipt reader is unavailable.");
        if (await reader.FindInputAsync(recipient.StartIdentity.WorkflowExecutionId, command.InputId, cancellationToken) is not { } accepted) return null;
        if (accepted.InputKind != command.InputKind || accepted.ContentFingerprint != RuntimeCheckpointCommitFingerprint.ComputeModelInput(command))
            throw new RuntimeCheckpointReplayConflictException(command.InputId);
        var state = await reader.FindAsync(recipient.StartIdentity.WorkflowExecutionId, cancellationToken);
        if (state is null || state.Scope != recipient.StartIdentity.Scope || state.TenantId != recipient.TenantId ||
            state.MaterialIncarnationId != recipient.MaterialIncarnation || !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(state.Executable, recipient.Material) ||
            !WorkflowExecutionAuthoritySnapshot.Matches(state.Authority, recipient.Authority.SystemIdentity, recipient.Authority.RootInitiator, recipient.Authority.Metadata))
            throw new RuntimeCheckpointReplayConflictException(command.InputId);
        return Receipt(RuntimeTargetAcceptanceKind.ModelInput, accepted.InputId);

        RuntimeTargetAcceptanceReceipt Receipt(RuntimeTargetAcceptanceKind kind, string evidence) =>
            new(recipient.RecipientId, seal.Fingerprint, recipient.StartIdentity, recipient.Material, kind, evidence, clock.GetUtcNow());
    }

    public async ValueTask DispatchAsync(RuntimePostCommitOutboxItem source, RuntimeSubscriptionRecipient recipient,
        RuntimeDispatchSeal seal, CancellationToken cancellationToken = default)
    {
        var command = await RequireSealAsync(source, recipient, seal, cancellationToken);
        if (actors is not InProcessWorkflowExecutionActorProvider || !Queue.SupportsClaimTransitions || !Queue.SupportsClaimValidation)
            throw new NotSupportedException("Captured BPMN model dispatch requires the qualified ordinary model actor and queue.");
        var identity = recipient.StartIdentity;
        var actor = await actors.GetAgentAsync(new(identity.WorkflowExecutionId, WorkflowExecutionActorActivationReason.Start,
            clock.GetUtcNow(), recipient.Authority.SystemIdentity, WorkflowExecutionActorCapabilities.InProcessMailbox,
            partition: recipient.Partition), cancellationToken);
        // The ordinary protocol router owns closure retention, queue deduplication and the permitted drain.
        // This call happens only after the actual physical source committed this exact immutable seal.
        _ = await actor.EnqueueAsync(new(identity.EnvelopeId, identity.WorkflowExecutionId,
            new(command.InputId, identity.WorkflowExecutionId, WorkflowExecutionCommandKind.ExecuteModel, seal.CommittedAt,
                JsonSerializer.SerializeToElement(command, Web), new Dictionary<string, string>()), identity.IdempotencyKey,
            WorkflowExecutionCommandDeliveryMode.AtLeastOnce, seal.CommittedAt, partition: recipient.Partition), cancellationToken);
    }

    private RuntimeSubscriptionDelivery Check(RuntimePostCommitOutboxItem source, RuntimeSubscriptionRecipient recipient)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(recipient);
        var selection = source.SubscriptionDelivery ?? throw new BpmnModelValidationException("The accepted subscription selection is missing.");
        var tenant = source.StandaloneIngress is { } external ? external.TenantId : source.RecipientSnapshot is { } ordinary
            ? ordinary.Scope.TenantId : throw new BpmnModelValidationException("The accepted source scope is missing.");
        if (recipient.ModelKey != ModelKey ||
            !selection.Recipients.Any(item => item.IsEquivalentTo(recipient)) || recipient.StartIdentity.SourceOutboxItemId != source.OutboxItemId ||
            source.SourceScope != recipient.StartIdentity.Scope || recipient.TenantId != tenant)
            throw new BpmnModelValidationException("The BPMN recipient differs from its actual accepted subscription selection.");
        access.Current.EnsureScope(recipient.StartIdentity.Scope);
        return selection;
    }

    private async ValueTask<RuntimeModelExecutionCommand> RequireSealAsync(RuntimePostCommitOutboxItem source,
        RuntimeSubscriptionRecipient recipient, RuntimeDispatchSeal seal, CancellationToken cancellationToken)
    {
        var selection = Check(source, recipient);
        var committed = await ingress.ReadCommittedRecipientAsync(new RuntimeCommittedRecipientAuthority(source.OutboxItemId,
            source.SourceFingerprint, selection.SelectionFingerprint, recipient.StartIdentity.Scope, recipient.RecipientId, seal.Fingerprint), cancellationToken);
        var expected = Command(source, selection, recipient);
        var command = ReadCommand(seal.Command, source, recipient, "sealed") ?? throw new BpmnModelValidationException("The sealed model command is missing.");
        if (!committed.Recipient.IsEquivalentTo(recipient) || !committed.Seal.IsEquivalentTo(seal) ||
            seal.CommandProtocol != BpmnModelProtocol.Key || seal.CommandProtocolVersion != BpmnModelProtocol.Version ||
            RuntimeCheckpointCommitFingerprint.ComputeModelInput(command) != RuntimeCheckpointCommitFingerprint.ComputeModelInput(expected))
            throw new BpmnModelValidationException("The model dispatch differs from its actual immutable committed seal.");
        return command;
    }

    private static RuntimeModelExecutionCommand? ReadCommand(JsonElement value, RuntimePostCommitOutboxItem source,
        RuntimeSubscriptionRecipient recipient, string kind)
    {
        try { return value.Deserialize<RuntimeModelExecutionCommand>(Web); }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new BpmnModelValidationException($"The {kind} BPMN command is malformed for source '{source.OutboxItemId}', " +
                $"recipient '{recipient.RecipientId}', scope '{recipient.StartIdentity.Scope.Value}'.", exception);
        }
    }

    private static RuntimeModelExecutionCommand Command(RuntimePostCommitOutboxItem source, RuntimeSubscriptionDelivery selection,
        RuntimeSubscriptionRecipient recipient) => new(recipient.Material,
        recipient.MaterialIncarnation ?? throw new BpmnModelValidationException("The model capture lacks its physical incarnation."),
        recipient.StartIdentity.InputId, BpmnDirectExecutionDriver.TriggerKind, JsonSerializer.SerializeToElement(new
        {
            inputs = recipient.Inputs, sourceReferenceId = recipient.SourceProvenance.SourceReferenceId,
            origin = new RuntimeIngressCommandOrigin(source.OutboxItemId, source.SourceFingerprint, selection.SelectionFingerprint,
                recipient.StartIdentity.Scope, recipient.RecipientId), trigger = recipient
        }, Web), recipient.Authority, recipient.TenantId)
        { IngressOrigin = new(source.OutboxItemId, source.SourceFingerprint, selection.SelectionFingerprint, recipient.StartIdentity.Scope, recipient.RecipientId) };
}
