using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Delivers a committed child-start intent through the existing workflow start dispatcher.</summary>
public sealed class ChildStartExecutor(IWorkflowStartDispatcher workflowStartDispatcher) : IRuntimePostCommitIntentHandler
{
    private const string DistributedOwningNodeMetadataKey = "runtime.distributed.owningNode";
    private const string DistributedTransportItemIdMetadataKey = "runtime.distributed.transportItemId";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!StringComparer.Ordinal.Equals(intent.Kind, DispatchWorkflowConstants.StartChildIntentKind))
            throw new InvalidOperationException($"ChildStartExecutor cannot handle post-commit intent kind '{intent.Kind}'.");
        if (intent.Payload is not { } payloadElement)
            throw new InvalidOperationException($"DispatchWorkflow child-start intent '{intent.IntentId}' has no payload.");

        var payload = payloadElement.Deserialize<WorkflowDispatchStartPayload>(SerializerOptions)
            ?? throw new InvalidOperationException($"DispatchWorkflow child-start intent '{intent.IntentId}' has an invalid payload.");
        var identity = new WorkflowDispatchIdentity(payload.ParentWorkflowExecutionId, payload.ParentActivityExecutionId);
        if (!StringComparer.Ordinal.Equals(intent.IntentId, identity.StartIntentId) ||
            !StringComparer.Ordinal.Equals(payload.DispatchId, identity.DispatchId) ||
            !StringComparer.Ordinal.Equals(payload.ChildWorkflowExecutionId, identity.ChildWorkflowExecutionId))
        {
            throw new InvalidOperationException($"DispatchWorkflow child-start intent '{intent.IntentId}' does not match its deterministic dispatch identity.");
        }

        var result = await workflowStartDispatcher.DispatchAsync(
            new WorkflowExecutionStartDispatchRequest(
                artifactId: payload.ChildExecutable.ArtifactId,
                requestedBy: payload.Authority.SystemIdentity,
                workflowExecutionId: payload.ChildWorkflowExecutionId,
                idempotencyKey: identity.StartIdempotencyKey,
                metadata: null,
                variables: null,
                inputs: payload.Inputs.ToDictionary(item => item.Key, item => (object?)item.Value.Clone(), StringComparer.Ordinal),
                stimulusInput: null,
                triggerNodeId: null,
                runKind: payload.RunKind,
                sourceSelection: new WorkflowExecutableSourceSelection(sourceReferenceId: payload.ChildSource.SourceReferenceId),
                provenanceRequirement: WorkflowExecutableProvenanceRequirement.RequireLiveReference,
                parentWorkflowExecutionId: payload.ParentWorkflowExecutionId,
                correlationId: payload.CorrelationId,
                tenantId: payload.TenantId,
                partition: payload.Partition,
                authority: payload.Authority),
            WorkflowExecutableReferenceScope.Published,
            cancellationToken: cancellationToken);

        if (result.CommandDispatch.Status == WorkflowExecutionCommandDispatchStatus.Rejected)
        {
            throw new InvalidOperationException(
                $"DispatchWorkflow child-start intent '{intent.IntentId}' was rejected: {result.CommandDispatch.Reason ?? "no reason supplied"}.");
        }

        if (result.CommandDispatch.Status == WorkflowExecutionCommandDispatchStatus.Deferred &&
            !HasDurableDistributedForwardingEvidence(result.CommandDispatch.Metadata))
        {
            throw new InvalidOperationException(
                $"DispatchWorkflow child-start intent '{intent.IntentId}' was deferred without durable distributed-forwarding evidence: {result.CommandDispatch.Reason ?? "no reason supplied"}.");
        }
    }

    private static bool HasDurableDistributedForwardingEvidence(IReadOnlyDictionary<string, string> metadata) =>
        metadata.TryGetValue(DistributedOwningNodeMetadataKey, out var owningNode) &&
        !string.IsNullOrWhiteSpace(owningNode) &&
        metadata.TryGetValue(DistributedTransportItemIdMetadataKey, out var transportItemId) &&
        !string.IsNullOrWhiteSpace(transportItemId);
}
