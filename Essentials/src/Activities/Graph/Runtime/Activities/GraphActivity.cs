using System.Text.Json;
using Elsa.Activities.Graph.Runtime.Models;
using Elsa.Activities.Graph.Runtime.Services;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Graph.Runtime.Activities;

/// <summary>
/// Ordinary composite activity that opens one activity-execution scope and schedules the placed graph entry node.
/// It never starts a child workflow or executes a nested graph synchronously.
/// </summary>
public sealed class GraphActivity(
    GraphActivityDescriptor descriptor,
    GraphActivityScopeFactory scopeFactory,
    GraphActivityRecovery recovery,
    IDurableValueStateStore durableValueStateStore)
    : StructuralActivity,
        IRuntimeStructuralActivity,
        IRuntimeActivityChildCompletionHandler,
        IRuntimeActivityChildFaultHandler,
        IRuntimeActivityChildNotificationHandler,
        IRuntimeLiveChildActivityConsumer,
        IRuntimeActivityCheckpointParticipant
{
    /// <summary>The fault code when a <c>Complete</c> node names a reference key this boundary does not declare.</summary>
    public const string CompleteOutcomeUnknownFaultCode = "activity.graph.complete.outcome-unknown";

    /// <summary>
    /// The fault code when the entry completes naturally in a body whose outcomes come only from <c>Complete</c> nodes.
    /// </summary>
    public const string EntryCompletedWithoutMappingFaultCode = "activity.graph.entry-completed-without-mapping";

    private string? _selectedOutcomeName;

    public GraphActivityDescriptor Descriptor { get; } = descriptor ?? throw new ArgumentNullException(nameof(descriptor));

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(runtimeContext);
        _selectedOutcomeName = null;
        ValidatePinnedDescriptor(runtimeContext);
        EnsureEntryIsDirectChild(runtimeContext.ExecutableNode, Descriptor.EntryNodeId);

        var outerActivityExecutionId = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId;
        var provenance = ActivitySchedulingProvenance.From(
            runtimeContext.WorkflowExecutionId,
            parentActivityExecutionId: outerActivityExecutionId,
            schedulingActivityExecutionId: outerActivityExecutionId,
            branchId: runtimeContext.ActivityExecutionState.BranchId,
            iterationId: runtimeContext.ActivityExecutionState.IterationId,
            executionPathId: runtimeContext.ActivityExecutionState.Provenance.ExecutionPathId,
            executionScopeId: outerActivityExecutionId,
            schedulingCause: "graph-entry",
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["graph.definitionId"] = Descriptor.DefinitionId,
                ["graph.definitionVersionId"] = Descriptor.DefinitionVersionId,
                ["graph.templateHash"] = Descriptor.TemplateHash
            });

        runtimeContext.ScheduleChildActivity(
            Descriptor.EntryNodeId,
            outerActivityExecutionId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["graph.executionScopeId"] = outerActivityExecutionId,
                ["graph.templateHash"] = Descriptor.TemplateHash
            },
            provenance);

        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!StringComparer.Ordinal.Equals(context.CompletedChildExecutableNodeId, Descriptor.EntryNodeId))
        {
            throw new InvalidOperationException(
                $"Graph boundary received completion from '{context.CompletedChildExecutableNodeId}', but its direct entry node is '{Descriptor.EntryNodeId}'.");
        }

        RequireRuntimeContext(context.ParentContext);
        if (Descriptor.OutcomeMappings.Count == 0 && Descriptor.CompleteOutcomes.Count != 0)
        {
            // Every boundary outcome is reached through Complete nodes, so a body that ends without reaching one has
            // selected none; inventing Done would emit an outcome the contract may not declare.
            return ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(new ActivityFault(
                EntryCompletedWithoutMappingFaultCode,
                $"Graph entry '{Descriptor.EntryNodeId}' completed without reaching a Complete node, and no outcome mapping covers natural completion.")));
        }

        if (Descriptor.OutcomeMappings.Count == 0)
        {
            _selectedOutcomeName = ActivityOutcomes.Done;
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }

        var matches = Descriptor.OutcomeMappings
            .Where(mapping => context.OutcomeNames.Contains(mapping.SourceOutcomeName, StringComparer.Ordinal))
            .ToArray();
        if (context.OutcomeNames.Count != 1 || matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"Graph entry '{Descriptor.EntryNodeId}' must complete with exactly one mapped outcome; " +
                $"received [{string.Join(", ", context.OutcomeNames.Order(StringComparer.Ordinal))}].");
        }

        _selectedOutcomeName = matches[0].BoundaryOutcomeName;
        return ValueTask.FromResult(RuntimeStructuralContinuation.Complete(_selectedOutcomeName));
    }

    /// <summary>
    /// Handles the exit a <c>Complete</c> node anywhere in this body sends to its nearest graph boundary: selects the
    /// declared outcome, durably cancels the rest of the body (the entry's whole subtree) in the same commit, and
    /// completes. A boundary that is no longer running never sees a later exit, so the first committed one wins.
    /// </summary>
    public ValueTask<RuntimeStructuralContinuation> OnChildNotifiedAsync(
        IRuntimeActivityExecutionContext context,
        ActivityChildNotifiedContext notification)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(notification);
        if (!StringComparer.Ordinal.Equals(notification.Code, GraphCompleteNotification.Code))
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);

        var referenceKey = ReadCompleteOutcomeReferenceKey(notification);
        var completeOutcome = referenceKey is null
            ? null
            : Descriptor.CompleteOutcomes.FirstOrDefault(x => StringComparer.Ordinal.Equals(x.ReferenceKey, referenceKey));
        if (completeOutcome is null)
        {
            return ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(new ActivityFault(
                CompleteOutcomeUnknownFaultCode,
                $"Complete node '{notification.NotifyingChildExecutableNodeId}' names boundary outcome '{referenceKey}', which graph activity '{context.ExecutableNode.ExecutableNodeId}' does not declare.")));
        }

        _selectedOutcomeName = completeOutcome.BoundaryOutcomeName;
        foreach (var child in context.GetLiveChildActivities())
            context.RequestChildSubtreeCancellation(child.ActivityExecutionId, "graph-complete");

        return ValueTask.FromResult(RuntimeStructuralContinuation.Complete(_selectedOutcomeName));
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RequireRuntimeContext(context.ParentContext);
        throw recovery.NewBoundaryFault(context);
    }

    public async ValueTask<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>> PrepareEntryCheckpointAsync(
        IRuntimeActivityExecutionContext context,
        IReadOnlyDictionary<string, object?> effectiveInputs,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(effectiveInputs);
        ValidatePinnedDescriptor(context);

        var outerActivityExecutionId = context.ActivityExecutionState.Execution.ActivityExecutionId;
        var isRetry = (context.ActivityExecutionState.Attempt ?? context.ActivityExecutionState.Provenance.Attempt)?.AttemptNumber > 1;
        GraphActivityScope scope;
        if (isRetry)
        {
            var persistedValues = await durableValueStateStore.ListAllDurableValueStatesAsync(context.WorkflowExecutionId, cancellationToken);
            var retryInputs = persistedValues
                .Where(value => StringComparer.Ordinal.Equals(value.SourceActivityExecutionId, outerActivityExecutionId))
                .Where(value => value.Metadata.ContainsKey(RuntimeMetadataKeys.RetrySourceActivityExecutionId))
                .Where(value => value.Metadata.TryGetValue(RuntimeMetadataKeys.BoundaryValueRole, out var role) && StringComparer.Ordinal.Equals(role, "input"))
                .ToArray();
            if (retryInputs.Length == 0 && Descriptor.Inputs.Count != 0)
                throw new InvalidOperationException("A graph boundary retry has no effective captured input snapshot.");
            scope = scopeFactory.Rehydrate(Descriptor, context.WorkflowExecutionId, outerActivityExecutionId, retryInputs);
        }
        else
        {
            scope = scopeFactory.Create(Descriptor, context.WorkflowExecutionId, outerActivityExecutionId);
        }
        var inputsByReferenceKey = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var input in Descriptor.Inputs)
        {
            var runtimeName = input.Name ?? input.ReferenceKey;
            if (effectiveInputs.TryGetValue(runtimeName, out var value) ||
                (!StringComparer.Ordinal.Equals(runtimeName, input.ReferenceKey) && effectiveInputs.TryGetValue(input.ReferenceKey, out value)))
            {
                inputsByReferenceKey[input.ReferenceKey] = value;
            }
        }

        var evaluator = NewEvaluator();
        var capturedInputs = isRetry
            ? []
            : await scope.CaptureInputsAsync(
                Descriptor.Inputs,
                inputsByReferenceKey,
                Descriptor.RequiredInputReferenceKeys.ToHashSet(StringComparer.Ordinal),
                evaluator,
                capturedAt,
                cancellationToken);
        var initializedVariables = await scope.InitializeVariablesAsync(
            Descriptor.Variables,
            evaluator,
            capturedAt,
            cancellationToken);

        return ToChanges(capturedInputs.Concat(initializedVariables));
    }

    public async ValueTask<RuntimeActivityCompletionCheckpointPreparation> PrepareCompletionCheckpointAsync(
        IRuntimeActivityExecutionContext context,
        IReadOnlyCollection<DurableValueState> persistedValues,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(persistedValues);
        ValidatePinnedDescriptor(context);

        var outerActivityExecutionId = context.ActivityExecutionState.Execution.ActivityExecutionId;
        var scopeValues = persistedValues
            .Where(value => StringComparer.Ordinal.Equals(value.SourceActivityExecutionId, outerActivityExecutionId))
            .Where(value => value.Metadata.TryGetValue(RuntimeMetadataKeys.BoundaryExecutionScopeId, out var scopeId) &&
                            StringComparer.Ordinal.Equals(scopeId, outerActivityExecutionId))
            .ToArray();
        var scope = scopeFactory.Rehydrate(
            Descriptor,
            context.WorkflowExecutionId,
            outerActivityExecutionId,
            scopeValues);
        var capturedOutputs = await scope.CaptureOutputsAsync(
            Descriptor.Outputs,
            Descriptor.OutputMappings,
            Descriptor.RequiredOutputReferenceKeys.ToHashSet(StringComparer.Ordinal),
            NewEvaluator(),
            capturedAt,
            cancellationToken);

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var output in Descriptor.Outputs)
        {
            var captured = await scope.ReadOutputAsync(output.ReferenceKey, cancellationToken);
            if (captured.Exists)
                result.Add(output.ReferenceKey, captured.Value);
        }

        // A Complete exit selects its outcome even when the body has no outcome mappings.
        var outcomeName = _selectedOutcomeName is not null && Descriptor.CompleteOutcomes.Count != 0
            ? _selectedOutcomeName
            : Descriptor.OutcomeMappings.Count == 0
            ? ActivityOutcomes.Done
            : _selectedOutcomeName ?? throw new InvalidOperationException(
                "A graph boundary with explicit outcome mappings cannot checkpoint completion before selecting an outcome.");
        return new RuntimeActivityCompletionCheckpointPreparation(
            ToChanges(capturedOutputs),
            ActivityTransition.Complete<IReadOnlyDictionary<string, object?>>(result, outcomeName));
    }

    private static GraphActivityExpressionEvaluator NewEvaluator() =>
        (expression, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (StringComparer.OrdinalIgnoreCase.Equals(expression.Syntax, "Literal") ||
                StringComparer.OrdinalIgnoreCase.Equals(expression.Syntax, "Object"))
            {
                return ValueTask.FromResult<object?>(expression.Value.Clone());
            }

            return ValueTask.FromException<object?>(new InvalidOperationException(
                $"Graph expression syntax '{expression.Syntax}' must be compiled to the portable expression model before runtime evaluation."));
        };

    private static IReadOnlyCollection<RuntimeStateChange<DurableValueState>> ToChanges(
        IEnumerable<DurableValueState> states) =>
        states.Select(state => new RuntimeStateChange<DurableValueState>(
            state.DurableValueId,
            RuntimeStateChangeOperation.Upsert,
            state,
            state.Metadata)).ToArray();

    private void ValidatePinnedDescriptor(IRuntimeActivityExecutionContext context)
    {
        var runtimeDescriptor = context.ExecutableNode.Descriptor;
        if (!StringComparer.Ordinal.Equals(runtimeDescriptor.ConsumerKey, WellKnownRuntimeActivityConsumers.GraphActivity) ||
            !StringComparer.Ordinal.Equals(runtimeDescriptor.SchemaVersion, RuntimeActivityDescriptor.InitialSchemaVersion))
        {
            throw new InvalidOperationException(
                $"Graph activity '{context.ExecutableNode.ExecutableNodeId}' is pinned to unsupported consumer/schema " +
                $"'{runtimeDescriptor.ConsumerKey}/{runtimeDescriptor.SchemaVersion}'.");
        }

        if (context.ExecutableNode.Metadata.TryGetValue("graph.templateHash", out var pinnedHash) &&
            !StringComparer.Ordinal.Equals(pinnedHash, Descriptor.TemplateHash))
        {
            throw new InvalidOperationException(
                $"Graph activity descriptor hash '{Descriptor.TemplateHash}' does not match pinned node hash '{pinnedHash}'.");
        }
    }

    private static void EnsureEntryIsDirectChild(ExecutableNode boundary, string entryNodeId)
    {
        if (boundary.ChildSlots
            .SelectMany(slot => slot.Activities)
            .Any(child => StringComparer.Ordinal.Equals(child.ExecutableNodeId, entryNodeId)))
            return;

        throw new InvalidOperationException(
            $"Graph entry node '{entryNodeId}' is not a direct child of boundary node '{boundary.ExecutableNodeId}'.");
    }

    private static string? ReadCompleteOutcomeReferenceKey(ActivityChildNotifiedContext notification) =>
        notification.Payload is { ValueKind: JsonValueKind.Object } payload &&
        payload.TryGetProperty(GraphCompleteNotification.OutcomePayloadKey, out var outcome) &&
        outcome.ValueKind == JsonValueKind.String
            ? outcome.GetString()
            : null;

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context) =>
        context as IRuntimeActivityExecutionContext
        ?? throw new InvalidOperationException("GraphActivity requires an Elsa runtime activity execution context.");

}
