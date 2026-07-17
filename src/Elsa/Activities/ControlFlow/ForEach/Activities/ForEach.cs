using System.Collections;
using System.Globalization;
using System.Text.Json;
using Elsa.Activities.ControlFlow.Results;
using Elsa.Activities.ForEach.Exceptions;
using Elsa.Activities.ForEach.Internal;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

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
/// durable frame primitive (<see cref="VariableFrameState"/>, ADR 0028). For each pass the loop chooses a
/// distinct <c>IterationId</c> and schedules the body child with that <c>IterationId</c> in
/// <see cref="ActivitySchedulingProvenance.IterationId"/>, carrying the pass's item/index in the body's
/// typed scheduler request. When the runtime starts the body it activates a fresh per-pass iteration frame
/// (item + optional index) as the
/// innermost scope on the body's visible chain, so the body resolves the current item through the real
/// expression evaluator. The runtime activity references only the runtime contract surface; the
/// design-side structure handler references <c>Elsa.Workflows.Design.Core</c> (Elsa §E2.2).
/// </remarks>
[ActivityStructure("elsa.foreach.structure", "1.0.0")]
[ActivityChildSlot("ForEach.Body", "body", "Body", ActivityChildSlotCardinalities.Single)]
public sealed class ForEach : ActivityBase, IActivityResult<ActivityUnit>, IRuntimeStructuralActivity, IRuntimeActivityChildCompletionHandler
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
    [ActivityInput(Key = nameof(Collection))]
    public object? Collection { get; set; }

    /// <summary>When <c>true</c>, the zero-based iteration index is exposed to the body alongside the current item.</summary>
    public bool ExposeIndex { get; set; } = true;

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext runtimeContext)
    {
        var navigator = ForEachNavigator.From(runtimeContext.ExecutableNode);
        var items = ForEachCollection.Resolve(Collection);

        // Empty/null collection or empty body short-circuits without scheduling a pass.
        if (items.Count == 0 || navigator.Body is null)
        {
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }

        ScheduleIteration(runtimeContext, navigator.Body, items, index: 0);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var runtimeContext = RequireRuntimeContext(context.ParentContext);
        var navigator = ForEachNavigator.From(runtimeContext.ExecutableNode);

        if (navigator.Body is null || !navigator.IsBody(context.CompletedChildExecutableNodeId))
            throw new ForEachExecutionException($"Completed child executable node '{context.CompletedChildExecutableNodeId}' is not the ForEach body.");

        // Early exit (#299): a body that completes with a Break outcome ends the loop now instead of
        // advancing to the next item. Recognized by outcome name so ForEach takes no dependency on the
        // (separate) Break activity module — mirroring For/While/Do.
        if (context.OutcomeNames.Contains(ActivityOutcomes.Break, StringComparer.Ordinal))
        {
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }

        var completedIndex = ResolveCompletedIndex(runtimeContext, context.CompletedChildIterationId);
        var items = ForEachCollection.Resolve(Collection);
        var nextIndex = completedIndex + 1;

        if (nextIndex >= items.Count)
        {
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }

        ScheduleIteration(runtimeContext, navigator.Body, items, nextIndex);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    private void ScheduleIteration(
        IRuntimeActivityExecutionContext runtimeContext,
        ExecutableNode body,
        ForEachCollection items,
        int index)
    {
        var ownerNodeId = runtimeContext.ExecutableNode.ExecutableNodeId;
        var parentActivityExecutionId = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId;
        var iterationId = StructuredExecutionIdentity.Iteration(parentActivityExecutionId, index);

        // Routing metadata carries identity only. Iteration values travel as protected envelopes on the
        // typed scheduler request and become a durable iteration frame before body input materialization.
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["foreach.parentActivityExecutionId"] = parentActivityExecutionId,
            ["foreach.targetNodeId"] = body.ExecutableNodeId,
            ["foreach.iterationIndex"] = index.ToString(CultureInfo.InvariantCulture)
        };
        var values = new Dictionary<string, ValueEnvelope>(StringComparer.Ordinal)
        {
            [CurrentItemVariableName] = ToItemEnvelope(runtimeContext, items.ItemAt(index))
        };
        if (ExposeIndex)
            values[CurrentIndexVariableName] = ToEnvelope(index, new ValueTypeDescriptor("Int32"), ValueProtectionPolicy.InstanceInline);

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
                metadata: metadata),
            new LoopIterationScopeRequest(ownerNodeId, iterationId, values));
    }

    private static ValueEnvelope ToItemEnvelope(IRuntimeActivityExecutionContext runtimeContext, object? value)
    {
        if (!runtimeContext.ExecutableNode.InputBindings.TryGetValue(nameof(Collection), out var collectionBinding))
            return ToEnvelope(value, new ValueTypeDescriptor("Elsa.Any"), ValueProtectionPolicy.InstanceInline);

        var policy = collectionBinding.EffectivePolicy;
        if (policy.Lifecycle == DurableValueLifecycle.None)
            throw new ForEachExecutionException("ForEach cannot create a durable iteration frame from a transient Collection input.");
        if (policy.Storage is DurableValueStorage.External or DurableValueStorage.Custom)
            throw new ForEachExecutionException($"ForEach cannot derive an item reference from Collection storage strategy '{policy.Storage}'. Materialize the items through an explicit persistable projection first.");

        var collectionType = collectionBinding.TargetType;
        var itemType = new ValueTypeDescriptor(
            collectionType.Alias,
            CollectionKind.Single,
            collectionType.Schema,
            collectionType.SchemaVersion);
        return ToEnvelope(value, itemType, policy);
    }

    private static ValueEnvelope ToEnvelope(object? value, ValueTypeDescriptor type, ValueProtectionPolicy policy)
    {
        if (value is null)
            return ValueEnvelope.Null(type, policy);
        var json = value is JsonElement element
            ? element.Clone()
            : JsonSerializer.SerializeToElement(value, value.GetType());
        return ValueEnvelope.Inline(type, json, policy);
    }

    private static int ResolveCompletedIndex(IRuntimeActivityExecutionContext runtimeContext, string? completedChildIterationId)
    {
        if (string.IsNullOrEmpty(completedChildIterationId))
            throw new ForEachExecutionException("ForEach cannot advance: the completed body child carries no iteration identity.");

        var parentActivityExecutionId = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId;
        if (!StructuredExecutionIdentity.TryReadIteration(parentActivityExecutionId, completedChildIterationId, out var index))
            throw new ForEachExecutionException($"ForEach cannot advance: completed body iteration id '{completedChildIterationId}' is not a ForEach iteration of '{parentActivityExecutionId}'.");

        return index;
    }

    /// <summary>
    /// Indexed view over the ForEach <see cref="Collection"/> input that resolves <b>only the item the
    /// current pass needs</b> instead of materializing the whole collection on every completion callback
    /// (#413 item 4). For the runtime's real input shape — a JSON array (<c>object</c> inputs are
    /// materialized as JSON) — <see cref="Count"/> is O(1) (<see cref="JsonElement.GetArrayLength"/>) and
    /// <see cref="ItemAt"/> is O(1) indexed access, so an n-item loop is O(n) total rather than O(n²). A
    /// non-JSON <see cref="IEnumerable"/> (reachable only via direct CLR collections) is materialized once
    /// per callback, preserving the prior semantics exactly. Input validation — the null, string, and
    /// non-enumerable/non-array guards — is byte-for-byte the same as the former <c>MaterializeItems</c>.
    /// </summary>
    private readonly struct ForEachCollection
    {
        private readonly JsonElement _jsonArray;
        private readonly IReadOnlyList<object?>? _materialized;
        private readonly bool _isJson;

        private ForEachCollection(JsonElement jsonArray)
        {
            _jsonArray = jsonArray;
            _isJson = true;
        }

        private ForEachCollection(IReadOnlyList<object?> materialized)
        {
            _materialized = materialized;
        }

        public int Count => _isJson ? _jsonArray.GetArrayLength() : _materialized!.Count;

        public object? ItemAt(int index) => _isJson ? UnwrapJsonScalar(_jsonArray[index]) : _materialized![index];

        public static ForEachCollection Resolve(object? collection) =>
            collection switch
            {
                null => new(Array.Empty<object?>()),
                JsonElement element => ResolveJson(element),
                string => throw new ForEachExecutionException("ForEach collection input must be an enumerable, not a string."),
                IEnumerable enumerable => new(enumerable.Cast<object?>().ToArray()),
                _ => throw new ForEachExecutionException($"ForEach collection input of type '{collection.GetType().FullName}' is not enumerable.")
            };

        // A literal/expression collection arrives as a JsonElement (the runtime materializes 'object'
        // inputs as JSON). An array is kept as-is for O(1) count/index; each item is unwrapped to its
        // scalar CLR value at access time so the body observes a plain item rather than a JsonElement.
        private static ForEachCollection ResolveJson(JsonElement element) =>
            element.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => new(Array.Empty<object?>()),
                JsonValueKind.Array => new(element),
                _ => throw new ForEachExecutionException($"ForEach collection input JSON value kind '{element.ValueKind}' is not an array.")
            };
    }

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
