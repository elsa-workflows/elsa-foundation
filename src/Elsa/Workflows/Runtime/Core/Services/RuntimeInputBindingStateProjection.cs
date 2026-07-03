using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Projects persisted runtime state into the name-keyed value snapshots that
/// <see cref="RuntimeInputBindingResolutionContext"/> exposes to activity input expressions.
/// </summary>
public static class RuntimeInputBindingStateProjection
{
    /// <summary>
    /// Builds the prior-activity-output snapshot (output name → value) from the durable values captured for a
    /// workflow execution. When several captures share an output name the most recently captured value wins.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> ProjectActivityOutputValues(IEnumerable<DurableValueState> durableValues) =>
        ProjectByMetadataKey(durableValues, RuntimeMetadataKeys.OutputName);

    /// <summary>
    /// Builds the workflow-variable snapshot (variable name → value) from the durable values captured for a
    /// workflow execution, mirroring <see cref="ProjectActivityOutputValues"/>. Variables are tagged with the
    /// <see cref="RuntimeMetadataKeys.VariableName"/> metadata key when seeded. When several captures share a
    /// variable name the most recently captured value wins. Feeds <c>variables.*</c> at materialization time.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> ProjectWorkflowVariables(IEnumerable<DurableValueState> durableValues) =>
        ProjectByMetadataKey(durableValues, RuntimeMetadataKeys.VariableName);

    /// <summary>
    /// Builds the workflow-input snapshot (input name → value) from the durable values captured for a workflow
    /// execution, mirroring <see cref="ProjectActivityOutputValues"/>. Inputs are tagged with the
    /// <see cref="RuntimeMetadataKeys.InputName"/> metadata key when seeded. When several captures share an
    /// input name the most recently captured value wins. Feeds <c>input.*</c> at materialization time.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> ProjectWorkflowInputs(IEnumerable<DurableValueState> durableValues) =>
        ProjectByMetadataKey(durableValues, RuntimeMetadataKeys.InputName);

    /// <summary>
    /// Builds a name → value snapshot from the durable values tagged with <paramref name="nameMetadataKey"/>, most
    /// recent <see cref="DurableValueState.CapturedAt"/> per name winning. Shared by the variable/input/output
    /// projections above and by <see cref="RuntimeIdentityStateProjection"/> so the last-capture-wins rule lives in
    /// one place. A cleared value persists as a JSON-null inline value and is retained (callers unwrap as needed).
    /// </summary>
    internal static IReadOnlyDictionary<string, object?> ProjectByMetadataKey(IEnumerable<DurableValueState> durableValues, string nameMetadataKey)
    {
        ArgumentNullException.ThrowIfNull(durableValues);

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var durableValue in durableValues
                     .Where(value => value.InlineValue.HasValue && value.Metadata.ContainsKey(nameMetadataKey))
                     .OrderBy(value => value.CapturedAt))
        {
            var name = durableValue.Metadata[nameMetadataKey];
            result[name] = durableValue.InlineValue!.Value;
        }

        return result;
    }
}

/// <summary>
/// Projects the workflow identity (correlation id / instance name) out of the durable values captured for a
/// workflow execution (spec 083 review). Identity slots are tagged with <see cref="RuntimeMetadataKeys.IdentityName"/>
/// (slot name = tag value) by <see cref="RuntimeWorkflowStateSeed.BuildIdentityChanges"/>, so any activity
/// invocation — including a concurrent sibling branch — re-lists them and observes the current value without a
/// per-invocation workflow-execution-state read, restoring cross-branch visibility over the metadata channel it
/// replaced. Most-recent <c>CapturedAt</c> wins, matching <see cref="RuntimeInputBindingStateProjection"/>.
/// </summary>
public static class RuntimeIdentityStateProjection
{
    /// <summary>
    /// Resolves both identity slots (correlation id / instance name) in a single pass over the durable values,
    /// reusing the shared <see cref="RuntimeInputBindingStateProjection.ProjectByMetadataKey"/> most-recent-wins
    /// projection over the <see cref="RuntimeMetadataKeys.IdentityName"/> tag, then unwrapping each slot's inline
    /// JSON. A cleared assignment persists as a JSON-null value, so the latest capture may legitimately be null.
    /// Handlers on the hot path call this once rather than scanning the list per slot.
    /// </summary>
    public static RuntimeWorkflowIdentity Project(IEnumerable<DurableValueState> durableValues)
    {
        var slots = RuntimeInputBindingStateProjection.ProjectByMetadataKey(durableValues, RuntimeMetadataKeys.IdentityName);
        return new RuntimeWorkflowIdentity(
            CorrelationId: Unwrap(slots, RuntimeWorkflowStateSeed.IdentityCorrelationIdName),
            InstanceName: Unwrap(slots, RuntimeWorkflowStateSeed.IdentityInstanceNameName));
    }

    /// <summary>Resolves the current correlation id for the execution-time carrier, or null when unassigned/cleared.</summary>
    public static string? ProjectCorrelationId(IEnumerable<DurableValueState> durableValues) => Project(durableValues).CorrelationId;

    /// <summary>Resolves the current instance name for the execution-time carrier, or null when unassigned/cleared.</summary>
    public static string? ProjectInstanceName(IEnumerable<DurableValueState> durableValues) => Project(durableValues).InstanceName;

    private static string? Unwrap(IReadOnlyDictionary<string, object?> slots, string slotName) =>
        slots.TryGetValue(slotName, out var value) && value is JsonElement { ValueKind: JsonValueKind.String } inline
            ? inline.GetString()
            : null; // Absent, or a JSON-null (cleared) inline value → no identity.
}

/// <summary>The workflow identity projected for the execution-time expression carrier (ADR 0030): the two
/// runtime-mutable slots a <c>Correlate</c>/<c>SetName</c> leaf assigns. Both null until first assigned.</summary>
public readonly record struct RuntimeWorkflowIdentity(string? CorrelationId, string? InstanceName);
