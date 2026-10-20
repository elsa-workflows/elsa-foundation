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
/// A handler fault is not absorbed: it stays a blocking incident and the run's incident strategy decides, while the
/// entry stays compensating. Workflow cancellation runs no handler. See <c>specs/250-native-compensation</c>.
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
        public const string StepRole = "step";
        public const string HandlerRole = "handler";
    }

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
            (CompensationScopePhase.Compensating, MetadataKeys.HandlerRole) => OnHandlerCompleted(runtimeContext, navigator, state, context, metadata),
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

        var isRunningStep = IsOwnChild(runtimeContext, metadata)
                            && state.Phase == CompensationScopePhase.Running
                            && StringComparer.Ordinal.Equals(metadata.GetValueOrDefault(MetadataKeys.Role), MetadataKeys.StepRole)
                            && navigator.StepIndexOfForward(context.FaultedChildExecutableNodeId) == state.NextStepIndex - 1;

        // A handler fault, or any fault the scope does not own, stays a blocking incident; the run's incident
        // strategy decides. The entry stays compensating, so it is never reported as compensated.
        if (!isRunningStep)
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);

        var step = navigator.Steps[state.NextStepIndex - 1];
        var cause = new CompensationRecoveryCause(
            CompensationRecoveryCause.FaultKind,
            step.Name,
            context.FaultedChildActivityExecutionId,
            context.IncidentId);
        var compensating = state with { Phase = CompensationScopePhase.Compensating, Cause = cause };

        // A fault continuation cannot also absorb the fault. A scope that re-raises the fault and has nothing to undo
        // therefore leaves the incident as it is and faults; otherwise it absorbs the fault and starts recovery.
        var hasWork = compensating.Entries.Any(entry => entry.Status == CompensationEntryStatus.Registered);
        if (!hasWork && FaultAfterCompensation)
        {
            var finished = compensating with { Phase = CompensationScopePhase.Compensated };
            return ValueTask.FromResult(CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Faulted(CompensatedFault(step.Name)), finished));
        }

        if (context.IncidentId is not { } incidentId)
            throw new CompensationScopeExecutionException($"Compensation scope '{runtimeContext.ExecutableNode.ExecutableNodeId}' cannot start recovery: step '{step.Name}' faulted without an incident.");

        runtimeContext.RequestChildFaultAbsorption(incidentId, $"Compensation scope started recovery because step '{step.Name}' faulted.");
        return ValueTask.FromResult(ScheduleNextHandlerOrFinish(runtimeContext, navigator, compensating));
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
        var entry = new CompensationEntry(
            $"e{state.NextOrdinal}",
            step.Name,
            state.NextOrdinal,
            step.Handler is null ? CompensationEntryStatus.NoHandler : CompensationEntryStatus.Registered,
            context.CompletedChildActivityExecutionId,
            context.CompletedChildIterationId);
        var journaled = state with { NextOrdinal = state.NextOrdinal + 1, Entries = [.. state.Entries, entry] };

        if (journaled.NextStepIndex >= navigator.Steps.Count)
        {
            var completed = journaled with { Phase = CompensationScopePhase.Completed };
            return CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Complete(), completed);
        }

        var next = ScheduleStep(runtimeContext, navigator, journaled, context.CompletedChildActivityExecutionId);
        return CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Defer, next);
    }

    private RuntimeStructuralContinuation? OnHandlerCompleted(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state,
        ActivityChildCompletedContext context,
        IReadOnlyDictionary<string, string> metadata)
    {
        var entryId = metadata.GetValueOrDefault(MetadataKeys.Entry);
        var entryIndex = state.Entries.ToList().FindIndex(entry => StringComparer.Ordinal.Equals(entry.Id, entryId));
        if (entryIndex < 0 || state.Entries[entryIndex].Status != CompensationEntryStatus.Compensating)
            return null;

        var entry = state.Entries[entryIndex];
        var handlerNodeId = navigator.FindStep(entry.Step)?.Handler?.ExecutableNodeId;
        if (!StringComparer.Ordinal.Equals(handlerNodeId, context.CompletedChildExecutableNodeId))
            throw new CompensationScopeExecutionException(
                $"Compensation scope '{runtimeContext.ExecutableNode.ExecutableNodeId}' received a handler completion of node '{context.CompletedChildExecutableNodeId}' for entry '{entry.Id}', whose handler is '{handlerNodeId}'.");

        var compensated = entry with
        {
            Status = CompensationEntryStatus.Compensated,
            HandlerActivityExecutionIds = [.. entry.HandlerActivityExecutionIds, context.CompletedChildActivityExecutionId]
        };
        var entries = state.Entries.ToArray();
        entries[entryIndex] = compensated;
        return ScheduleNextHandlerOrFinish(runtimeContext, navigator, state with { Entries = entries });
    }

    private RuntimeStructuralContinuation ScheduleNextHandlerOrFinish(
        IRuntimeActivityExecutionContext runtimeContext,
        CompensationScopeNavigator navigator,
        CompensationScopeState state)
    {
        var next = state.Entries
            .Where(entry => entry.Status == CompensationEntryStatus.Registered)
            .OrderByDescending(entry => entry.Ordinal)
            .FirstOrDefault();

        if (next is null)
        {
            var finished = state with { Phase = CompensationScopePhase.Compensated };
            var continuation = FaultAfterCompensation
                ? RuntimeStructuralContinuation.Faulted(CompensatedFault(state.Cause?.Step))
                : RuntimeStructuralContinuation.Complete(CompensatedOutcome);
            return CompensationScopeStatePersister.Stage(continuation, finished);
        }

        var handler = navigator.FindStep(next.Step)?.Handler
                      ?? throw new CompensationScopeExecutionException(
                          $"Compensation scope '{runtimeContext.ExecutableNode.ExecutableNodeId}' entry '{next.Id}' names step '{next.Step}', which has no handler in the pinned structure.");

        var scopeActivityExecutionId = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId;
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MetadataKeys.Role] = MetadataKeys.HandlerRole,
            [MetadataKeys.Scope] = scopeActivityExecutionId,
            [MetadataKeys.Step] = next.Step,
            [MetadataKeys.Entry] = next.Id,
            [MetadataKeys.Forward] = next.ForwardActivityExecutionId
        };
        runtimeContext.ScheduleChildActivity(
            handler.ExecutableNodeId,
            next.ForwardActivityExecutionId,
            metadata,
            ActivitySchedulingProvenance.From(
                runtimeContext.WorkflowExecutionId,
                scopeActivityExecutionId,
                next.ForwardActivityExecutionId,
                branchId: null,
                iterationId: null,
                executionPathId: null,
                executionScopeId: null,
                schedulingCause: HandlerSchedulingCause,
                metadata: metadata));

        var entries = state.Entries
            .Select(entry => StringComparer.Ordinal.Equals(entry.Id, next.Id) ? entry with { Status = CompensationEntryStatus.Compensating } : entry)
            .ToArray();
        return CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Defer, state with { Entries = entries });
    }

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
                [MetadataKeys.Step] = step.Name
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
