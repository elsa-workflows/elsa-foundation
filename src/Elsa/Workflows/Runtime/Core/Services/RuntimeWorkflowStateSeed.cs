using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Turns workflow variables and inputs into persisted runtime state at workflow start, mirroring how
/// activity outputs become durable values (see <c>ActivityOutputPublisher.NewDurableValueChange</c>). Each
/// seeded value is a <see cref="DurableValueState"/> tagged with <see cref="RuntimeMetadataKeys.VariableName"/>
/// or <see cref="RuntimeMetadataKeys.InputName"/> so that
/// <see cref="RuntimeInputBindingStateProjection.ProjectWorkflowVariables"/> /
/// <see cref="RuntimeInputBindingStateProjection.ProjectWorkflowInputs"/> can rebuild the <c>variables.*</c>
/// and <c>input.*</c> snapshots for later activities and after the instance unloads/resumes.
/// </summary>
public static class RuntimeWorkflowStateSeed
{
    public const string VariableValueIdPrefix = "variable:";
    public const string InputValueIdPrefix = "input:";
    private const string DurableValueIdPrefix = "durable-";

    /// <summary>
    /// Builds the durable-value state changes that persist the supplied workflow variables and inputs for a
    /// workflow execution. Variable and input names share no key space because they are stored under distinct
    /// value-id prefixes. Null collections are treated as empty.
    /// </summary>
    public static IReadOnlyCollection<RuntimeStateChange<DurableValueState>> BuildSeedChanges(
        string workflowExecutionId,
        IReadOnlyDictionary<string, object?>? variables,
        IReadOnlyDictionary<string, object?>? inputs,
        DateTimeOffset capturedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        var changes = new List<RuntimeStateChange<DurableValueState>>();

        foreach (var (name, value) in variables ?? EmptyValues)
            changes.Add(NewSeedChange(workflowExecutionId, RuntimeMetadataKeys.VariableName, VariableValueIdPrefix, name, value, capturedAt));

        foreach (var (name, value) in inputs ?? EmptyValues)
            changes.Add(NewSeedChange(workflowExecutionId, RuntimeMetadataKeys.InputName, InputValueIdPrefix, name, value, capturedAt));

        return changes;
    }

    private static RuntimeStateChange<DurableValueState> NewSeedChange(
        string workflowExecutionId,
        string nameMetadataKey,
        string valueIdPrefix,
        string name,
        object? value,
        DateTimeOffset capturedAt)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Seeded variable/input names cannot be blank.", nameof(name));

        var valueId = $"{valueIdPrefix}{name}";
        var durableValueId = $"{DurableValueIdPrefix}{valueId}";
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameMetadataKey] = name
        };

        var state = new DurableValueState(
            durableValueId: durableValueId,
            workflowExecutionId: workflowExecutionId,
            valueId: valueId,
            type: TypeDescriptorFor(value),
            lifecycle: DurableValueLifecycle.Instance,
            storage: DurableValueStorage.Inline,
            inlineValue: Serialize(value),
            externalReference: null,
            sourceActivityExecutionId: null,
            capturedAt: capturedAt,
            metadata: metadata);

        return new RuntimeStateChange<DurableValueState>(
            StateId: durableValueId,
            Operation: RuntimeStateChangeOperation.Upsert,
            State: state,
            Metadata: metadata);
    }

    private static JsonElement Serialize(object? value) =>
        value is JsonElement json
            ? json.Clone()
            : JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object));

    private static RuntimeValueTypeDescriptor TypeDescriptorFor(object? value) =>
        new("clr", value?.GetType().FullName ?? typeof(object).FullName, null);

    private static readonly IReadOnlyDictionary<string, object?> EmptyValues =
        new Dictionary<string, object?>(StringComparer.Ordinal);
}
