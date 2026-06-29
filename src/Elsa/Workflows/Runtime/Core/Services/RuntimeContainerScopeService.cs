using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Builds and persists the live container-scoped variable state for one activity execution (ADR 0027,
/// #210). It walks the activity's ancestor container executions, projects each container's declared
/// variables from its compiled executable structure, and restores its current values from that
/// container execution's persisted snapshot — the container's <see cref="ActivityExecutionState"/>
/// metadata is the single source of truth for its values, so reads and writes never diverge across a
/// second store. The resulting <see cref="VariableScope"/> chain (outermost container first) is
/// threaded into input-expression evaluation and activity execution; mutations are written back to
/// the owning container execution's snapshot.
/// </summary>
/// <remarks>
/// The chain is anchored by the outermost <b>root activity scope</b> — the root node carries the workflow's
/// variables. It keeps its node-id identity so structured <c>Variable</c> references against the root resolve,
/// but in this runtime its value store is the seeded durable-value state (Seam C, #254): when the caller
/// supplies the current <c>variables.*</c> projection, the root scope is restored from that projection each
/// materialization and its mutations are written back as <c>VariableName</c>-tagged durable values (#286), so
/// there is no second divergent copy — the scope is a per-materialization view over that one store. Nested
/// container scopes remain reference-key addressed and persist to their own execution snapshot per concrete
/// execution (#210).
/// </remarks>
public sealed class RuntimeContainerScopeService(IActivityExecutionStateStore activityExecutionStateStore)
{
    private readonly RuntimeVariableScopeFactory _scopeFactory = new();
    private readonly RuntimeLoopIterationScopeFactory _iterationScopeFactory = new();

    /// <summary>
    /// Builds the innermost visible <see cref="VariableScope"/> for <paramref name="activityState"/>
    /// by walking its ancestor container executions (nearest first), then layering the per-iteration
    /// loop scope (#259, ADR 0028) on top when the activity was scheduled as a loop body, or <c>null</c>
    /// when neither contributes any variable.
    /// </summary>
    public async ValueTask<VariableScope?> BuildScopeAsync(
        WorkflowExecutable executable,
        string workflowExecutionId,
        ActivityExecutionState activityState,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, object?>? workflowVariableValues = null)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(activityState);

        // The root activity carries the workflow's variables (#286). When the caller supplies the current
        // workflow-variable projection, the root scope is sourced from and written back to durable-value state
        // (so `variables.*` reads see prior mutations and the body's writes persist); callers that pass null
        // keep the prior behavior of seeding the root from its own ActivityExecutionState snapshot only.
        var workflowScopeActive = workflowVariableValues is not null;
        var rootNodeId = executable.RootActivity.ExecutableNodeId;

        var layers = new List<RuntimeContainerScopeLayer>();
        var ancestorId = activityState.ParentActivityExecutionId;
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (!string.IsNullOrEmpty(ancestorId) && visited.Add(ancestorId))
        {
            var ancestorState = await activityExecutionStateStore.FindAsync(workflowExecutionId, ancestorId, cancellationToken);
            if (ancestorState is null)
                break;

            if (executable.NodesById.TryGetValue(ancestorState.Execution.ExecutableNodeId, out var ancestorNode))
            {
                var declared = _scopeFactory.ProjectDeclaredVariables(ancestorNode);
                if (declared.Count > 0)
                {
                    // The root scope keeps its node-id identity (so structured Variable references against the
                    // root still resolve) but, when the workflow scope is active, draws its values from the
                    // durable-value projection mapped onto reference keys rather than the local snapshot.
                    var isWorkflowRoot = StringComparer.Ordinal.Equals(ancestorState.Execution.ExecutableNodeId, rootNodeId);
                    var values = workflowScopeActive && isWorkflowRoot
                        ? MapValuesByNameToReferenceKey(declared, workflowVariableValues!)
                        : ReadSnapshot(ancestorState);

                    layers.Add(new RuntimeContainerScopeLayer(
                        ScopeId: ancestorState.Execution.ExecutableNodeId,
                        ExecutionId: ancestorState.Execution.ActivityExecutionId,
                        Variables: declared,
                        Values: values,
                        IsCompleted: IsScopeCompleted(ancestorState)));
                }
            }

            ancestorId = ancestorState.ParentActivityExecutionId;
        }

        // Container chain assembled outermost-first (ancestors were collected nearest-first).
        layers.Reverse();

        var containerScope = layers.Count == 0 ? null : _scopeFactory.BuildChain(layers);

        // The loop body resolves its current item/index through an innermost iteration scope chained on
        // top of the enclosing container chain (ADR 0028). The loop owner publishes the iteration values
        // in the body's scheduling-provenance metadata; this wires #259's factory into the real
        // evaluation path so input expressions referencing the loop variable resolve end-to-end.
        return BuildIterationScopeOrDefault(activityState, containerScope);
    }

    private VariableScope? BuildIterationScopeOrDefault(ActivityExecutionState activityState, VariableScope? containerScope)
    {
        var metadata = activityState.Provenance.Metadata;
        if (!metadata.TryGetValue(RuntimeMetadataKeys.LoopIterationOwnerNodeId, out var ownerNodeId) ||
            string.IsNullOrEmpty(ownerNodeId) ||
            !metadata.TryGetValue(RuntimeMetadataKeys.LoopIterationItemName, out var itemName) ||
            string.IsNullOrEmpty(itemName))
            return containerScope;

        var iterationId = activityState.IterationId;
        if (string.IsNullOrEmpty(iterationId))
            return containerScope;

        var item = DeserializeIterationValue(metadata, RuntimeMetadataKeys.LoopIterationItemValue);

        string? indexName = null;
        var index = 0;
        if (metadata.TryGetValue(RuntimeMetadataKeys.LoopIterationIndexName, out var declaredIndexName) &&
            !string.IsNullOrEmpty(declaredIndexName))
        {
            indexName = declaredIndexName;
            index = DeserializeIterationValue(metadata, RuntimeMetadataKeys.LoopIterationIndexValue) is int parsedIndex ? parsedIndex : 0;
        }

        return _iterationScopeFactory.BuildIterationScope(
            new LoopIterationScopeRequest(
                OwnerNodeId: ownerNodeId,
                IterationId: iterationId,
                ItemReferenceKey: itemName,
                ItemName: itemName,
                Item: item,
                Index: index,
                IndexReferenceKey: indexName,
                IndexName: indexName),
            parent: containerScope);
    }

    private static object? DeserializeIterationValue(IReadOnlyDictionary<string, string> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var serialized) || string.IsNullOrEmpty(serialized))
            return null;

        try
        {
            using var document = JsonDocument.Parse(serialized);
            return ConvertJsonElement(document.RootElement);
        }
        catch (JsonException)
        {
            return serialized;
        }
    }

    private static object? ConvertJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => ConvertJsonNumber(element),
        _ => element.Clone()
    };

    // Box an integral JSON number as its own integer type rather than letting the conditional operator
    // widen everything to double: a single ternary over int/long/double infers a `double` common type,
    // so callers' `is int`/`is long` pattern checks would fail. Explicit returns keep an integer an
    // integer (int when it fits, else long) and a genuine fractional value a double.
    private static object ConvertJsonNumber(JsonElement element)
    {
        if (element.TryGetInt32(out var i))
            return i;
        if (element.TryGetInt64(out var l))
            return l;
        return element.GetDouble();
    }

    /// <summary>
    /// Writes back every container scope in <paramref name="scope"/>'s visible chain whose live
    /// values now differ from the persisted snapshot of its owning container execution, so sibling
    /// branches and later activities observe assignments and resume restores them. No-op when the
    /// chain is null. Returns the number of container executions whose snapshot was updated.
    /// </summary>
    public async ValueTask<int> PersistScopeMutationsAsync(
        VariableScope? scope,
        string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        var updated = 0;

        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.ExecutionId is not { } executionId)
                continue;

            var containerState = await activityExecutionStateStore.FindAsync(workflowExecutionId, executionId, cancellationToken);
            if (containerState is null)
                continue;

            var mutated = CaptureScopeMutation(containerState, current);
            if (mutated is null)
                continue;

            await activityExecutionStateStore.SaveAsync(mutated, cancellationToken);
            updated++;
        }

        return updated;
    }

    /// <summary>
    /// Maps a name-keyed value projection (e.g. the current <c>variables.*</c> durable-value projection) onto
    /// the reference-key-addressed value store a <see cref="VariableScope"/> expects, using the declared
    /// variables to translate each authored name to its reference key. Reference keys with no projected value
    /// are omitted so the scope falls back to the declared default.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> MapValuesByNameToReferenceKey(
        IReadOnlyDictionary<string, IVariable> declaredByReferenceKey,
        IReadOnlyDictionary<string, object?> valuesByName)
    {
        if (valuesByName.Count == 0)
            return EmptyValues;

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (referenceKey, variable) in declaredByReferenceKey)
        {
            if (valuesByName.TryGetValue(variable.Name, out var value))
                result[referenceKey] = value;
        }

        return result;
    }

    /// <summary>
    /// Captures mid-run mutations of the workflow scope's variables from <paramref name="scope"/>'s visible
    /// chain (#286) — the scope whose identity is the root activity node id, <paramref name="rootNodeId"/> —
    /// and returns the durable-value write-back changes that persist them, keyed by authored variable name so
    /// <see cref="RuntimeInputBindingStateProjection.ProjectWorkflowVariables"/> re-projects them next
    /// materialization. Only variables whose current value differs from <paramref name="sourceValuesByName"/>
    /// (the projection the scope was sourced from) are emitted, so a read-only activity over a workflow that
    /// declares variables produces no write — mirroring the dirty-tracking guard
    /// <see cref="CaptureScopeMutation"/> applies to container scopes. Returns an empty collection when no such
    /// scope is present or nothing changed. Unlike container scopes (which persist to their owning execution's
    /// snapshot), the workflow scope persists to durable-value state, mirroring how the start-time seed lives
    /// there; the caller folds the returned changes into the activity-completion checkpoint.
    /// </summary>
    public IReadOnlyCollection<RuntimeStateChange<DurableValueState>> BuildWorkflowScopeWriteBackChanges(
        VariableScope? scope,
        string workflowExecutionId,
        string rootNodeId,
        IReadOnlyDictionary<string, object?> sourceValuesByName,
        DateTimeOffset capturedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootNodeId);
        ArgumentNullException.ThrowIfNull(sourceValuesByName);

        for (var current = scope; current is not null; current = current.Parent)
        {
            if (!StringComparer.Ordinal.Equals(current.ScopeId, rootNodeId))
                continue;

            var changed = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (name, value) in ProjectScopeValuesByName(current))
            {
                // Emit only variables whose value actually changed from the start-of-activity projection. The
                // scope is seeded from JSON-encoded projection values and a mutation replaces them with a CLR
                // value, so compare on the canonical JSON form (the same shape the write-back persists).
                var hadSource = sourceValuesByName.TryGetValue(name, out var sourceValue);
                if (!hadSource || !SerializedValuesEqual(sourceValue, value))
                    changed[name] = value;
            }

            return changed.Count == 0
                ? []
                : RuntimeWorkflowStateSeed.BuildVariableWriteBackChanges(workflowExecutionId, changed, capturedAt);
        }

        return [];
    }

    private static bool SerializedValuesEqual(object? left, object? right) =>
        StringComparer.Ordinal.Equals(SerializeForComparison(left), SerializeForComparison(right));

    private static string SerializeForComparison(object? value) =>
        value switch
        {
            null => "null",
            JsonElement json => json.GetRawText(),
            _ => JsonSerializer.Serialize(value, value.GetType())
        };

    /// <summary>
    /// Maps a scope's current values (reference-key addressed) onto the authored variable names its
    /// declarations carry, dropping any reference key with no live value. Translates the scope's reference-key
    /// value store back into the name-keyed shape durable-value projection expects. Only this scope's own
    /// declarations are considered (the scope is consulted as the chain head, so its parents are not walked).
    /// </summary>
    private static IReadOnlyDictionary<string, object?> ProjectScopeValuesByName(VariableScope scope)
    {
        var snapshot = scope.SnapshotValues();
        if (snapshot.Count == 0)
            return EmptyValues;

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var variable in scope.EnumerateVisibleVariables())
        {
            if (snapshot.TryGetValue(variable.Id, out var value))
                result[variable.Name] = value;
        }

        return result;
    }

    /// <summary>
    /// Returns <paramref name="containerState"/> with its persisted scope-value snapshot refreshed
    /// from <paramref name="scope"/> when the live values differ, or <c>null</c> when nothing changed.
    /// </summary>
    public static ActivityExecutionState? CaptureScopeMutation(ActivityExecutionState containerState, VariableScope scope)
    {
        ArgumentNullException.ThrowIfNull(containerState);
        ArgumentNullException.ThrowIfNull(scope);

        var snapshot = scope.SnapshotValues();
        var serialized = JsonSerializer.Serialize(snapshot);

        if (containerState.Metadata.TryGetValue(RuntimeMetadataKeys.ScopedVariableValues, out var existing) &&
            StringComparer.Ordinal.Equals(existing, serialized))
            return null;

        var metadata = containerState.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.ScopedVariableValues] = serialized;
        return containerState with { Metadata = metadata };
    }

    /// <summary>
    /// Marks <paramref name="containerState"/>'s scope completed (ADR 0027, #210) so that any later
    /// rebuild treats it as non-live for runtime expressions. Idempotent; returns the same instance
    /// when already marked.
    /// </summary>
    public static ActivityExecutionState MarkScopeCompleted(ActivityExecutionState containerState)
    {
        ArgumentNullException.ThrowIfNull(containerState);

        if (IsScopeCompleted(containerState))
            return containerState;

        var metadata = containerState.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.ScopedVariableScopeCompleted] = bool.TrueString;
        return containerState with { Metadata = metadata };
    }

    /// <summary>
    /// Reads the current values of the variables a container declares, for capturing completed-scope
    /// evidence: the assigned value where present, otherwise the declared default. Returns an empty
    /// list when the node declares no container-scoped variables.
    /// </summary>
    public IReadOnlyList<RuntimeScopedVariableValue> ReadScopeVariableValues(ExecutableNode containerNode, ActivityExecutionState containerState)
    {
        ArgumentNullException.ThrowIfNull(containerNode);
        ArgumentNullException.ThrowIfNull(containerState);

        var declared = _scopeFactory.ProjectDeclaredVariables(containerNode);
        if (declared.Count == 0)
            return [];

        var values = ReadSnapshot(containerState);
        return declared
            .Select(entry => new RuntimeScopedVariableValue(
                entry.Value.Name,
                entry.Key,
                values.TryGetValue(entry.Key, out var value) ? value : entry.Value.DefaultValue))
            .ToArray();
    }

    private static bool IsScopeCompleted(ActivityExecutionState containerState) =>
        containerState.Metadata.TryGetValue(RuntimeMetadataKeys.ScopedVariableScopeCompleted, out var flag) &&
        StringComparer.OrdinalIgnoreCase.Equals(flag, bool.TrueString);

    private static IReadOnlyDictionary<string, object?> ReadSnapshot(ActivityExecutionState containerState)
    {
        if (!containerState.Metadata.TryGetValue(RuntimeMetadataKeys.ScopedVariableValues, out var serialized) ||
            string.IsNullOrWhiteSpace(serialized))
            return EmptyValues;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(serialized) ?? EmptyValues;
        }
        catch (JsonException)
        {
            return EmptyValues;
        }
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyValues = new Dictionary<string, object?>(StringComparer.Ordinal);
}
