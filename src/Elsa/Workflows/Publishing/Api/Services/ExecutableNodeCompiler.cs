using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>
/// Compiles the authored activity tree into the executable node tree and indexes its resume targets.
/// Extracted from <see cref="WorkflowExecutableCompiler"/> (W30b, #418): the compiler resolves the source and
/// assembles the artifact, while node/child-slot/structure compilation and [ResumeTarget] reflection live here.
/// Consumes the single-walk <see cref="ActivityTreeProjection"/> so children are never re-projected.
/// </summary>
public sealed class ExecutableNodeCompiler(
    IActivityStructureService activityStructureService,
    IWellKnownTypeRegistry wellKnownTypeRegistry,
    RuntimeInputBindingCompiler inputBindingCompiler)
{
    private static readonly JsonSerializerOptions DescriptorSerializerOptions = new(JsonSerializerDefaults.Web);

    public ExecutableNode CompileRoot(
        ActivityNode rootActivity,
        ActivityTreeProjection projection,
        IReadOnlyDictionary<string, ActivityDefinitionVersion> activityRows,
        IReadOnlyDictionary<string, ExecutableNode>? placedActivities = null) =>
        CompileNode(rootActivity, projection, activityRows, placedActivities ?? new Dictionary<string, ExecutableNode>());

    private ExecutableNode CompileNode(
        ActivityNode activity,
        ActivityTreeProjection projection,
        IReadOnlyDictionary<string, ActivityDefinitionVersion> activityRows,
        IReadOnlyDictionary<string, ExecutableNode> placedActivities)
    {
        if (placedActivities.TryGetValue(activity.NodeId, out var placed))
            return placed;
        var activityVersion = activityRows[activity.ActivityVersionId];

        var inputDefinitionsByReferenceKey = activityVersion.Inputs.ToDictionary(input => input.ReferenceKey, StringComparer.Ordinal);
        var inputBindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase);
        var childSlots = CompileChildSlots(projection.ChildProjections(activity), projection, activityRows, placedActivities);

        foreach (var inputState in activity.Inputs)
        {
            if (!inputDefinitionsByReferenceKey.TryGetValue(inputState.ReferenceKey, out var inputDefinition))
                throw new ArgumentException($"Activity node '{activity.NodeId}' input '{inputState.ReferenceKey}' does not match any input definition on activity version '{activity.ActivityVersionId}'.");

            inputBindings[inputDefinition.Name] = inputBindingCompiler.Compile(activity.NodeId, inputDefinition, inputState.Value);
        }

        var catalogActivityType = activityVersion.Definition?.ActivityTypeKey
            ?? throw new ArgumentException($"Activity version '{activity.ActivityVersionId}' did not include its activity definition.");
        var descriptor = CompileRuntimeDescriptor(activityVersion.DescriptorType, activityVersion.DescriptorPayload);
        var clrActivityType = ResolveClrActivityType(descriptor);
        var activityType = clrActivityType is null
            ? catalogActivityType
            : ActivityTypeMetadata.GetDeclaredActivityType(clrActivityType) ?? catalogActivityType;
        var executionType = clrActivityType is not null && ActivityTypeMetadata.IsTrigger(clrActivityType)
            ? TriggerNodeMetadata.TriggerExecutionType
            : activityVersion.ExecutionType.ToString();

        return new ExecutableNode(
            executableNodeId: activity.NodeId,
            authoredActivityId: activity.NodeId,
            activityType: activityType,
            activityTypeVersion: activityVersion.Version,
            descriptor: descriptor,
            inputBindings: inputBindings,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>
            {
                ["authoredNodeId"] = activity.NodeId,
                [TriggerNodeMetadata.ExecutionTypeKey] = executionType
            },
            childSlots: childSlots,
            structure: CompileStructure(activityStructureService.CompileExecutableStructure(activity)));
    }

    private IReadOnlyCollection<ExecutableChildSlot> CompileChildSlots(
        IEnumerable<ActivityChildProjection> childSlots,
        ActivityTreeProjection projection,
        IReadOnlyDictionary<string, ActivityDefinitionVersion> activityRows,
        IReadOnlyDictionary<string, ExecutableNode> placedActivities)
    {
        return childSlots
            .Select(slot => new ExecutableChildSlot(
                slot.Name,
                slot.Activities.Select(activity => CompileNode(activity, projection, activityRows, placedActivities)).ToArray()))
            .ToArray();
    }

    private static ExecutableActivityStructure? CompileStructure(ActivityNodeStructure? structure) =>
        structure is null
            ? null
            : new ExecutableActivityStructure(structure.Kind, structure.SchemaVersion, structure.Payload);

    public IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> BuildResumeTargets(
        ExecutableNode root,
        IReadOnlySet<string>? precompiledNodeIds = null)
    {
        // Index [ResumeTarget] handlers declared by each node's activity CLR type into the executable's
        // resume-target map. Suspending activities (e.g. Delay) create a durable bookmark against a resume
        // target id; the CreateBookmark handler validates that id against this map, and the resume handler
        // reflects the matching method back at resume time. Activities without resume targets (all existing
        // activities) contribute nothing, so the map stays empty for them.
        var resumeTargets = new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal);

        foreach (var node in FlattenExecutableNodes(root))
        {
            if (precompiledNodeIds?.Contains(node.ExecutableNodeId) == true)
                continue;
            var activityType = ResolveClrActivityType(node.Descriptor);
            if (activityType is null &&
                wellKnownTypeRegistry.TryGetTypeOrDefault(node.ActivityType, out var registeredActivityType) &&
                registeredActivityType != typeof(object))
                activityType = registeredActivityType;
            if (activityType is null)
                continue;

            foreach (var method in activityType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var attribute = method.GetCustomAttribute<ResumeTargetAttribute>();
                if (attribute is null)
                    continue;

                ValidateResumeTargetSignature(activityType, method);

                var resumeTargetId = attribute.ResumeTargetId;
                if (resumeTargets.TryGetValue(resumeTargetId, out var existing))
                    throw new ArgumentException(
                        $"Resume target '{resumeTargetId}' is declared by executable nodes '{existing.ExecutableNodeId}' and '{node.ExecutableNodeId}'. A resume target id must be unique within a workflow executable; multiple instances of the same resume-target activity in one workflow are not yet supported.");

                resumeTargets[resumeTargetId] = new WorkflowExecutableResumeTarget(
                    ResumeTargetId: resumeTargetId,
                    ExecutableNodeId: node.ExecutableNodeId,
                    HandlerKey: method.Name,
                    Metadata: new Dictionary<string, string>(StringComparer.Ordinal));
            }
        }

        return resumeTargets;
    }

    private static RuntimeActivityDescriptor CompileRuntimeDescriptor(string descriptorType, JsonElement descriptorPayload)
    {
        var consumerKey = descriptorType switch
        {
            var value when StringComparer.Ordinal.Equals(value, typeof(ClrActivityDescriptor).FullName) => WellKnownRuntimeActivityConsumers.ClrActivity,
            "Elsa.Workflows.Primitives.Models.WorkflowIdentity" => WellKnownRuntimeActivityConsumers.WorkflowDefinitionActivity,
            _ => descriptorType
        };

        return new RuntimeActivityDescriptor(
            consumerKey,
            RuntimeActivityDescriptor.InitialSchemaVersion,
            descriptorPayload);
    }

    private Type? ResolveClrActivityType(RuntimeActivityDescriptor descriptor)
    {
        if (!StringComparer.Ordinal.Equals(descriptor.ConsumerKey, WellKnownRuntimeActivityConsumers.ClrActivity))
            return null;

        var clrDescriptor = descriptor.Payload.Deserialize<ClrActivityDescriptor>(DescriptorSerializerOptions);
        return clrDescriptor is not null &&
               wellKnownTypeRegistry.TryGetTypeOrDefault(clrDescriptor.TypeAlias, out var activityType) &&
               activityType != typeof(object)
            ? activityType
            : null;
    }

    private static void ValidateResumeTargetSignature(Type activityType, MethodInfo method)
    {
        var parameters = method.GetParameters();
        var hasSupportedParameter =
            parameters.Length == 0 ||
            parameters.Length == 1 && (parameters[0].ParameterType == typeof(IActivityExecutionContext) || parameters[0].ParameterType == typeof(JsonElement));
        var hasSupportedReturn =
            method.ReturnType == typeof(void) ||
            method.ReturnType == typeof(Task) ||
            method.ReturnType == typeof(ValueTask);

        if (!hasSupportedParameter || !hasSupportedReturn)
            throw new ArgumentException(
                $"Resume target method '{activityType.FullName}.{method.Name}' has an unsupported signature. A resume target must take no parameters or a single {nameof(IActivityExecutionContext)}/{nameof(JsonElement)} parameter and return void, Task, or ValueTask.");
    }

    private static IEnumerable<ExecutableNode> FlattenExecutableNodes(ExecutableNode root)
    {
        var stack = new Stack<ExecutableNode>();
        stack.Push(root);
        while (stack.TryPop(out var node))
        {
            yield return node;
            foreach (var child in node.ChildSlots.SelectMany(x => x.Activities).Reverse())
                stack.Push(child);
        }
    }
}
