using System.Collections.ObjectModel;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Runtime-owned node inside a workflow executable.
/// </summary>
public sealed class ExecutableNode
{
    public ExecutableNode(
        string executableNodeId,
        string authoredActivityId,
        string activityType,
        string activityTypeVersion,
        string descriptorType,
        JsonElement descriptorPayload,
        IReadOnlyDictionary<string, RuntimeInputBinding> inputBindings,
        IReadOnlyDictionary<string, RuntimeOutputCapture> outputCaptures,
        IReadOnlyDictionary<string, string> metadata,
        IReadOnlyCollection<ExecutableChildSlot>? childSlots = null,
        ExecutableActivityStructure? structure = null,
        ActivityContract? activityContract = null,
        WorkflowIntrinsicKind? intrinsicKind = null,
        RuntimeVariableReference? intrinsicVariable = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(authoredActivityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityTypeVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptorType);
        ArgumentNullException.ThrowIfNull(inputBindings);
        ArgumentNullException.ThrowIfNull(outputCaptures);
        ArgumentNullException.ThrowIfNull(metadata);

        var inputBindingSnapshot = inputBindings.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var outputCaptureSnapshot = outputCaptures.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

        if (intrinsicKind is not null && !Enum.IsDefined(intrinsicKind.Value))
            throw new ArgumentOutOfRangeException(nameof(intrinsicKind), intrinsicKind, "Workflow intrinsic kind is not defined.");
        if (intrinsicKind is not null && activityContract is not null)
            throw new ArgumentException("An executable node cannot be both a CLR activity and an engine intrinsic.", nameof(intrinsicKind));
        if (intrinsicKind is WorkflowIntrinsicKind.Set or WorkflowIntrinsicKind.Merge or WorkflowIntrinsicKind.Reduce)
        {
            if (intrinsicVariable is null)
                throw new ArgumentException($"Workflow intrinsic '{intrinsicKind}' requires a variable target.", nameof(intrinsicVariable));
            if (!inputBindingSnapshot.ContainsKey(WorkflowIntrinsicInputKeys.Value))
                throw new ArgumentException($"Workflow intrinsic '{intrinsicKind}' requires a '{WorkflowIntrinsicInputKeys.Value}' input binding.", nameof(inputBindings));
        }
        else if (intrinsicKind is WorkflowIntrinsicKind.Return &&
                 !inputBindingSnapshot.ContainsKey(WorkflowIntrinsicInputKeys.Value))
        {
            throw new ArgumentException($"Workflow intrinsic '{intrinsicKind}' requires a '{WorkflowIntrinsicInputKeys.Value}' input binding.", nameof(inputBindings));
        }
        else if (intrinsicKind is WorkflowIntrinsicKind.Control &&
                 !inputBindingSnapshot.ContainsKey(WorkflowIntrinsicInputKeys.Outcome))
        {
            throw new ArgumentException($"Workflow intrinsic '{intrinsicKind}' requires an '{WorkflowIntrinsicInputKeys.Outcome}' input binding.", nameof(inputBindings));
        }
        else if (intrinsicKind is WorkflowIntrinsicKind.SetCorrelationId or WorkflowIntrinsicKind.SetInstanceName &&
                 !inputBindingSnapshot.ContainsKey(WorkflowIntrinsicInputKeys.Value))
        {
            throw new ArgumentException($"Workflow intrinsic '{intrinsicKind}' requires a '{WorkflowIntrinsicInputKeys.Value}' input binding.", nameof(inputBindings));
        }
        else if (intrinsicKind is WorkflowIntrinsicKind.SetOutput &&
                 (!inputBindingSnapshot.ContainsKey(WorkflowIntrinsicInputKeys.Name) ||
                  !inputBindingSnapshot.ContainsKey(WorkflowIntrinsicInputKeys.Value)))
        {
            throw new ArgumentException($"Workflow intrinsic '{intrinsicKind}' requires '{WorkflowIntrinsicInputKeys.Name}' and '{WorkflowIntrinsicInputKeys.Value}' input bindings.", nameof(inputBindings));
        }
        else if (intrinsicKind is WorkflowIntrinsicKind.Finish &&
                 !inputBindingSnapshot.ContainsKey(WorkflowIntrinsicInputKeys.Outcome))
        {
            throw new ArgumentException($"Workflow intrinsic '{intrinsicKind}' requires an '{WorkflowIntrinsicInputKeys.Outcome}' input binding.", nameof(inputBindings));
        }
        else if (intrinsicVariable is not null)
        {
            throw new ArgumentException("Only variable-writing intrinsics can carry a variable target.", nameof(intrinsicVariable));
        }

        foreach (var (inputName, binding) in inputBindingSnapshot)
        {
            if (!StringComparer.Ordinal.Equals(inputName, binding.InputName))
                throw new ArgumentException($"Input binding dictionary key '{inputName}' must match binding input name '{binding.InputName}'.", nameof(inputBindings));
        }

        foreach (var (outputName, capture) in outputCaptureSnapshot)
        {
            if (!StringComparer.Ordinal.Equals(outputName, capture.OutputName))
                throw new ArgumentException($"Output capture dictionary key '{outputName}' must match capture output name '{capture.OutputName}'.", nameof(outputCaptures));
        }

        ExecutableNodeId = executableNodeId;
        AuthoredActivityId = authoredActivityId;
        ActivityType = activityType;
        ActivityTypeVersion = activityTypeVersion;
        DescriptorType = descriptorType;
        DescriptorPayload = descriptorPayload.Clone();
        InputBindings = new ReadOnlyDictionary<string, RuntimeInputBinding>(inputBindingSnapshot);
        OutputCaptures = new ReadOnlyDictionary<string, RuntimeOutputCapture>(outputCaptureSnapshot);
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
        ChildSlots = Array.AsReadOnly((childSlots ?? []).ToArray());
        Structure = structure;
        ActivityContract = activityContract;
        IntrinsicKind = intrinsicKind;
        IntrinsicVariable = intrinsicVariable;
    }

    public string ExecutableNodeId { get; }
    public string AuthoredActivityId { get; }
    public string ActivityType { get; }
    public string ActivityTypeVersion { get; }
    public string DescriptorType { get; }
    public JsonElement DescriptorPayload { get; }
    public IReadOnlyDictionary<string, RuntimeInputBinding> InputBindings { get; }
    public IReadOnlyDictionary<string, RuntimeOutputCapture> OutputCaptures { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public IReadOnlyCollection<ExecutableChildSlot> ChildSlots { get; }
    public ExecutableActivityStructure? Structure { get; }
    public ActivityContract? ActivityContract { get; }
    public WorkflowIntrinsicKind? IntrinsicKind { get; }
    public RuntimeVariableReference? IntrinsicVariable { get; }
}

public enum WorkflowIntrinsicKind
{
    Set,
    Merge,
    Reduce,
    Return,
    Control,
    SetCorrelationId,
    SetInstanceName,
    SetOutput,
    Finish
}

public static class WorkflowIntrinsicInputKeys
{
    public const string Value = "value";
    public const string Outcome = "outcome";
    public const string Name = "name";
}
