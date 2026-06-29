using System.Collections;
using System.Globalization;
using System.Text.Json;
using Elsa.Activities.ForEach.Exceptions;
using Elsa.Activities.ForEach.Internal;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Activities.ForEach.Activities;

/// <summary>
/// Looping composite. Iterates a <see cref="Collection"/> input and runs its single body activity once
/// per item, exposing the current item (and an optional zero-based index) to the body through a
/// per-iteration variable scope. The body lives in a named child slot (<see cref="BodySlotName"/>); its
/// node id is recorded in the compiled structure. Each pass schedules that same body node under a
/// distinct engine iteration identity; on each body completion the loop advances to the next item, and
/// when the collection is exhausted it completes with a <see cref="ActivityOutcomes.Done"/> outcome. A
/// null or empty collection short-circuits to <see cref="ActivityOutcomes.Done"/> without scheduling the
/// body.
/// </summary>
/// <remarks>
/// The current item and index are loop-owned per-pass state realized through the shared loop-scope
/// primitive (<see cref="RuntimeLoopIterationScopeFactory"/>, ADR 0028). For each pass the loop chooses a
/// distinct <c>IterationId</c> and schedules the body child with that <c>IterationId</c> in
/// <see cref="ActivitySchedulingProvenance.IterationId"/>, carrying the pass's item/index in the body's
/// scheduling provenance metadata (the merged <c>RuntimeMetadataKeys.LoopIteration*</c> keys, #296). When the runtime starts the
/// body it reads that metadata in <c>RuntimeContainerScopeService.BuildScopeAsync</c> and calls
/// <c>BuildIterationScope</c> to layer a fresh per-pass iteration scope (item + optional index) as the
/// innermost scope on the body's visible chain, so the body resolves the current item through the real
/// expression evaluator. The runtime activity references only the runtime contract surface; the
/// design-side structure handler references <c>Elsa.Workflows.Design.Core</c> (Elsa §E2.2).
/// </remarks>
public sealed class ForEach : ActivityBase, IActivityChildCompletionHandler
{
    public const string BodySlotName = "ForEach.Body";
    public const string StructureKind = "elsa.foreach.structure";
    public const string StructureSchemaVersion = "1.0.0";

    /// <summary>
    /// The variable name body activities use to read the current item. Per the merged loop-scope wiring
    /// (#296), this name doubles as the iteration scope's reference key: a body input bound to a
    /// <c>Variable</c> expression <c>{ referenceKey: "currentItem", declaringScopeId: &lt;ForEach node id&gt; }</c>
    /// resolves to the current pass's item.
    /// </summary>
    public const string CurrentItemVariableName = "currentItem";

    /// <summary>
    /// The variable name body activities use to read the zero-based iteration index (also its reference
    /// key within the iteration scope). Surfaced only when <see cref="ExposeIndex"/> is <c>true</c>.
    /// </summary>
    public const string CurrentIndexVariableName = "currentIndex";

    /// <summary>The collection iterated over; each item is exposed to the body for one pass.</summary>
    public InputArgument<object> Collection { get; set; } = null!;

    /// <summary>When <c>true</c>, the zero-based iteration index is exposed to the body alongside the current item.</summary>
    public bool ExposeIndex { get; set; } = true;

    protected override void Execute(IActivityExecutionContext context)
    {
        var runtimeContext = RequireRuntimeContext(context);
        var navigator = ForEachNavigator.From(runtimeContext.ExecutableNode);
        var items = MaterializeItems(context.Get(Collection));

        // Empty/null collection or empty body short-circuits without scheduling a pass.
        if (items.Count == 0 || navigator.Body is null)
        {
            runtimeContext.CompleteCompositeActivity([ActivityOutcomes.Done]);
            return;
        }

        ScheduleIteration(runtimeContext, navigator.Body, items, index: 0);
    }

    public ValueTask OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var runtimeContext = RequireRuntimeContext(context.ParentContext);
        var navigator = ForEachNavigator.From(runtimeContext.ExecutableNode);

        if (navigator.Body is null || !navigator.IsBody(context.CompletedChildExecutableNodeId))
            throw new ForEachExecutionException($"Completed child executable node '{context.CompletedChildExecutableNodeId}' is not the ForEach body.");

        var completedIndex = ResolveCompletedIndex(runtimeContext, context.CompletedChildIterationId);
        var items = MaterializeItems(context.ParentContext.Get(Collection));
        var nextIndex = completedIndex + 1;

        if (nextIndex >= items.Count)
        {
            runtimeContext.CompleteCompositeActivity([ActivityOutcomes.Done]);
            return ValueTask.CompletedTask;
        }

        ScheduleIteration(runtimeContext, navigator.Body, items, nextIndex);
        return ValueTask.CompletedTask;
    }

    private void ScheduleIteration(
        IRuntimeActivityExecutionContext runtimeContext,
        ExecutableNode body,
        IReadOnlyList<object?> items,
        int index)
    {
        var ownerNodeId = runtimeContext.ExecutableNode.ExecutableNodeId;
        var parentActivityExecutionId = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId;
        var iterationId = IterationId(parentActivityExecutionId, index);

        // ADR 0028 / #259: publish this pass's iteration variables in the body child's scheduling
        // provenance metadata, using the merged loop-scope keys (#296). The runtime
        // (RuntimeContainerScopeService.BuildScopeAsync) reads these back and calls
        // RuntimeLoopIterationScopeFactory.BuildIterationScope, layering a fresh per-pass iteration scope
        // as the innermost scope on the body's enclosing container chain, so the body resolves
        // 'currentItem' (and optionally 'currentIndex') through the real expression evaluator. The item
        // and index values are JSON-serialized; the index is serialized as a JSON number so the scope
        // builder's int parse recovers it. A distinct scope per pass (one per IterationId) isolates
        // iterations; the IterationId is the scope's execution identity for correlation only.
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.LoopIterationOwnerNodeId] = ownerNodeId,
            [RuntimeMetadataKeys.LoopIterationItemName] = CurrentItemVariableName,
            [RuntimeMetadataKeys.LoopIterationItemValue] = JsonSerializer.Serialize(items[index]),
            ["foreach.parentActivityExecutionId"] = parentActivityExecutionId,
            ["foreach.targetNodeId"] = body.ExecutableNodeId,
            ["foreach.iterationIndex"] = index.ToString(CultureInfo.InvariantCulture)
        };

        if (ExposeIndex)
        {
            metadata[RuntimeMetadataKeys.LoopIterationIndexName] = CurrentIndexVariableName;
            metadata[RuntimeMetadataKeys.LoopIterationIndexValue] = JsonSerializer.Serialize(index);
        }

        // ADR 0028 contract step 3: schedule the body under that same IterationId (also carried in the
        // provenance metadata) so the body execution's iteration identity agrees with its scope.
        runtimeContext.ScheduleChildActivity(
            body.ExecutableNodeId,
            parentActivityExecutionId,
            metadata,
            ActivitySchedulingProvenance.From(
                runtimeContext.WorkflowExecutionId,
                parentActivityExecutionId: parentActivityExecutionId,
                schedulingActivityExecutionId: parentActivityExecutionId,
                branchId: null,
                iterationId: iterationId,
                executionPathId: null,
                executionScopeId: null,
                schedulingCause: "foreach.iteration",
                metadata: metadata));
    }

    private static int ResolveCompletedIndex(IRuntimeActivityExecutionContext runtimeContext, string? completedChildIterationId)
    {
        if (string.IsNullOrEmpty(completedChildIterationId))
            throw new ForEachExecutionException("ForEach cannot advance: the completed body child carries no iteration identity.");

        var parentActivityExecutionId = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId;
        var prefix = IterationIdPrefix(parentActivityExecutionId);

        if (!completedChildIterationId.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(completedChildIterationId[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ||
            index < 0)
            throw new ForEachExecutionException($"ForEach cannot advance: completed body iteration id '{completedChildIterationId}' is not a ForEach iteration of '{parentActivityExecutionId}'.");

        return index;
    }

    private static string IterationId(string parentActivityExecutionId, int index) =>
        $"{IterationIdPrefix(parentActivityExecutionId)}{index.ToString(CultureInfo.InvariantCulture)}";

    private static string IterationIdPrefix(string parentActivityExecutionId) =>
        $"{parentActivityExecutionId}:foreach-iteration:";

    private static IReadOnlyList<object?> MaterializeItems(object? collection) =>
        collection switch
        {
            null => [],
            JsonElement element => MaterializeJsonItems(element),
            string => throw new ForEachExecutionException("ForEach collection input must be an enumerable, not a string."),
            IEnumerable enumerable => enumerable.Cast<object?>().ToArray(),
            _ => throw new ForEachExecutionException($"ForEach collection input of type '{collection.GetType().FullName}' is not enumerable.")
        };

    // A literal/expression collection arrives as a JsonElement (the runtime materializes 'object' inputs
    // as JSON). Enumerate array elements, unwrapping each to its scalar CLR value so the body observes a
    // plain item rather than a JsonElement.
    private static IReadOnlyList<object?> MaterializeJsonItems(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => [],
            JsonValueKind.Array => element.EnumerateArray().Select(UnwrapJsonScalar).ToArray(),
            _ => throw new ForEachExecutionException($"ForEach collection input JSON value kind '{element.ValueKind}' is not an array.")
        };

    private static object? UnwrapJsonScalar(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element
        };

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new ForEachExecutionException("ForEach requires an Elsa runtime activity execution context.");
    }
}
