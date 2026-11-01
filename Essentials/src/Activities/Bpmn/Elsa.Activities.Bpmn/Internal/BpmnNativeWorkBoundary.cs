using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Workflows.Runtime.Core.Constants;
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
    IPersistenceAccessContextAccessor access, TimeProvider clock, IWorkflowSchedulerWorkQueue queue,
    IRuntimeModelExecutionReader? models = null,
    CoalescingInner<IWorkflowExecutionStateStore>? physicalWorkflows = null,
    CoalescingInner<IActivityExecutionStateStore>? physicalActivities = null,
    CoalescingInner<IRuntimePostCommitOutboxStore>? physicalOutbox = null,
    CoalescingInner<IPostCommitOutboxLookupStore>? physicalLookup = null)
{
    internal const string StartKind = "Elsa.Bpmn.StartNativeWork.v1";
    internal const string ResultKind = "Elsa.Bpmn.NativeWorkResult.v1";
    internal const string CancelKind = "Elsa.Bpmn.CancelNativeWork.v1";
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
        await ValidateRetainedResultAsync(state, result, cancellationToken);
        // Worker-origin cancellation owes the same downstream settlement as model-origin cancellation.
        await RequireCancellationSettlementAsync(result, cancellationToken);
    }

    internal async ValueTask ValidateRetainedResultAsync(RuntimeModelExecutionState state, BpmnNativeWorkResult result,
        CancellationToken cancellationToken)
    {
        EnsureQualified();
        var origin = result.Origin;
        access.Current.EnsureScope(state.Scope);
        access.Current.EnsureTenantScope(state.TenantId);
        if (!origin.MatchesModel(state) || !result.Status.IsTerminal())
            throw new BpmnModelValidationException("The retained native result does not address this model execution.");
        var source = await PhysicalLookup.FindDurableAsync(result.SourceOutboxItemId, cancellationToken)
            ?? throw Unavailable("The retained native result source is unavailable.");
        var worker = await FindWorkerAsync(origin, cancellationToken) ?? throw Unavailable("The native result owner is unavailable.");
        if (worker.Status != result.Status || worker.TenantId != state.TenantId || worker.Authority is null ||
            !WorkflowExecutionAuthoritySnapshot.Matches(state.Authority, worker.Authority.SystemIdentity, worker.Authority.RootInitiator, worker.Authority.Metadata) ||
            source.StimulusDeliverySourceScope != state.Scope || source.Intent.Kind != ResultKind ||
            source.Intent.WorkflowExecutionId != origin.WorkerExecutionId || source.Intent.Payload is not { } payload ||
            !JsonElement.DeepEquals(payload, JsonSerializer.SerializeToElement(result, Web)))
            throw new BpmnModelValidationException("The native result differs from its committed terminal owner and outbox source.");
    }

    private async ValueTask RequireCancellationSettlementAsync(BpmnNativeWorkResult result, CancellationToken cancellationToken)
    {
        if (result.Status != WorkflowExecutionStatus.Cancelled) return; // Preserve an already completed/faulted worker's actual result.
        // A scope cancellation may have committed its child intent before the terminal checkpoint. Read the
        // physical execution's responsibility as well, including results already queued by the older writer.
        // ValidateRetainedResultAsync has proved this exact native owner physically Cancelled. Native scheduler
        // continuation intents can only enqueue into their own execution (the dispatcher enforces that address),
        // whose terminal drain refuses further work. Those obsolete continuations do not owe child cancellation.
        // Keep every other retained kind fenced, even when delayed, claimed or failed at an earlier checkpoint.
        if (await PhysicalLookup.FindUndeliveredDurableAsync(result.Origin.WorkerExecutionId, [ResultKind, RuntimePostCommitIntentKinds.EnqueueSchedulerWork], cancellationToken) is { } prior)
            throw Unavailable($"Native downstream source '{prior.OutboxItemId}' remains owed.");
        if (result.CancellationSources is not { } sources)
            throw Unavailable("The native cancellation has no retained downstream settlement inventory.");
        // Intent ids can contain colons. Remove the exact deterministic result intent, rather than splitting an address.
        var resultIntent = "bpmn-native-result:" + BpmnModelIdentity.Create(result.Origin.WorkerExecutionId, result.Origin.InvocationId, result.Origin.AttemptId);
        if (!result.SourceOutboxItemId.EndsWith(":" + resultIntent, StringComparison.Ordinal))
            throw new BpmnModelValidationException("The native cancellation result source identity is invalid.");
        var checkpoint = result.SourceOutboxItemId[..^(resultIntent.Length + 1)];
        if (sources.Distinct(StringComparer.Ordinal).Count() != sources.Count)
            throw new BpmnModelValidationException("The native cancellation settlement inventory contains duplicates.");
        foreach (var id in sources)
        {
            var source = await PhysicalLookup.FindDurableAsync(id, cancellationToken)
                ?? throw Unavailable("A native downstream cancellation source is unavailable.");
            // Older integrations such as ETL own durable intents without opting into claimed source delivery.
            // The qualified physical lookup proves their scope; an explicit source scope must still match.
            if (source.StimulusDeliverySourceScope is { } scope && scope != result.Origin.Scope ||
                source.Intent.WorkflowExecutionId != result.Origin.WorkerExecutionId || source.Intent.Kind == ResultKind ||
                RuntimePostCommitOutboxItems.OutboxItemId(checkpoint, source.Intent.IntentId) != id)
                throw new BpmnModelValidationException("A native downstream cancellation source differs from its terminal checkpoint.");
            if (source.Status != RuntimePostCommitOutboxStatus.Delivered)
                throw Unavailable("Native downstream cancellation remains owed.");
        }
    }

    internal async ValueTask<RuntimeModelExecutionState> RequireCancellationAsync(PostCommitDeliveryContext source,
        BpmnNativeWorkCancellation cancellation, CancellationToken cancellationToken)
    {
        EnsureQualified();
        access.Current.EnsureScope(source.Scope);
        var origin = cancellation.Origin;
        var model = await models!.FindAsync(origin.ExecutionId, cancellationToken)
            ?? throw Unavailable("The cancelling model execution is unavailable.");
        access.Current.EnsureTenantScope(model.TenantId);
        var document = BpmnDirectExecutionDriver.Read(model);
        var retained = document.Cancellations?.SingleOrDefault(item => origin.IsEquivalentTo(item.Origin));
        if (!origin.MatchesModel(model) || document.FormatVersion is not (11 or 12 or 13 or 14) && model.TerminalResult?.GetProperty("status").GetString() != "Cancelled" ||
            source.Scope != origin.Scope || source.Intent.WorkflowExecutionId != origin.ExecutionId ||
            cancellation.SourceOutboxItemId != source.SourceOutboxItemId || cancellation.Settled ||
            retained is null || retained.SourceOutboxItemId != cancellation.SourceOutboxItemId ||
            source.Intent.IntentId != CancellationIntentId(origin) || source.Intent.Payload is not { } payload ||
            !JsonElement.DeepEquals(payload, JsonSerializer.SerializeToElement(cancellation, Web)))
            throw new BpmnModelValidationException("The native cancellation differs from its model's retained responsibility.");
        return model;
    }

    internal static string CancellationIntentId(RuntimeModelNativeWorkOrigin origin) =>
        "bpmn-cancel-native:" + BpmnModelIdentity.Create(origin.InvocationId, origin.AttemptId);

    internal static string RetirementInputId(BpmnNativeWorkCancellation cancellation) =>
        "native-start-retired:" + BpmnModelIdentity.Create(cancellation.Origin.SourceOutboxItemId, cancellation.SourceOutboxItemId);

    internal async ValueTask<(RuntimeModelExecutionState Model, BpmnNativeWorkCancellation Cancellation, RuntimePostCommitOutboxItem Source)?>
        FindCancellationAsync(RuntimeModelNativeWorkOrigin origin, CancellationToken cancellationToken)
    {
        EnsureQualified(); access.Current.EnsureScope(origin.Scope);
        var model = await models!.FindAsync(origin.ExecutionId, cancellationToken)
            ?? throw Unavailable("The model execution authorizing this native start is unavailable.");
        access.Current.EnsureTenantScope(model.TenantId);
        if (!origin.MatchesModel(model)) throw new BpmnModelValidationException("The native start's model pin changed.");
        var document = BpmnDirectExecutionDriver.Read(model);
        var cancellation = document.Cancellations?.SingleOrDefault(item => origin.IsEquivalentTo(item.Origin));
        if (cancellation is null) return null;
        var source = await PhysicalLookup.FindDurableAsync(cancellation.SourceOutboxItemId, cancellationToken)
            ?? throw Unavailable("The model's committed cancellation source is unavailable.");
        if (document.FormatVersion is not (11 or 12 or 13 or 14) && model.TerminalResult?.GetProperty("status").GetString() != "Cancelled" || source.StimulusDeliverySourceScope != origin.Scope ||
            source.Intent.Kind != CancelKind || source.Intent.WorkflowExecutionId != model.ExecutionId ||
            source.Intent.IntentId != CancellationIntentId(origin) || source.Intent.Payload is not { } payload ||
            !JsonElement.DeepEquals(payload, JsonSerializer.SerializeToElement(cancellation with { Settled = false, NotAdmitted = false }, Web)))
            throw new BpmnModelValidationException("Native start retirement requires its exact committed model cancellation.");
        return (model, cancellation, source);
    }

    internal async ValueTask<RuntimePostCommitOutboxItem> RequireStartSourceAsync(RuntimeModelNativeWorkOrigin origin, CancellationToken cancellationToken)
    {
        var source = await PhysicalLookup.FindDurableAsync(origin.SourceOutboxItemId, cancellationToken)
            ?? throw Unavailable("The original native start source is unavailable.");
        if (source.StimulusDeliverySourceScope != origin.Scope || source.Intent.Kind != StartKind ||
            source.Intent.WorkflowExecutionId != origin.ExecutionId || source.Intent.IntentId != origin.SourceIntentId ||
            source.Intent.Payload is not { } payload || !origin.IsEquivalentTo(payload.Deserialize<RuntimeModelNativeWorkOrigin>(Web)))
            throw new BpmnModelValidationException("The original native start source differs from its retired invocation.");
        return source;
    }

    internal static RuntimeModelExecutionCommand RetirementCommand(RuntimeModelExecutionState model, BpmnNativeWorkCancellation cancellation) =>
        new(model.Executable, model.MaterialIncarnationId, RetirementInputId(cancellation), "native-start-retired",
            JsonSerializer.SerializeToElement(cancellation with { Settled = false, NotAdmitted = false }, Web), model.Authority, model.TenantId);

    internal async ValueTask<bool> HasRetirementReceiptAsync(RuntimeModelExecutionState model, BpmnNativeWorkCancellation cancellation,
        CancellationToken cancellationToken)
    {
        var command = RetirementCommand(model, cancellation);
        var receipt = await models!.FindInputAsync(model.ExecutionId, command.InputId, cancellationToken);
        if (receipt is null) return false;
        if (!cancellation.Settled || !cancellation.NotAdmitted)
            throw Unavailable("The native start retirement receipt advanced beyond the observed model snapshot.");
        if (receipt.InputKind != command.InputKind || receipt.ContentFingerprint != RuntimeCheckpointCommitFingerprint.ComputeModelInput(command) ||
            receipt.Result.GetProperty("status").GetString() != "Accepted")
            throw new BpmnModelValidationException("Native start retirement differs from its exact retained model receipt.");
        return true;
    }

    internal async ValueTask RequireNoQueuedStartAsync(RuntimeModelNativeWorkOrigin origin, CancellationToken cancellationToken)
    {
        if (!queue.SupportsClaimTransitions || !queue.SupportsClaimValidation)
            throw new NotSupportedException("Native start retirement requires the qualified claimed scheduler queue.");
        var page = await queue.ListAsync(new(origin.WorkerExecutionId, limit: 1), cancellationToken);
        if (page.Items.Count != 0 || page.NextContinuationToken is not null)
            throw Unavailable("The original native start still has queued work to retire.");
    }

    internal async ValueTask ValidateRetirementAsync(RuntimeModelExecutionContext context, BpmnNativeWorkCancellation cancellation,
        CancellationToken cancellationToken)
    {
        var authorized = await FindCancellationAsync(cancellation.Origin, cancellationToken)
            ?? throw new BpmnModelValidationException("The native start has no committed cancellation to retire it.");
        var expected = RetirementCommand(authorized.Model, authorized.Cancellation);
        if (context.State is null || context.State.Revision != authorized.Model.Revision)
            throw Unavailable("The cancelling model advanced before native start retirement.");
        if (context.Command.InputId != expected.InputId || RuntimeCheckpointCommitFingerprint.ComputeModelInput(context.Command) !=
            RuntimeCheckpointCommitFingerprint.ComputeModelInput(expected))
            throw new BpmnModelValidationException("The native start retirement does not address this exact cancelled invocation.");
        if (await FindWorkerAsync(cancellation.Origin, cancellationToken) is not null)
            throw new BpmnModelValidationException("An admitted native worker must retain its actual terminal result.");
        if (await HasRetirementReceiptAsync(authorized.Model, authorized.Cancellation, cancellationToken)) return;
        if (authorized.Cancellation.Settled)
            throw new BpmnModelValidationException("The native cancellation has no matching retirement receipt.");
        var start = await RequireStartSourceAsync(cancellation.Origin, cancellationToken);
        if (start.Status is RuntimePostCommitOutboxStatus.Delivered or RuntimePostCommitOutboxStatus.FailedFinal)
            throw Unavailable("A terminal native start source has no positive retirement receipt.");
        await RequireNoQueuedStartAsync(cancellation.Origin, cancellationToken);
        // This is more than an absent header: the qualified writer made cancellation irreversible before
        // this read. Every initial native admission compares that same model row atomically while Running.
        // An admission that won first would already have its header; a later one cannot commit.
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
        var cancellationSources = worker.Status == WorkflowExecutionStatus.Cancelled
            ? commit.PostCommitIntents.Where(item => item.Kind != ResultKind)
                .Select(item => RuntimePostCommitOutboxItems.OutboxItemId(commit.CommitId, item.IntentId)).Order(StringComparer.Ordinal).ToArray()
            : null;
        return new(origin, RuntimePostCommitOutboxItems.OutboxItemId(commit.CommitId, intentId), worker.Status, completion, root?.Fault?.Code, cancellationSources);
    }

    internal static RuntimeSchedulerWorkUnavailableException Unavailable(string message) => new("bpmn.native-work-unavailable", message);
}

internal sealed record BpmnNativeCompletion(string InvocationId, string AttemptId, string ContractFingerprint,
    string OutcomeKey, ValueTypeDescriptor ResultType, JsonElement? Result, bool IsRedacted);

internal sealed record BpmnNativeWorkResult(RuntimeModelNativeWorkOrigin Origin, string SourceOutboxItemId,
    WorkflowExecutionStatus Status, BpmnNativeCompletion? Completion, string? FaultCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? CancellationSources = null);

internal sealed record BpmnNativeWorkCancellation(RuntimeModelNativeWorkOrigin Origin, string SourceOutboxItemId, bool Settled = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool NotAdmitted = false);
