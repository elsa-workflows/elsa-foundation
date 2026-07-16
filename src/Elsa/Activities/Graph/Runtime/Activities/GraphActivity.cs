using System.Text.Json;
using Elsa.Activities.Graph.Runtime.Models;
using Elsa.Activities.Graph.Runtime.Services;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
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
    IReadOnlyDictionary<string, InputArgument> inputs,
    IReadOnlyDictionary<string, OutputArgument> outputs)
    : ActivityBase, IActivityChildCompletionHandler, IActivityChildFaultHandler, IRuntimeActivityCheckpointParticipant
{
    public GraphActivityDescriptor Descriptor { get; } = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    public IReadOnlyDictionary<string, InputArgument> Inputs { get; } = Snapshot(inputs);
    public IReadOnlyDictionary<string, OutputArgument> Outputs { get; } = Snapshot(outputs);

    protected override void Execute(IActivityExecutionContext context)
    {
        var runtimeContext = RequireRuntimeContext(context);
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

        runtimeContext.DeferCompositeCompletion();
        runtimeContext.ScheduleChildActivity(
            Descriptor.EntryNodeId,
            outerActivityExecutionId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["graph.executionScopeId"] = outerActivityExecutionId,
                ["graph.templateHash"] = Descriptor.TemplateHash
            },
            provenance);
    }

    public ValueTask OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!StringComparer.Ordinal.Equals(context.CompletedChildExecutableNodeId, Descriptor.EntryNodeId))
        {
            throw new InvalidOperationException(
                $"Graph boundary received completion from '{context.CompletedChildExecutableNodeId}', but its direct entry node is '{Descriptor.EntryNodeId}'.");
        }

        // Completion is requested only after PrepareCompletionCheckpointAsync has evaluated and staged every
        // boundary output. This prevents parent continuation from observing a terminal boundary without outputs.
        RequireRuntimeContext(context.ParentContext);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnChildFaultedAsync(ActivityChildFaultedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        throw RequireRuntimeContext(context.ParentContext)
            .GetRequiredService<GraphActivityRecovery>()
            .NewBoundaryFault(context);
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
        var scopeFactory = context.GetRequiredService<GraphActivityScopeFactory>();
        var isRetry = (context.ActivityExecutionState.Attempt ?? context.ActivityExecutionState.Provenance.Attempt)?.AttemptNumber > 1;
        GraphActivityScope scope;
        if (isRetry)
        {
            var persistedValues = await context.GetRequiredService<IDurableValueStateStore>()
                .ListAsync(context.WorkflowExecutionId, cancellationToken);
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

        var evaluator = NewEvaluator(context);
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

    public async ValueTask<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>> PrepareCompletionCheckpointAsync(
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
        var scope = context.GetRequiredService<GraphActivityScopeFactory>().Rehydrate(
            Descriptor,
            context.WorkflowExecutionId,
            outerActivityExecutionId,
            scopeValues);
        var capturedOutputs = await scope.CaptureOutputsAsync(
            Descriptor.Outputs,
            Descriptor.OutputMappings,
            Descriptor.RequiredOutputReferenceKeys.ToHashSet(StringComparer.Ordinal),
            NewEvaluator(context),
            capturedAt,
            cancellationToken);

        foreach (var output in Descriptor.Outputs)
        {
            if (!scope.Outputs.ContainsKey(output.ReferenceKey))
                continue;

            var result = await scope.ReadOutputAsync(output.ReferenceKey, cancellationToken);
            if (result.Exists)
                context.RecordActivityOutput(output.Name ?? output.ReferenceKey, result.Value);
        }

        context.CompleteCompositeActivity([ActivityOutcomes.Done]);
        return ToChanges(capturedOutputs);
    }

    private static GraphActivityExpressionEvaluator NewEvaluator(IRuntimeActivityExecutionContext context) =>
        async (expression, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (StringComparer.OrdinalIgnoreCase.Equals(expression.Syntax, "Literal") ||
                StringComparer.OrdinalIgnoreCase.Equals(expression.Syntax, "Object"))
            {
                return expression.Value.Clone();
            }

            var evaluator = context.GetRequiredService<IExpressionEvaluator>();
            return await evaluator.EvaluateAsync(
                new RuntimeGraphExpression(expression.Syntax, ExpressionValue(expression.Value)),
                typeof(object),
                context.ExpressionExecutionContext);
        };

    private static object? ExpressionValue(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() : value.Clone();

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

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context) =>
        context as IRuntimeActivityExecutionContext
        ?? throw new InvalidOperationException("GraphActivity requires an Elsa runtime activity execution context.");

    private static IReadOnlyDictionary<string, TValue> Snapshot<TValue>(IReadOnlyDictionary<string, TValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new Dictionary<string, TValue>(values, StringComparer.Ordinal);
    }

    private sealed class RuntimeGraphExpression(string type, object? value) : IExpression
    {
        public string Type { get; set; } = type;
        public object? Value { get; set; } = value;
        public TValue GetValue<TValue>() => (TValue)Value!;
    }
}
