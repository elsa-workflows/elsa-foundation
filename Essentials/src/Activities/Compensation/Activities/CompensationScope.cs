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
public sealed class CompensationScope : StructuralActivity, IRuntimeStructuralActivity, IRuntimeActivityChildCompletionHandler, IRuntimeActivityChildFaultHandler
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
        var metadata = runtimeContext.SchedulerWorkItem.CommandMetadata;

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
        var runtimeContext = RequireRuntimeContext(context.ParentContext);
        var navigator = Navigator(runtimeContext);
        var state = LoadState(runtimeContext);
        var metadata = runtimeContext.SchedulerWorkItem.CommandMetadata;

        // Any fault the scope does not own stays a blocking incident; the run's incident strategy decides.
        if (!IsOwnChild(runtimeContext, metadata))
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);

        var role = metadata.GetValueOrDefault(MetadataKeys.Role);
        var continuation = (state.Phase, role) switch
        {
            (CompensationScopePhase.Running, MetadataKeys.StepRole) => OnStepFaulted(runtimeContext, navigator, state, context),
            (CompensationScopePhase.AwaitingIntervention, MetadataKeys.StepRole) => OnRetriedStepFaulted(runtimeContext, navigator, state, context),
            (CompensationScopePhase.Compensating or CompensationScopePhase.AwaitingIntervention, MetadataKeys.HandlerRole) =>
                OnHandlerFaulted(runtimeContext, navigator, state, context, metadata),
            _ => null
        };

        return ValueTask.FromResult(continuation ?? RuntimeStructuralContinuation.Defer);
    }

    private RuntimeStructuralContinuation? OnStepFaulted(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        ActivityChildFaultedContext context)
    {
        var stepIndex = state.NextStepIndex - 1;
        if (navigator.StepIndexOfForward(context.FaultedChildExecutableNodeId) != stepIndex)
            return null;

        var step = navigator.Steps[stepIndex];
        var incidentId = RequireIncident(runtimeContext, context, step.Name);

        if (IsOutcomeUnknown(context))
        {
            // The effect may exist: journal the step as uncertain so it is undone, never treated as "no effect".
            var entry = NewEntry(runtimeContext, state, stepIndex, step.Name, context.FaultedChildActivityExecutionId, context.FaultedChildIterationId,
                CompensationEntryStatus.Uncertain, CompensationForwardOutcome.Uncertain);
            var cause = new CompensationRecoveryCause(CompensationRecoveryCause.UncertainKind, step.Name, context.FaultedChildActivityExecutionId, incidentId);
            var journaled = state with { NextOrdinal = state.NextOrdinal + 1, Entries = [.. state.Entries, entry], Cause = cause };

            if (step.Handler is null)
                return WaitForStep(runtimeContext, journaled, entry, incidentId);

            runtimeContext.RequestChildFaultAbsorption(
                incidentId,
                $"Compensation scope started recovery because step '{step.Name}' could not confirm its outcome.",
                new Dictionary<string, string>(StringComparer.Ordinal) { [MetadataKeys.ForwardOutcome] = MetadataKeys.UnknownForwardOutcome });
            return ScheduleNextHandlerOrFinish(runtimeContext, navigator, journaled with { Phase = CompensationScopePhase.Compensating });
        }

        var faultCause = new CompensationRecoveryCause(CompensationRecoveryCause.FaultKind, step.Name, context.FaultedChildActivityExecutionId, incidentId);
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

    // An uncertain step without a handler, retried by an operator (spec 251 FR-213), faulted again.
    private RuntimeStructuralContinuation? OnRetriedStepFaulted(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        ActivityChildFaultedContext context)
    {
        if (WaitingStepEntry(navigator, state, context.FaultedChildExecutableNodeId, context.FaultedChildActivityExecutionId) is not { } entry)
            return null;

        var resumed = Replace(state, entry with { ForwardActivityExecutionId = context.FaultedChildActivityExecutionId, InterventionIncidentId = null })
                      with { Phase = CompensationScopePhase.Running };
        return OnStepFaultedAfterRetry(runtimeContext, navigator, resumed, context, entry.Id);
    }

    private RuntimeStructuralContinuation OnStepFaultedAfterRetry(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        ActivityChildFaultedContext context,
        string entryId)
    {
        var entry = state.Entries.Single(candidate => StringComparer.Ordinal.Equals(candidate.Id, entryId));
        var step = navigator.Steps[state.NextStepIndex - 1];
        var incidentId = RequireIncident(runtimeContext, context, step.Name);

        // Still uncertain: wait again. A definite fault starts recovery of the steps before it, as in M1; the entry stays
        // uncertain, because the earlier attempt's effect is still unknown.
        if (IsOutcomeUnknown(context))
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
        ActivityChildFaultedContext context,
        IReadOnlyDictionary<string, string> metadata)
    {
        if (HandlerEntry(runtimeContext, navigator, state, metadata, context.FaultedChildExecutableNodeId, context.FaultedChildActivityExecutionId) is not { } entry)
            return null;

        var incidentId = RequireIncident(runtimeContext, context, entry.Step);
        var faulted = new CompensationHandlerAttempt(CompensationAttemptResult.Faulted, context.FaultedChildActivityExecutionId, incidentId);
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
        if (WaitingStepEntry(navigator, state, context.CompletedChildExecutableNodeId, context.CompletedChildActivityExecutionId) is not { } entry)
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

    private static RuntimeStructuralContinuation ContinueForward(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        string completedActivityExecutionId)
    {
        if (state.NextStepIndex >= navigator.Steps.Count)
        {
            var completed = state with { Phase = CompensationScopePhase.Completed };
            return CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Complete(), completed);
        }

        var next = ScheduleStep(runtimeContext, navigator, state, completedActivityExecutionId);
        return CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Defer, next);
    }

    private RuntimeStructuralContinuation? OnHandlerCompleted(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        ActivityChildCompletedContext context,
        IReadOnlyDictionary<string, string> metadata)
    {
        if (HandlerEntry(runtimeContext, navigator, state, metadata, context.CompletedChildExecutableNodeId, context.CompletedChildActivityExecutionId) is not { } entry)
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
            var continuation = FaultAfterCompensation
                ? RuntimeStructuralContinuation.Faulted(CompensatedFault(state.Cause?.Step))
                : RuntimeStructuralContinuation.Complete(CompensatedOutcome);
            return CompensationScopeStatePersister.Stage(continuation, finished);
        }

        return ScheduleHandler(runtimeContext, navigator, state, next);
    }

    // Schedules the next attempt of an entry's handler and marks the entry compensating, in one commit (FR-008).
    private RuntimeStructuralContinuation ScheduleHandler(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        CompensationEntry entry)
    {
        var handler = navigator.FindStep(entry.Step)?.Handler
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
            [MetadataKeys.Attempt] = attempt.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [MetadataKeys.MaxAttempts] = MaxAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            // Every attempt, an operator retry included, sends the participant the same keys (spec 251 FR-202).
            [MetadataKeys.OperationKey] = $"{entry.OperationKey}/compensate",
            [MetadataKeys.TargetOperationKey] = entry.OperationKey
        };
        runtimeContext.ScheduleChildActivity(
            handler.ExecutableNodeId,
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
    private static CompensationEntry? NextEligible(CompensationScopeNavigator navigator, CompensationScopeState state) =>
        state.Entries
            .Where(entry => entry.Status == CompensationEntryStatus.Registered
                            || entry.Status == CompensationEntryStatus.Uncertain && navigator.FindStep(entry.Step)?.Handler is not null)
            .OrderByDescending(entry => entry.Ordinal)
            .FirstOrDefault();

    // The entry a handler outcome belongs to, when it is the latest attempt: the running attempt of a compensating
    // entry, or an operator retry of a failed entry. Anything else is a repeat delivery or a stale attempt (FR-215).
    private static CompensationEntry? HandlerEntry(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        IReadOnlyDictionary<string, string> metadata,
        string childExecutableNodeId,
        string childActivityExecutionId)
    {
        var entryId = metadata.GetValueOrDefault(MetadataKeys.Entry);
        var entry = state.Entries.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.Id, entryId));
        if (entry is null || entry.HandlerAttempts.Any(attempt => StringComparer.Ordinal.Equals(attempt.ActivityExecutionId, childActivityExecutionId)))
            return null;

        var isLatest = entry.Status switch
        {
            CompensationEntryStatus.Compensating => IsRunningAttempt(entry, metadata.GetValueOrDefault(MetadataKeys.Attempt)),
            CompensationEntryStatus.Failed => state.Phase == CompensationScopePhase.AwaitingIntervention,
            _ => false
        };
        if (!isLatest)
            return null;

        var handlerNodeId = navigator.FindStep(entry.Step)?.Handler?.ExecutableNodeId;
        if (!StringComparer.Ordinal.Equals(handlerNodeId, childExecutableNodeId))
            throw new CompensationScopeExecutionException(
                $"Compensation scope '{runtimeContext.ExecutableNode.ExecutableNodeId}' received a handler outcome of node '{childExecutableNodeId}' for entry '{entry.Id}', whose handler is '{handlerNodeId}'.");
        return entry;
    }

    // Whether a handler outcome with this attempt number belongs to the entry's running attempt. A handler that M1
    // scheduled carries no attempt number; the version-1 upcast gives its entry exactly one running attempt.
    internal static bool IsRunningAttempt(CompensationEntry entry, string? attempt) =>
        entry.HandlerAttempts.LastOrDefault()?.Result == CompensationAttemptResult.Running
        && (attempt is null
            ? entry.HandlerAttempts.Count == 1
            : StringComparer.Ordinal.Equals(attempt, entry.HandlerAttempts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    // The uncertain entry without a handler that the scope waits on, when the child is an operator retry of its step.
    private static CompensationEntry? WaitingStepEntry(
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        string childExecutableNodeId,
        string childActivityExecutionId)
    {
        var stepIndex = state.NextStepIndex - 1;
        if (stepIndex < 0 || navigator.StepIndexOfForward(childExecutableNodeId) != stepIndex)
            return null;

        var step = navigator.Steps[stepIndex];
        return state.Entries.LastOrDefault(entry =>
            StringComparer.Ordinal.Equals(entry.Step, step.Name)
            && entry.Status == CompensationEntryStatus.Uncertain
            && entry.InterventionIncidentId is not null
            && step.Handler is null
            && !StringComparer.Ordinal.Equals(entry.ForwardActivityExecutionId, childActivityExecutionId));
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
        [MetadataKeys.Attempt] = attempt.ToString(System.Globalization.CultureInfo.InvariantCulture),
        [MetadataKeys.MaxAttempts] = MaxAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture)
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

    private static bool IsOutcomeUnknown(ActivityChildFaultedContext context) =>
        StringComparer.Ordinal.Equals(context.FaultCategory, OutcomeUnknownCategory);

    private static string RequireIncident(IRuntimeActivityExecutionContext runtimeContext, ActivityChildFaultedContext context, string stepName) =>
        context.IncidentId
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

    private static CompensationScopeNavigator Navigator(IRuntimeActivityExecutionContext runtimeContext) =>
        runtimeContext.ExecutableNode.GetOrAddRoutingStructure(CompensationScopeNavigator.From);

    private static CompensationScopeState LoadState(IRuntimeActivityExecutionContext runtimeContext) =>
        CompensationScopeStatePersister.Load(runtimeContext.ActivityExecutionState)
        ?? throw new CompensationScopeExecutionException($"Compensation scope '{runtimeContext.ExecutableNode.ExecutableNodeId}' has no private state.");

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context) =>
        context as IRuntimeActivityExecutionContext
        ?? throw new CompensationScopeExecutionException("Compensation scope requires an Elsa runtime activity execution context.");
}
