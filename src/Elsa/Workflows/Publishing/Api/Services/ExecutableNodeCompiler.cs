using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
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
        IReadOnlyDictionary<string, ActivityDefinitionVersion> activityRows) =>
        CompileNode(rootActivity, projection, activityRows);

    private ExecutableNode CompileNode(
        ActivityNode activity,
        ActivityTreeProjection projection,
        IReadOnlyDictionary<string, ActivityDefinitionVersion> activityRows)
    {
        if (activity.Intrinsic is not null)
            return CompileIntrinsicNode(activity, projection, activityRows);

        var activityVersion = activityRows[activity.ActivityVersionId];

        var inputDefinitionsByReferenceKey = activityVersion.Inputs.ToDictionary(input => input.ReferenceKey, StringComparer.Ordinal);
        var inputBindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase);
        var childSlots = CompileChildSlots(projection.ChildProjections(activity), projection, activityRows);

        foreach (var inputState in activity.Inputs)
        {
            if (!inputDefinitionsByReferenceKey.TryGetValue(inputState.ReferenceKey, out var inputDefinition))
                throw new ArgumentException($"Activity node '{activity.NodeId}' input '{inputState.ReferenceKey}' does not match any input definition on activity version '{activity.ActivityVersionId}'.");

            var binding = inputBindingCompiler.Compile(activity.NodeId, inputDefinition, inputState.Value);
            inputBindings[binding.InputName] = binding;
        }

        var catalogActivityType = activityVersion.Definition?.ActivityTypeKey
            ?? throw new ArgumentException($"Activity version '{activity.ActivityVersionId}' did not include its activity definition.");
        var clrActivityType = ResolveClrActivityType(activityVersion.DescriptorType, activityVersion.DescriptorPayload);
        var activityType = clrActivityType is null
            ? catalogActivityType
            : ActivityTypeMetadata.GetDeclaredActivityType(clrActivityType) ?? catalogActivityType;
        var executionType = clrActivityType is not null && ActivityTypeMetadata.IsTrigger(clrActivityType)
            ? TriggerNodeMetadata.TriggerExecutionType
            : activityVersion.ExecutionType.ToString();
        var activityContract = clrActivityType is null ? null : BuildActivityContract(activityVersion, clrActivityType, activityType);
        var outputCaptures = activityContract?.Result.Projections.Values.ToDictionary(
            projection => projection.Key,
            projection => new RuntimeOutputCapture(
                projection.Key,
                $"{activity.NodeId}:result:{projection.Key}",
                new RuntimeValueTypeDescriptor("alias", projection.Type.Alias, projection.Type.Schema),
                DurableValueLifecycle.Instance,
                DurableValueStorage.Inline,
                captureOnSuccessfulCompletion: true),
            StringComparer.Ordinal) ?? new Dictionary<string, RuntimeOutputCapture>(StringComparer.Ordinal);

        return new ExecutableNode(
            executableNodeId: activity.NodeId,
            authoredActivityId: activity.NodeId,
            activityType: activityType,
            activityTypeVersion: activityVersion.Version,
            descriptorType: activityVersion.DescriptorType,
            descriptorPayload: activityVersion.DescriptorPayload,
            inputBindings: inputBindings,
            outputCaptures: outputCaptures,
            metadata: new Dictionary<string, string>
            {
                ["authoredNodeId"] = activity.NodeId,
                [TriggerNodeMetadata.ExecutionTypeKey] = executionType
            },
            childSlots: childSlots,
            structure: CompileStructure(activityStructureService.CompileExecutableStructure(activity)),
            activityContract: activityContract);
    }

    private ExecutableNode CompileIntrinsicNode(
        ActivityNode activity,
        ActivityTreeProjection projection,
        IReadOnlyDictionary<string, ActivityDefinitionVersion> activityRows)
    {
        var intrinsic = activity.Intrinsic!;
        var runtimeKind = intrinsic.Kind switch
        {
            AuthoredWorkflowIntrinsicKind.Set => WorkflowIntrinsicKind.Set,
            AuthoredWorkflowIntrinsicKind.Merge => WorkflowIntrinsicKind.Merge,
            AuthoredWorkflowIntrinsicKind.Reduce => WorkflowIntrinsicKind.Reduce,
            AuthoredWorkflowIntrinsicKind.Return => WorkflowIntrinsicKind.Return,
            AuthoredWorkflowIntrinsicKind.Control => WorkflowIntrinsicKind.Control,
            AuthoredWorkflowIntrinsicKind.SetCorrelationId => WorkflowIntrinsicKind.SetCorrelationId,
            AuthoredWorkflowIntrinsicKind.SetInstanceName => WorkflowIntrinsicKind.SetInstanceName,
            AuthoredWorkflowIntrinsicKind.SetOutput => WorkflowIntrinsicKind.SetOutput,
            AuthoredWorkflowIntrinsicKind.Finish => WorkflowIntrinsicKind.Finish,
            _ => throw new ArgumentOutOfRangeException(nameof(intrinsic.Kind), intrinsic.Kind, "Authored workflow intrinsic kind is not defined.")
        };
        var stringType = new TypeReference("String");
        var inputTypes = runtimeKind switch
        {
            WorkflowIntrinsicKind.Control or WorkflowIntrinsicKind.Finish =>
                new Dictionary<string, TypeReference>(StringComparer.Ordinal) { [WorkflowIntrinsicInputKeys.Outcome] = stringType },
            WorkflowIntrinsicKind.SetOutput =>
                new Dictionary<string, TypeReference>(StringComparer.Ordinal)
                {
                    [WorkflowIntrinsicInputKeys.Name] = stringType,
                    [WorkflowIntrinsicInputKeys.Value] = intrinsic.ValueType!
                },
            _ => new Dictionary<string, TypeReference>(StringComparer.Ordinal) { [WorkflowIntrinsicInputKeys.Value] = intrinsic.ValueType! }
        };
        var authoredInputs = activity.Inputs.ToDictionary(input => input.ReferenceKey, StringComparer.Ordinal);
        if (authoredInputs.Count != inputTypes.Count || inputTypes.Keys.Any(key => !authoredInputs.ContainsKey(key)))
        {
            throw new ArgumentException(
                $"Workflow intrinsic node '{activity.NodeId}' requires exactly these inputs: {string.Join(", ", inputTypes.Keys.Order(StringComparer.Ordinal))}.");
        }

        var bindings = inputTypes.ToDictionary(
            input => input.Key,
            input => inputBindingCompiler.Compile(
                activity.NodeId,
                new InputDefinition(
                    input.Key,
                    input.Key,
                    input.Value,
                    StorageDriverType: null,
                    DisplayName: input.Key,
                    Category: null,
                    IsRequired: true),
                authoredInputs[input.Key].Value),
            StringComparer.Ordinal);
        foreach (var literalKey in LiteralIntrinsicKeys(runtimeKind))
        {
            var binding = bindings[literalKey];
            if (binding.Source != RuntimeInputBindingSource.Literal ||
                binding.Literal is not { Presence: ValuePresence.Present, InlineValue.ValueKind: JsonValueKind.String } literal ||
                string.IsNullOrWhiteSpace(literal.InlineValue.Value.GetString()))
            {
                throw new ArgumentException($"Workflow {runtimeKind} intrinsic node '{activity.NodeId}' requires a non-blank literal '{literalKey}'.");
            }
        }
        var variable = intrinsic.Variable is null
            ? null
            : new RuntimeVariableReference(
                intrinsic.Variable.ReferenceKey,
                intrinsic.Variable.DeclaringScopeId ?? VariableReference.WorkflowScopeId);
        var activityType = $"elsa.intrinsic.{intrinsic.Kind.ToString().ToLowerInvariant()}";
        var descriptorPayload = JsonSerializer.SerializeToElement(new
        {
            kind = intrinsic.Kind.ToString(),
            schemaVersion = "1.0.0"
        });

        return new ExecutableNode(
            executableNodeId: activity.NodeId,
            authoredActivityId: activity.NodeId,
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptorType: "intrinsic",
            descriptorPayload: descriptorPayload,
            inputBindings: bindings,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(StringComparer.Ordinal),
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["authoredNodeId"] = activity.NodeId,
                [TriggerNodeMetadata.ExecutionTypeKey] = ActivityExecutionType.Action.ToString()
            },
            childSlots: CompileChildSlots(projection.ChildProjections(activity), projection, activityRows),
            structure: CompileStructure(activityStructureService.CompileExecutableStructure(activity)),
            activityContract: null,
            intrinsicKind: runtimeKind,
            intrinsicVariable: variable);
    }

    private static IReadOnlyCollection<string> LiteralIntrinsicKeys(WorkflowIntrinsicKind kind) => kind switch
    {
        WorkflowIntrinsicKind.Control or WorkflowIntrinsicKind.Finish => [WorkflowIntrinsicInputKeys.Outcome],
        WorkflowIntrinsicKind.SetOutput => [WorkflowIntrinsicInputKeys.Name],
        _ => []
    };

    private static ActivityContract? BuildActivityContract(ActivityDefinitionVersion activityVersion, Type activityType, string activityTypeKey)
    {
        var resultType = FindTypedActivityResult(activityType);
        if (resultType is null)
            return null;

        var inputs = activityVersion.Inputs.Select(input => new ActivityInputContract(
            input.ReferenceKey,
            input.Name,
            new ValueTypeDescriptor(input.Type.Alias, input.Type.CollectionKind),
            input.IsRequired,
            input.DefaultValue.HasValue,
            input.DefaultValue,
            ActivityValuePolicy.Default));
        var projections = resultType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (Property: property, Attribute: property.GetCustomAttribute<OutputAttribute>(inherit: true)))
            .Where(candidate => candidate.Attribute is not null)
            .Select(candidate =>
            {
                var attribute = candidate.Attribute!;
                var type = TypeReferenceFactory.FromClrType(candidate.Property.PropertyType, TypeAliasConvention.CanonicalAlias);
                return new ActivityResultProjectionContract(
                    attribute.Key ?? candidate.Property.Name,
                    attribute.Path ?? JsonNamingPolicy.CamelCase.ConvertName(candidate.Property.Name),
                    new ValueTypeDescriptor(type.Alias, type.CollectionKind),
                    attribute.IsRequired,
                    ActivityValuePolicy.Default);
            });
        var resultReference = TypeReferenceFactory.FromClrType(resultType, TypeAliasConvention.CanonicalAlias);
        var outcomes = activityType.GetCustomAttributes<ActivityOutcomeAttribute>(inherit: true)
            .Select(attribute => attribute.Key)
            .DefaultIfEmpty("Done");

        return new ActivityContract(
            activityTypeKey,
            activityVersion.Version,
            activityVersion.DescriptorType,
            activityVersion.DescriptorPayload,
            inputs,
            new ActivityResultContract(
                new ValueTypeDescriptor(resultReference.Alias, resultReference.CollectionKind),
                isRequired: true,
                ActivityValuePolicy.Default,
                projections),
            outcomes,
            new ActivityActivationRequirement(activityVersion.DescriptorType, TypeAliasConvention.CanonicalAlias(activityType)));
    }

    private static Type? FindTypedActivityResult(Type activityType)
    {
        for (var current = activityType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Activity<>))
                return current.GetGenericArguments()[0];
        }

        return null;
    }

    private IReadOnlyCollection<ExecutableChildSlot> CompileChildSlots(
        IEnumerable<ActivityChildProjection> childSlots,
        ActivityTreeProjection projection,
        IReadOnlyDictionary<string, ActivityDefinitionVersion> activityRows)
    {
        return childSlots
            .Select(slot => new ExecutableChildSlot(
                slot.Name,
                slot.Activities.Select(activity => CompileNode(activity, projection, activityRows)).ToArray()))
            .ToArray();
    }

    private static ExecutableActivityStructure? CompileStructure(ActivityNodeStructure? structure) =>
        structure is null
            ? null
            : new ExecutableActivityStructure(structure.Kind, structure.SchemaVersion, structure.Payload);

    public IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> BuildResumeTargets(ExecutableNode root)
    {
        // Index [ResumeTarget] handlers declared by each node's activity CLR type into the executable's
        // resume-target map. Suspending activities (e.g. Delay) create a durable bookmark against a resume
        // target id; the CreateBookmark handler validates that id against this map, and the resume handler
        // reflects the matching method back at resume time. Activities without resume targets (all existing
        // activities) contribute nothing, so the map stays empty for them.
        var resumeTargets = new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal);

        foreach (var node in FlattenExecutableNodes(root))
        {
            var activityType = ResolveClrActivityType(node.DescriptorType, node.DescriptorPayload);
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

    private Type? ResolveClrActivityType(string descriptorType, JsonElement descriptorPayload)
    {
        if (!StringComparer.Ordinal.Equals(descriptorType, typeof(ClrActivityDescriptor).FullName))
            return null;

        var descriptor = descriptorPayload.Deserialize<ClrActivityDescriptor>(DescriptorSerializerOptions);
        return descriptor is not null &&
               wellKnownTypeRegistry.TryGetTypeOrDefault(descriptor.TypeAlias, out var activityType) &&
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
        yield return root;

        foreach (var slot in root.ChildSlots)
            foreach (var child in slot.Activities)
                foreach (var descendant in FlattenExecutableNodes(child))
                    yield return descendant;
    }
}
