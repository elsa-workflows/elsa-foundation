using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Turns workflow inputs into persisted runtime state at workflow start, mirroring how activity results
/// become immutable completion records owned by the producing invocation. Each seeded value is a
/// <see cref="DurableValueState"/> tagged with <see cref="RuntimeMetadataKeys.InputName"/> so that
/// <see cref="RuntimeInputBindingStateProjection.ProjectWorkflowInputs"/> can rebuild the <c>input.*</c>
/// snapshot for later activities and after the instance unloads/resumes.
/// </summary>
/// <remarks>
/// Workflow variables are NOT seeded here (#972): the canonical root variable frame — seeded from
/// <c>WorkflowExecutable.WorkflowVariables</c> at the workflow-started checkpoint — is the single runtime
/// truth for workflow-scope variables. The retired <c>variable:*</c> durable channel had no live readers.
/// </remarks>
public static class RuntimeWorkflowStateSeed
{
    // Reserved durable-value-id namespaces. Other durable values derive their value ids from the authored
    // output reference, so a structural collision is only possible if a compiler ever emitted an output
    // reference literally prefixed with one of these. Projection also disambiguates by metadata key, so a
    // collision would have to share both the value-id prefix and the tag; keep these prefixes reserved.
    // The variable: prefix is no longer written as a durable value id — it survives solely as the compiled
    // output-capture marker identifying workflow-variable capture targets (#972).
    public const string VariableValueIdPrefix = "variable:";
    public const string InputValueIdPrefix = "input:";
    public const string OutputValueIdPrefix = "output:";
    private const string DurableValueIdPrefix = "durable-";

    // Reserved durable-value-id namespace for the start stimulus payload (spec 089 A). A single reserved slot:
    // the stimulus is a per-execution singleton, not a named bag, and it deliberately does NOT share the
    // input:* namespace so it can never collide with (or be spoofed through) author-declared workflow inputs.
    public const string StimulusValueIdPrefix = "stimulus:";
    public const string StimulusInputSlotName = "input";

    // Reserved durable-value-id namespace for the trigger-node identity (spec 089 D). A single reserved slot:
    // the run has at most one trigger node, and — like the stimulus input above — it deliberately does NOT share
    // the input:* namespace so it can never collide with (or be spoofed through) author-declared workflow inputs.
    public const string TriggerNodeValueIdPrefix = "trigger:";
    public const string TriggerNodeIdSlotName = "nodeId";

    // Reserved durable-value-id namespace for the matched trigger binding's metadata map (spec 117 D4). A
    // single reserved slot holding the serialized string→string map: like the trigger-node identity above it
    // deliberately does NOT share the input:* namespace, so it can never collide with (or be spoofed through)
    // author-declared workflow inputs.
    public const string TriggerMetadataValueIdPrefix = "trigger-meta:";
    public const string TriggerMetadataSlotName = "metadata";

    // Reserved durable-value-id namespace for the workflow-identity projection (correlation id / instance name).
    // The identity slot name is the suffix (e.g. "identity:correlationId"); it doubles as the IdentityName tag
    // value the read-side projection filters on. Kept distinct from the variable/input/output namespaces above.
    public const string IdentityValueIdPrefix = "identity:";
    public const string IdentityCorrelationIdName = "correlationId";
    public const string IdentityInstanceNameName = "instanceName";

    /// <summary>
    /// Builds the durable-value state changes that persist the supplied workflow inputs for a workflow
    /// execution. Null collections are treated as empty. Workflow variables are seeded into the root
    /// variable frame, not this channel (#972).
    /// </summary>
    public static IReadOnlyCollection<RuntimeStateChange<DurableValueState>> BuildSeedChanges(
        string workflowExecutionId,
        IReadOnlyDictionary<string, object?>? inputs,
        DateTimeOffset capturedAt,
        JsonElement? stimulusInput = null,
        string? triggerNodeId = null,
        IReadOnlyDictionary<string, string>? triggerMetadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        var changes = new List<RuntimeStateChange<DurableValueState>>();

        foreach (var (name, value) in inputs ?? EmptyValues)
            changes.Add(NewSeedChange(workflowExecutionId, RuntimeMetadataKeys.InputName, InputValueIdPrefix, name, value, capturedAt));

        // The start stimulus payload rides its own reserved channel (spec 089 A): never the input:* namespace.
        if (stimulusInput is { } stimulus)
            changes.Add(NewSeedChange(workflowExecutionId, RuntimeMetadataKeys.StimulusInputName, StimulusValueIdPrefix, StimulusInputSlotName, stimulus, capturedAt));

        // The trigger-node identity rides its own reserved channel (spec 089 D): never the input:* namespace.
        if (!string.IsNullOrWhiteSpace(triggerNodeId))
            changes.Add(NewSeedChange(workflowExecutionId, RuntimeMetadataKeys.TriggerNodeId, TriggerNodeValueIdPrefix, TriggerNodeIdSlotName, triggerNodeId, capturedAt));

        // The matched trigger binding's metadata map rides its own reserved channel (spec 117 D4): the whole
        // string→string map is one durable value, never the input:* namespace. An empty map is not seeded.
        if (triggerMetadata is { Count: > 0 })
            changes.Add(NewSeedChange(
                workflowExecutionId,
                RuntimeMetadataKeys.TriggerMetadataName,
                TriggerMetadataValueIdPrefix,
                TriggerMetadataSlotName,
                triggerMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
                capturedAt));

        return changes;
    }

    /// <summary>
    /// Builds the durable-value changes that persist named workflow outputs assigned by the <c>SetOutput</c>
    /// leaf control activity (#260). Each output is captured as an inline <c>Instance</c>-lifecycle durable
    /// value tagged with <see cref="RuntimeMetadataKeys.OutputName"/> under a reserved
    /// <see cref="OutputValueIdPrefix"/> value id so it is
    /// durably queryable after the run. A later assignment of the same name upserts the same durable value.
    /// Null collection is treated as empty.
    /// </summary>
    public static IReadOnlyCollection<RuntimeStateChange<DurableValueState>> BuildWorkflowOutputChanges(
        string workflowExecutionId,
        IReadOnlyDictionary<string, object?>? outputs,
        DateTimeOffset capturedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        var changes = new List<RuntimeStateChange<DurableValueState>>();

        foreach (var (name, value) in outputs ?? EmptyValues)
            changes.Add(NewSeedChange(workflowExecutionId, RuntimeMetadataKeys.OutputName, OutputValueIdPrefix, name, value, capturedAt));

        return changes;
    }

    /// <summary>
    /// Builds the durable-value changes that project the workflow identity (correlation id / instance name)
    /// assigned by a <c>Correlate</c>/<c>SetName</c> leaf (spec 083 review). Each requested slot is upserted under
    /// its reserved <see cref="IdentityValueIdPrefix"/> value id, tagged with <see cref="RuntimeMetadataKeys.IdentityName"/>
    /// so <c>RuntimeIdentityStateProjection</c> can surface it to a later — including a concurrent sibling-branch —
    /// activity invocation without a workflow-execution-state read. This is an additional projection channel for
    /// expressions; <see cref="WorkflowExecutionState.CorrelationId"/> / system metadata remain the authoritative
    /// queryable home, written in the same commit. A cleared assignment (null value) is written as a JSON-null
    /// inline value under the same value id — the clear must propagate, so it is never skipped. Only requested
    /// slots are emitted; an unrequested slot leaves any prior projection untouched.
    /// </summary>
    public static IReadOnlyCollection<RuntimeStateChange<DurableValueState>> BuildIdentityChanges(
        string workflowExecutionId,
        bool correlationIdAssignmentRequested,
        string? correlationId,
        bool instanceNameAssignmentRequested,
        string? instanceName,
        DateTimeOffset capturedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        var changes = new List<RuntimeStateChange<DurableValueState>>();

        if (correlationIdAssignmentRequested)
            changes.Add(NewSeedChange(workflowExecutionId, RuntimeMetadataKeys.IdentityName, IdentityValueIdPrefix, IdentityCorrelationIdName, correlationId, capturedAt));

        if (instanceNameAssignmentRequested)
            changes.Add(NewSeedChange(workflowExecutionId, RuntimeMetadataKeys.IdentityName, IdentityValueIdPrefix, IdentityInstanceNameName, instanceName, capturedAt));

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
