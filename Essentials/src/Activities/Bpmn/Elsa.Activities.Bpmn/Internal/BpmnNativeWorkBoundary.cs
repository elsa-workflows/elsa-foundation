using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Physical source and result proofs for the actual native service worker.</summary>
internal sealed class BpmnNativeWorkBoundary(IWorkflowExecutableStore materials,
    IPostCommitOutboxLookupStore outbox, IWorkflowExecutionStateStore workflows, IActivityExecutionStateStore activities,
    IRuntimeCheckpointCommitStore checkpoints, IRuntimePostCommitOutboxStore outboxStore,
    IPersistenceAccessContextAccessor access, TimeProvider clock, IRuntimeModelExecutionReader? models = null,
    CoalescingInner<IWorkflowExecutionStateStore>? physicalWorkflows = null,
    CoalescingInner<IActivityExecutionStateStore>? physicalActivities = null,
    CoalescingInner<IRuntimePostCommitOutboxStore>? physicalOutbox = null,
    CoalescingInner<IPostCommitOutboxLookupStore>? physicalLookup = null)
{
    internal const string StartKind = "Elsa.Bpmn.StartNativeWork.v1";
    internal const string ResultKind = "Elsa.Bpmn.NativeWorkResult.v1";
    internal static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    internal IWorkflowExecutionStateStore PhysicalWorkflows => workflows.GetType() == typeof(CoalescingWorkflowExecutionStateStore)
        ? physicalWorkflows?.Value ?? throw new NotSupportedException("The physical native workflow reader is unavailable.") : workflows;
    internal IActivityExecutionStateStore PhysicalActivities => activities.GetType() == typeof(CoalescingActivityExecutionStateStore)
        ? physicalActivities?.Value ?? throw new NotSupportedException("The physical native activity reader is unavailable.") : activities;
    private IRuntimePostCommitOutboxStore PhysicalOutbox => outboxStore.GetType() == typeof(CoalescingRuntimePostCommitOutboxStore)
        ? physicalOutbox?.Value ?? throw new NotSupportedException("The physical native outbox is unavailable.") : outboxStore;
    internal IPostCommitOutboxLookupStore PhysicalLookup => physicalLookup?.Value ?? outbox;

    internal void EnsureQualified()
    {
        if (models is null || !ReferenceEquals(PhysicalLookup, PhysicalOutbox) || !checkpoints.SupportsModelAdmission ||
            !checkpoints.CanCommitModelAdmission(models, PhysicalWorkflows, PhysicalActivities, PhysicalOutbox))
            throw new NotSupportedException("Native BPMN work requires its qualified physical model admission writer and reader.");
    }

    internal static KeyedWorkflowStartIdentity WorkerIdentity(PersistenceScope scope, string model, string invocation,
        string attempt, WorkflowExecutableIdentity worker) => KeyedWorkflowStartIdentity.For(
        "bpmn-native:" + BpmnModelIdentity.Create(scope.Value, model, invocation, attempt), worker.ArtifactId);

    internal static string ResultInputId(RuntimeModelNativeWorkOrigin origin) =>
        origin.ResultInputId;

    internal async ValueTask<RuntimeModelExecutionState> RequireLiveAsync(RuntimeModelNativeWorkOrigin origin, CancellationToken cancellationToken)
    {
        EnsureQualified();
        access.Current.EnsureScope(origin.Scope);
        var state = await models!.FindAsync(origin.ExecutionId, cancellationToken) ?? throw Unavailable("The authorizing model execution is unavailable.");
        access.Current.EnsureTenantScope(state.TenantId);
        if (!origin.MatchesModel(state) || state.Lifecycle != RuntimeModelLifecycle.Running ||
            !BpmnDirectExecutionDriver.Read(state).Work.Any(work => origin.IsEquivalentTo(work.NativeWork)))
            throw Unavailable("The native worker's exact model invocation is no longer live.");
        var source = await PhysicalLookup.FindDurableAsync(origin.SourceOutboxItemId, cancellationToken) ?? throw Unavailable("The model-work source is unavailable.");
        if (source.StimulusDeliverySourceScope != origin.Scope || source.Intent.Kind != StartKind ||
            source.Status is RuntimePostCommitOutboxStatus.Delivered or RuntimePostCommitOutboxStatus.FailedFinal ||
            source.Intent.WorkflowExecutionId != origin.ExecutionId || source.Intent.IntentId != origin.SourceIntentId ||
            source.Intent.Payload is not { } payload || !origin.IsEquivalentTo(payload.Deserialize<RuntimeModelNativeWorkOrigin>(Web)))
            throw new BpmnModelValidationException("The native worker differs from its physically committed model-work source.");
        return state;
    }

    internal async ValueTask RevalidateSourceAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken)
    {
        EnsureQualified();
        access.Current.EnsureScope(source.Scope);
        var current = await PhysicalLookup.FindDurableAsync(source.SourceOutboxItemId, cancellationToken)
            ?? throw Unavailable("The native binding source is unavailable.");
        if (current.StimulusDeliverySourceScope != source.Scope ||
            RuntimeCheckpointCommitFingerprint.ComputeIntent(current.Intent) != source.IntentFingerprint)
            throw new BpmnModelValidationException("The native binding source changed its physical scope or content.");
        if (current.Status != RuntimePostCommitOutboxStatus.Delivering || current.DeliveringOwnerId != source.Claim.OwnerId ||
            current.DeliveryFencingToken != source.Claim.FencingToken || current.DeliveryVisibleAfter <= clock.GetUtcNow() ||
            current.DeliveryVisibleAfter is null)
            throw new RuntimePostCommitOutboxStaleClaimException(current.OutboxItemId, source.Claim.OwnerId,
                source.Claim.FencingToken, current.DeliveringOwnerId, current.DeliveryFencingToken, current.Status);
    }

    internal async ValueTask<WorkflowExecutionState?> FindWorkerAsync(RuntimeModelNativeWorkOrigin origin, CancellationToken cancellationToken)
    {
        EnsureQualified();
        access.Current.EnsureScope(origin.Scope);
        var state = await PhysicalWorkflows.FindAsync(origin.WorkerExecutionId, cancellationToken);
        if (state is not null && (!origin.IsEquivalentTo(state.ModelWorkOrigin) || state.ParentWorkflowExecutionId is not null ||
            state.Partition?.Value != origin.Scope.Value || !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(state.PinnedExecutable, origin.WorkerExecutable)))
            throw new BpmnModelValidationException("The native execution differs from its model worker address.");
        return state;
    }

    internal async ValueTask ValidateResultAsync(RuntimeModelExecutionContext context, BpmnNativeWorkResult result,
        CancellationToken cancellationToken)
    {
        EnsureQualified();
        var state = context.State ?? throw new BpmnModelValidationException("A native result cannot start a model execution.");
        var origin = result.Origin;
        access.Current.EnsureScope(origin.Scope);
        if (!origin.MatchesModel(state) || origin.ExecutionId != context.WorkItem.WorkflowExecutionId ||
            context.Command.InputId != ResultInputId(origin) || !result.Status.IsTerminal())
            throw new BpmnModelValidationException("The native result does not address this exact model invocation.");
        var source = await PhysicalLookup.FindDurableAsync(result.SourceOutboxItemId, cancellationToken)
            ?? throw Unavailable("The retained native result source is unavailable.");
        var worker = await FindWorkerAsync(origin, cancellationToken) ?? throw Unavailable("The native result owner is unavailable.");
        if (worker.Status != result.Status || worker.TenantId != state.TenantId || worker.Authority is null ||
            !WorkflowExecutionAuthoritySnapshot.Matches(state.Authority, worker.Authority.SystemIdentity, worker.Authority.RootInitiator, worker.Authority.Metadata) ||
            source.StimulusDeliverySourceScope != context.Scope || source.Intent.Kind != ResultKind ||
            source.Intent.WorkflowExecutionId != origin.WorkerExecutionId || source.Intent.Payload is not { } payload ||
            !JsonElement.DeepEquals(payload, JsonSerializer.SerializeToElement(result, Web)))
            throw new BpmnModelValidationException("The native result differs from its committed terminal owner and outbox source.");
    }

    internal async ValueTask<BpmnNativeWorkResult> CaptureAsync(RuntimeCheckpointCommit commit, WorkflowExecutionState worker,
        CancellationToken cancellationToken)
    {
        var origin = worker.ModelWorkOrigin!;
        var material = await materials.FindMaterialAsync(worker.PinnedExecutable.ArtifactId, cancellationToken);
        if (material?.NativeWorkflow is not { } native || !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(material.Identity, origin.WorkerExecutable))
            throw Unavailable("The native worker's complete pinned material is unavailable.");
        var candidates = commit.StateChanges.ActivityExecutions.Where(change => change.Operation == RuntimeStateChangeOperation.Upsert)
            .ToDictionary(change => change.StateId, change => change.State, StringComparer.Ordinal);
        string? continuation = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            var page = await PhysicalActivities.ListPageAsync(new(worker.WorkflowExecutionId, continuationToken: continuation), cancellationToken);
            foreach (var activity in page.Items) candidates.TryAdd(activity.InvocationId, activity);
            continuation = page.NextContinuationToken;
            if (continuation is not null && !seen.Add(continuation)) throw new BpmnModelValidationException("The native root activity page did not advance.");
        } while (continuation is not null);
        var root = candidates.Values.Where(activity => activity.Execution.ExecutableNodeId == native.RootActivity.ExecutableNodeId &&
            activity.ParentActivityExecutionId is null && activity.SupersededByActivityExecutionId is null).SingleOrDefault();
        if (worker.Status == WorkflowExecutionStatus.Completed && root?.Completion is null)
            throw Unavailable("A completed native worker has no authoritative root completion.");
        BpmnNativeCompletion? completion = null;
        if (root?.Completion is { } value)
        {
            var redacted = value.Result.Policy.HidesValue() || value.Result.Presence == ValuePresence.Withheld || value.Result.ExternalReference is not null;
            completion = new(value.InvocationId, value.AttemptId, value.ContractFingerprint, value.OutcomeKey, value.Result.Type,
                redacted ? null : value.Result.InlineValue?.Clone(), redacted);
        }
        var intentId = "bpmn-native-result:" + BpmnModelIdentity.Create(origin.WorkerExecutionId, origin.InvocationId, origin.AttemptId);
        return new(origin, RuntimePostCommitOutboxItems.OutboxItemId(commit.CommitId, intentId), worker.Status, completion, root?.Fault?.Code);
    }

    internal static RuntimeSchedulerWorkUnavailableException Unavailable(string message) => new("bpmn.native-work-unavailable", message);
}

internal sealed record BpmnNativeCompletion(string InvocationId, string AttemptId, string ContractFingerprint,
    string OutcomeKey, ValueTypeDescriptor ResultType, JsonElement? Result, bool IsRedacted);

internal sealed record BpmnNativeWorkResult(RuntimeModelNativeWorkOrigin Origin, string SourceOutboxItemId,
    WorkflowExecutionStatus Status, BpmnNativeCompletion? Completion, string? FaultCode);
