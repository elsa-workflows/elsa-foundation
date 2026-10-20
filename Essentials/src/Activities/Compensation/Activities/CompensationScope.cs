using System.Globalization;
using Elsa.Activities.Compensation.Exceptions;
using Elsa.Activities.Compensation.Internal;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Compensation.Activities;

/// <summary>
/// Runs named steps in order and, when a step faults, undoes the steps that completed: it absorbs the fault and runs
/// the compensation handlers of the completed steps one at a time, newest first, then completes with
/// <see cref="CompensatedOutcome"/>. A scope whose steps all complete finishes with <c>Done</c> and runs no handler.
/// </summary>
/// <remarks>
/// <para>
/// The scope keeps a journal of completed forward occurrences in its private state, committed in the same checkpoint
/// as each child evaluation. A handler is scheduled with its forward occurrence as the scheduling execution, so its
/// inputs bind to that exact occurrence's committed result. An entry is marked compensating in the same commit as
/// its handler's schedule request, so a restart never runs a handler twice for one entry.
/// </para>
/// <para>
/// A step that faults with category <see cref="OutcomeUnknownCategory"/> may have had its effect: it is journaled as
/// uncertain and undone like a completed step. A failed handler is retried with the same operation key up to
/// <see cref="HandlerMaxAttempts"/> times; after that its incident stays blocking with Wait for intervention and an
/// operator resumes it with the <c>RetryFaultedActivity/1</c> alteration. Workflow cancellation runs no handler. See
/// <c>specs/250-native-compensation</c> and <c>specs/251-compensation-uncertainty</c>.
/// </para>
/// <para>
/// Scopes nest (spec 258). A scope that completes <c>Done</c> hands its unconfirmed entries to the nearest enclosing
/// scope, which journals them and can later run their handlers; <c>confirmOnComplete</c> confirms them instead. A
/// <see cref="Confirm"/> activity confirms entries, and a <see cref="Compensate"/> activity starts recovery explicitly.
/// Faults inside a Sequence or a loop step reach the scope as faults of that step.
/// </para>
/// </remarks>
[Activity(Icon = "Undo2")]
[ActivityStructure(StructureKind, StructureSchemaVersion, Mode = DesignerMode)]
[ActivityOutcome(ActivityOutcomes.Done)]
[ActivityOutcome(CompensatedOutcome)]
[ActivityChildSlot(
    StepSlotPrefix,
    "steps",
    "Steps",
    ActivityChildSlotCardinalities.Single,
    CollectionProperty = "steps",
    ChildProperty = "activity",
    LabelProperty = "name",
    SlotNameTemplate = StepSlotPrefix + "[{name}]")]
[ActivityChildSlot(
    CompensationSlotPrefix,
    "steps",
    "Compensations",
    ActivityChildSlotCardinalities.Single,
    CollectionProperty = "steps",
    ChildProperty = "compensation",
    LabelProperty = "name",
    SlotNameTemplate = CompensationSlotPrefix + "[{name}]")]
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class CompensationScope : StructuralActivity,
    IRuntimeStructuralActivity,
    IRuntimeActivityChildCompletionHandler,
    IRuntimeActivityChildFaultHandler,
    IRuntimeActivityChildOutcomeHandler,
    IRuntimeActivityChildNotificationHandler,
    IRuntimeActivityDescendantFaultHandler
{
    public const string StructureKind = "elsa.compensation-scope.structure";
    public const string StructureSchemaVersion = "1.0.0";
    public const string DesignerMode = "compensation";
    public const string StepSlotPrefix = "CompensationScope.Step";
    public const string CompensationSlotPrefix = "CompensationScope.Compensation";

    /// <summary>The outcome a scope completes with after its handlers ran.</summary>
    public const string CompensatedOutcome = "Compensated";

    /// <summary>The fault code when <see cref="FaultAfterCompensation"/> re-raises a step fault after recovery.</summary>
    public const string CompensatedFaultCode = "compensation.scope.compensated";

    /// <summary>
    /// The fault code of a scope that compensated itself because a Compensate aimed at an outer scope. Its category
    /// names the target (<see cref="TargetCategoryPrefix"/>); the next scope out treats it as a fault of its step.
    /// </summary>
    public const string CompensateRequestedFaultCode = "compensation.scope.compensate-requested";

    /// <summary>
    /// The fault code of a scope whose Compensate stopped a started step that has no handler (spec 258 FR-313, research
    /// R9). It carries <see cref="OutcomeUnknownCategory"/>, so an enclosing scope treats the scope as an uncertain step.
    /// </summary>
    public const string UncertainUnhandledFaultCode = "compensation.scope.uncertain-unhandled";

    /// <summary>The fault category prefix that names the outer scope a cascading Compensate aims at.</summary>
    public const string TargetCategoryPrefix = "compensation.target:";

    /// <summary>The runtime activity type key ancestor notifications use to find the nearest enclosing scope.</summary>
    public static readonly string ActivityTypeKey = typeof(CompensationScope).FullName!;

    /// <summary>At most this many entries ride one hand-over notification (spec 258 R10).</summary>
    public const int HandOverBatchSize = 16;

    /// <summary>The codes of the ancestor notifications a scope receives (spec 258 data model).</summary>
    public static class Notifications
    {
        public const string Register = "compensation.register";
        public const string Confirm = "compensation.confirm";
        public const string Compensate = "compensation.compensate";
    }

    /// <summary>The scheduling-metadata keys a scope stamps on the children it schedules.</summary>
    public static class MetadataKeys
    {
        public const string Role = "compensation.role";
        public const string Scope = "compensation.scopeActivityExecutionId";
        public const string Step = "compensation.step";
        public const string Entry = "compensation.entryId";
        public const string Forward = "compensation.forwardActivityExecutionId";
        public const string Attempt = "compensation.attempt";
        public const string MaxAttempts = "compensation.maxAttempts";
        public const string ForwardOutcome = "compensation.forwardOutcome";
        public const string UnknownForwardOutcome = "unknown";
        public const string StepRole = "step";
        public const string HandlerRole = "handler";
        public const string OriginScope = "compensation.originScopeActivityExecutionId";
        public const string OriginEntry = "compensation.originEntryId";
        public const string Cause = "compensation.cause";
        public const string ExplicitCause = "explicit";

        // The runtime gives an assigned operation key to the child's activity context (spec 251 FR-201).
        public const string OperationKey = "runtime.operationKey";
        public const string TargetOperationKey = "runtime.targetOperationKey";
    }

    /// <summary>The reserved fault category an activity reports when it cannot tell whether its effect happened.</summary>
    public const string OutcomeUnknownCategory = "outcome-unknown";

    /// <summary>The scheduling cause recorded on handler executions.</summary>
    public const string HandlerSchedulingCause = "compensation";

    /// <summary>Builds the runtime slot name of a step's forward activity.</summary>
    public static string StepSlotName(string stepName) => $"{StepSlotPrefix}[{CompensationStepRules.Normalize(stepName)}]";

    /// <summary>Builds the runtime slot name of a step's compensation handler.</summary>
    public static string CompensationSlotName(string stepName) => $"{CompensationSlotPrefix}[{CompensationStepRules.Normalize(stepName)}]";

    /// <summary>
    /// When <c>true</c>, the scope faults with the step's fault after its handlers ran, instead of completing with
    /// <see cref="CompensatedOutcome"/>.
    /// </summary>
    [ActivityInput(Key = "faultAfterCompensation", DefaultValue = "false")]
    public bool FaultAfterCompensation { get; set; }

    /// <summary>
    /// How many times a handler runs for one entry, operator retries included, before the scope waits for an operator.
    /// Values below 1 count as 1.
    /// </summary>
    [ActivityInput(Key = "handlerMaxAttempts", DefaultValue = "3")]
    public int HandlerMaxAttempts { get; set; } = 3;

    /// <summary>
    /// When <c>true</c>, completing <c>Done</c> confirms the scope's entries instead of handing them to the enclosing
    /// scope, so nothing outside can undo them (spec 258 FR-309).
    /// </summary>
    [ActivityInput(Key = "confirmOnComplete", DefaultValue = "false")]
    public bool ConfirmOnComplete { get; set; }

    /// <summary>The selector of the nearest enclosing scope on this side of every reusable-activity boundary.</summary>
    internal static RuntimeAncestorSelector NearestScope(bool optional = false) =>
        new(ActivityTypeKey: ActivityTypeKey, StopAtGraphBoundary: true, Optional: optional);

    /// <summary>The selector of the enclosing scope authored as <paramref name="authoredActivityId"/>.</summary>
    internal static RuntimeAncestorSelector NamedScope(string authoredActivityId) =>
        new(ActivityTypeKey: ActivityTypeKey, AuthoredActivityId: authoredActivityId, StopAtGraphBoundary: true);

    private int MaxAttempts => Math.Max(1, HandlerMaxAttempts);

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(runtimeContext);
        var navigator = Navigator(runtimeContext);

        if (navigator.Steps.Count == 0)
        {
            var empty = CompensationScopeState.Initial with { Phase = CompensationScopePhase.Completed };
            return ValueTask.FromResult(CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Complete(), empty));
        }

        var state = ScheduleStep(runtimeContext, navigator, CompensationScopeState.Initial, schedulingActivityExecutionId: null);
        return ValueTask.FromResult(CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Defer, state));
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var runtimeContext = RequireRuntimeContext(context.ParentContext);
        var navigator = Navigator(runtimeContext);
        var state = LoadState(runtimeContext);
        var metadata = context.ChildSchedulingMetadata;

        if (!IsOwnChild(runtimeContext, metadata))
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);

        var role = metadata.GetValueOrDefault(MetadataKeys.Role);
        var continuation = (state.Phase, role) switch
        {
            (CompensationScopePhase.Running, MetadataKeys.StepRole) => OnStepCompleted(runtimeContext, navigator, state, context),
            (CompensationScopePhase.AwaitingIntervention, MetadataKeys.StepRole) => OnRetriedStepCompleted(runtimeContext, navigator, state, context),
            (CompensationScopePhase.Compensating or CompensationScopePhase.AwaitingIntervention, MetadataKeys.HandlerRole) =>
                OnHandlerCompleted(runtimeContext, navigator, state, context, metadata),
            _ => null
        };

        // Anything else is a repeat delivery or a stale child of a phase the scope already left (FR-009).
        return ValueTask.FromResult(continuation ?? RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ValueTask.FromResult(OnFault(
            RequireRuntimeContext(context.ParentContext),
            new ChildFault(context.FaultedChildActivityExecutionId, context.FaultedChildExecutableNodeId, context.FaultedChildIterationId,
                context.IncidentId, context.FaultCode, context.FaultCategory)));
    }

    /// <summary>
    /// A step's fault that arrived together with notifications the step raised to the scope. The fault decides; the
    /// scope's own notifications (hand-over, Confirm, Compensate) always arrive as separate ancestor notifications, so
    /// a direct child's parent notifications carry nothing the scope acts on.
    /// </summary>
    public ValueTask<RuntimeStructuralContinuation> OnChildOutcomeAsync(ActivityChildOutcomeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ValueTask.FromResult(OnFault(
            RequireRuntimeContext(context.ParentContext),
            new ChildFault(context.FaultedChildActivityExecutionId, context.FaultedChildExecutableNodeId, context.FaultedChildIterationId,
                context.IncidentId, context.Fault.Code, context.Fault.Category)));
    }

    /// <summary>
    /// A fault from inside a step or handler that is a Sequence or a loop, which do not handle their children's faults
    /// (spec 258 R-3). It is handled as a fault of that step or handler: the via child stands for it, and absorbing the
    /// fault cancels the via child's subtree. The delivery's command metadata carries the via child's routing keys. An
    /// operator retry of the faulted descendant runs again under the same via child, so a waiting scope tells the via
    /// child's later outcome from a repeat delivery by the fault's incident, not by the via child's id.
    /// </summary>
    public ValueTask<RuntimeStructuralContinuation> OnDescendantFaultedAsync(ActivityDescendantFaultedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ValueTask.FromResult(OnFault(
            RequireRuntimeContext(context.ParentContext),
            new ChildFault(context.ViaChildActivityExecutionId, context.ViaChildExecutableNodeId, IterationId: null,
                context.IncidentId, context.FaultCode, context.FaultCategory)));
    }

    private RuntimeStructuralContinuation OnFault(IRuntimeActivityExecutionContext runtimeContext, ChildFault fault)
    {
        var navigator = Navigator(runtimeContext);
        var state = LoadState(runtimeContext);
        var metadata = context.ChildSchedulingMetadata;

        // Any fault the scope does not own stays a blocking incident; the run's incident strategy decides.
        if (!IsOwnChild(runtimeContext, metadata))
            return RuntimeStructuralContinuation.Defer;

        var role = metadata.GetValueOrDefault(MetadataKeys.Role);
        var continuation = (state.Phase, role) switch
        {
            (CompensationScopePhase.Running, MetadataKeys.StepRole) => OnStepFaulted(runtimeContext, navigator, state, fault),
            (CompensationScopePhase.AwaitingIntervention, MetadataKeys.StepRole) => OnRetriedStepFaulted(runtimeContext, navigator, state, fault),
            (CompensationScopePhase.Compensating or CompensationScopePhase.AwaitingIntervention, MetadataKeys.HandlerRole) =>
                OnHandlerFaulted(runtimeContext, navigator, state, fault, metadata),
            _ => null
        };

        return continuation ?? RuntimeStructuralContinuation.Defer;
    }

    /// <summary>
    /// An ancestor notification that reached this scope through one of its children: an inner scope's hand-over, a
    /// Confirm or a Compensate (spec 258). Anything else, and anything this scope's phase does not accept, changes
    /// nothing.
    /// </summary>
    public ValueTask<RuntimeStructuralContinuation> OnChildNotifiedAsync(IRuntimeActivityExecutionContext context, ActivityChildNotifiedContext notification)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(notification);
        var navigator = Navigator(context);
        var state = LoadState(context);

        var continuation = notification.Code switch
        {
            Notifications.Register => OnHandOver(context, state, notification),
            Notifications.Confirm => OnConfirm(state, navigator, notification),
            Notifications.Compensate => OnCompensateRequested(context, navigator, state, notification),
            _ => null
        };

        return ValueTask.FromResult(continuation ?? RuntimeStructuralContinuation.Defer);
    }

    // Journals an inner scope's handed-over entries with fresh ordinals, keeping their order and first origin (FR-306).
    private static RuntimeStructuralContinuation? OnHandOver(IRuntimeActivityExecutionContext runtimeContext, CompensationScopeState state, ActivityChildNotifiedContext notification)
    {
        if (state.Phase is not (CompensationScopePhase.Running or CompensationScopePhase.Compensating or CompensationScopePhase.AwaitingIntervention))
            return null;

        var handOver = CompensationMessages.Deserialize<CompensationHandOver>(notification.Payload)
                       ?? throw new CompensationScopeExecutionException(
                           $"Compensation scope '{runtimeContext.ExecutableNode.ExecutableNodeId}' received a hand-over it cannot read.");
        if (state.ReceivedHandOvers.Contains(handOver.OriginScope, StringComparer.Ordinal))
            return null;

        var journaled = state;
        foreach (var handed in handOver.Entries)
        {
            var origin = handed.Origin ?? new CompensationEntryOrigin(handOver.OriginScope, handed.OriginEntryId);
            if (journaled.Entries.Any(entry => entry.Origin == origin))
                continue;

            var entry = new CompensationEntry(
                $"e{journaled.NextOrdinal}",
                handed.Step,
                journaled.NextOrdinal,
                handed.Status,
                handed.ForwardActivityExecutionId,
                handed.OperationKey,
                handed.ForwardOutcome,
                handed.ForwardIterationId,
                handlerExecutableNodeId: handed.HandlerExecutableNodeId,
                origin: origin);
            journaled = journaled with { NextOrdinal = journaled.NextOrdinal + 1, Entries = [.. journaled.Entries, entry] };
        }

        // Batches are separate deliveries and may arrive in any order, so the origin counts as received only when every
        // batch has been journaled; until then a repeat is still filtered per entry by its origin.
        var others = journaled.HandOversInProgress.Where(progress => !StringComparer.Ordinal.Equals(progress.OriginScope, handOver.OriginScope)).ToArray();
        var batches = (journaled.HandOversInProgress.FirstOrDefault(progress => StringComparer.Ordinal.Equals(progress.OriginScope, handOver.OriginScope))?.Batches ?? [])
            .Append(handOver.Batch).Distinct().Order().ToArray();
        journaled = Enumerable.Range(0, handOver.BatchCount).All(batches.Contains)
            ? journaled with { ReceivedHandOvers = [.. journaled.ReceivedHandOvers, handOver.OriginScope], HandOversInProgress = others }
            : journaled with { HandOversInProgress = [.. others, new CompensationHandOverProgress(handOver.OriginScope, batches)] };

        // In recovery the new entries are eligible: the next handler to run picks the newest (late result, FR-306).
        return CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Defer, journaled);
    }

    // Confirms every entry still eligible, while the scope runs forward (FR-310). During recovery the scope decides
    // alone, so a Confirm then changes nothing (spec 258 research R11).
    private static RuntimeStructuralContinuation? OnConfirm(CompensationScopeState state, CompensationScopeNavigator navigator, ActivityChildNotifiedContext notification)
    {
        if (state.Phase != CompensationScopePhase.Running || navigator.StepIndexOfForward(notification.ViaChildExecutableNodeId) is null)
            return null;

        var request = CompensationMessages.Deserialize<CompensationConfirmRequest>(notification.Payload);
        var confirmingActivityExecutionId = request?.ConfirmingActivityExecutionId ?? notification.NotifyingChildActivityExecutionId;
        if (state.Confirmations.Any(confirmation => StringComparer.Ordinal.Equals(confirmation.ActivityExecutionId, confirmingActivityExecutionId)))
            return null;

        return CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Defer, ConfirmEligible(state, confirmingActivityExecutionId));
    }

    private static CompensationScopeState ConfirmEligible(CompensationScopeState state, string confirmingActivityExecutionId)
    {
        var confirmed = state.Entries.Where(IsConfirmable).Select(entry => entry.Id).ToArray();
        return state with
        {
            Entries = state.Entries.Select(entry => IsConfirmable(entry) ? entry with { Status = CompensationEntryStatus.Confirmed } : entry).ToArray(),
            Confirmations = [.. state.Confirmations, new CompensationConfirmation(confirmingActivityExecutionId, confirmed)]
        };
    }

    private static bool IsConfirmable(CompensationEntry entry) =>
        entry.Status is CompensationEntryStatus.Registered or CompensationEntryStatus.NoHandler or CompensationEntryStatus.Uncertain;

    // A Compensate inside the running step: stop the step and recover as after a fault (FR-313). Aimed at an outer
    // scope, this scope compensates itself, then faults so the next scope out does the same (FR-314).
    private RuntimeStructuralContinuation? OnCompensateRequested(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        ActivityChildNotifiedContext notification)
    {
        // Recovery already running, or a Compensate inside a handler: there is no recovery of recovery (research R11).
        var stepIndex = state.NextStepIndex - 1;
        if (state.Phase != CompensationScopePhase.Running || stepIndex < 0 || navigator.StepIndexOfForward(notification.ViaChildExecutableNodeId) != stepIndex)
            return null;

        var request = CompensationMessages.Deserialize<CompensationCompensateRequest>(notification.Payload);
        var step = navigator.Steps[stepIndex];
        var target = string.IsNullOrWhiteSpace(request?.Target) || StringComparer.Ordinal.Equals(request.Target, runtimeContext.ExecutableNode.AuthoredActivityId)
            ? null
            : request.Target;
        var cause = new CompensationRecoveryCause(
            CompensationRecoveryCause.ExplicitKind,
            step.Name,
            notification.ViaChildActivityExecutionId,
            requestedBy: request?.RequestingActivityExecutionId ?? notification.NotifyingChildActivityExecutionId,
            target: target);

        // A step that is the Compensate itself has no effect and is already completing: nothing to journal or stop.
        if (StringComparer.Ordinal.Equals(notification.NotifyingChildActivityExecutionId, notification.ViaChildActivityExecutionId))
            return ScheduleNextHandlerOrFinish(runtimeContext, navigator, state with { Phase = CompensationScopePhase.Compensating, Cause = state.Cause ?? cause });

        // The step was running, so it may have had an effect: it is journaled uncertain, and undone when it has a handler.
        var entry = NewEntry(runtimeContext, state, stepIndex, step.Name, notification.ViaChildActivityExecutionId, forwardIterationId: null,
            CompensationEntryStatus.Uncertain, CompensationForwardOutcome.Uncertain);
        var compensating = state with
        {
            Phase = CompensationScopePhase.Compensating,
            Cause = state.Cause ?? cause,
            NextOrdinal = state.NextOrdinal + 1,
            Entries = [.. state.Entries, entry]
        };

        // A fault cannot ride the same commit as a subtree cancellation. With nothing to undo, a scope that must fault
        // does so at once and leaves the step to the next scope out, whose absorption of the fault cancels it, or to an
        // operator (research R11).
        if (NextEligible(navigator, compensating) is null && FinishFault(navigator, compensating) is { } fault)
            return CompensationScopeStatePersister.Stage(
                RuntimeStructuralContinuation.Faulted(fault),
                compensating with { Phase = CompensationScopePhase.Compensated });

        runtimeContext.RequestChildSubtreeCancellation(
            notification.ViaChildActivityExecutionId,
            $"Compensation scope stopped step '{step.Name}' because a Compensate activity asked for recovery.",
            new Dictionary<string, string>(StringComparer.Ordinal) { [MetadataKeys.Cause] = MetadataKeys.ExplicitCause });
        return ScheduleNextHandlerOrFinish(runtimeContext, navigator, compensating);
    }

    private RuntimeStructuralContinuation? OnStepFaulted(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        ChildFault fault)
    {
        var stepIndex = state.NextStepIndex - 1;
        if (navigator.StepIndexOfForward(fault.ExecutableNodeId) != stepIndex)
            return null;

        var step = navigator.Steps[stepIndex];
        var incidentId = RequireIncident(runtimeContext, fault, step.Name);

        if (StringComparer.Ordinal.Equals(fault.FaultCode, CompensateRequestedFaultCode))
            return OnInnerScopeCompensated(runtimeContext, navigator, state, fault, step, incidentId);

        if (IsOutcomeUnknown(fault))
        {
            // The effect may exist: journal the step as uncertain so it is undone, never treated as "no effect".
            var entry = NewEntry(runtimeContext, state, stepIndex, step.Name, fault.ActivityExecutionId, fault.IterationId,
                CompensationEntryStatus.Uncertain, CompensationForwardOutcome.Uncertain);
            var cause = new CompensationRecoveryCause(CompensationRecoveryCause.UncertainKind, step.Name, fault.ActivityExecutionId, incidentId);
            var journaled = state with { NextOrdinal = state.NextOrdinal + 1, Entries = [.. state.Entries, entry], Cause = cause };

            if (step.Handler is null)
                return WaitForStep(runtimeContext, journaled, entry, incidentId);

            runtimeContext.RequestChildFaultAbsorption(
                incidentId,
                $"Compensation scope started recovery because step '{step.Name}' could not confirm its outcome.",
                new Dictionary<string, string>(StringComparer.Ordinal) { [MetadataKeys.ForwardOutcome] = MetadataKeys.UnknownForwardOutcome });
            return ScheduleNextHandlerOrFinish(runtimeContext, navigator, journaled with { Phase = CompensationScopePhase.Compensating });
        }

        var faultCause = new CompensationRecoveryCause(CompensationRecoveryCause.FaultKind, step.Name, fault.ActivityExecutionId, incidentId);
        var compensating = state with { Phase = CompensationScopePhase.Compensating, Cause = state.Cause ?? faultCause };

        // A fault continuation cannot also absorb the fault. A scope that re-raises the fault and has nothing to undo
        // therefore leaves the incident as it is and faults; otherwise it absorbs the fault and starts recovery.
        if (NextEligible(navigator, compensating) is null && FaultAfterCompensation)
        {
            var finished = compensating with { Phase = CompensationScopePhase.Compensated };
            return CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Faulted(CompensatedFault(step.Name)), finished);
        }

        runtimeContext.RequestChildFaultAbsorption(incidentId, $"Compensation scope started recovery because step '{step.Name}' faulted.");
        return ScheduleNextHandlerOrFinish(runtimeContext, navigator, compensating);
    }

    // The step is an inner scope that compensated itself because a Compensate aimed further out (FR-314). The target
    // recovers as after a fault; a scope in between compensates itself too and passes the request on.
    private RuntimeStructuralContinuation OnInnerScopeCompensated(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        ChildFault fault,
        CompensationScopeStep step,
        string incidentId)
    {
        var requested = fault.FaultCategory is { } category && category.StartsWith(TargetCategoryPrefix, StringComparison.Ordinal)
            ? category[TargetCategoryPrefix.Length..]
            : null;
        var target = requested is null || StringComparer.Ordinal.Equals(requested, runtimeContext.ExecutableNode.AuthoredActivityId) ? null : requested;
        var cause = new CompensationRecoveryCause(CompensationRecoveryCause.RequestedKind, step.Name, fault.ActivityExecutionId, incidentId, target: target);
        var compensating = state with { Phase = CompensationScopePhase.Compensating, Cause = state.Cause ?? cause };

        if (NextEligible(navigator, compensating) is null && target is not null)
            return CompensationScopeStatePersister.Stage(
                RuntimeStructuralContinuation.Faulted(CompensateRequestedFault(target)),
                compensating with { Phase = CompensationScopePhase.Compensated });

        runtimeContext.RequestChildFaultAbsorption(incidentId, $"Compensation scope started recovery because step '{step.Name}' compensated itself on request.");
        return ScheduleNextHandlerOrFinish(runtimeContext, navigator, compensating);
    }

    // An uncertain step without a handler, retried by an operator (spec 251 FR-213), faulted again.
    private RuntimeStructuralContinuation? OnRetriedStepFaulted(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        ChildFault fault)
    {
        if (WaitingStepEntry(navigator, state, fault.ExecutableNodeId, fault.IncidentId) is not { } entry)
            return null;

        var resumed = Replace(state, entry with { ForwardActivityExecutionId = fault.ActivityExecutionId, InterventionIncidentId = null })
                      with { Phase = CompensationScopePhase.Running };
        return OnStepFaultedAfterRetry(runtimeContext, navigator, resumed, fault, entry.Id);
    }

    private RuntimeStructuralContinuation OnStepFaultedAfterRetry(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        ChildFault fault,
        string entryId)
    {
        var entry = state.Entries.Single(candidate => StringComparer.Ordinal.Equals(candidate.Id, entryId));
        var step = navigator.Steps[state.NextStepIndex - 1];
        var incidentId = RequireIncident(runtimeContext, fault, step.Name);

        // Still uncertain: wait again. A definite fault starts recovery of the steps before it, as in M1; the entry stays
        // uncertain, because the earlier attempt's effect is still unknown.
        if (IsOutcomeUnknown(fault))
            return WaitForStep(runtimeContext, state, entry, incidentId);

        var compensating = state with { Phase = CompensationScopePhase.Compensating };
        if (NextEligible(navigator, compensating) is null && FaultAfterCompensation)
            return CompensationScopeStatePersister.Stage(
                RuntimeStructuralContinuation.Faulted(CompensatedFault(step.Name)),
                compensating with { Phase = CompensationScopePhase.Compensated });

        runtimeContext.RequestChildFaultAbsorption(incidentId, $"Compensation scope started recovery because step '{step.Name}' faulted.");
        return ScheduleNextHandlerOrFinish(runtimeContext, navigator, compensating);
    }

    private RuntimeStructuralContinuation? OnHandlerFaulted(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        ChildFault fault,
        IReadOnlyDictionary<string, string> metadata)
    {
        if (HandlerEntry(runtimeContext, navigator, state, metadata, fault.ExecutableNodeId, fault.ActivityExecutionId, fault.IncidentId) is not { } entry)
            return null;

        var incidentId = RequireIncident(runtimeContext, fault, entry.Step);
        var faulted = new CompensationHandlerAttempt(CompensationAttemptResult.Faulted, fault.ActivityExecutionId, incidentId);
        var recorded = RecordAttempt(entry, faulted);

        if (recorded.HandlerAttempts.Count < MaxAttempts)
        {
            // Attempts left: absorb this fault and run the same handler again, with the same keys, in one commit.
            runtimeContext.RequestChildFaultAbsorption(
                incidentId,
                $"Compensation scope retries the compensation of step '{entry.Step}'.",
                AttemptMetadata(recorded.HandlerAttempts.Count));
            var retrying = Replace(state, recorded with { InterventionIncidentId = null }) with { Phase = CompensationScopePhase.Compensating };
            return ScheduleHandler(runtimeContext, navigator, retrying, recorded);
        }

        // Every attempt failed: the incident stays blocking for an operator, and nothing is reported as compensated.
        var failed = recorded with { Status = CompensationEntryStatus.Failed, InterventionIncidentId = incidentId };
        var metadataForOperator = AttemptMetadata(recorded.HandlerAttempts.Count);
        metadataForOperator[MetadataKeys.Scope] = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId;
        metadataForOperator[MetadataKeys.Entry] = entry.Id;
        metadataForOperator[MetadataKeys.Step] = entry.Step;
        runtimeContext.RequestChildFaultIntervention(
            incidentId,
            $"The compensation of step '{entry.Step}' failed on all {MaxAttempts} attempts; an operator must retry it.",
            metadataForOperator);
        return CompensationScopeStatePersister.Stage(
            RuntimeStructuralContinuation.Defer,
            Replace(state, failed) with { Phase = CompensationScopePhase.AwaitingIntervention });
    }

    private RuntimeStructuralContinuation? OnStepCompleted(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        ActivityChildCompletedContext context)
    {
        if (state.Entries.Any(entry => StringComparer.Ordinal.Equals(entry.ForwardActivityExecutionId, context.CompletedChildActivityExecutionId)))
            return null;

        var stepIndex = navigator.StepIndexOfForward(context.CompletedChildExecutableNodeId);
        if (stepIndex != state.NextStepIndex - 1)
            throw new CompensationScopeExecutionException(
                $"Compensation scope '{runtimeContext.ExecutableNode.ExecutableNodeId}' received a completion of node '{context.CompletedChildExecutableNodeId}', which is not its running step.");

        var step = navigator.Steps[stepIndex.Value];
        var entry = NewEntry(runtimeContext, state, stepIndex.Value, step.Name, context.CompletedChildActivityExecutionId, context.CompletedChildIterationId,
            step.Handler is null ? CompensationEntryStatus.NoHandler : CompensationEntryStatus.Registered,
            CompensationForwardOutcome.Completed);
        var journaled = state with { NextOrdinal = state.NextOrdinal + 1, Entries = [.. state.Entries, entry] };
        return ContinueForward(runtimeContext, navigator, journaled, context.CompletedChildActivityExecutionId);
    }

    // An uncertain step without a handler, retried by an operator (spec 251 FR-213), completed: the scope moves on.
    private RuntimeStructuralContinuation? OnRetriedStepCompleted(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        ActivityChildCompletedContext context)
    {
        if (WaitingStepEntry(navigator, state, context.CompletedChildExecutableNodeId, faultIncidentId: null) is not { } entry)
            return null;

        var settled = entry with
        {
            Status = CompensationEntryStatus.NoHandler,
            ForwardActivityExecutionId = context.CompletedChildActivityExecutionId,
            InterventionIncidentId = null
        };
        var running = Replace(state, settled) with { Phase = CompensationScopePhase.Running };
        return ContinueForward(runtimeContext, navigator, running, context.CompletedChildActivityExecutionId);
    }

    private RuntimeStructuralContinuation ContinueForward(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        string completedActivityExecutionId)
    {
        if (state.NextStepIndex < navigator.Steps.Count)
        {
            var next = ScheduleStep(runtimeContext, navigator, state, completedActivityExecutionId);
            return CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Defer, next);
        }

        // Done. The runtime clears a completed scope's private state, so whatever stays compensable moves to the nearest
        // enclosing scope in this same commit (FR-305), unless the scope confirms its entries on completion (FR-309).
        var scopeActivityExecutionId = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId;
        if (ConfirmOnComplete)
        {
            var confirmed = ConfirmEligible(state, scopeActivityExecutionId) with { Phase = CompensationScopePhase.Completed };
            return CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Complete(), confirmed);
        }

        HandOver(runtimeContext, navigator, state);
        return CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Complete(), state with { Phase = CompensationScopePhase.Completed });
    }

    // Sends the entries still eligible to the nearest enclosing scope, at most HandOverBatchSize per message, all in the
    // completing commit. A "maybe" delivery that stops at a reusable boundary: with no enclosing scope they end as kept.
    private static void HandOver(IRuntimeActivityExecutionContext runtimeContext, CompensationScopeNavigator navigator, CompensationScopeState state)
    {
        var eligible = state.Entries.Where(IsConfirmable).OrderBy(entry => entry.Ordinal).ToArray();
        if (eligible.Length == 0)
            return;

        var scopeActivityExecutionId = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId;
        var scopeIterationId = runtimeContext.ActivityExecutionState.IterationId;
        var batches = eligible.Chunk(HandOverBatchSize).ToArray();
        for (var batch = 0; batch < batches.Length; batch++)
        {
            var entries = batches[batch]
                .Select(entry => new CompensationHandedOverEntry(
                    entry.Id,
                    entry.Step,
                    entry.OperationKey,
                    entry.ForwardActivityExecutionId,
                    entry.ForwardIterationId ?? scopeIterationId,
                    entry.ForwardOutcome,
                    entry.Status,
                    HandlerNodeOf(navigator, entry),
                    entry.Origin))
                .ToArray();
            runtimeContext.RequestAncestorNotification(
                NearestScope(optional: true),
                Notifications.Register,
                CompensationMessages.Serialize(new CompensationHandOver(scopeActivityExecutionId, batch, batches.Length, entries)));
        }
    }

    private RuntimeStructuralContinuation? OnHandlerCompleted(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        ActivityChildCompletedContext context,
        IReadOnlyDictionary<string, string> metadata)
    {
        if (HandlerEntry(runtimeContext, navigator, state, metadata, context.CompletedChildExecutableNodeId, context.CompletedChildActivityExecutionId, faultIncidentId: null) is not { } entry)
            return null;

        var compensated = RecordAttempt(entry, new CompensationHandlerAttempt(CompensationAttemptResult.Completed, context.CompletedChildActivityExecutionId))
            with { Status = CompensationEntryStatus.Compensated, InterventionIncidentId = null };
        var resumed = Replace(state, compensated) with { Phase = CompensationScopePhase.Compensating };
        return ScheduleNextHandlerOrFinish(runtimeContext, navigator, resumed);
    }

    private RuntimeStructuralContinuation ScheduleNextHandlerOrFinish(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state)
    {
        if (NextEligible(navigator, state) is not { } next)
        {
            var finished = state with { Phase = CompensationScopePhase.Compensated };
            var continuation = FinishFault(navigator, state) is { } fault
                ? RuntimeStructuralContinuation.Faulted(fault)
                : RuntimeStructuralContinuation.Complete(CompensatedOutcome);
            return CompensationScopeStatePersister.Stage(continuation, finished);
        }

        return ScheduleHandler(runtimeContext, navigator, state, next);
    }

    // How a finished recovery ends: null completes Compensated.
    private ActivityFault? FinishFault(CompensationScopeNavigator navigator, CompensationScopeState state)
    {
        // A step a Compensate stopped may have had an effect nothing can undo: an operator decides (research R9). This
        // wins over a cascade, because an enclosing scope must not treat the step as undone.
        if (state.Cause is { Kind: CompensationRecoveryCause.ExplicitKind } explicitCause
            && state.Entries.Any(entry => entry.Status == CompensationEntryStatus.Uncertain
                                          && StringComparer.Ordinal.Equals(entry.ForwardActivityExecutionId, explicitCause.ActivityExecutionId)
                                          && HandlerNodeOf(navigator, entry) is null))
            return UncertainUnhandledFault(explicitCause.Step);

        return state.Cause switch
        {
            // A Compensate aimed further out: pass the request on to the next scope (FR-314).
            { Target: { } target } => CompensateRequestedFault(target),
            // Recovery the author asked for is the outcome, not a fault to re-raise (research R11).
            { Kind: CompensationRecoveryCause.ExplicitKind or CompensationRecoveryCause.RequestedKind } => null,
            _ when FaultAfterCompensation => CompensatedFault(state.Cause?.Step),
            _ => null
        };
    }

    // Schedules the next attempt of an entry's handler and marks the entry compensating, in one commit (FR-008).
    private RuntimeStructuralContinuation ScheduleHandler(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        CompensationEntry entry)
    {
        var handlerNodeId = HandlerNodeOf(navigator, entry)
                            ?? throw new CompensationScopeExecutionException(
                                $"Compensation scope '{runtimeContext.ExecutableNode.ExecutableNodeId}' entry '{entry.Id}' names step '{entry.Step}', which has no handler in the pinned structure.");

        var attempt = entry.HandlerAttempts.Count + 1;
        var scopeActivityExecutionId = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId;
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MetadataKeys.Role] = MetadataKeys.HandlerRole,
            [MetadataKeys.Scope] = scopeActivityExecutionId,
            [MetadataKeys.Step] = entry.Step,
            [MetadataKeys.Entry] = entry.Id,
            [MetadataKeys.Forward] = entry.ForwardActivityExecutionId,
            [MetadataKeys.Attempt] = attempt.ToString(CultureInfo.InvariantCulture),
            [MetadataKeys.MaxAttempts] = MaxAttempts.ToString(CultureInfo.InvariantCulture),
            // Every attempt, an operator retry included, sends the participant the same keys (spec 251 FR-202).
            [MetadataKeys.OperationKey] = $"{entry.OperationKey}/compensate",
            [MetadataKeys.TargetOperationKey] = entry.OperationKey
        };
        if (entry.Origin is { } origin)
        {
            metadata[MetadataKeys.OriginScope] = origin.ScopeActivityExecutionId;
            metadata[MetadataKeys.OriginEntry] = origin.EntryId;
        }

        // The routing keys go in both the request and the provenance metadata: an operator retry rebuilds the handler's
        // command from its provenance, so the scope still recognises the retried handler (spec 258 FR-304).
        runtimeContext.ScheduleChildActivity(
            handlerNodeId,
            entry.ForwardActivityExecutionId,
            metadata,
            ActivitySchedulingProvenance.From(
                runtimeContext.WorkflowExecutionId,
                scopeActivityExecutionId,
                entry.ForwardActivityExecutionId,
                branchId: null,
                iterationId: null,
                executionPathId: null,
                executionScopeId: null,
                schedulingCause: HandlerSchedulingCause,
                metadata: metadata));

        var compensating = entry with
        {
            Status = CompensationEntryStatus.Compensating,
            HandlerAttempts = [.. entry.HandlerAttempts, new CompensationHandlerAttempt(CompensationAttemptResult.Running)]
        };
        return CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Defer, Replace(state, compensating));
    }

    // Recovery undoes completed entries and uncertain entries that have a handler, newest first (spec 251 FR-207).
    // Confirmed entries are never eligible (FR-311); handed-over entries take part in the one newest-first order.
    private static CompensationEntry? NextEligible(CompensationScopeNavigator navigator, CompensationScopeState state) =>
        state.Entries
            .Where(entry => entry.Status is CompensationEntryStatus.Registered or CompensationEntryStatus.Uncertain && HandlerNodeOf(navigator, entry) is not null)
            .OrderByDescending(entry => entry.Ordinal)
            .FirstOrDefault();

    // A handed-over entry names its own handler node; the scope's own entries resolve it from their step. A handed-over
    // entry is never resolved by step name, which can repeat across scopes.
    private static string? HandlerNodeOf(CompensationScopeNavigator navigator, CompensationEntry entry) =>
        entry.Origin is not null
            ? entry.HandlerExecutableNodeId
            : entry.HandlerExecutableNodeId ?? navigator.FindStep(entry.Step)?.Handler?.ExecutableNodeId;

    // The entry a handler outcome belongs to, when it is the latest attempt: the running attempt of a compensating
    // entry, or an operator retry of a failed entry. Anything else is a repeat delivery or a stale attempt (FR-215).
    // A fault is passed with its incident (faultIncidentId); a completion without one.
    private static CompensationEntry? HandlerEntry(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        IReadOnlyDictionary<string, string> metadata,
        string childExecutableNodeId,
        string childActivityExecutionId,
        string? faultIncidentId)
    {
        var entryId = metadata.GetValueOrDefault(MetadataKeys.Entry);
        var entry = state.Entries.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.Id, entryId));
        if (entry is null || entry.HandlerAttempts.Any(attempt => IsRecordedOutcome(attempt, childActivityExecutionId, faultIncidentId)))
            return null;

        var isLatest = entry.Status switch
        {
            CompensationEntryStatus.Compensating => IsRunningAttempt(entry, metadata.GetValueOrDefault(MetadataKeys.Attempt)),
            CompensationEntryStatus.Failed => state.Phase == CompensationScopePhase.AwaitingIntervention,
            _ => false
        };
        if (!isLatest)
            return null;

        var handlerNodeId = HandlerNodeOf(navigator, entry);
        if (!StringComparer.Ordinal.Equals(handlerNodeId, childExecutableNodeId))
            throw new CompensationScopeExecutionException(
                $"Compensation scope '{runtimeContext.ExecutableNode.ExecutableNodeId}' received a handler outcome of node '{childExecutableNodeId}' for entry '{entry.Id}', whose handler is '{handlerNodeId}'.");
        return entry;
    }

    // Whether an attempt already records this outcome. A fault is known by its incident: an operator retry of a faulted
    // descendant runs under the same handler occurrence, whose id the faulted attempt recorded, and that occurrence's
    // completion or next fault is new. An attempt recorded without an incident falls back to the occurrence id.
    private static bool IsRecordedOutcome(CompensationHandlerAttempt attempt, string childActivityExecutionId, string? faultIncidentId)
    {
        if (faultIncidentId is null)
            return attempt.Result == CompensationAttemptResult.Completed
                   && StringComparer.Ordinal.Equals(attempt.ActivityExecutionId, childActivityExecutionId);

        return attempt.IncidentId is null
            ? StringComparer.Ordinal.Equals(attempt.ActivityExecutionId, childActivityExecutionId)
            : StringComparer.Ordinal.Equals(attempt.IncidentId, faultIncidentId);
    }

    // Whether a handler outcome with this attempt number belongs to the entry's running attempt. A handler that M1
    // scheduled carries no attempt number; the version-1 upcast gives its entry exactly one running attempt.
    internal static bool IsRunningAttempt(CompensationEntry entry, string? attempt) =>
        entry.HandlerAttempts.LastOrDefault()?.Result == CompensationAttemptResult.Running
        && (attempt is null
            ? entry.HandlerAttempts.Count == 1
            : StringComparer.Ordinal.Equals(attempt, entry.HandlerAttempts.Count.ToString(CultureInfo.InvariantCulture)));

    // The uncertain entry without a handler that the scope waits on, when the child's outcome follows an operator retry
    // of its step. A fault (faultIncidentId set) is new unless it is the incident the entry waits on; that is a repeat
    // delivery. A retried descendant runs under the same step occurrence, so the occurrence id cannot tell them apart.
    private static CompensationEntry? WaitingStepEntry(
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        string childExecutableNodeId,
        string? faultIncidentId)
    {
        var stepIndex = state.NextStepIndex - 1;
        if (stepIndex < 0 || navigator.StepIndexOfForward(childExecutableNodeId) != stepIndex)
            return null;

        var step = navigator.Steps[stepIndex];
        return state.Entries.LastOrDefault(entry =>
            entry.Origin is null
            && StringComparer.Ordinal.Equals(entry.Step, step.Name)
            && entry.Status == CompensationEntryStatus.Uncertain
            && entry.InterventionIncidentId is not null
            && step.Handler is null
            && !StringComparer.Ordinal.Equals(entry.InterventionIncidentId, faultIncidentId));
    }

    private RuntimeStructuralContinuation WaitForStep(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeState state,
        CompensationEntry entry,
        string incidentId)
    {
        runtimeContext.RequestChildFaultIntervention(
            incidentId,
            $"Step '{entry.Step}' could not confirm its outcome and has no compensation; an operator must decide.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [MetadataKeys.ForwardOutcome] = MetadataKeys.UnknownForwardOutcome,
                [MetadataKeys.Scope] = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
                [MetadataKeys.Entry] = entry.Id,
                [MetadataKeys.Step] = entry.Step
            });
        var waiting = entry with { InterventionIncidentId = incidentId };
        return CompensationScopeStatePersister.Stage(
            RuntimeStructuralContinuation.Defer,
            Replace(state, waiting) with { Phase = CompensationScopePhase.AwaitingIntervention });
    }

    private Dictionary<string, string> AttemptMetadata(int attempt) => new(StringComparer.Ordinal)
    {
        [MetadataKeys.Attempt] = attempt.ToString(CultureInfo.InvariantCulture),
        [MetadataKeys.MaxAttempts] = MaxAttempts.ToString(CultureInfo.InvariantCulture)
    };

    // Completes the running attempt, or appends the attempt of an operator retry of a failed entry.
    private static CompensationEntry RecordAttempt(CompensationEntry entry, CompensationHandlerAttempt attempt) =>
        entry.Status == CompensationEntryStatus.Compensating && entry.HandlerAttempts.Count > 0
            ? entry with { HandlerAttempts = [.. entry.HandlerAttempts.Take(entry.HandlerAttempts.Count - 1), attempt] }
            : entry with { HandlerAttempts = [.. entry.HandlerAttempts, attempt] };

    private static CompensationScopeState Replace(CompensationScopeState state, CompensationEntry entry) =>
        state with { Entries = state.Entries.Select(candidate => StringComparer.Ordinal.Equals(candidate.Id, entry.Id) ? entry : candidate).ToArray() };

    private static CompensationEntry NewEntry(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeState state,
        int stepIndex,
        string stepName,
        string forwardActivityExecutionId,
        string? forwardIterationId,
        CompensationEntryStatus status,
        CompensationForwardOutcome forwardOutcome) =>
        new(
            $"e{state.NextOrdinal}",
            stepName,
            state.NextOrdinal,
            status,
            forwardActivityExecutionId,
            StepOperationKey(runtimeContext, stepIndex),
            forwardOutcome,
            forwardIterationId);

    private static string StepOperationKey(IRuntimeActivityExecutionContext runtimeContext, int stepIndex) =>
        $"{runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId}/step/{stepIndex}";

    private static bool IsOutcomeUnknown(ChildFault fault) =>
        StringComparer.Ordinal.Equals(fault.FaultCategory, OutcomeUnknownCategory);

    private static string RequireIncident(IRuntimeActivityExecutionContext runtimeContext, ChildFault fault, string stepName) =>
        fault.IncidentId
        ?? throw new CompensationScopeExecutionException(
            $"Compensation scope '{runtimeContext.ExecutableNode.ExecutableNodeId}' cannot handle the fault of step '{stepName}': it carries no incident.");

    private static CompensationScopeState ScheduleStep(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        string? schedulingActivityExecutionId)
    {
        var step = navigator.Steps[state.NextStepIndex];
        runtimeContext.ScheduleChildActivity(
            step.Forward.ExecutableNodeId,
            schedulingActivityExecutionId ?? runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [MetadataKeys.Role] = MetadataKeys.StepRole,
                [MetadataKeys.Scope] = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
                [MetadataKeys.Step] = step.Name,
                [MetadataKeys.OperationKey] = StepOperationKey(runtimeContext, state.NextStepIndex)
            });

        return state with { NextStepIndex = state.NextStepIndex + 1 };
    }

    private static bool IsOwnChild(IRuntimeActivityExecutionContext runtimeContext, IReadOnlyDictionary<string, string> metadata) =>
        StringComparer.Ordinal.Equals(metadata.GetValueOrDefault(MetadataKeys.Scope), runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId);

    private static ActivityFault CompensatedFault(string? stepName) =>
        new(CompensatedFaultCode, $"Step '{stepName}' failed; the completed steps were compensated.");

    private static ActivityFault UncertainUnhandledFault(string stepName) =>
        new(UncertainUnhandledFaultCode, $"A Compensate activity stopped step '{stepName}', which has no handler; its effect is unknown.", category: OutcomeUnknownCategory);

    private static ActivityFault CompensateRequestedFault(string target) =>
        new(CompensateRequestedFaultCode, $"A Compensate activity asked scope '{target}' to recover; this scope compensated itself first.", category: TargetCategoryPrefix + target);

    private static CompensationScopeNavigator Navigator(IRuntimeActivityExecutionContext runtimeContext) =>
        runtimeContext.ExecutableNode.GetOrAddRoutingStructure(CompensationScopeNavigator.From);

    private static CompensationScopeState LoadState(IRuntimeActivityExecutionContext runtimeContext) =>
        CompensationScopeStatePersister.Load(runtimeContext.ActivityExecutionState)
        ?? throw new CompensationScopeExecutionException($"Compensation scope '{runtimeContext.ExecutableNode.ExecutableNodeId}' has no private state.");

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context) =>
        context as IRuntimeActivityExecutionContext
        ?? throw new CompensationScopeExecutionException("Compensation scope requires an Elsa runtime activity execution context.");

    // A fault the scope evaluates: a direct child's, or a descendant's standing in for the step or handler it came through.
    private sealed record ChildFault(string ActivityExecutionId, string ExecutableNodeId, string? IterationId, string? IncidentId, string? FaultCode, string? FaultCategory);
}
