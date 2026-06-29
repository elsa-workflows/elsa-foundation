using System.Collections;
using System.Globalization;
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
/// The current item and index are loop-owned per-pass state built with the shared loop-scope primitive
/// (<see cref="RuntimeLoopIterationScopeFactory"/>, ADR 0028): for each pass the loop chooses a distinct
/// <c>IterationId</c>, calls <c>BuildIterationScope</c> once with the item (and optional index) and the
/// enclosing scope chain as parent, and schedules the body child with that same <c>IterationId</c> in
/// <see cref="ActivitySchedulingProvenance.IterationId"/> so the body execution's iteration identity
/// agrees with its scope. The runtime activity references only the runtime contract surface; the
/// design-side structure handler references <c>Elsa.Workflows.Design.Core</c> (Elsa §E2.2).
/// </remarks>
public sealed class ForEach : ActivityBase, IActivityChildCompletionHandler
{
    public const string BodySlotName = "ForEach.Body";
    public const string StructureKind = "elsa.foreach.structure";
    public const string StructureSchemaVersion = "1.0.0";

    /// <summary>The reference key the current-item variable is addressed by within the iteration scope.</summary>
    public const string CurrentItemReferenceKey = "foreach.currentItem";

    /// <summary>The bare name body activities use to read the current item.</summary>
    public const string CurrentItemVariableName = "currentItem";

    /// <summary>The reference key the iteration-index variable is addressed by within the iteration scope.</summary>
    public const string CurrentIndexReferenceKey = "foreach.currentIndex";

    /// <summary>The bare name body activities use to read the zero-based iteration index.</summary>
    public const string CurrentIndexVariableName = "currentIndex";

    private readonly RuntimeLoopIterationScopeFactory _iterationScopeFactory = new();

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

        // ADR 0028 contract step 2: build a fresh per-pass iteration scope carrying this item (and
        // optional index). A distinct scope instance per pass is what isolates iterations; the
        // IterationId is recorded as the scope's execution identity for correlation only. The enclosing
        // visible scope chain is threaded by the runtime; this composite layers the iteration scope on top.
        _iterationScopeFactory.BuildIterationScope(
            new LoopIterationScopeRequest(
                OwnerNodeId: ownerNodeId,
                IterationId: iterationId,
                ItemReferenceKey: CurrentItemReferenceKey,
                ItemName: CurrentItemVariableName,
                Item: items[index],
                Index: index,
                IndexReferenceKey: ExposeIndex ? CurrentIndexReferenceKey : null,
                IndexName: ExposeIndex ? CurrentIndexVariableName : null),
            parent: null);

        // ADR 0028 contract step 3: schedule the body under that same IterationId so the body execution's
        // iteration identity agrees with its scope.
        runtimeContext.ScheduleChildActivity(
            body.ExecutableNodeId,
            parentActivityExecutionId,
            new Dictionary<string, string>
            {
                ["foreach.parentActivityExecutionId"] = parentActivityExecutionId,
                ["foreach.targetNodeId"] = body.ExecutableNodeId,
                ["foreach.iterationIndex"] = index.ToString(CultureInfo.InvariantCulture)
            },
            ActivitySchedulingProvenance.From(
                runtimeContext.WorkflowExecutionId,
                parentActivityExecutionId: parentActivityExecutionId,
                schedulingActivityExecutionId: parentActivityExecutionId,
                branchId: null,
                iterationId: iterationId,
                executionPathId: null,
                executionScopeId: null,
                schedulingCause: "foreach.iteration"));
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
            string => throw new ForEachExecutionException("ForEach collection input must be an enumerable, not a string."),
            IEnumerable enumerable => enumerable.Cast<object?>().ToArray(),
            _ => throw new ForEachExecutionException($"ForEach collection input of type '{collection.GetType().FullName}' is not enumerable.")
        };

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new ForEachExecutionException("ForEach requires an Elsa runtime activity execution context.");
    }
}
