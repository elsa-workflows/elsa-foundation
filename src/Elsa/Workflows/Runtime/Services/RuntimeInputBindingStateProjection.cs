using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
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
    /// Projects the start stimulus payload (spec 089 A) from its reserved single-slot channel
    /// (<see cref="RuntimeMetadataKeys.StimulusInputName"/>). Null when the execution was not started by a
    /// stimulus carrying a payload. Deliberately not part of the workflow-input namespace.
    /// </summary>
    public static object? ProjectStimulusInput(IEnumerable<DurableValueState> durableValues) =>
        ProjectByMetadataKey(durableValues, RuntimeMetadataKeys.StimulusInputName)
            .GetValueOrDefault(RuntimeWorkflowStateSeed.StimulusInputSlotName);

    /// <summary>
    /// Projects the executable node id of the trigger that started this execution (spec 089 D) from its reserved
    /// single-slot channel (<see cref="RuntimeMetadataKeys.TriggerNodeId"/>). Null when the execution was not
    /// started by a trigger (a direct run) or was a resume-only path. Deliberately not part of the workflow-input
    /// namespace. Unwrapped from the inline JSON string the seed persisted.
    /// </summary>
    public static string? ProjectTriggerNodeId(IEnumerable<DurableValueState> durableValues) =>
        ProjectByMetadataKey(durableValues, RuntimeMetadataKeys.TriggerNodeId)
            .GetValueOrDefault(RuntimeWorkflowStateSeed.TriggerNodeIdSlotName) is JsonElement { ValueKind: JsonValueKind.String } inline
            ? inline.GetString()
            : null;

    /// <summary>
    /// Projects the workflow identity (correlation id / instance name), workflow-input, workflow-variable, and
    /// prior-activity-output snapshots in one call. Every scheduler work handler needs all of these from the same
    /// durable-value list before building its resolution context and execution-time expression carrier, so this
    /// collapses the repeated projection block. Identity (spec 083 review) is projected from the same list, so a
    /// <c>Correlate</c>/<c>SetName</c> is visible to a concurrent sibling branch without a workflow-execution-state read.
    /// </summary>
    public static RuntimeInputBindingStateProjectionSet ProjectAll(IEnumerable<DurableValueState> durableValues)
    {
        ArgumentNullException.ThrowIfNull(durableValues);

        var materialized = durableValues as IReadOnlyCollection<DurableValueState> ?? durableValues.ToArray();
        var identity = RuntimeIdentityStateProjection.Project(materialized);
        return new RuntimeInputBindingStateProjectionSet(
            WorkflowInputs: ProjectWorkflowInputs(materialized),
            WorkflowVariables: ProjectWorkflowVariables(materialized),
            ActivityOutputValues: ProjectActivityOutputValues(materialized),
            CorrelationId: identity.CorrelationId,
            InstanceName: identity.InstanceName,
            StimulusInput: ProjectStimulusInput(materialized),
            TriggerNodeId: ProjectTriggerNodeId(materialized));
    }

    /// <summary>
    /// Driver-aware projection used by execution handlers. Durable values carrying an explicit stable storage
    /// driver are decoded through that driver before entering workflow-variable/input/output expression state;
    /// legacy inline states without a driver marker retain the existing JSON-element projection.
    /// </summary>
    public static async ValueTask<RuntimeInputBindingStateProjectionSet> ProjectAllAsync(
        IEnumerable<DurableValueState> durableValues,
        IRuntimeDurableValueStorageDriverRegistry storageDrivers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(durableValues);
        ArgumentNullException.ThrowIfNull(storageDrivers);
        var materialized = durableValues as IReadOnlyCollection<DurableValueState> ?? durableValues.ToArray();
        var identity = RuntimeIdentityStateProjection.Project(materialized);
        return new RuntimeInputBindingStateProjectionSet(
            WorkflowInputs: await ProjectByMetadataKeyAsync(materialized, RuntimeMetadataKeys.InputName, storageDrivers, cancellationToken),
            WorkflowVariables: await ProjectByMetadataKeyAsync(materialized, RuntimeMetadataKeys.VariableName, storageDrivers, cancellationToken),
            ActivityOutputValues: await ProjectByMetadataKeyAsync(materialized, RuntimeMetadataKeys.OutputName, storageDrivers, cancellationToken),
            CorrelationId: identity.CorrelationId,
            InstanceName: identity.InstanceName,
            StimulusInput: ProjectStimulusInput(materialized),
            TriggerNodeId: ProjectTriggerNodeId(materialized));
    }

    private static async ValueTask<IReadOnlyDictionary<string, object?>> ProjectByMetadataKeyAsync(
        IEnumerable<DurableValueState> durableValues,
        string nameMetadataKey,
        IRuntimeDurableValueStorageDriverRegistry storageDrivers,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var durableValue in durableValues
                     .Where(value => value.Metadata.ContainsKey(nameMetadataKey))
                     .OrderBy(value => value.CapturedAt))
        {
            cancellationToken.ThrowIfCancellationRequested();
            object? value;
            if (durableValue.Metadata.TryGetValue(RuntimeMetadataKeys.StorageDriverKey, out var driverKey))
                value = await storageDrivers.GetRequired(driverKey).DecodeAsync(durableValue, cancellationToken);
            else if (durableValue.InlineValue.HasValue)
                value = durableValue.InlineValue.Value;
            else
                continue;
            result[durableValue.Metadata[nameMetadataKey]] = value;
        }
        return result;
    }

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
/// The workflow identity plus the workflow-input, workflow-variable, and prior-activity-output snapshots projected
/// together from a single durable-value list (see <see cref="RuntimeInputBindingStateProjection.ProjectAll"/>).
/// </summary>
public readonly record struct RuntimeInputBindingStateProjectionSet(
    IReadOnlyDictionary<string, object?> WorkflowInputs,
    IReadOnlyDictionary<string, object?> WorkflowVariables,
    IReadOnlyDictionary<string, object?> ActivityOutputValues,
    string? CorrelationId,
    string? InstanceName,
    object? StimulusInput,
    string? TriggerNodeId);

/// <summary>
/// Projects the workflow identity (correlation id / instance name) out of the durable values captured for a workflow
/// execution (spec 083 review). Identity slots are tagged with <see cref="RuntimeMetadataKeys.IdentityName"/> (slot
/// name = tag value) by <see cref="RuntimeWorkflowStateSeed.BuildIdentityChanges"/>, so any activity invocation —
/// including a concurrent sibling branch — re-lists them and observes the current value without a per-invocation
/// workflow-execution-state read, restoring cross-branch visibility. Most-recent <c>CapturedAt</c> wins.
/// </summary>
public static class RuntimeIdentityStateProjection
{
    /// <summary>
    /// Resolves both identity slots in a single pass, reusing the shared
    /// <see cref="RuntimeInputBindingStateProjection.ProjectByMetadataKey"/> most-recent-wins projection over the
    /// <see cref="RuntimeMetadataKeys.IdentityName"/> tag, then unwrapping each slot's inline JSON. A cleared
    /// assignment persists as a JSON-null value, so the latest capture may legitimately be null.
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
