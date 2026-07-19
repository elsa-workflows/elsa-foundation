using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        IReadOnlyDictionary<string, ActivityDefinitionVersion> activityRows,
        IReadOnlyDictionary<string, ExecutableNode>? placedActivities = null) =>
        CompileNode(rootActivity, projection, activityRows, placedActivities ?? new Dictionary<string, ExecutableNode>());

    /// <summary>
    /// Compiles the unbound root of a source-owned activity template through the same descriptor and
    /// typed-contract path used for ordinary authored workflow nodes. Placement supplies the concrete
    /// boundary bindings later.
    /// </summary>
    public ExecutableNode CompileSourceOwnedRoot(ActivityDefinitionVersion activityVersion)
    {
        ArgumentNullException.ThrowIfNull(activityVersion);

        var descriptor = new RuntimeActivityDescriptor(
            activityVersion.ConsumerKey,
            activityVersion.ConsumerSchemaVersion,
            activityVersion.DescriptorPayload);
        var clrActivityType = ResolveClrActivityType(descriptor);
        var inputDefinitions = NormalizeLegacyClrInputNullability(activityVersion.Inputs, clrActivityType);
        var catalogActivityType = activityVersion.Definition?.ActivityTypeKey
            ?? throw new ArgumentException($"Activity version '{activityVersion.Id}' did not include its activity definition.");
        var activityType = clrActivityType is null
            ? catalogActivityType
            : ActivityTypeMetadata.GetDeclaredActivityType(clrActivityType) ?? catalogActivityType;
        var activity = new ActivityNode("root", activityVersion.Id, [], []);
        var activityContract = clrActivityType is null
            ? null
            : BuildActivityContract(
                activity,
                activityVersion,
                inputDefinitions,
                clrActivityType,
                activityType,
                structure: null,
                contractVersion: activityVersion.ConsumerSchemaVersion);

        return new ExecutableNode(
            executableNodeId: activity.NodeId,
            authoredActivityId: activity.NodeId,
            activityType: activityType,
            activityTypeVersion: activityVersion.ConsumerSchemaVersion,
            descriptor: descriptor,
            inputBindings: new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(StringComparer.Ordinal),
            metadata: new Dictionary<string, string>(StringComparer.Ordinal),
            activityContract: activityContract);
    }

    private ExecutableNode CompileNode(
        ActivityNode activity,
        ActivityTreeProjection projection,
        IReadOnlyDictionary<string, ActivityDefinitionVersion> activityRows,
        IReadOnlyDictionary<string, ExecutableNode> placedActivities)
    {
        if (placedActivities.TryGetValue(activity.NodeId, out var placed))
            return placed;

        if (activity.Intrinsic is not null)
            return CompileIntrinsicNode(activity, projection, activityRows, placedActivities);

        var activityVersion = activityRows[activity.ActivityVersionId];
        var descriptor = new RuntimeActivityDescriptor(
            activityVersion.ConsumerKey,
            activityVersion.ConsumerSchemaVersion,
            activityVersion.DescriptorPayload);
        var clrActivityType = ResolveClrActivityType(descriptor);
        var inputDefinitions = NormalizeLegacyClrInputNullability(activityVersion.Inputs, clrActivityType);

        var inputDefinitionsByReferenceKey = inputDefinitions.ToDictionary(input => input.ReferenceKey, StringComparer.Ordinal);
        var inputBindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal);
        var childSlots = CompileChildSlots(projection.ChildProjections(activity), projection, activityRows, placedActivities);

        foreach (var inputState in activity.Inputs)
        {
            if (!inputDefinitionsByReferenceKey.TryGetValue(inputState.ReferenceKey, out var inputDefinition))
                throw new ArgumentException($"Activity node '{activity.NodeId}' input '{inputState.ReferenceKey}' does not match any input definition on activity version '{activity.ActivityVersionId}'.");

            var binding = inputBindingCompiler.Compile(activity.NodeId, inputDefinition, inputState);
            if (!inputBindings.TryAdd(binding.InputName, binding))
            {
                throw new ArgumentException(
                    $"VF-ACT-003: Activity node '{activity.NodeId}' declares duplicate input '{inputState.ReferenceKey}'. " +
                    "Every published input must lower to exactly one canonical binding.");
            }
        }

        foreach (var inputDefinition in inputDefinitions.Where(input => !inputBindings.ContainsKey(input.ReferenceKey)))
        {
            if (inputDefinition.DefaultValue.HasValue)
            {
                var binding = inputBindingCompiler.Compile(
                    activity.NodeId,
                    inputDefinition,
                    new ArgumentValue(null, "Default"));
                inputBindings.Add(binding.InputName, binding);
                continue;
            }

            if (!inputDefinition.IsRequired)
            {
                var binding = inputBindingCompiler.CompileOmitted(inputDefinition);
                inputBindings.Add(binding.InputName, binding);
                continue;
            }

            throw new ArgumentException(
                $"VF-ACT-003: Activity node '{activity.NodeId}' omits required input '{inputDefinition.ReferenceKey}', which has no pinned default. " +
                "Every published input must lower to exactly one canonical binding.");
        }

        var catalogActivityType = activityVersion.Definition?.ActivityTypeKey
            ?? throw new ArgumentException($"Activity version '{activity.ActivityVersionId}' did not include its activity definition.");
        var activityType = clrActivityType is null
            ? catalogActivityType
            : ActivityTypeMetadata.GetDeclaredActivityType(clrActivityType) ?? catalogActivityType;
        var executionType = clrActivityType is not null && ActivityTypeMetadata.IsTrigger(clrActivityType)
            ? TriggerNodeMetadata.TriggerExecutionType
            : activityVersion.ExecutionType.ToString();
        var structure = CompileStructure(activity.NodeId, activityStructureService.CompileExecutableStructure(activity));
        var activityContract = clrActivityType is null
            ? null
            : BuildActivityContract(activity, activityVersion, inputDefinitions, clrActivityType, activityType, structure);

        return new ExecutableNode(
            executableNodeId: activity.NodeId,
            authoredActivityId: activity.NodeId,
            activityType: activityType,
            activityTypeVersion: activityVersion.Version,
            descriptor: descriptor,
            inputBindings: inputBindings,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(StringComparer.Ordinal),
            metadata: new Dictionary<string, string>
            {
                ["authoredNodeId"] = activity.NodeId,
                [TriggerNodeMetadata.ExecutionTypeKey] = executionType
            },
            childSlots: childSlots,
            structure: structure,
            activityContract: activityContract);
    }

    private ExecutableNode CompileIntrinsicNode(
        ActivityNode activity,
        ActivityTreeProjection projection,
        IReadOnlyDictionary<string, ActivityDefinitionVersion> activityRows,
        IReadOnlyDictionary<string, ExecutableNode> placedActivities)
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
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["authoredNodeId"] = activity.NodeId,
                [TriggerNodeMetadata.ExecutionTypeKey] = ActivityExecutionType.Action.ToString()
            },
            childSlots: CompileChildSlots(projection.ChildProjections(activity), projection, activityRows, placedActivities),
            structure: CompileStructure(activity.NodeId, activityStructureService.CompileExecutableStructure(activity)),
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

    private static Elsa.Activities.Runtime.Core.Models.ActivityContract? BuildActivityContract(
        ActivityNode activity,
        ActivityDefinitionVersion activityVersion,
        IReadOnlyCollection<InputDefinition> inputDefinitions,
        Type activityType,
        string activityTypeKey,
        ExecutableActivityStructure? structure,
        string? contractVersion = null)
    {
        var resultType = FindTypedActivityResult(activityType);
        if (resultType is null)
            return null;

        var inputStates = activity.Inputs.ToDictionary(input => input.ReferenceKey, StringComparer.Ordinal);
        var inputs = inputDefinitions.Select(input =>
        {
            inputStates.TryGetValue(input.ReferenceKey, out var state);
            return new Elsa.Activities.Runtime.Core.Models.ActivityInputContract(
                input.ReferenceKey,
                input.Name,
                new ValueTypeDescriptor(input.Type.Alias, input.Type.CollectionKind),
                input.IsRequired,
                input.DefaultValue.HasValue,
                input.DefaultValue,
                CompileActivityPolicy(input.StorageDriverType, state, $"Input '{input.ReferenceKey}' on activity node '{activity.NodeId}'"))
            {
                IsNullable = input.IsNullable
            };
        });
        var outputDefinitions = activityVersion.Outputs.ToDictionary(output => output.ReferenceKey, StringComparer.Ordinal);
        var outputStates = activity.Outputs.ToDictionary(output => output.ReferenceKey, StringComparer.Ordinal);
        var projections = resultType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (Property: property, Attribute: property.GetCustomAttribute<OutputAttribute>(inherit: true)))
            .Where(candidate => candidate.Attribute is not null)
            .Select(candidate =>
            {
                var attribute = candidate.Attribute!;
                var type = TypeReferenceFactory.FromClrType(candidate.Property.PropertyType, TypeAliasConvention.CanonicalAlias);
                var key = attribute.Key ?? candidate.Property.Name;
                outputDefinitions.TryGetValue(key, out var outputDefinition);
                outputStates.TryGetValue(key, out var outputState);
                return new ActivityResultProjectionContract(
                    key,
                    attribute.Path ?? JsonNamingPolicy.CamelCase.ConvertName(candidate.Property.Name),
                    new ValueTypeDescriptor(type.Alias, type.CollectionKind),
                    attribute.IsRequired,
                    CompileActivityPolicy(outputDefinition?.StorageDriverType, outputState, $"Result projection '{key}' on activity node '{activity.NodeId}'"),
                    ResolveSourceRepresentation(attribute, outputDefinition, new ValueTypeDescriptor(type.Alias, type.CollectionKind)));
            })
            .ToArray();
        var resultReference = TypeReferenceFactory.FromClrType(resultType, TypeAliasConvention.CanonicalAlias);
        var outcomes = ResolveOutcomes(activityType, structure);
        var resultPolicy = projections.Aggregate(
            ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result },
            (policy, projection) => ValuePolicyCombiner.Combine(
                policy,
                projection.Policy,
                $"Atomic result on activity node '{activity.NodeId}'"));

        return new Elsa.Activities.Runtime.Core.Models.ActivityContract(
            activityTypeKey,
            contractVersion ?? activityVersion.Version,
            typeof(ClrActivityDescriptor).FullName!,
            activityVersion.DescriptorPayload,
            inputs,
            new ActivityResultContract(
                new ValueTypeDescriptor(resultReference.Alias, resultReference.CollectionKind),
                isRequired: true,
                resultPolicy,
                projections,
                ValueRepresentationDefaults.Infer(new ValueTypeDescriptor(resultReference.Alias, resultReference.CollectionKind))),
            outcomes,
            new ActivityActivationRequirement(typeof(ClrActivityDescriptor).FullName!, TypeAliasConvention.CanonicalAlias(activityType)));
    }

    private static ValueRepresentation ResolveSourceRepresentation(
        OutputAttribute attribute,
        OutputDefinition? outputDefinition,
        ValueTypeDescriptor sourceType) =>
        outputDefinition?.SourceRepresentation ??
        (attribute.HasSourceRepresentation ? attribute.SourceRepresentation : ValueRepresentationDefaults.Infer(sourceType));

    private static IReadOnlyCollection<InputDefinition> NormalizeLegacyClrInputNullability(
        IEnumerable<InputDefinition> inputDefinitions,
        Type? clrActivityType)
    {
        var inputs = inputDefinitions as IReadOnlyCollection<InputDefinition> ?? inputDefinitions.ToArray();
        if (clrActivityType is null || inputs.All(input => input.IsNullable.HasValue))
            return inputs;

        var propertiesByKey = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
        foreach (var property in clrActivityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attribute = FindActivityInputAttribute(property);
            if (attribute is null)
                continue;

            var key = attribute.Key ?? property.Name;
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            if (!propertiesByKey.TryAdd(key, property))
                throw new ArgumentException($"Activity '{clrActivityType.FullName}' declares duplicate input key '{key}'.");
        }

        var nullabilityContext = new NullabilityInfoContext();
        return inputs
            .Select(input =>
            {
                if (input.IsNullable.HasValue)
                    return input;

                if (!propertiesByKey.TryGetValue(input.ReferenceKey, out var property))
                {
                    throw new ArgumentException(
                        $"VF-ACT-003: Legacy CLR input '{input.ReferenceKey}' on activity '{clrActivityType.FullName}' has unknown nullability " +
                        "and does not match an annotated public activity property by stable key.");
                }

                return input with { IsNullable = IsNullable(property, nullabilityContext) };
            })
            .ToArray();
    }

    private static ActivityInputAttribute? FindActivityInputAttribute(PropertyInfo property)
    {
        for (var declaringType = property.DeclaringType; declaringType is not null; declaringType = declaringType.BaseType)
        {
            var declaredProperty = declaringType.GetProperty(
                property.Name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            var attribute = declaredProperty?.GetCustomAttribute<ActivityInputAttribute>(inherit: false);
            if (attribute is not null)
                return attribute;
        }

        return null;
    }

    private static bool IsNullable(PropertyInfo property, NullabilityInfoContext nullabilityContext)
    {
        if (property.PropertyType.IsValueType)
            return Nullable.GetUnderlyingType(property.PropertyType) is not null;

        // Assemblies without nullable-reference metadata retain the permissive CLR interpretation.
        // Only an explicit NotNull annotation may tighten a legacy catalog input at publication.
        return nullabilityContext.Create(property).WriteState is not NullabilityState.NotNull;
    }

    private static ActivityValuePolicy CompileActivityPolicy(
        string? ownerStorageProfile,
        ArgumentState? authored,
        string valueRole)
    {
        var owner = ValuePolicyCombiner.FromAuthoredStorage(ownerStorageProfile);
        if (authored is null)
            return owner;

        var authoredMinimum = ValuePolicyCombiner.FromAuthoredStorage(authored.StorageDriverType, authored.IsSensitive == true);
        return ValuePolicyCombiner.Combine(owner, authoredMinimum, valueRole);
    }

    private static IReadOnlyCollection<string> ResolveOutcomes(Type activityType, ExecutableActivityStructure? structure)
    {
        var outcomes = activityType.GetCustomAttributes<ActivityOutcomeAttribute>(inherit: true)
            .Select(attribute => attribute.Key)
            .ToHashSet(StringComparer.Ordinal);

        // Switch case labels are authored control ports. They belong to the pinned invocation contract even
        // though no finite set can be declared on the CLR type itself.
        if (StringComparer.Ordinal.Equals(structure?.Kind, "elsa.switch.structure") &&
            structure.Payload.TryGetProperty("cases", out var cases))
        {
            foreach (var @case in cases.EnumerateArray())
                if (@case.TryGetProperty("match", out var match) && match.GetString() is { Length: > 0 } value)
                    outcomes.Add(value);
        }

        if (outcomes.Count == 0)
            outcomes.Add(ActivityOutcomes.Done);
        return outcomes.ToArray();
    }

    private static Type? FindTypedActivityResult(Type activityType)
    {
        var contracts = activityType.GetInterfaces()
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IActivityResult<>))
            .Select(candidate => candidate.GetGenericArguments()[0])
            .Distinct()
            .ToArray();

        return contracts.Length switch
        {
            0 => null,
            1 => contracts[0],
            _ => throw new ArgumentException($"Activity type '{activityType.FullName}' declares more than one atomic result type.")
        };
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

    private ExecutableActivityStructure? CompileStructure(string nodeId, ActivityNodeStructure? structure)
    {
        if (structure is null)
            return null;

        var payload = JsonNode.Parse(structure.Payload.GetRawText()) as JsonObject
            ?? throw new ArgumentException($"Activity node '{nodeId}' has a non-object executable structure payload.");
        if (payload["variables"] is { } variablesNode)
        {
            var authored = variablesNode.Deserialize<IReadOnlyCollection<VariableDefinition>>(DescriptorSerializerOptions)
                ?? throw new ArgumentException($"Activity node '{nodeId}' has malformed variable declarations.");
            var compiled = authored.Select(variable => CompileVariableDeclaration(nodeId, variable)).ToArray();
            payload["variables"] = JsonSerializer.SerializeToNode(compiled, DescriptorSerializerOptions);
        }

        return new ExecutableActivityStructure(
            structure.Kind,
            structure.SchemaVersion,
            JsonSerializer.SerializeToElement(payload, DescriptorSerializerOptions));
    }

    private RuntimeVariableDeclaration CompileVariableDeclaration(string nodeId, VariableDefinition variable)
    {
        var type = new ValueTypeDescriptor(variable.Type.Alias, variable.Type.CollectionKind);
        var policy = ValuePolicyCombiner.ToProtectionPolicy(
            ValuePolicyCombiner.FromAuthoredStorage(variable.StorageDriverType));
        if (policy.Storage != DurableValueStorage.Inline)
        {
            throw new ArgumentException(
                $"VF-ACT-005: Variable '{variable.ReferenceKey}' on activity node '{nodeId}' selects external storage profile '{variable.StorageDriverType}', but canonical variable-frame externalization is not implemented. Publication is refused instead of accepting an unusable declaration.");
        }
        RuntimeInputBinding? initialBinding = null;
        if (variable.Default is not null)
        {
            initialBinding = inputBindingCompiler.Compile(
                nodeId,
                new InputDefinition(
                    variable.ReferenceKey,
                    variable.Name,
                    variable.Type,
                    variable.StorageDriverType,
                    variable.Name,
                    Category: null),
                variable.Default);
            if (initialBinding.Source != RuntimeInputBindingSource.Literal)
                throw new ArgumentException($"Variable '{variable.ReferenceKey}' on activity node '{nodeId}' requires a persistable literal initial binding.");
        }

        return new RuntimeVariableDeclaration(variable.ReferenceKey, variable.Name, type, policy, initialBinding);
    }

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
        if (IsTypedStatefulResumeTarget(activityType, method))
            return;

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
                $"Resume target method '{activityType.FullName}.{method.Name}' has an unsupported signature. A legacy resume target must take no parameters or a single {nameof(IActivityExecutionContext)}/{nameof(JsonElement)} parameter and return void, Task, or ValueTask; a typed stateful target must match IStatefulActivity<TResult,TState,TTrigger>.ResumeAsync.");
    }

    private static bool IsTypedStatefulResumeTarget(Type activityType, MethodInfo method)
    {
        var parameters = method.GetParameters();
        if (parameters.Length != 1 ||
            !parameters[0].ParameterType.IsGenericType ||
            parameters[0].ParameterType.GetGenericTypeDefinition() != typeof(ActivityResumeContext<,>) ||
            !method.ReturnType.IsGenericType ||
            method.ReturnType.GetGenericTypeDefinition() != typeof(ValueTask<>))
            return false;

        var transitionType = method.ReturnType.GetGenericArguments()[0];
        if (!transitionType.IsGenericType || transitionType.GetGenericTypeDefinition() != typeof(ActivityTransition<,>))
            return false;

        var contextArguments = parameters[0].ParameterType.GetGenericArguments();
        var transitionArguments = transitionType.GetGenericArguments();
        return activityType.GetInterfaces()
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IStatefulActivity<,,>))
            .Select(candidate => candidate.GetGenericArguments())
            .Any(contractArguments =>
                contractArguments[0] == transitionArguments[0] &&
                contractArguments[1] == transitionArguments[1] &&
                contractArguments[1] == contextArguments[0] &&
                contractArguments[2] == contextArguments[1]);
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
