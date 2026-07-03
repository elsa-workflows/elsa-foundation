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
    /// Projects the workflow-input, workflow-variable, and prior-activity-output snapshots in one call. Every
    /// scheduler work handler needs all three from the same durable-value list before building its resolution
    /// context and execution-time expression carrier, so this collapses the repeated three-projection block.
    /// </summary>
    public static RuntimeInputBindingStateProjectionSet ProjectAll(IEnumerable<DurableValueState> durableValues)
    {
        ArgumentNullException.ThrowIfNull(durableValues);

        var materialized = durableValues as IReadOnlyCollection<DurableValueState> ?? durableValues.ToArray();
        return new RuntimeInputBindingStateProjectionSet(
            WorkflowInputs: ProjectWorkflowInputs(materialized),
            WorkflowVariables: ProjectWorkflowVariables(materialized),
            ActivityOutputValues: ProjectActivityOutputValues(materialized));
    }

    private static IReadOnlyDictionary<string, object?> ProjectByMetadataKey(IEnumerable<DurableValueState> durableValues, string nameMetadataKey)
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
/// The workflow-input, workflow-variable, and prior-activity-output snapshots projected together from a single
/// durable-value list (see <see cref="RuntimeInputBindingStateProjection.ProjectAll"/>).
/// </summary>
public readonly record struct RuntimeInputBindingStateProjectionSet(
    IReadOnlyDictionary<string, object?> WorkflowInputs,
    IReadOnlyDictionary<string, object?> WorkflowVariables,
    IReadOnlyDictionary<string, object?> ActivityOutputValues);
