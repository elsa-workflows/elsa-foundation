using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Configuration;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Delivers a committed child-start intent through the existing workflow start dispatcher.</summary>
public sealed class ChildStartExecutor : IRuntimePostCommitIntentHandler
{
    private const string DeliveryFailureCode = "child-start-delivery-failed";
    private const string DeliveryFailureSummary = "The child workflow could not be started.";
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

        var admittedBeforeDispatch = false;
        WorkflowDispatchRecord? durableDispatch = null;
        if (_workflowDispatchStore is not null)
        {
            if (_workflowDispatchStore is not IWorkflowDispatchAdmissionStore admissionStore)
            {
                throw new InvalidOperationException(
                    $"DispatchWorkflow child start requires '{nameof(IWorkflowDispatchAdmissionStore)}' on the configured dispatch store.");
            }

            try
            {
                durableDispatch = await _workflowDispatchStore.FindAsync(payload.DispatchId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw DeliveryFailure(exception);
            }
            if (durableDispatch is null)
                throw DeliveryFailure(PostCommitFailureKind.Transient);
            if (WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(durableDispatch) || IsTerminal(durableDispatch.Status))
                return;
            ValidatePayloadAgainstDispatch(intent.IntentId, payload, durableDispatch);

            WorkflowDispatchAdmissionResult admission;
            try
            {
                admission = await admissionStore.TryAdmitAsync(
                    payload.DispatchId,
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw DeliveryFailure(exception);
            }
            if (admission.Disposition is
                WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission or
                WorkflowDispatchAdmissionDisposition.Terminal)
            {
                return;
            }

            durableDispatch = admission.Record;
            ValidatePayloadAgainstDispatch(intent.IntentId, payload, durableDispatch);
            admittedBeforeDispatch = true;
        }

        var childExecutable = durableDispatch?.ChildExecutable ?? payload.ChildExecutable;
        var childSource = durableDispatch?.ChildSource ?? payload.ChildSource;
        var authority = durableDispatch?.Authority ?? payload.Authority;
        var parentWorkflowExecutionId = durableDispatch?.ParentWorkflowExecutionId ?? payload.ParentWorkflowExecutionId;
        var correlationId = durableDispatch?.CorrelationId ?? payload.CorrelationId;
        var tenantId = durableDispatch?.TenantId ?? payload.TenantId;
        var partition = durableDispatch?.Partition ?? payload.Partition;
        var runKind = durableDispatch?.RunKind ?? payload.RunKind;
        var retainedStart = payload.ParentExecutable is not null;
        var testScope = durableDispatch?.TestScope ?? payload.TestScope;
        WorkflowExecutionStartDispatchResult result;
        try
        {
            result = await _workflowStartDispatcher.DispatchAsync(
                new WorkflowExecutionStartDispatchRequest(
                    artifactId: childExecutable.ArtifactId,
                    requestedBy: authority.SystemIdentity,
                    workflowExecutionId: payload.ChildWorkflowExecutionId,
                    idempotencyKey: identity.StartIdempotencyKey,
                    metadata: null,
                    variables: null,
                    inputs: payload.Inputs.ToDictionary(item => item.Key, item => (object?)item.Value.Clone(), StringComparer.Ordinal),
                    stimulusInput: null,
                    triggerNodeId: null,
                    runKind: runKind,
                    sourceSelection: retainedStart
                        ? null
                        : new WorkflowExecutableSourceSelection(sourceReferenceId: childSource!.SourceReferenceId),
                    provenanceRequirement: retainedStart
                        ? WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy
                        : WorkflowExecutableProvenanceRequirement.RequireLiveReference,
                    parentWorkflowExecutionId: parentWorkflowExecutionId,
                    correlationId: correlationId,
                    tenantId: tenantId,
                    partition: partition,
                    authority: authority,
                    startAuthority: retainedStart
                        ? WorkflowExecutableStartAuthority.FromRetainedDependency(
                            payload.ParentExecutable!.ArtifactId,
                            payload.ParentExecutable.ArtifactHash,
                            payload.DispatchNodeId!)
                        : null,
                    dispatchNestingDepth: payload.DispatchNestingDepth,
                    testScope: testScope),
                WorkflowExecutableReferenceScope.Published,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (RuntimeCheckpointCommitValidationException.IsCauseOf(exception))
        {
            // The child's runtime faults a child a checkpoint rule refused, which ends its dispatch and queues the parent's
            // resume, and then still reports the refusal. The child really ran, so its start is delivered.
            if (await HasChildOutcomeAsync(payload.DispatchId, cancellationToken))
                return;
            throw DeliveryFailure(exception);
        }
        catch (Exception exception)
        {
            throw DeliveryFailure(exception);
        }

        if (result.CommandDispatch.Status == WorkflowExecutionCommandDispatchStatus.Rejected)
            throw DeliveryFailure(PostCommitFailureKind.Permanent);

        // The same refusal, reported by the child's run instead of thrown, when a handler's commit was refused. The runtime
        // faults a refused child that has accepted state, which gives its dispatch the child's outcome. Without that outcome
        // the refusal came before any child state was accepted, so no child exists and its start failed. The claim
        // completion's child-evidence rule still overrules this if a child does exist.
        if (result.CommandDispatch is { Status: WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted } faulted &&
            faulted.Metadata.TryGetValue(RuntimeMetadataKeys.DispatchCheckpointRuleViolation, out var violation) &&
            StringComparer.Ordinal.Equals(violation, "true") &&
            !await HasChildOutcomeAsync(payload.DispatchId, cancellationToken))
        {
            throw DeliveryFailure(new RuntimeCheckpointCommitValidationException(
                $"A checkpoint rule refused a commit of child workflow execution '{payload.ChildWorkflowExecutionId}' before it reached an outcome: {faulted.Reason}"));
        }

        if (result.CommandDispatch.Status == WorkflowExecutionCommandDispatchStatus.Deferred &&
            !HasDurableDistributedForwardingEvidence(result.CommandDispatch.Metadata))
            throw DeliveryFailure(PostCommitFailureKind.Transient);

        if (!admittedBeforeDispatch && result.CommandDispatch.Status is
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

    /// <summary>Whether the dispatch already carries the child's own terminal outcome, as opposed to a delivery failure.</summary>
    private async ValueTask<bool> HasChildOutcomeAsync(string dispatchId, CancellationToken cancellationToken)
    {
        if (_workflowDispatchStore is null)
            return false;

        try
        {
            return await _workflowDispatchStore.FindAsync(dispatchId, cancellationToken) is
            {
                Status: WorkflowDispatchStatus.Completed or WorkflowDispatchStatus.Faulted or WorkflowDispatchStatus.Cancelled
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The refusal stays the reported failure; a dispatch that cannot be read proves no outcome.
            return false;
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

    private static RuntimePostCommitDeliveryException DeliveryFailure(
        PostCommitFailureKind kind,
        Exception? innerException = null) =>
        new(kind, DeliveryFailureCode, DeliveryFailureSummary, innerException);

    /// <summary>
    /// A checkpoint rule violation is permanent: the child's run commits the same refused checkpoint on every attempt, and
    /// a retry that finds the child already started would report success while its parent waits forever. Anything else may
    /// be infrastructure, so it stays transient.
    /// </summary>
    private static RuntimePostCommitDeliveryException DeliveryFailure(Exception exception) =>
        DeliveryFailure(
            RuntimeCheckpointCommitValidationException.IsCauseOf(exception) ? PostCommitFailureKind.Permanent : PostCommitFailureKind.Transient,
            exception);

    private static void ValidatePayloadAgainstDispatch(
        string intentId,
        WorkflowDispatchStartPayload payload,
        WorkflowDispatchRecord dispatch)
    {
        var payloadInputNames = payload.Inputs.Keys.Order(StringComparer.Ordinal);
        var durableInputNames = dispatch.InputDescriptors.Select(item => item.Name).Order(StringComparer.Ordinal);
        if (!StringComparer.Ordinal.Equals(payload.DispatchId, dispatch.DispatchId) ||
            !StringComparer.Ordinal.Equals(payload.ParentWorkflowExecutionId, dispatch.ParentWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(payload.ParentActivityExecutionId, dispatch.ParentActivityExecutionId) ||
            !StringComparer.Ordinal.Equals(payload.ChildWorkflowExecutionId, dispatch.ChildWorkflowExecutionId) ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(payload.ChildExecutable, dispatch.ChildExecutable) ||
            (payload.ParentExecutable is null && payload.ChildSource != dispatch.ChildSource) ||
            !StringComparer.Ordinal.Equals(payload.CorrelationId, dispatch.CorrelationId) ||
            !StringComparer.Ordinal.Equals(payload.TenantId, dispatch.TenantId) ||
            payload.Partition != dispatch.Partition ||
            payload.RunKind != dispatch.RunKind ||
            payload.DispatchNestingDepth != dispatch.DispatchNestingDepth ||
            !WorkflowTestScope.ContextEquals(payload.TestScope, dispatch.TestScope) ||
            !AuthorityEquals(payload.Authority, dispatch.Authority) ||
            !payloadInputNames.SequenceEqual(durableInputNames, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"DispatchWorkflow child-start intent '{intentId}' does not match the committed dispatch context.");
        }
    }

    private static bool AuthorityEquals(
        WorkflowExecutionAuthoritySnapshot left,
        WorkflowExecutionAuthoritySnapshot right) =>
        StringComparer.Ordinal.Equals(left.SystemIdentity, right.SystemIdentity) &&
        StringComparer.Ordinal.Equals(left.RootInitiator, right.RootInitiator) &&
        left.Metadata.Count == right.Metadata.Count &&
        left.Metadata.All(item =>
            right.Metadata.TryGetValue(item.Key, out var value) && StringComparer.Ordinal.Equals(item.Value, value));
}
