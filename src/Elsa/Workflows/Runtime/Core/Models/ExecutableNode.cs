using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Runtime-owned node inside a workflow executable.
/// </summary>
public sealed class ExecutableNode
{
    [JsonConstructor]
    public ExecutableNode(
        string executableNodeId,
        string authoredActivityId,
        string activityType,
        string activityTypeVersion,
        string descriptorType,
        JsonElement descriptorPayload,
        IReadOnlyDictionary<string, RuntimeInputBinding> inputBindings,
        IReadOnlyDictionary<string, string> metadata,
        IReadOnlyCollection<ExecutableChildSlot>? childSlots = null,
        ExecutableActivityStructure? structure = null,
        ActivityContract? activityContract = null,
        WorkflowIntrinsicKind? intrinsicKind = null,
        RuntimeVariableReference? intrinsicVariable = null,
        IReadOnlyDictionary<string, RuntimeOutputCapture>? outputCaptures = null,
        string? descriptorSchemaVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(authoredActivityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityTypeVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptorType);
        ArgumentNullException.ThrowIfNull(inputBindings);
        ArgumentNullException.ThrowIfNull(metadata);

        var inputBindingSnapshot = inputBindings.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var outputCaptureSnapshot = (outputCaptures ?? new Dictionary<string, RuntimeOutputCapture>())
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

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
        DescriptorSchemaVersion = string.IsNullOrWhiteSpace(descriptorSchemaVersion)
            ? RuntimeActivityDescriptor.InitialSchemaVersion
            : descriptorSchemaVersion;
        DescriptorPayload = descriptorPayload.Clone();
        Descriptor = new RuntimeActivityDescriptor(DescriptorType, DescriptorSchemaVersion, DescriptorPayload);
        InputBindings = new ReadOnlyDictionary<string, RuntimeInputBinding>(inputBindingSnapshot);
        OutputCaptures = new ReadOnlyDictionary<string, RuntimeOutputCapture>(outputCaptureSnapshot);
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
        ChildSlots = Array.AsReadOnly((childSlots ?? []).ToArray());
        Structure = structure;
        ActivityContract = activityContract;
        IntrinsicKind = intrinsicKind;
        IntrinsicVariable = intrinsicVariable;
    }

    public ExecutableNode(
        string executableNodeId,
        string authoredActivityId,
        string activityType,
        string activityTypeVersion,
        RuntimeActivityDescriptor descriptor,
        IReadOnlyDictionary<string, RuntimeInputBinding> inputBindings,
        IReadOnlyDictionary<string, string> metadata,
        IReadOnlyCollection<ExecutableChildSlot>? childSlots = null,
        ExecutableActivityStructure? structure = null,
        ActivityContract? activityContract = null,
        WorkflowIntrinsicKind? intrinsicKind = null,
        RuntimeVariableReference? intrinsicVariable = null)
        : this(
            executableNodeId,
            authoredActivityId,
            activityType,
            activityTypeVersion,
            descriptor.ConsumerKey,
            descriptor.Payload,
            inputBindings,
            metadata,
            childSlots,
            structure,
            activityContract,
            intrinsicKind,
            intrinsicVariable,
            outputCaptures: null,
            descriptorSchemaVersion: descriptor.SchemaVersion)
    {
    }

    public ExecutableNode(
        string executableNodeId,
        string authoredActivityId,
        string activityType,
        string activityTypeVersion,
        RuntimeActivityDescriptor descriptor,
        IReadOnlyDictionary<string, RuntimeInputBinding> inputBindings,
        IReadOnlyDictionary<string, RuntimeOutputCapture> outputCaptures,
        IReadOnlyDictionary<string, string> metadata,
        IReadOnlyCollection<ExecutableChildSlot>? childSlots = null,
        ExecutableActivityStructure? structure = null,
        ActivityContract? activityContract = null,
        WorkflowIntrinsicKind? intrinsicKind = null,
        RuntimeVariableReference? intrinsicVariable = null)
        : this(
            executableNodeId,
            authoredActivityId,
            activityType,
            activityTypeVersion,
            descriptor.ConsumerKey,
            descriptor.Payload,
            inputBindings,
            metadata,
            childSlots,
            structure,
            activityContract,
            intrinsicKind,
            intrinsicVariable,
            outputCaptures,
            descriptor.SchemaVersion)
    {
    }

    public string ExecutableNodeId { get; }
    public string AuthoredActivityId { get; }
    public string ActivityType { get; }
    public string ActivityTypeVersion { get; }
    [JsonIgnore]
    public RuntimeActivityDescriptor Descriptor { get; }
    public string DescriptorType { get; }
    public string DescriptorSchemaVersion { get; }
    public JsonElement DescriptorPayload { get; }
    public IReadOnlyDictionary<string, RuntimeInputBinding> InputBindings { get; }
    public IReadOnlyDictionary<string, RuntimeOutputCapture> OutputCaptures { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public IReadOnlyCollection<ExecutableChildSlot> ChildSlots { get; }
    public ExecutableActivityStructure? Structure { get; }
    public ActivityContract? ActivityContract { get; }
    public WorkflowIntrinsicKind? IntrinsicKind { get; }
    public RuntimeVariableReference? IntrinsicVariable { get; }

    // ADR 0047 D3: in-memory-only routing-structure memo. Private, so it is invisible to System.Text.Json —
    // it never touches the persisted executable schema or the ADR 0038 content hash (resolution #2:
    // "recomputed on materialization"). Keyed by structure type because a node reconstructs exactly one
    // routing structure kind (FlowchartGraph / BpmnGraph / SequenceNavigator).
    private ConcurrentDictionary<Type, object>? _routingStructures;

    /// <summary>
    /// ADR 0047 D3: returns a routing structure derived once from this node's immutable graph content
    /// (the connections / sequence flows / ordered children carried in <see cref="Structure"/>), computed on
    /// first access by <paramref name="factory"/> and cached on this materialized instance only.
    /// </summary>
    /// <remarks>
    /// The routing relation the runtime walks per child-completion (which successor(s) an outcome routes to) is
    /// fully determined at publish time, but the composite engines used to rebuild it — deserializing
    /// <see cref="Structure"/> and re-indexing — on every completion hop. This memo makes the reconstruction a
    /// once-per-materialized-executable cost: because the spec-111 burst cache serves the same
    /// <see cref="ExecutableNode"/> instance for every hop in a drain, the structure builds once per composite per
    /// burst and every later hop is a dictionary lookup. It is derived by the exact same code path the walk uses
    /// (<paramref name="factory"/> is the composite's own <c>From</c>), so the table cannot diverge from the walk.
    ///
    /// The memo carries no runtime state — it is a pure function of this immutable node — so it is not serialized,
    /// not part of the content hash, and correct to recompute whenever a fresh executable instance is materialized
    /// (the burst-absent path simply recomputes per hop, byte-identical, only without the reuse).
    /// </remarks>
    public T GetOrAddRoutingStructure<T>(Func<ExecutableNode, T> factory) where T : class
    {
        ArgumentNullException.ThrowIfNull(factory);

        var structures = LazyInitializer.EnsureInitialized(
            ref _routingStructures,
            static () => new ConcurrentDictionary<Type, object>());

        return (T)structures.GetOrAdd(typeof(T), _ =>
        {
            RoutingStructureMaterializationDiagnostics.OnMaterialized();
            return factory(this);
        });
    }
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
