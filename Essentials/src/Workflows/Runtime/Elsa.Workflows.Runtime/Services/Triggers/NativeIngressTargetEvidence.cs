using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Values;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Reads actual queue/checkpoint/state responsibility. Absence is unknown and never releases a source.</summary>
internal sealed class NativeIngressTargetEvidence(IWorkflowSchedulerWorkQueue queue, IWorkflowExecutionStateStore executions,
    TimeProvider time, CoalescingInner<IWorkflowSchedulerWorkQueue>? innerQueue = null,
    CoalescingInner<IWorkflowExecutionStateStore>? innerExecutions = null, IWorkflowExecutableStore? executables = null)
{
    internal IWorkflowSchedulerWorkQueue PhysicalQueue => queue is CoalescingWorkflowSchedulerWorkQueue
        ? innerQueue?.Value ?? throw Unsupported() : queue;
    internal IWorkflowExecutionStateStore PhysicalExecutions => executions is CoalescingWorkflowExecutionStateStore
        ? innerExecutions?.Value ?? throw Unsupported() : executions;

    internal async ValueTask<RuntimeTargetAcceptanceReceipt?> ReconcileAsync(RuntimePostCommitOutboxItem source,
        RuntimeSubscriptionRecipient recipient, RuntimeDispatchSeal seal, CancellationToken cancellationToken = default)
    {
        ValidateSelection(source, recipient);
        var envelope = ReadEnvelope(seal);
        ValidateEnvelope(new(source, recipient, seal), envelope);
        var successorId = RuntimeChainId.Derive(envelope.EnvelopeId, $"checkpoint:{RuntimeCheckpointNames.WorkflowStarted}");
        // Read queue before state: consumption of a queued successor must commit state before removing responsibility.
        // Each request is finite; a non-observed target remains owed rather than being inferred absent/finished.
        var page = await PhysicalQueue.ListAsync(new(envelope.WorkflowExecutionId, RuntimeStorePageRequest.MaximumLimit), cancellationToken);
        foreach (var item in page.Items)
        {
            if (item.WorkItemId == envelope.EnvelopeId)
            {
                ValidateStartWorkItem(new(source, recipient, seal), item);
                return Receipt(RuntimeTargetAcceptanceKind.SchedulerCommand, item.WorkItemId);
            }
            if (item.WorkItemId == successorId)
            {
                await ValidateCheckpointAsync(new(source, recipient, seal), item, cancellationToken);
                return Receipt(RuntimeTargetAcceptanceKind.InitialCheckpoint, item.WorkItemId);
            }
        }
        if (await PhysicalExecutions.FindAsync(envelope.WorkflowExecutionId, cancellationToken) is { } state)
        {
            ValidateState(new(source, recipient, seal), state);
            return Receipt(RuntimeTargetAcceptanceKind.InitialCheckpoint, state.WorkflowExecutionId);
        }
        return null;

        RuntimeTargetAcceptanceReceipt Receipt(RuntimeTargetAcceptanceKind kind, string id) => new(recipient.RecipientId,
            seal.Fingerprint, recipient.StartIdentity, recipient.Material, kind, id, time.GetUtcNow());
    }

    internal static RuntimeIngressCommandOrigin Origin(RuntimePostCommitOutboxItem source, RuntimeSubscriptionRecipient recipient) =>
        new(source.OutboxItemId, source.SourceFingerprint, source.SubscriptionDelivery?.SelectionFingerprint ?? throw Invalid(),
            recipient.StartIdentity.Scope, recipient.RecipientId);

    internal static RuntimeIngressCommandOrigin? Origin(WorkflowExecutionCommandEnvelope envelope)
    {
        // Probe only our explicit marker. Ordinary command payloads retain their original decoder.
        if (envelope.Command.Kind != WorkflowExecutionCommandKind.Start || envelope.Command.Payload is not { } payload ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(nameof(WorkflowExecutionStartCommandPayload.StartAuthority), out var authority) ||
            authority.ValueKind != JsonValueKind.Object ||
            !authority.TryGetProperty(nameof(WorkflowExecutableStartAuthority.StandaloneTrigger), out var marker) ||
            marker.ValueKind == JsonValueKind.Null)
            return null;
        return payload.Deserialize<WorkflowExecutionStartCommandPayload>()?.StartAuthority?.StandaloneTrigger;
    }

    internal static void ValidateSelection(RuntimePostCommitOutboxItem source, RuntimeSubscriptionRecipient recipient)
    {
        if (source.SubscriptionDelivery is not { } selection || selection.SourceOutboxItemId != source.OutboxItemId ||
            selection.SourceFingerprint != source.SourceFingerprint || selection.Scope != recipient.StartIdentity.Scope ||
            selection.Recipients.SingleOrDefault(item => item.RecipientId == recipient.RecipientId)?.IsEquivalentTo(recipient) != true)
            throw Invalid();
    }
    internal static void ValidateSeal(RuntimeCommittedRecipient actual, RuntimeDispatchSeal expected)
    {
        ValidateSelection(actual.Item, actual.Recipient);
        if (!actual.Seal.IsEquivalentTo(expected)) throw Invalid();
        ValidateEnvelope(actual, ReadEnvelope(actual.Seal));
    }
    internal static WorkflowExecutionCommandEnvelope ReadEnvelope(RuntimeDispatchSeal seal)
    {
        if (seal.CommandProtocol != NativeStandaloneTriggerRuntimeTarget.Protocol || seal.CommandProtocolVersion != 1) throw Unsupported();
        try { return seal.Command.Deserialize<WorkflowExecutionCommandEnvelope>() ?? throw Invalid(); }
        catch (JsonException exception) { throw new TriggerOperationException("conflict", "The sealed native command is invalid.", innerException: exception); }
    }
    internal static void ValidateEnvelope(RuntimeCommittedRecipient committed, WorkflowExecutionCommandEnvelope envelope)
    {
        var recipient = committed.Recipient; var seal = committed.Seal; var id = recipient.StartIdentity;
        if (!Equal(JsonSerializer.SerializeToElement(envelope), seal.Command) || envelope.Command.Kind != WorkflowExecutionCommandKind.Start ||
            envelope.WorkflowExecutionId != id.WorkflowExecutionId || envelope.Command.CommandId != id.CommandId || envelope.EnvelopeId != id.EnvelopeId ||
            envelope.IdempotencyKey != id.IdempotencyKey || envelope.EnqueuedAt != seal.CommittedAt || envelope.Command.EnqueuedAt != seal.CommittedAt ||
            envelope.Partition != recipient.Partition || envelope.Command.Payload is not { } json) throw Invalid();
        var payload = json.Deserialize<WorkflowExecutionStartCommandPayload>() ?? throw Invalid();
        if (payload.StartAuthority?.Kind != WorkflowExecutableStartAuthorityKind.StandaloneTrigger ||
            !Origin(committed.Item, recipient).IsEquivalentTo(payload.StartAuthority.StandaloneTrigger) ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(payload.PinnedExecutable, recipient.Material) ||
            payload.RequestedArtifactId != recipient.Material.ArtifactId || !Equal(payload.Inputs, recipient.Inputs) ||
            payload.StimulusInput is not null ||
            !Equal(payload.PinnedSource, recipient.SourceProvenance) || !Equal(payload.Authority, recipient.Authority) ||
            payload.TenantId != recipient.TenantId || payload.Partition != recipient.Partition || payload.RunKind != WorkflowRunKind.PublishedRun ||
            payload.ParentWorkflowExecutionId is not null || payload.TriggerNodeId is not null || payload.TriggerMetadata.Count != 0 ||
            payload.DispatchNestingDepth != 0 || payload.TestScope is not null) throw Invalid();
    }
    internal static void ValidateStartWorkItem(RuntimeCommittedRecipient committed, RuntimeSchedulerWorkItem item)
    {
        var envelope = ReadEnvelope(committed.Seal); ValidateEnvelope(committed, envelope);
        if (item.WorkItemId != envelope.EnvelopeId || item.EnvelopeId != envelope.EnvelopeId || item.WorkflowExecutionId != envelope.WorkflowExecutionId ||
            item.CommandId != envelope.Command.CommandId || item.CommandKind != WorkflowExecutionCommandKind.Start ||
            item.IdempotencyKey != envelope.IdempotencyKey || item.EnqueuedAt != envelope.EnqueuedAt || item.Sequence != envelope.Sequence ||
            !Equal(item.Payload, envelope.Command.Payload) || !Equal(item.CommandMetadata, envelope.Command.Metadata) || !Equal(item.EnvelopeMetadata, envelope.Metadata)) throw Invalid();
    }
    internal static void ValidateExecutable(RuntimeCommittedRecipient committed, WorkflowExecutable executable)
    {
        var payload = ReadEnvelope(committed.Seal).Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!;
        var defaults = WorkflowExecutionStartCommandPayload.ToJsonValues(new RuntimeVariableDeclarationProjector()
            .ProjectDeclaredVariableDefaultsByName(executable.WorkflowVariables));
        if (!WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(executable.Identity, committed.Recipient.Material) ||
            executable.InputContract is not { } contract || RuntimeIngressAdmission.ComputeInputContractFingerprint(contract) != committed.Recipient.InputContractFingerprint ||
            !Equal(defaults, payload.Variables)) throw Invalid();
    }
    internal ValueTask ValidateCheckpointAsync(RuntimeCommittedRecipient committed, RuntimeSchedulerWorkItem item, CancellationToken cancellationToken) =>
        ValidateCheckpointAsync(committed, item, executables, cancellationToken);

    internal static async ValueTask ValidateCheckpointAsync(RuntimeCommittedRecipient committed, RuntimeSchedulerWorkItem item,
        IWorkflowExecutableStore? executableStore, CancellationToken cancellationToken)
    {
        var envelope = ReadEnvelope(committed.Seal); var payload = item.Payload?.Deserialize<RuntimeCheckpointCommandPayload>() ?? throw Invalid();
        var start = envelope.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!;
        if (item.CommandKind != WorkflowExecutionCommandKind.Checkpoint || item.WorkflowExecutionId != envelope.WorkflowExecutionId ||
            item.EnvelopeId != envelope.EnvelopeId || item.CommandId != RuntimeChainId.Derive(envelope.Command.CommandId, $"checkpoint:{RuntimeCheckpointNames.WorkflowStarted}") ||
            item.IdempotencyKey != RuntimeChainId.Derive(envelope.IdempotencyKey, $"checkpoint:{RuntimeCheckpointNames.WorkflowStarted}") ||
            item.EnqueuedAt != envelope.EnqueuedAt || payload.CheckpointName != RuntimeCheckpointNames.WorkflowStarted ||
            payload.IngressOrigin?.IsEquivalentTo(Origin(committed.Item, committed.Recipient)) != true ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(payload.PinnedExecutable, committed.Recipient.Material) ||
            !Equal(payload.SeedInputs, start.Inputs) || !Equal(payload.SeedVariables, start.Variables) ||
            !Equal(payload.PinnedSource, start.PinnedSource) || !Equal(payload.Authority, start.Authority) || payload.TenantId != start.TenantId ||
            payload.Partition != start.Partition || payload.ParentWorkflowExecutionId is not null || payload.RunKind != WorkflowRunKind.PublishedRun) throw Invalid();
        var executable = executableStore is null ? null : await executableStore.FindAsync(committed.Recipient.Material.ArtifactId, cancellationToken);
        if (executable is null || !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(executable.Identity, committed.Recipient.Material)) throw Unsupported();
        ValidateExecutable(committed, executable);
        var original = new RuntimeSchedulerWorkItem(envelope.EnvelopeId, envelope.WorkflowExecutionId, envelope.Command.CommandId,
            WorkflowExecutionCommandKind.Start, envelope.EnvelopeId, envelope.IdempotencyKey, envelope.EnqueuedAt, item.RecordedAt,
            envelope.Sequence, envelope.Command.Payload, envelope.Command.Metadata, envelope.Metadata);
        var expected = NativeIngressStartLineage.Build(original, start, executable.RootActivity.ExecutableNodeId);
        if (!Equal(item, expected)) throw Invalid();
    }
    internal static void ValidateState(RuntimeCommittedRecipient committed, WorkflowExecutionState state)
    {
        var recipient = committed.Recipient;
        if (state.WorkflowExecutionId != recipient.StartIdentity.WorkflowExecutionId ||
            state.IngressOrigin?.IsEquivalentTo(Origin(committed.Item, recipient)) != true ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(state.PinnedExecutable, recipient.Material) ||
            !Equal(state.PinnedSource, recipient.SourceProvenance) || !Equal(state.Authority, recipient.Authority) ||
            state.TenantId != recipient.TenantId || state.Partition != recipient.Partition || state.ParentWorkflowExecutionId is not null ||
            state.RunKind != WorkflowRunKind.PublishedRun || state.TestScope is not null) throw Invalid();
    }
    internal static bool Equal<T>(T left, T right) => JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left), JsonSerializer.SerializeToElement(right));
    private static TriggerOperationException Invalid() => new("conflict", "Native ingress responsibility differs from its exact committed origin.");
    private static TriggerOperationException Unsupported() => new("unavailable", "The selected native target evidence boundary is unsupported.");
}

/// <summary>Mailbox-internal exact witness; absence never turns an in-memory memo into an accepted duplicate.</summary>
internal interface IIngressCommandWitness
{
    ValueTask<bool> HasIngressWitnessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken);
}
