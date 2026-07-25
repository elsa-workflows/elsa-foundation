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
using Elsa.Workflows.Design.Core.Services;
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
    RuntimeInputBindingCompiler inputBindingCompiler,
    RuntimeOutputCaptureCompiler outputCaptureCompiler)
{
    private static readonly JsonSerializerOptions DescriptorSerializerOptions = new(JsonSerializerDefaults.Web);

    public ExecutableNode CompileRoot(
        ActivityNode rootActivity,
        ActivityTreeProjection projection,
        IReadOnlyDictionary<string, ActivityDefinitionVersion> activityRows,
        IReadOnlyDictionary<string, ExecutableNode>? placedActivities = null,
        IEnumerable<VariableDefinition>? workflowVariables = null) =>
        CompileNode(
            rootActivity,
            projection,
            activityRows,
            placedActivities ?? new Dictionary<string, ExecutableNode>(),
            workflowVariables as IReadOnlyCollection<VariableDefinition> ?? workflowVariables?.ToArray() ?? []);

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
        var inputDefinitions = activityVersion.Inputs.ToArray();
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
        IReadOnlyDictionary<string, ExecutableNode> placedActivities,
        IReadOnlyCollection<VariableDefinition> workflowVariables)
    {
        if (placedActivities.TryGetValue(activity.NodeId, out var placed))
            return placed;

        if (activity.Intrinsic is not null)
            return CompileIntrinsicNode(activity, projection, activityRows, placedActivities, workflowVariables);

        var activityVersion = activityRows[activity.ActivityVersionId];
        var descriptor = new RuntimeActivityDescriptor(
            activityVersion.ConsumerKey,
            activityVersion.ConsumerSchemaVersion,
            activityVersion.DescriptorPayload);
        var clrActivityType = ResolveClrActivityType(descriptor);
        var inputDefinitions = activityVersion.Inputs.ToArray();

        var inputBindings = inputBindingCompiler.CompileAll(activity.NodeId, inputDefinitions, activity.Inputs);
        var childSlots = CompileChildSlots(projection.ChildProjections(activity), projection, activityRows, placedActivities, workflowVariables);

        var catalogActivityType = activityVersion.Definition?.ActivityTypeKey
            ?? throw new ArgumentException($"Activity version '{activity.ActivityVersionId}' did not include its activity definition.");
        var activityType = clrActivityType is null
            ? catalogActivityType
            : ActivityTypeMetadata.GetDeclaredActivityType(clrActivityType) ?? catalogActivityType;
        var executionType = clrActivityType is not null && ActivityTypeMetadata.IsTrigger(clrActivityType)
            ? TriggerNodeMetadata.TriggerExecutionType
            : activityVersion.ExecutionType.ToString();
        EnsureDeclaredStructureHasHandler(activity, activityVersion, catalogActivityType);
        var structure = CompileStructure(activity.NodeId, activityStructureService.CompileExecutableStructure(activity));
        var activityContract = clrActivityType is null
            ? null
            : BuildActivityContract(activity, activityVersion, inputDefinitions, clrActivityType, activityType, structure, inputBindings: inputBindings);

        var outputCaptures = CompileOutputCaptures(activity, activityContract, workflowVariables);

        return new ExecutableNode(
            executableNodeId: activity.NodeId,
            authoredActivityId: activity.NodeId,
            activityType: activityType,
            activityTypeVersion: activityVersion.Version,
            descriptor: descriptor,
            inputBindings: inputBindings,
            outputCaptures: outputCaptures,
            metadata: new Dictionary<string, string>
            {
                ["authoredNodeId"] = activity.NodeId,
                [TriggerNodeMetadata.ExecutionTypeKey] = executionType
            },
            childSlots: childSlots,
            structure: structure,
            activityContract: activityContract);
    }

    /// <summary>
    /// Turns a leaf activity's authored outputs (<see cref="ActivityNode.Outputs"/>) into durable output captures
    /// against its typed result-projection contracts. An activity with no authored outputs yields none, preserving
    /// the prior empty-capture behavior; the shared <see cref="RuntimeOutputCaptureCompiler"/> enforces the same
    /// workflow-scope Variable rules as the reusable-activity boundary path.
    /// </summary>
    private IReadOnlyDictionary<string, RuntimeOutputCapture> CompileOutputCaptures(
        ActivityNode activity,
        Elsa.Activities.Runtime.Core.Models.ActivityContract? activityContract,
        IReadOnlyCollection<VariableDefinition> workflowVariables)
    {
        var authoredOutputs = activity.Outputs as IReadOnlyCollection<ArgumentState> ?? activity.Outputs.ToArray();
        var projections = activityContract?.Result.Projections.Values.ToArray() ?? [];
        if (authoredOutputs.Count == 0)
            return new Dictionary<string, RuntimeOutputCapture>(StringComparer.Ordinal);

        return outputCaptureCompiler.CompileResultProjectionOutputs(
            activity.NodeId,
            projections,
            authoredOutputs,
            workflowVariables);
    }

    private ExecutableNode CompileIntrinsicNode(
        ActivityNode activity,
        ActivityTreeProjection projection,
        IReadOnlyDictionary<string, ActivityDefinitionVersion> activityRows,
        IReadOnlyDictionary<string, ExecutableNode> placedActivities,
        IReadOnlyCollection<VariableDefinition> workflowVariables)
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
                    IsNullable: false,
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
            childSlots: CompileChildSlots(projection.ChildProjections(activity), projection, activityRows, placedActivities, workflowVariables),
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
        string? contractVersion = null,
        IReadOnlyDictionary<string, RuntimeInputBinding>? inputBindings = null)
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
                input.IsNullable,
                input.DefaultValue.HasValue,
                input.DefaultValue,
                CompileActivityPolicy(input.StorageDriverType, state, $"Input '{input.ReferenceKey}' on activity node '{activity.NodeId}'"));
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
        var outcomes = ResolveOutcomes(activityType, structure, inputBindings);
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
            new ActivityActivationRequirement(typeof(ClrActivityDescriptor).FullName!, TypeAliasConvention.CanonicalAlias(activityType)),
            ResolveSideEffectProfile(activityType));
    }

    // ADR 0032 R1 / spec 107: an unmarked activity is External (fail-safe); only an explicit
    // [ActivitySideEffectProfile(ReplaySafe)] opts a pure/deterministic activity into a deferrable claim boundary.
    private static SideEffectProfile ResolveSideEffectProfile(Type activityType) =>
        activityType.GetCustomAttribute<ActivitySideEffectProfileAttribute>(inherit: true)?.Profile
        ?? SideEffectProfile.External;

    private static ValueRepresentation ResolveSourceRepresentation(
        OutputAttribute attribute,
        OutputDefinition? outputDefinition,
        ValueTypeDescriptor sourceType) =>
        outputDefinition?.SourceRepresentation ??
        (attribute.HasSourceRepresentation ? attribute.SourceRepresentation : ValueRepresentationDefaults.Infer(sourceType));

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

    private static IReadOnlyCollection<string> ResolveOutcomes(
        Type activityType,
        ExecutableActivityStructure? structure,
        IReadOnlyDictionary<string, RuntimeInputBinding>? inputBindings)
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

        // spec 125: a BPMN transaction subprocess declares the additional "Cancelled" outcome (structure-dependent,
        // the same FlowSwitch/VF-ACT-006 pattern as Switch case labels): a cancel end event inside the transaction
        // completes the nested process with "Cancelled" instead of "Done". A non-transaction BpmnProcess keeps only
        // its statically declared "Done" outcome, so the contract stays byte-identical for ordinary processes.
        if (StringComparer.Ordinal.Equals(structure?.Kind, "elsa.bpmn.structure") &&
            structure.Payload.TryGetProperty("isTransaction", out var isTransaction) &&
            isTransaction.ValueKind == JsonValueKind.True)
        {
            outcomes.Add("Cancelled");
        }

        AddValueDerivedOutcomes(activityType, inputBindings, outcomes);

        if (outcomes.Count == 0)
            outcomes.Add(ActivityOutcomes.Done);
        return outcomes.ToArray();
    }

    /// <summary>
    /// Pins the per-node outcome ports an activity derives from an authored collection input (see
    /// <see cref="ActivityValueOutcomesAttribute"/>): one outcome named after each configured item plus the
    /// unmatched catch-all. Reads the compiled literal binding so the pinned names match exactly what the runtime
    /// emits. When the source input has no authored items the activity keeps only its statically declared outcomes,
    /// preserving back-compat for an unconfigured node (#926).
    /// </summary>
    private static void AddValueDerivedOutcomes(
        Type activityType,
        IReadOnlyDictionary<string, RuntimeInputBinding>? inputBindings,
        HashSet<string> outcomes)
    {
        var declaration = activityType.GetCustomAttribute<ActivityValueOutcomesAttribute>(inherit: true);
        if (declaration is null || inputBindings is null ||
            !inputBindings.TryGetValue(declaration.InputKey, out var binding) ||
            binding.LiteralValue is not { ValueKind: JsonValueKind.Array } array)
            return;

        var added = false;
        foreach (var item in array.EnumerateArray())
        {
            var name = item.ValueKind switch
            {
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Number => item.GetRawText(),
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(name) && outcomes.Add(name))
                added = true;
        }

        if (added)
            outcomes.Add(declaration.UnmatchedOutcome);
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
        IReadOnlyDictionary<string, ExecutableNode> placedActivities,
        IReadOnlyCollection<VariableDefinition> workflowVariables)
    {
        return childSlots
            .Select(slot => new ExecutableChildSlot(
                slot.Name,
                slot.Activities.Select(activity => CompileNode(activity, projection, activityRows, placedActivities, workflowVariables)).ToArray()))
            .ToArray();
    }

    /// <summary>
    /// Publication guard against silently mis-compiling a composite. Spec 071 FR-008 keeps a
    /// structure of UNKNOWN kind opaque (copied through, no children projected) — but a kind the
    /// activity's own catalog design facets declare is not unknown: its handler ships with the
    /// feature that provides the activity, so a missing handler means that feature is disabled or
    /// absent from this shell. Compiling such a node opaquely publishes an executable whose
    /// children were never lowered and that faults confusingly deep in runtime execution
    /// (e.g. a BPMN process publishing while ActivitiesBpmn is disabled).
    /// </summary>
    private void EnsureDeclaredStructureHasHandler(ActivityNode activity, ActivityDefinitionVersion activityVersion, string activityTypeKey)
    {
        var structure = activity.Structure;
        if (structure is null || activityStructureService.HasHandler(structure))
            return;

        var declaredFacet = activityVersion.DesignFacets.FirstOrDefault(facet => StringComparer.Ordinal.Equals(facet.Kind, structure.Kind));
        if (declaredFacet is null)
            return;

        throw new ArgumentException(
            $"Activity node '{activity.NodeId}' carries structure '{structure.Kind}@{structure.SchemaVersion}', which its activity type '{activityTypeKey}' declares as an owned structure contract (declared schema version '{declaredFacet.SchemaVersion}'), " +
            "but no structure handler for that kind is registered in this shell. The feature that provides the activity is disabled or missing; enable it before publishing. " +
            "Compiling the structure as opaque would publish an executable that fails at execution time.");
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

    /// <summary>
    /// Publish-time backstop for the <c>IntrinsicVariableTargetValidator</c> draft guard (#972, PR #971
    /// two-guard pattern): every variable-writing intrinsic must target a variable visible from its scope
    /// (workflow-scope <c>state.Variables</c> or a visible ancestor container's declarations, ADR 0027).
    /// Compiling an invisible target would publish an executable that poisons at runtime
    /// ("Set intrinsic ... targets undeclared variable"); publication is refused instead.
    /// </summary>
    public void ValidateIntrinsicVariableTargets(
        WorkflowDefinitionState state,
        IReadOnlyCollection<ActivityNode> nodes,
        int maxDepth = 100)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(nodes);

        var intrinsicTargets = nodes
            .Where(node => node.Intrinsic?.Variable is not null)
            .ToArray();
        if (intrinsicTargets.Length == 0)
            return;

        var visibility = new ScopedVariableResolver(activityStructureService)
            .Resolve(state.Variables, state.RootActivity, maxDepth);
        foreach (var node in intrinsicTargets)
        {
            var reference = node.Intrinsic!.Variable!;
            if (visibility.IsReferenceVisible(node.NodeId, reference))
                continue;

            var scopeId = reference.IsWorkflowScope ? VariableReference.WorkflowScopeId : reference.DeclaringScopeId;
            throw new ArgumentException(
                $"Workflow {node.Intrinsic.Kind} intrinsic node '{node.NodeId}' targets variable '{reference.ReferenceKey}' in scope '{scopeId}', " +
                "which is not visible from this node's scope. Declare the variable on the workflow or on a visible ancestor container before publishing; " +
                "compiling it would publish an executable that fails at execution time.");
        }
    }

    /// <summary>
    /// Compiles the authored workflow-scope variable declarations (<c>state.Variables</c>) into the canonical
    /// executable form carried on <see cref="WorkflowExecutable.WorkflowVariables"/>. These seed the runtime's
    /// root variable frame (scope id <c>"workflow"</c>); the same VF-ACT-005 storage rules and literal-default
    /// requirement apply as for container-structure declarations.
    /// </summary>
    public IReadOnlyCollection<RuntimeVariableDeclaration> CompileWorkflowVariables(IEnumerable<VariableDefinition> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        var authored = variables.ToArray();
        var duplicate = authored
            .GroupBy(variable => variable.ReferenceKey, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Workflow variable '{duplicate.Key}' is declared more than once.");

        return authored
            .Select(variable => CompileVariableDeclaration(VariableReference.WorkflowScopeId, variable))
            .ToArray();
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
                    Category: null,
                    IsNullable: true),
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

                // Node-scoped resume-target ids (lifting the W8 one-instance-per-workflow limit): the map key
                // embeds the executable node id, so multiple instances of the same resume-target activity
                // (e.g. two Delay nodes, or two synthesized BPMN timer catch events) coexist. Activities keep
                // registering by their local attribute id — the resume resolver falls back to matching
                // (ExecutableNodeId, LocalResumeTargetId), the same rescoped shape the template placer emits.
                var localResumeTargetId = attribute.ResumeTargetId;
                var resumeTargetId = WorkflowExecutableResumeTarget.ComposeScopedId(node.ExecutableNodeId, localResumeTargetId);
                if (resumeTargets.TryGetValue(resumeTargetId, out var existing))
                    throw new ArgumentException(
                        $"Resume target '{localResumeTargetId}' is declared more than once by executable node '{existing.ExecutableNodeId}'. A resume target id must be unique within one activity type.");

                resumeTargets[resumeTargetId] = new WorkflowExecutableResumeTarget(
                    ResumeTargetId: resumeTargetId,
                    ExecutableNodeId: node.ExecutableNodeId,
                    HandlerKey: method.Name,
                    Metadata: new Dictionary<string, string>(StringComparer.Ordinal),
                    LocalResumeTargetId: localResumeTargetId);
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
