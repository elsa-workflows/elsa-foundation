using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Configuration;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Delivers a committed child-start intent through the existing workflow start dispatcher.</summary>
public sealed class ChildStartExecutor : IRuntimePostCommitIntentHandler
{
    private const string DistributedOwningNodeMetadataKey = "runtime.distributed.owningNode";
    private const string DistributedTransportItemIdMetadataKey = "runtime.distributed.transportItemId";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IWorkflowStartDispatcher _workflowStartDispatcher;
    private readonly int _maxNestingDepth;
    private readonly IWorkflowDispatchStore? _workflowDispatchStore;
    private readonly TimeProvider _timeProvider;

    public ChildStartExecutor(IWorkflowStartDispatcher workflowStartDispatcher)
        : this(workflowStartDispatcher, Options.Create(new DispatchWorkflowOptions()), null, TimeProvider.System)
    {
    }

    public ChildStartExecutor(
        IWorkflowStartDispatcher workflowStartDispatcher,
        IOptions<DispatchWorkflowOptions> options)
        : this(workflowStartDispatcher, options, null, TimeProvider.System)
    {
    }

    public ChildStartExecutor(
        IWorkflowStartDispatcher workflowStartDispatcher,
        IWorkflowDispatchStore? workflowDispatchStore,
        TimeProvider timeProvider)
        : this(workflowStartDispatcher, Options.Create(new DispatchWorkflowOptions()), workflowDispatchStore, timeProvider)
    {
    }

    public ChildStartExecutor(
        IWorkflowStartDispatcher workflowStartDispatcher,
        IOptions<DispatchWorkflowOptions> options,
        IWorkflowDispatchStore? workflowDispatchStore,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(workflowStartDispatcher);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        DispatchWorkflowOptions.ValidateMaxNestingDepth(options.Value.MaxNestingDepth, nameof(DispatchWorkflowOptions.MaxNestingDepth));
        _workflowStartDispatcher = workflowStartDispatcher;
        _maxNestingDepth = options.Value.MaxNestingDepth;
        _workflowDispatchStore = workflowDispatchStore;
        _timeProvider = timeProvider;
    }

    public async ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!StringComparer.Ordinal.Equals(intent.Kind, DispatchWorkflowConstants.StartChildIntentKind))
            throw new InvalidOperationException($"ChildStartExecutor cannot handle post-commit intent kind '{intent.Kind}'.");
        if (intent.Payload is not { } payloadElement)
            throw new InvalidOperationException($"DispatchWorkflow child-start intent '{intent.IntentId}' has no payload.");

        var payload = payloadElement.Deserialize<WorkflowDispatchStartPayload>(SerializerOptions)
            ?? throw new InvalidOperationException($"DispatchWorkflow child-start intent '{intent.IntentId}' has an invalid payload.");
        if (payload.DispatchNestingDepth > _maxNestingDepth)
        {
            throw new InvalidOperationException(
                $"DispatchWorkflow child-start intent '{intent.IntentId}' carries nesting depth {payload.DispatchNestingDepth}, which exceeds the configured maximum of {_maxNestingDepth}.");
        }
        var identity = new WorkflowDispatchIdentity(payload.ParentWorkflowExecutionId, payload.ParentActivityExecutionId);
        if (!StringComparer.Ordinal.Equals(intent.IntentId, identity.StartIntentId) ||
            !StringComparer.Ordinal.Equals(payload.DispatchId, identity.DispatchId) ||
            !StringComparer.Ordinal.Equals(payload.ChildWorkflowExecutionId, identity.ChildWorkflowExecutionId))
        {
            throw new InvalidOperationException($"DispatchWorkflow child-start intent '{intent.IntentId}' does not match its deterministic dispatch identity.");
        }

        var retainedStart = payload.ParentExecutable is not null;
        var result = await _workflowStartDispatcher.DispatchAsync(
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
                sourceSelection: retainedStart
                    ? null
                    : new WorkflowExecutableSourceSelection(sourceReferenceId: payload.ChildSource!.SourceReferenceId),
                provenanceRequirement: retainedStart
                    ? WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy
                    : WorkflowExecutableProvenanceRequirement.RequireLiveReference,
                parentWorkflowExecutionId: payload.ParentWorkflowExecutionId,
                correlationId: payload.CorrelationId,
                tenantId: payload.TenantId,
                partition: payload.Partition,
                authority: payload.Authority,
                startAuthority: retainedStart
                    ? WorkflowExecutableStartAuthority.FromRetainedDependency(
                        payload.ParentExecutable!.ArtifactId,
                        payload.ParentExecutable.ArtifactHash,
                        payload.DispatchNodeId!)
                    : null,
                dispatchNestingDepth: payload.DispatchNestingDepth),
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

        if (result.CommandDispatch.Status is
            WorkflowExecutionCommandDispatchStatus.Accepted or
            WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted or
            WorkflowExecutionCommandDispatchStatus.Duplicate)
        {
            try
            {
                await MarkStartedAsync(payload.DispatchId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new WorkflowDispatchAdmissionProjectionException(payload.DispatchId, exception);
            }
        }
    }

    private async ValueTask MarkStartedAsync(string dispatchId, CancellationToken cancellationToken)
    {
        if (_workflowDispatchStore is null)
            return;

        var current = await _workflowDispatchStore.FindAsync(dispatchId, cancellationToken)
            ?? throw new InvalidOperationException($"Committed workflow dispatch '{dispatchId}' was not found after child admission.");
        if (IsTerminal(current.Status) || current.Status == WorkflowDispatchStatus.Started)
            return;

        try
        {
            await _workflowDispatchStore.SaveAsync(
                current.TransitionTo(WorkflowDispatchStatus.Started, _timeProvider.GetUtcNow()),
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // A child can finish synchronously while admission is returning. Its terminal checkpoint owns the
            // stronger state and must supersede this best-effort Started observation.
            var latest = await _workflowDispatchStore.FindAsync(dispatchId, cancellationToken);
            if (latest is not null && (latest.Status == WorkflowDispatchStatus.Started || IsTerminal(latest.Status)))
                return;
            throw;
        }
    }

    private static bool IsTerminal(WorkflowDispatchStatus status) => status is
        WorkflowDispatchStatus.Completed or
        WorkflowDispatchStatus.Faulted or
        WorkflowDispatchStatus.Cancelled or
        WorkflowDispatchStatus.DispatchFailed;

    private static bool HasDurableDistributedForwardingEvidence(IReadOnlyDictionary<string, string> metadata) =>
        metadata.TryGetValue(DistributedOwningNodeMetadataKey, out var owningNode) &&
        !string.IsNullOrWhiteSpace(owningNode) &&
        metadata.TryGetValue(DistributedTransportItemIdMetadataKey, out var transportItemId) &&
        !string.IsNullOrWhiteSpace(transportItemId);
}
