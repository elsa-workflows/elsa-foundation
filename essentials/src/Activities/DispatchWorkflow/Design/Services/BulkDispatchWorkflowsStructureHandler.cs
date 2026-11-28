using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Design.Models;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Design.Services;

/// <summary>
/// Design-side handler that projects and compiles the <c>BulkDispatchWorkflows</c> slots (spec 1242 plan §1). The
/// runtime activity references no design types.
/// </summary>
internal sealed class BulkDispatchWorkflowsStructureHandler : IActivityStructureHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(Elsa.Serialization.Core.JsonBaseline.Options)
    {
        // Authored ArgumentState.Conversion enums arrive as camelCase strings from the global web JSON options.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Kind => BulkDispatchWorkflowsConstants.StructureKind;

    public string SchemaVersion => BulkDispatchWorkflowsConstants.StructureSchemaVersion;

    public Type AuthoredPayloadType => typeof(BulkDispatchWorkflowsAuthoredStructure);

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
    {
        var structure = ReadAuthoredStructure(activity);
        return
        [
            new ActivityChildProjection(BulkDispatchWorkflowsConstants.DispatchSlotName, ToSlot(structure.Dispatch)),
            new ActivityChildProjection(BulkDispatchWorkflowsConstants.ChildCompletedSlotName, ToSlot(structure.ChildCompleted)),
            new ActivityChildProjection(BulkDispatchWorkflowsConstants.ChildFaultedSlotName, ToSlot(structure.ChildFaulted))
        ];
    }

    // Passes run concurrently, and the runtime hides other iterations' results, so a result binding between two different
    // children cannot tell which item's run it reads: availability stays unknown, the runs may overlap, and an earlier
    // run of the producer can lead to the consumer (spec 1242 FR-010). A child paired with itself is scheduled per pass by
    // this activity, so one pass never leads to a later one, and bindings inside one slot keep their own rules.
    public ActivityChildResultFacts AssessChildResult(ActivityNode activity, string producerChildNodeId, string consumerChildNodeId) =>
        StringComparer.Ordinal.Equals(producerChildNodeId, consumerChildNodeId)
            ? new(ActivityChildResultAvailability.Unknown) { EarlierProducerRunCanReachConsumer = false }
            : new(ActivityChildResultAvailability.Unknown, MayRunConcurrently: true) { EarlierProducerRunCanReachConsumer = true };

    // Dispatch and both slots run concurrently across passes, each also with itself (spec 1242 FR-010a).
    public ActivityChildTimeRelation RelateChildren(ActivityNode activity, string firstChildNodeId, string secondChildNodeId) =>
        ActivityChildTimeRelation.Unordered;

    public bool? CompletesAfterItsChildren(ActivityNode activity) => true;

    /// <summary>The iteration values the Dispatch node and the slots read: <c>currentItem</c> and <c>currentIndex</c>.</summary>
    public IReadOnlyCollection<VariableDefinition> ProjectIterationVariables(ActivityNode activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        return
        [
            new VariableDefinition(BulkDispatchWorkflowsConstants.CurrentItemVariableName, BulkDispatchWorkflowsConstants.CurrentItemVariableName,
                new TypeReference(TypeAliasConvention.AnyAlias), null, null),
            new VariableDefinition(BulkDispatchWorkflowsConstants.CurrentIndexVariableName, BulkDispatchWorkflowsConstants.CurrentIndexVariableName,
                new TypeReference(TypeAliasConvention.AnyAlias), null, null)
        ];
    }

    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections)
    {
        var structure = new BulkDispatchWorkflowsAuthoredStructure(
            Single(childProjections, BulkDispatchWorkflowsConstants.DispatchSlotName),
            Single(childProjections, BulkDispatchWorkflowsConstants.ChildCompletedSlotName),
            Single(childProjections, BulkDispatchWorkflowsConstants.ChildFaultedSlotName));
        return activity with { Structure = ToStructure(structure) };
    }

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity)
    {
        var authored = ReadAuthoredStructure(activity);
        return ToStructure(new BulkDispatchWorkflowsExecutableStructure(
            authored.Dispatch?.NodeId,
            authored.ChildCompleted?.NodeId,
            authored.ChildFaulted?.NodeId));
    }

    public ActivityNodeStructure RemapExecutableStructure(
        ActivityNodeStructure structure,
        IReadOnlyDictionary<string, string> authoredToExecutableNodeIds)
    {
        var executable = structure.Payload.Deserialize<BulkDispatchWorkflowsExecutableStructure>(SerializerOptions)
                         ?? throw new InvalidOperationException("BulkDispatchWorkflows executable structure payload is invalid.");
        return ToStructure(new BulkDispatchWorkflowsExecutableStructure(
            Remap(executable.Dispatch, authoredToExecutableNodeIds),
            Remap(executable.ChildCompleted, authoredToExecutableNodeIds),
            Remap(executable.ChildFaulted, authoredToExecutableNodeIds)));
    }

    private static IEnumerable<ActivityNode> ToSlot(ActivityNode? node) => node is null ? [] : [node];

    private static ActivityNode? Single(IReadOnlyCollection<ActivityChildProjection> childProjections, string slotName) =>
        childProjections.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, slotName))?.Activities.FirstOrDefault();

    private static ActivityNodeStructure ToStructure<TPayload>(TPayload payload) =>
        new(BulkDispatchWorkflowsConstants.StructureKind, BulkDispatchWorkflowsConstants.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(payload, SerializerOptions));

    private static BulkDispatchWorkflowsAuthoredStructure ReadAuthoredStructure(ActivityNode activity) =>
        activity.Structure?.Payload.Deserialize<BulkDispatchWorkflowsAuthoredStructure>(SerializerOptions)
        ?? new BulkDispatchWorkflowsAuthoredStructure();

    private static string? Remap(string? nodeId, IReadOnlyDictionary<string, string> nodeIds) =>
        nodeId is not null && nodeIds.TryGetValue(nodeId, out var executableNodeId) ? executableNodeId : nodeId;
}
