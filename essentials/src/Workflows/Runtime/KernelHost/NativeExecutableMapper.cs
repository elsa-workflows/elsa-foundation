using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Native = Kernel.Native;

namespace Elsa.Workflows.Runtime.KernelHost;

/// <summary>
/// Maps a compiled <see cref="WorkflowExecutable"/>, as publishing stores it, to the Native workflow the kernel runs. The
/// workflow's id is the executable's artifact id and its definition is the executable's definition id, so instance reads
/// and schedules key on the definition and an instance runs the artifact it started on. Anything the kernel does not run
/// is refused with a <see cref="NativeMappingRefusedException"/> that names the node, before anything is minted or served.
/// The rules are those of <c>docs/architecture/definitions-on-the-kernel.md</c>, "Mapping rules".
/// </summary>
public static class NativeExecutableMapper
{
    /// <summary>The Native workflow for <paramref name="executable"/>, or a refusal naming the node (and input) it cannot map.</summary>
    /// <exception cref="NativeMappingRefusedException">The executable holds an activity or a binding the kernel does not run.</exception>
    public static Native.Workflow Map(WorkflowExecutable executable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        var mapping = new Mapping(executable);
        var root = mapping.MapNode(executable.RootActivity, Scope.Workflow);
        var variables = mapping.WorkflowVariables();
        try
        {
            return new Native.Workflow(executable.Identity.ArtifactId, root, variables, executable.Identity.DefinitionId);
        }
        catch (InvalidOperationException invalid)
        {
            // The Native definition's own rules (unique Switch labels, a GraphActivity's single body and its declarations).
            throw NativeMappingRefusedException.Activity(root.Id, invalid.Message);
        }
    }

    /// <summary>Where a node is mapped: in the workflow, or inside a GraphActivity's body, which sees only its boundary.</summary>
    private sealed record Scope(GraphScope? Graph)
    {
        public static readonly Scope Workflow = new((GraphScope?)null);
    }

    /// <summary>
    /// A GraphActivity's body sees only its boundary's inputs and variables. Inside it, a request read names a boundary
    /// input by its reference key (the Native input's name), and a workflow-scope variable read names a boundary variable.
    /// </summary>
    private sealed record GraphScope(string Node, IReadOnlySet<string> Inputs, IReadOnlyDictionary<string, string> Variables);

    private sealed class Mapping(WorkflowExecutable executable)
    {
        private const string WorkflowScopeId = VariableReference.WorkflowScopeId;
        private const string LegacyGraphScopeId = "activity-graph";
        private const string WorkflowVariableValueIdPrefix = "variable:";
        private const string GraphVariableValueIdPrefix = "graph-variable:";
        private const string WholeResult = "$result";
        private const string InputVariable = "input";

        private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

        private readonly Dictionary<string, RuntimeVariableDeclaration> _variables =
            executable.WorkflowVariables.ToDictionary(variable => variable.VariableKey, StringComparer.Ordinal);

        // Every node's Native type, resolved up front so a result read can find its producer's shape wherever it sits, and
        // so the first node in document order that the kernel does not run is the one refused.
        private readonly Dictionary<string, Type> _types = executable.Nodes.ToDictionary(node => node.ExecutableNodeId, TypeOf, StringComparer.Ordinal);

        public IReadOnlyDictionary<string, JsonNode?> WorkflowVariables()
        {
            var root = executable.RootActivity.ExecutableNodeId;
            var variables = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
            foreach (var declaration in executable.WorkflowVariables)
            {
                if (declaration.Name == InputVariable)
                    throw NativeMappingRefusedException.Binding(root, declaration.Name, $"A workflow variable cannot be named '{InputVariable}': on the kernel it is the run's input.");
                if (!variables.TryAdd(declaration.Name, null))
                    throw NativeMappingRefusedException.Binding(root, declaration.Name, $"Two workflow variables are named '{declaration.Name}'; the kernel names variables.");
                if (declaration.InitialBinding is not { } initial) continue;
                variables[declaration.Name] = MapBinding(initial, root, declaration.Name, Scope.Workflow) switch
                {
                    null => null,
                    Native.LiteralBinding literal => literal.Value,
                    _ => throw NativeMappingRefusedException.Binding(root, declaration.Name, "A workflow variable's initial value must be a literal."),
                };
            }
            return variables;
        }

        // ---- activity types --------------------------------------------------------------------------------------

        private static Type TypeOf(ExecutableNode node)
        {
            var id = node.ExecutableNodeId;
            if (node.IntrinsicKind is { } kind)
                return kind switch
                {
                    WorkflowIntrinsicKind.Set => typeof(Native.SetVariable),
                    WorkflowIntrinsicKind.SetOutput => typeof(Native.SetOutput),
                    WorkflowIntrinsicKind.SetCorrelationId => typeof(Native.SetCorrelationId),
                    WorkflowIntrinsicKind.SetInstanceName => typeof(Native.SetInstanceName),
                    WorkflowIntrinsicKind.Finish => typeof(Native.Finish),
                    _ => throw NativeMappingRefusedException.Activity(id, $"The {kind} intrinsic does not run on the kernel in release 1."),
                };
            if (StringComparer.Ordinal.Equals(node.DescriptorType, WellKnownRuntimeActivityConsumers.GraphActivity))
                return typeof(Native.GraphActivity);
            try
            {
                return Native.NativeActivityCatalog.Resolve(node.ActivityType, id);
            }
            catch (Native.NativeActivityUnsupportedException unsupported)
            {
                throw NativeMappingRefusedException.Activity(id, unsupported.Message);
            }
        }

        // ---- nodes -----------------------------------------------------------------------------------------------

        public Native.Node MapNode(ExecutableNode source, Scope scope)
        {
            var id = source.ExecutableNodeId;
            var type = _types[id];
            var node = new Native.Node(id, type);
            if (source.IntrinsicKind is not null)
            {
                if (source.ChildSlots.Any(slot => slot.Activities.Count != 0))
                    throw NativeMappingRefusedException.Activity(id, "An intrinsic node with children does not run on the kernel.");
                MapIntrinsic(source, node, scope);
            }
            else
            {
                var children = new Children(source);
                var consumed = MapStructure(source, node, children, scope);
                children.RequireAllPlaced();
                if (type != typeof(Native.GraphActivity)) MapInputs(source, node, consumed, scope);
            }
            MapCaptures(source, node, scope);
            return node;
        }

        /// <summary>The structure of a composite onto the Native shape; returns the inputs it consumed (a Switch's case conditions).</summary>
        private IReadOnlySet<string> MapStructure(ExecutableNode source, Native.Node node, Children children, Scope scope)
        {
            var id = source.ExecutableNodeId;
            var consumed = new HashSet<string>(StringComparer.Ordinal);
            var type = node.Type;
            if (type == typeof(Native.Sequence))
            {
                var structure = source.Structure is null ? null : Read<SequenceStructure>(source, "Sequence", "elsa.sequence.structure", "1.0.0");
                RefuseContainerVariables(id, structure?.Variables);
                var order = structure?.Activities ?? children.InSlot("Sequence.Activities");
                foreach (var child in order) node.Children.Add(MapNode(children.Take(child), scope));
            }
            else if (type == typeof(Native.If))
            {
                var structure = Read<IfStructure>(source, "If", "elsa.if.structure", "1.0.0");
                if (structure.Then is { } then) node.Children.Add(Slotted(MapNode(children.Take(then), scope), "Then"));
                if (structure.Else is { } @else) node.Children.Add(Slotted(MapNode(children.Take(@else), scope), "Else"));
            }
            else if (type == typeof(Native.Switch))
            {
                var version = source.Structure?.SchemaVersion is "1.0.0" ? "1.0.0" : "2.0.0";
                var structure = Read<SwitchStructure>(source, "Switch", "elsa.switch.structure", version);
                foreach (var @case in structure.Cases ?? [])
                {
                    Native.Binding? condition = null;
                    if (@case.ConditionInputKey is { } key)
                    {
                        if (!source.InputBindings.TryGetValue(key, out var binding))
                            throw NativeMappingRefusedException.Activity(id, $"Case '{@case.Match}' names condition input '{key}', which the node does not bind.");
                        // An unbound condition is false, as on the old core.
                        condition = MapBinding(binding, id, key, scope) ?? Native.Binding.Literal(false);
                        consumed.Add(key);
                    }
                    node.Cases.Add(new Native.SwitchCase(@case.Match, condition));
                    if (@case.Activity is { } branch) node.Children.Add(Slotted(MapNode(children.Take(branch), scope), Native.Switch.CaseSlot(@case.Match)));
                }
                if (structure.Default is { } @default) node.Children.Add(Slotted(MapNode(children.Take(@default), scope), Native.Switch.DefaultSlot));
            }
            else if (type == typeof(Native.For) || type == typeof(Native.ForEach) || type == typeof(Native.While) || type == typeof(Native.Do))
            {
                var name = type.Name;
                var structure = Read<LoopStructure>(source, name, $"elsa.{name.ToLowerInvariant()}.structure", "1.0.0");
                if (structure.Body is { } body) node.Children.Add(MapNode(children.Take(body), scope));
            }
            else if (type == typeof(Native.Parallel))
            {
                var structure = Read<ParallelStructure>(source, "Parallel", "elsa.parallel.structure", "1.0.0");
                var branches = structure.Branches ?? [];
                // Native counts its children; an empty branch, which the old core counts as finished at once, has none.
                if (structure.Threshold is { } threshold && threshold >= 1 && threshold <= branches.Count)
                {
                    if (branches.Any(branch => branch.Activity is null))
                        throw NativeMappingRefusedException.Activity(id, "A Parallel with a threshold and an empty branch does not run on the kernel.");
                    node.Inputs["Threshold"] = Native.Binding.Literal(threshold);
                }
                foreach (var branch in branches)
                    if (branch.Activity is { } activity) node.Children.Add(MapNode(children.Take(activity), scope));
            }
            else if (type == typeof(Native.Flowchart))
                MapFlowchart(source, node, children, scope);
            else if (type == typeof(Native.GraphActivity))
                MapGraph(source, node, children, scope);
            // Leaves: Children.RequireAllPlaced refuses any child they carry.
            return consumed;
        }

        private void MapFlowchart(ExecutableNode source, Native.Node node, Children children, Scope scope)
        {
            var id = source.ExecutableNodeId;
            var structure = Read<FlowchartStructure>(source, "Flowchart", "elsa.flowchart.structure", "1.0.0");
            RefuseContainerVariables(id, structure.Variables);
            var connections = (structure.Connections ?? [])
                .Select(c => new Native.Connection(c.Source.NodeId, c.Target.NodeId, string.IsNullOrWhiteSpace(c.Source.Port) ? "Done" : c.Source.Port.Trim()))
                .ToList();
            var order = children.InSlot("Flowchart.Activities").ToList();
            // Native begins where the graph engine does without a start node: at the first child no connection leads to, else
            // the first child. So the start node goes first, which keeps that choice when nothing leads to it.
            if (structure.StartNodeId is { Length: > 0 } start)
            {
                if (!order.Remove(start))
                    throw NativeMappingRefusedException.Activity(id, $"The start node '{start}' is not a child of the Flowchart.");
                var targets = connections.Select(c => c.Target).ToHashSet(StringComparer.Ordinal);
                if (targets.Contains(start) && order.Any(child => !targets.Contains(child)))
                    throw NativeMappingRefusedException.Activity(id, $"The start node '{start}' has an inbound connection while another child has none; the kernel would start there.");
                order.Insert(0, start);
            }
            foreach (var child in order) node.Children.Add(MapNode(children.Take(child), scope));
            node.Connections.AddRange(connections);
            foreach (var (child, metadata) in structure.NodeMetadata ?? new Dictionary<string, FlowchartNodeStructure>())
            {
                if (metadata.PolicyKind is not { Length: > 0 } kind) continue;
                node.Gateways[child] = kind.Trim() switch
                {
                    "decision" => Native.Gateway.Decision,
                    "parallelFork" => Native.Gateway.ParallelFork,
                    "parallelJoin" => Native.Gateway.ParallelJoin,
                    "inclusiveFork" => Native.Gateway.InclusiveFork,
                    "inclusiveJoin" => Native.Gateway.InclusiveJoin,
                    "firstWins" => Native.Gateway.FirstWins,
                    "merge" => Native.Gateway.Merge,
                    var other => throw NativeMappingRefusedException.Activity(child, $"The Flowchart policy '{other}' does not run on the kernel."),
                };
            }
        }

        /// <summary>
        /// A GraphActivity's pinned descriptor as a <see cref="Native.GraphBoundary"/>. Its inputs are named by their reference
        /// keys, which is how the node binds them and how its body reads them; its variables by their names; its outputs by their
        /// reference keys, which is how its result names them.
        /// </summary>
        private void MapGraph(ExecutableNode source, Native.Node node, Children children, Scope scope)
        {
            var id = source.ExecutableNodeId;
            GraphDescriptor descriptor;
            try
            {
                descriptor = source.DescriptorPayload.Deserialize<GraphDescriptor>(PayloadOptions)
                             ?? throw NativeMappingRefusedException.Activity(id, "The GraphActivity descriptor is empty.");
            }
            catch (JsonException invalid)
            {
                throw NativeMappingRefusedException.Activity(id, $"The GraphActivity descriptor does not read: {invalid.Message}");
            }
            var entry = !string.IsNullOrWhiteSpace(descriptor.EntryNodeId) ? descriptor.EntryNodeId : descriptor.EntryOccurrenceId;
            if (string.IsNullOrWhiteSpace(entry))
                throw NativeMappingRefusedException.Activity(id, "The GraphActivity descriptor names no entry node.");

            var inputs = descriptor.Inputs ?? [];
            var variables = descriptor.Variables ?? [];
            var required = (descriptor.RequiredInputReferenceKeys ?? []).ToHashSet(StringComparer.Ordinal);
            var requiredOutputs = (descriptor.RequiredOutputReferenceKeys ?? []).ToHashSet(StringComparer.Ordinal);
            var inputKeys = inputs.Select(input => input.ReferenceKey).ToHashSet(StringComparer.Ordinal);
            var variableNames = variables.ToDictionary(variable => variable.ReferenceKey, variable => variable.Name, StringComparer.Ordinal);
            var mappings = (descriptor.OutputMappings ?? []).ToDictionary(mapping => mapping.OutputReferenceKey, mapping => mapping.Source, StringComparer.Ordinal);

            node.Graph = new Native.GraphBoundary
            {
                Inputs = [.. inputs.Select(input => new Native.GraphInput(input.ReferenceKey, required.Contains(input.ReferenceKey),
                    input.Default is { } fallback ? GraphExpression(id, input.ReferenceKey, fallback, variableNames, outputs: false) : null))],
                Variables = [.. variables.Select(variable => new Native.GraphVariable(variable.Name,
                    variable.InitialValue is { } initial ? GraphExpression(id, variable.Name, initial, variableNames, outputs: false) : null))],
                Outputs = [.. (descriptor.Outputs ?? []).Select(output => new Native.GraphOutput(output.ReferenceKey,
                    mappings.TryGetValue(output.ReferenceKey, out var mapping) ? GraphExpression(id, output.ReferenceKey, mapping, variableNames, outputs: true) : null,
                    requiredOutputs.Contains(output.ReferenceKey)))],
                OutcomeMappings = (descriptor.OutcomeMappings ?? []).ToDictionary(m => m.SourceOutcomeName, m => m.BoundaryOutcomeName, StringComparer.Ordinal),
                CompleteOutcomes = (descriptor.CompleteOutcomes ?? []).ToDictionary(m => m.ReferenceKey, m => m.BoundaryOutcomeName, StringComparer.Ordinal),
            };

            // The node's input bindings are read where the GraphActivity is placed, so in the enclosing scope.
            foreach (var (key, binding) in source.InputBindings.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (!inputKeys.Contains(key))
                    throw NativeMappingRefusedException.Binding(id, key, "The GraphActivity does not declare this input.");
                if (MapBinding(binding, id, key, scope) is { } mapped) node.Inputs[key] = mapped;
            }

            var body = new Scope(new GraphScope(id, inputKeys, variableNames));
            node.Children.Add(MapNode(children.Take(entry), body));
        }

        // A boundary's defaults, initial values and output mappings are authored expressions: the descriptor pins their syntax and value.
        private static Native.Binding GraphExpression(string node, string member, GraphExpressionPayload expression,
            IReadOnlyDictionary<string, string> variables, bool outputs)
        {
            var syntax = expression.Syntax;
            if (string.Equals(syntax, "Literal", StringComparison.OrdinalIgnoreCase) || string.Equals(syntax, "Object", StringComparison.OrdinalIgnoreCase))
                return new Native.LiteralBinding(ToNode(expression.Value));
            if (string.Equals(syntax, "Input", StringComparison.OrdinalIgnoreCase) && expression.Value.ValueKind == JsonValueKind.Object &&
                expression.Value.TryGetProperty("referenceKey", out var key) && key.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(key.GetString()))
                return new Native.VariableBinding(key.GetString()!);
            if (outputs && string.Equals(syntax, "Variable", StringComparison.OrdinalIgnoreCase) &&
                VariableReference.TryParse(expression.Value, out var reference) && reference is not null &&
                (reference.IsWorkflowScope || reference.DeclaringScopeId == LegacyGraphScopeId) && variables.TryGetValue(reference.ReferenceKey, out var name))
                return new Native.VariableBinding(name);
            throw NativeMappingRefusedException.Binding(node, member, $"A GraphActivity expression of syntax '{syntax}' does not run on the kernel in this slice.");
        }

        // ---- intrinsics ------------------------------------------------------------------------------------------

        private void MapIntrinsic(ExecutableNode source, Native.Node node, Scope scope)
        {
            var id = source.ExecutableNodeId;
            var kind = source.IntrinsicKind!.Value;
            var expected = kind switch
            {
                WorkflowIntrinsicKind.Set or WorkflowIntrinsicKind.SetCorrelationId or WorkflowIntrinsicKind.SetInstanceName => new[] { WorkflowIntrinsicInputKeys.Value },
                WorkflowIntrinsicKind.SetOutput => [WorkflowIntrinsicInputKeys.Name, WorkflowIntrinsicInputKeys.Value],
                _ => [WorkflowIntrinsicInputKeys.Outcome],
            };
            if (source.InputBindings.Keys.FirstOrDefault(key => !expected.Contains(key, StringComparer.Ordinal)) is { } unknown)
                throw NativeMappingRefusedException.Binding(id, unknown, $"The {kind} intrinsic has no such input.");

            Native.Binding Input(string key) =>
                source.InputBindings.TryGetValue(key, out var binding) ? MapBinding(binding, id, key, scope) ?? Native.Binding.Literal(null) : Native.Binding.Literal(null);

            Native.Binding LiteralText(string key) =>
                Input(key) is Native.LiteralBinding { Value: JsonValue value } literal && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
                    ? literal
                    : throw NativeMappingRefusedException.Binding(id, key, $"The {kind} intrinsic needs a literal, non-blank '{key}'.");

            switch (kind)
            {
                case WorkflowIntrinsicKind.Set:
                    var target = source.IntrinsicVariable!;
                    node.Inputs["Name"] = Native.Binding.Literal(VariableName(target.VariableKey, target.DeclaringScopeId, id, WorkflowIntrinsicInputKeys.Value, scope));
                    node.Inputs["Value"] = Input(WorkflowIntrinsicInputKeys.Value);
                    break;
                case WorkflowIntrinsicKind.SetOutput:
                    node.Inputs["Name"] = LiteralText(WorkflowIntrinsicInputKeys.Name);
                    node.Inputs["Value"] = Input(WorkflowIntrinsicInputKeys.Value);
                    break;
                case WorkflowIntrinsicKind.SetCorrelationId or WorkflowIntrinsicKind.SetInstanceName:
                    node.Inputs["Value"] = Input(WorkflowIntrinsicInputKeys.Value);
                    break;
                case WorkflowIntrinsicKind.Finish:
                    node.Inputs["Outcome"] = LiteralText(WorkflowIntrinsicInputKeys.Outcome);
                    break;
            }
        }

        // ---- inputs, captures ------------------------------------------------------------------------------------

        private void MapInputs(ExecutableNode source, Native.Node node, IReadOnlySet<string> consumed, Scope scope)
        {
            var id = source.ExecutableNodeId;
            var shape = NativeActivityShape.For(node.Type);
            foreach (var (key, binding) in source.InputBindings.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (consumed.Contains(key)) continue;
                var mapped = MapBinding(binding, id, key, scope);
                switch (shape.RuleFor(key))
                {
                    case InputRule.Pass pass:
                        if (mapped is not null) node.Inputs[pass.Native] = mapped;
                        break;
                    case InputRule.Literal literal:
                        if (mapped is null) break;
                        if (mapped is not Native.LiteralBinding value || literal.Convert(value.Value) is not { } converted)
                            throw NativeMappingRefusedException.Binding(id, key, $"On the kernel this input takes {literal.Expected}.");
                        node.Inputs[literal.Native] = new Native.LiteralBinding(converted);
                        break;
                    case InputRule.Unmodelled unmodelled:
                        if (mapped is null) break;
                        if (mapped is not Native.LiteralBinding fixedValue || !unmodelled.Accepts(fixedValue.Value))
                            throw NativeMappingRefusedException.Binding(id, key, $"This value does not run on the kernel: {unmodelled.Reason}.");
                        break;
                    default:
                        throw NativeMappingRefusedException.Binding(id, key, $"The kernel's {node.Type.Name} has no such input.");
                }
            }
            foreach (var (native, (old, value)) in shape.AbsentDefaults)
                if (!source.InputBindings.ContainsKey(old) && !node.Inputs.ContainsKey(native))
                    node.Inputs[native] = new Native.LiteralBinding(value?.DeepClone());
        }

        private void MapCaptures(ExecutableNode source, Native.Node node, Scope scope)
        {
            var id = source.ExecutableNodeId;
            foreach (var capture in source.OutputCaptures.Values.OrderBy(capture => capture.OutputName, StringComparer.Ordinal))
            {
                RequireConversion(capture.ConversionPlan, id, capture.OutputName);
                string variable;
                if (capture.ValueId.StartsWith(GraphVariableValueIdPrefix, StringComparison.Ordinal) && scope.Graph is { } graph &&
                    graph.Variables.TryGetValue(capture.ValueId[GraphVariableValueIdPrefix.Length..], out var graphVariable))
                    variable = graphVariable;
                else if (capture.ValueId.StartsWith(WorkflowVariableValueIdPrefix, StringComparison.Ordinal) && scope.Graph is null &&
                         executable.WorkflowVariables.Any(declaration => declaration.Name == capture.ValueId[WorkflowVariableValueIdPrefix.Length..]))
                    variable = capture.ValueId[WorkflowVariableValueIdPrefix.Length..];
                else
                    throw NativeMappingRefusedException.Binding(id, capture.OutputName, $"The output is captured into '{capture.ValueId}', which is not a variable this node can see.");
                node.Captures[variable] = ResultPath(id, capture.OutputName, null, id, capture.OutputName);
            }
        }

        // ---- bindings --------------------------------------------------------------------------------------------

        /// <summary>The Native binding for a compiled one, or null for an absent literal, which leaves the Native input unbound.</summary>
        private Native.Binding? MapBinding(RuntimeInputBinding binding, string node, string input, Scope scope)
        {
            RequireConversion(binding.ConversionPlan, node, input);
            if (binding.ConversionPlan is null && binding.ConversionRequest is { Mode: not (ValueConversionMode.Auto or ValueConversionMode.None) } request)
                throw NativeMappingRefusedException.Binding(node, input, $"The kernel applies no {request.Mode} conversion.");
            var segments = Segments(binding.Selection, node, input);
            switch (binding.Source)
            {
                case RuntimeInputBindingSource.Literal:
                    return binding.Literal switch
                    {
                        null or { Presence: ValuePresence.Absent } => null,
                        { Presence: ValuePresence.ExplicitNull } => Native.Binding.Literal(null),
                        { Presence: ValuePresence.Present, InlineValue: { } value } => new Native.LiteralBinding(ToNode(value)),
                        _ => throw NativeMappingRefusedException.Binding(node, input, "The kernel reads literals stored inline only."),
                    };
                case RuntimeInputBindingSource.VariableRead or RuntimeInputBindingSource.VariableMember:
                    // A transient value lives in one activation only, and the kernel keeps every variable in the instance's state.
                    if (binding.EffectivePolicy.Lifecycle == DurableValueLifecycle.None)
                        throw NativeMappingRefusedException.Binding(node, input, "The binding reads a transient variable, which the kernel does not keep.");
                    var variable = binding.Variable!;
                    return new Native.VariableBinding(VariableName(variable.VariableKey, variable.DeclaringScopeId, node, input, scope), Join(null, segments));
                case RuntimeInputBindingSource.WorkflowRequest or RuntimeInputBindingSource.WorkflowRequestMember:
                    return RequestRead(binding.WorkflowRequest!.MemberKey, binding.WorkflowRequest.Path, segments, node, input, scope);
                case RuntimeInputBindingSource.ActivityResult or RuntimeInputBindingSource.ActivityResultMember:
                    // Native keeps one result per node, so a read of a result an iteration scoped reads the latest.
                    var result = binding.ActivityResult!;
                    return new Native.ResultBinding(result.ProducerExecutableNodeId, ResultPath(result.ProducerExecutableNodeId, result.ProjectionKey, segments, node, input));
                case RuntimeInputBindingSource.Expression:
                    return HostExpression(binding, node, input, scope);
                case RuntimeInputBindingSource.SecretRead:
                    throw NativeMappingRefusedException.Binding(node, input, "Secrets are outside release 1 on the kernel.");
                default:
                    throw NativeMappingRefusedException.Binding(node, input, $"The binding source {binding.Source} does not run on the kernel.");
            }
        }

        private Native.HostExpressionBinding HostExpression(RuntimeInputBinding binding, string node, string input, Scope scope)
        {
            var expression = binding.Expression!;
            if (!string.Equals(expression.Language, "JavaScript", StringComparison.OrdinalIgnoreCase) && !string.Equals(expression.Language, "Liquid", StringComparison.OrdinalIgnoreCase))
                throw NativeMappingRefusedException.Binding(node, input, $"{expression.Language} expressions do not run on the kernel; JavaScript and Liquid do.");
            if (!StringComparer.Ordinal.Equals(expression.CapabilityProfile, ExpressionCapabilityProfiles.BindingPureV1))
                throw NativeMappingRefusedException.Binding(node, input, $"The kernel evaluates expressions under {ExpressionCapabilityProfiles.BindingPureV1} only, not {expression.CapabilityProfile}.");
            var parameters = new List<Native.HostParameter>();
            foreach (var (name, parameter) in expression.Parameters)
            {
                if (expression.ParameterConversionPlans.TryGetValue(name, out var plan) && plan.Operation != ValueConversionOperation.Identity)
                    throw NativeMappingRefusedException.Binding(node, input, $"Parameter '{name}' converts its value ({plan.Operation}), which the kernel does not.");
                Native.Binding value = parameter switch
                {
                    LiteralExpressionParameterBinding literal => new Native.LiteralBinding(ToNode(literal.Value)),
                    WorkflowRequestExpressionParameterBinding request => RequestRead(request.MemberKey, request.Path, null, node, input, scope),
                    VariableExpressionParameterBinding variable => new Native.VariableBinding(VariableName(variable.VariableKey, variable.DeclaringScopeNodeId, node, input, scope)),
                    ActivityResultExpressionParameterBinding result => new Native.ResultBinding(result.ProducerNodeId, ResultPath(result.ProducerNodeId, result.ProjectionKey, null, node, input)),
                    WorkflowInstanceExpressionParameterBinding { Member: WorkflowInstanceExpressionParameterBinding.Members.InstanceId } => new Native.InstanceBinding(Native.InstanceField.Id),
                    WorkflowInstanceExpressionParameterBinding => new Native.InstanceBinding(Native.InstanceField.CorrelationId),
                    _ => throw NativeMappingRefusedException.Binding(node, input, $"Parameter '{name}' reads its value in a way the kernel does not support."),
                };
                parameters.Add(new Native.HostParameter(name, value));
            }
            var options = ToNode(expression.Options) as JsonObject;
            // The result type is required: the host's evaluator normalizes the result by the type the reading input expects.
            return new Native.HostExpressionBinding(expression.Language, expression.Expression, parameters, binding.TargetType.Alias, options is { Count: > 0 } ? options : null);
        }

        /// <summary>
        /// A read of the run's request: in the workflow, a member of the run's input; inside a GraphActivity's body, the boundary
        /// input whose reference key the member key is.
        /// </summary>
        private static Native.VariableBinding RequestRead(string member, string? legacyPath, IReadOnlyList<string>? segments, string node, string input, Scope scope)
        {
            if (legacyPath is not null)
                throw NativeMappingRefusedException.Binding(node, input, "A request read by a legacy path does not run on the kernel; select the member instead.");
            if (scope.Graph is { } graph)
            {
                if (!graph.Inputs.Contains(member))
                    throw NativeMappingRefusedException.Binding(node, input, $"GraphActivity '{graph.Node}' declares no input '{member}'.");
                return new Native.VariableBinding(member, Join(null, segments));
            }
            RequireSegment(member, node, input);
            return new Native.VariableBinding(InputVariable, Join(member, segments));
        }

        /// <summary>A variable's Native name. Only workflow variables, and inside a GraphActivity's body its own, are mapped.</summary>
        private string VariableName(string key, string declaringScope, string node, string input, Scope scope)
        {
            if (scope.Graph is { } graph)
            {
                if ((declaringScope == WorkflowScopeId || declaringScope == LegacyGraphScopeId) && graph.Variables.TryGetValue(key, out var local)) return local;
                throw NativeMappingRefusedException.Binding(node, input, $"Variable '{key}' in scope '{declaringScope}' is not a variable of GraphActivity '{graph.Node}'.");
            }
            // Container-scoped variables (a Sequence's or a Flowchart's own) are refused until their Native form is settled
            // (definitions-on-the-kernel.md, "Outside this path").
            if (declaringScope != WorkflowScopeId)
                throw NativeMappingRefusedException.Binding(node, input, $"Variable '{key}' is declared on container '{declaringScope}'; the kernel maps workflow variables only.");
            if (!_variables.TryGetValue(key, out var declaration))
                throw NativeMappingRefusedException.Binding(node, input, $"Variable '{key}' is not a workflow variable.");
            if (declaration.Policy.Lifecycle == DurableValueLifecycle.None)
                throw NativeMappingRefusedException.Binding(node, input, $"Variable '{declaration.Name}' is transient, which the kernel does not keep.");
            return declaration.Name;
        }

        /// <summary>The Native path of a producer's result projection, with the member segments a member read selects.</summary>
        private string ResultPath(string producer, string projection, IReadOnlyList<string>? segments, string node, string input)
        {
            if (!_types.TryGetValue(producer, out var type))
                throw NativeMappingRefusedException.Binding(node, input, $"The result of '{producer}', which is not a node of this workflow, is read.");
            var shape = NativeActivityShape.For(type);
            if (shape.Results.TryGetValue(projection, out var mapped))
            {
                if (mapped.Members is { } members && segments is { Count: > 0 })
                {
                    if (!members.TryGetValue(segments[0], out var first))
                        throw NativeMappingRefusedException.Binding(node, input, $"The {type.Name} result of '{producer}' has no member '{segments[0]}' on the kernel.");
                    segments = [first, .. segments.Skip(1)];
                }
                return Join(mapped.Path, segments) ?? "";
            }
            // A GraphActivity's result is its outputs, by reference key.
            if (type == typeof(Native.GraphActivity) && projection != WholeResult)
            {
                RequireSegment(projection, node, input);
                return Join(projection, segments)!;
            }
            throw NativeMappingRefusedException.Binding(node, input, $"The kernel's {type.Name} has no result '{projection}' (read from '{producer}').");
        }

        private static void RequireConversion(ValueConversionPlan? plan, string node, string input)
        {
            if (plan is not null && plan.Operation is not (ValueConversionOperation.Identity or ValueConversionOperation.NumericWidening))
                throw NativeMappingRefusedException.Binding(node, input, $"The kernel applies no {plan.Operation} conversion; only identity and numeric widening.");
        }

        private static IReadOnlyList<string>? Segments(RuntimeReadSelection? selection, string node, string input)
        {
            if (selection is null) return null;
            foreach (var segment in selection.Segments) RequireSegment(segment, node, input);
            return selection.Segments;
        }

        // A Native path splits on '.', so a member whose name holds a dot, or is empty, cannot be named in one.
        private static void RequireSegment(string segment, string node, string input)
        {
            if (segment.Length == 0 || segment.Contains('.', StringComparison.Ordinal))
                throw NativeMappingRefusedException.Binding(node, input, $"The member '{segment}' cannot be named in a kernel path, which splits on '.'.");
        }

        private static string? Join(string? head, IReadOnlyList<string>? segments)
        {
            var parts = (string.IsNullOrEmpty(head) ? [] : new[] { head }).Concat(segments ?? []).ToArray();
            return parts.Length == 0 ? null : string.Join('.', parts);
        }

        private static void RefuseContainerVariables(string node, IReadOnlyList<JsonElement>? variables)
        {
            if (variables is { Count: > 0 })
                throw NativeMappingRefusedException.Activity(node, "The container declares its own variables, which the kernel does not map yet.");
        }

        private static Native.Node Slotted(Native.Node node, string slot)
        {
            node.Slot = slot;
            return node;
        }

        private static T Read<T>(ExecutableNode node, string label, string kind, string version) =>
            ExecutableStructureReader.ReadStructure<T>(node, label, kind, version, (message, _) => NativeMappingRefusedException.Activity(node.ExecutableNodeId, message));

        private static JsonNode? ToNode(JsonElement value) => value.ValueKind is JsonValueKind.Undefined ? null : JsonNode.Parse(value.GetRawText());
    }

    /// <summary>A node's children across its slots, each to be placed by its structure exactly once.</summary>
    private sealed class Children
    {
        private readonly ExecutableNode _parent;
        private readonly Dictionary<string, ExecutableNode> _byId = new(StringComparer.Ordinal);
        private readonly HashSet<string> _placed = new(StringComparer.Ordinal);

        public Children(ExecutableNode parent)
        {
            _parent = parent;
            foreach (var child in parent.ChildSlots.SelectMany(slot => slot.Activities))
                if (!_byId.TryAdd(child.ExecutableNodeId, child))
                    throw NativeMappingRefusedException.Activity(parent.ExecutableNodeId, $"Child '{child.ExecutableNodeId}' is in two slots.");
        }

        public IEnumerable<string> InSlot(string slot) =>
            _parent.ChildSlots.Where(s => s.Name == slot).SelectMany(s => s.Activities).Select(child => child.ExecutableNodeId);

        public ExecutableNode Take(string id)
        {
            if (!_byId.TryGetValue(id, out var child))
                throw NativeMappingRefusedException.Activity(_parent.ExecutableNodeId, $"The structure names child '{id}', which no slot carries.");
            if (!_placed.Add(id))
                throw NativeMappingRefusedException.Activity(_parent.ExecutableNodeId, $"The structure places child '{id}' twice.");
            return child;
        }

        public void RequireAllPlaced()
        {
            if (_byId.Keys.FirstOrDefault(id => !_placed.Contains(id)) is { } stray)
                throw NativeMappingRefusedException.Activity(_parent.ExecutableNodeId, $"Child '{stray}' has no place in the kernel's {_parent.ActivityType} structure.");
        }
    }

    // ---- the compiled structure payloads, as the activity modules write them --------------------------------------

    private sealed record SequenceStructure(IReadOnlyList<string>? Activities, IReadOnlyList<JsonElement>? Variables);
    private sealed record IfStructure(string? Then, string? Else);
    private sealed record SwitchStructure(IReadOnlyList<SwitchCaseStructure>? Cases, string? Default);
    private sealed record SwitchCaseStructure(string Match, string? Activity, string? ConditionInputKey);
    private sealed record LoopStructure(string? Body);
    private sealed record ParallelStructure(IReadOnlyList<ParallelBranchStructure>? Branches, int? Threshold);
    private sealed record ParallelBranchStructure(string Name, string? Activity);
    private sealed record FlowchartStructure(IReadOnlyList<FlowchartConnectionStructure>? Connections, string? StartNodeId,
        IReadOnlyDictionary<string, FlowchartNodeStructure>? NodeMetadata, IReadOnlyList<JsonElement>? Variables);
    private sealed record FlowchartConnectionStructure(FlowchartEndpointStructure Source, FlowchartEndpointStructure Target);
    private sealed record FlowchartEndpointStructure(string NodeId, string? Port);
    private sealed record FlowchartNodeStructure(string? PolicyKind);

    private sealed record GraphDescriptor(
        string? EntryNodeId,
        string? EntryOccurrenceId,
        IReadOnlyList<GraphInputPayload>? Inputs,
        IReadOnlyList<GraphVariablePayload>? Variables,
        IReadOnlyList<GraphOutputPayload>? Outputs,
        IReadOnlyList<GraphOutputMappingPayload>? OutputMappings,
        IReadOnlyList<string>? RequiredInputReferenceKeys,
        IReadOnlyList<string>? RequiredOutputReferenceKeys,
        IReadOnlyList<GraphOutcomeMappingPayload>? OutcomeMappings,
        IReadOnlyList<GraphCompleteOutcomePayload>? CompleteOutcomes);
    private sealed record GraphInputPayload(string ReferenceKey, GraphExpressionPayload? Default);
    private sealed record GraphVariablePayload(string ReferenceKey, string Name, GraphExpressionPayload? InitialValue);
    private sealed record GraphOutputPayload(string ReferenceKey);
    private sealed record GraphOutputMappingPayload(string OutputReferenceKey, GraphExpressionPayload Source);
    private sealed record GraphOutcomeMappingPayload(string SourceOutcomeName, string BoundaryOutcomeName);
    private sealed record GraphCompleteOutcomePayload(string ReferenceKey, string BoundaryOutcomeName);
    private sealed record GraphExpressionPayload(string Syntax, JsonElement Value);
}
