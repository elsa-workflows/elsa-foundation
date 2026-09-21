using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Validators;

/// <summary>
/// Rejects authored value flows whose producer, scope, or write ordering cannot be resolved
/// deterministically before an executable is published.
/// </summary>
public sealed class ValueFlowValidator(IActivityStructureService activityStructureService) : IDraftValidator
{
    private const string SequenceStructureKind = "elsa.sequence.structure";
    private const string ParallelStructureKind = "elsa.parallel.structure";
    private const string FlowchartStructureKind = "elsa.flowchart.structure";
    private const string ActivityResultExpressionType = "ActivityResult";

    public ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();

        if (draft.State.RootActivity is null)
            return ValueTask.FromResult<IEnumerable<ValidationError>>([]);

        var graph = ValueFlowGraph.Create(draft.State.RootActivity, activityStructureService);
        var errors = new List<ValidationError>();
        ValidateConcurrentWrites(graph, errors);
        ValidateResultBindings(graph, errors);
        ValidateStableCollectionIdentities(graph, errors);
        return ValueTask.FromResult<IEnumerable<ValidationError>>(errors);
    }

    private static void ValidateConcurrentWrites(ValueFlowGraph graph, ICollection<ValidationError> errors)
    {
        var writes = graph.Nodes
            .Select(info => (Info: info, Target: GetVariableWriteTarget(info.Node)))
            .Where(item => item.Target is not null)
            .GroupBy(
                item => new VariableKey(NormalizeScopeId(item.Target!.DeclaringScopeId), item.Target.ReferenceKey),
                item => item.Info);

        foreach (var group in writes)
        {
            var candidates = group.ToArray();
            if (!HasConcurrentPair(candidates, graph))
                continue;

            AddOnce(errors, new ValidationError(
                VariablePath(group.Key),
                "ValueFlow/ConcurrentWrite",
                $"Variable '{group.Key.ReferenceKey}' in scope '{group.Key.ScopeId}' has potentially concurrent writes. Collect branch contributions and apply one explicit deterministic merge or reduction after the join."));
        }
    }

    private static void ValidateResultBindings(ValueFlowGraph graph, ICollection<ValidationError> errors)
    {
        foreach (var consumer in graph.Nodes)
        {
            foreach (var input in consumer.Node.Inputs)
            {
                foreach (var reference in ReadActivityResultReferences(input.Value))
                {
                    var path = $"{consumer.Node.NodeId}/inputs/{input.ReferenceKey}";
                    if (!graph.ByNodeId.TryGetValue(reference.ProducerNodeId, out var producer))
                    {
                        if (!reference.IsOptional)
                        {
                            AddOnce(errors, new ValidationError(
                                path,
                                "ValueFlow/UnavailableProducer",
                                $"Required result producer '{reference.ProducerNodeId}' is not present in the workflow graph."));
                        }

                        continue;
                    }

                    if (CrossesOutOfScope(producer, consumer))
                    {
                        AddOnce(errors, new ValidationError(
                            path,
                            "ValueFlow/ScopeBoundary",
                            $"Result '{reference.ProducerNodeId}' crosses a structured-scope boundary without an explicit typed return, collection, merge, or reduction."));
                        continue;
                    }

                    if (RequiresExplicitReturn(producer) && !ContainsExplicitReturn(producer, graph))
                    {
                        AddOnce(errors, new ValidationError(
                            path,
                            "ValueFlow/ScopeBoundary",
                            $"Structured scope '{reference.ProducerNodeId}' is consumed as a result but declares no explicit typed return."));
                        continue;
                    }

                    if (IsCyclicBackEdgeLookup(producer, consumer, graph))
                    {
                        AddOnce(errors, new ValidationError(
                            path,
                            "ValueFlow/CyclicBackEdge",
                            $"Result '{reference.ProducerNodeId}' reaches '{consumer.Node.NodeId}' over a cyclic back-edge. Transfer iteration state explicitly instead of reading a latest node result."));
                        continue;
                    }

                    if (!reference.IsOptional && !IsDefinitelyAvailable(producer, consumer, graph))
                    {
                        AddOnce(errors, new ValidationError(
                            path,
                            "ValueFlow/UnavailableProducer",
                            $"Required result producer '{reference.ProducerNodeId}' is not causally available on every path to '{consumer.Node.NodeId}'."));
                    }
                }
            }
        }
    }

    private static void ValidateStableCollectionIdentities(ValueFlowGraph graph, ICollection<ValidationError> errors)
    {
        foreach (var parallel in graph.Nodes.Where(info => IsStructure(info, ParallelStructureKind)))
        {
            if (!TryGetArray(parallel.Node.Structure!.Payload, "branches", out var branches))
                continue;

            var names = new HashSet<string>(StringComparer.Ordinal);
            var ordinal = 0;
            foreach (var branch in branches.EnumerateArray())
            {
                ordinal++;
                var name = TryGetString(branch, "name");
                if (!string.IsNullOrWhiteSpace(name) && names.Add(name))
                    continue;

                var identity = string.IsNullOrWhiteSpace(name) ? $"#{ordinal}" : name;
                AddOnce(errors, new ValidationError(
                    $"{parallel.Node.NodeId}/structure/branches/{identity}",
                    "ValueFlow/UnstableCollectionIdentity",
                    "Parallel branches must have non-blank, unique stable identities so collected results cannot depend on completion order."));
            }
        }
    }

    private static bool HasConcurrentPair(IReadOnlyList<NodeInfo> writes, ValueFlowGraph graph)
    {
        for (var left = 0; left < writes.Count; left++)
        for (var right = left + 1; right < writes.Count; right++)
        {
            if (ArePotentiallyConcurrent(writes[left], writes[right], graph))
                return true;
        }

        return false;
    }

    private static bool ArePotentiallyConcurrent(NodeInfo left, NodeInfo right, ValueFlowGraph graph)
    {
        var common = FindNearestCommonContainer(left, right);
        if (common is null)
            return false;

        var leftChild = ChildBelow(left, common);
        var rightChild = ChildBelow(right, common);
        if (ReferenceEquals(leftChild, rightChild))
            return false;

        if (IsStructure(common, ParallelStructureKind))
            return true;

        if (!IsStructure(common, FlowchartStructureKind))
            return false;

        var flow = graph.Flowchart(common);
        return flow is not null &&
               !flow.IsReachable(leftChild.Node.NodeId, rightChild.Node.NodeId) &&
               !flow.IsReachable(rightChild.Node.NodeId, leftChild.Node.NodeId);
    }

    private static bool CrossesOutOfScope(NodeInfo producer, NodeInfo consumer) =>
        producer.Parent is not null &&
        consumer.Parent is not null &&
        !ReferenceEquals(producer.Parent, consumer.Parent) &&
        !IsAncestorOrSelf(producer.Parent, consumer.Parent);

    private static bool RequiresExplicitReturn(NodeInfo producer) =>
        IsStructure(producer, SequenceStructureKind) ||
        IsStructure(producer, ParallelStructureKind) ||
        IsStructure(producer, FlowchartStructureKind);

    private static bool ContainsExplicitReturn(NodeInfo producer, ValueFlowGraph graph) =>
        graph.Nodes.Any(candidate => ReferenceEquals(candidate.Parent, producer) && IsReturn(candidate.Node));

    private static bool IsCyclicBackEdgeLookup(NodeInfo producer, NodeInfo consumer, ValueFlowGraph graph)
    {
        var flowchart = FindNearestCommonContainer(producer, consumer, FlowchartStructureKind);
        if (flowchart is null)
            return false;

        var flow = graph.Flowchart(flowchart);
        if (flow is null)
            return false;

        var producerChild = ChildBelow(producer, flowchart).Node.NodeId;
        var consumerChild = ChildBelow(consumer, flowchart).Node.NodeId;
        if (StringComparer.Ordinal.Equals(producerChild, consumerChild))
            return flow.IsReachable(producerChild, producerChild, requireEdge: true);

        return flow.IsReachable(producerChild, consumerChild) &&
               flow.IsReachable(consumerChild, producerChild) &&
               !flow.Dominates(producerChild, consumerChild);
    }

    private static bool IsDefinitelyAvailable(NodeInfo producer, NodeInfo consumer, ValueFlowGraph graph)
    {
        if (ReferenceEquals(producer, consumer))
            return false;

        var common = FindNearestCommonContainer(producer, consumer);
        if (common is null)
            return false;

        var producerChild = ChildBelow(producer, common);
        var consumerChild = ChildBelow(consumer, common);
        if (ReferenceEquals(producerChild, consumerChild))
            return false;

        if (IsStructure(common, SequenceStructureKind))
            return producerChild.Order < consumerChild.Order;

        if (IsStructure(common, FlowchartStructureKind))
        {
            var flow = graph.Flowchart(common);
            return flow is not null && flow.Dominates(producerChild.Node.NodeId, consumerChild.Node.NodeId);
        }

        return false;
    }

    private static IEnumerable<ActivityResultReference> ReadActivityResultReferences(ArgumentValue argument)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(argument.ExpressionType, ActivityResultExpressionType) &&
            TryReadActivityResult(argument.Value, out var direct))
            yield return direct;

        if (argument.Value is ExpressionDefinition definition)
        {
            foreach (var binding in definition.Parameters.Values.OfType<ActivityResultExpressionParameterBinding>())
                yield return new ActivityResultReference(binding.ProducerNodeId, binding.IsOptional);
            yield break;
        }

        if (!TryAsJsonObject(argument.Value, out var json) || !json.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var parameter in parameters.EnumerateObject())
        {
            var binding = parameter.Value;
            if (!StringComparer.OrdinalIgnoreCase.Equals(TryGetString(binding, "kind"), ActivityResultExpressionType))
                continue;
            if (TryReadActivityResult(binding, out var expressionReference))
                yield return expressionReference;
        }
    }

    private static bool TryReadActivityResult(object? value, out ActivityResultReference reference)
    {
        reference = default;
        if (!TryAsJsonObject(value, out var json))
            return false;

        var producerNodeId = TryGetString(json, "producerNodeId");
        if (string.IsNullOrWhiteSpace(producerNodeId))
            return false;

        var isOptional = json.TryGetProperty("isOptional", out var optional) && optional.ValueKind == JsonValueKind.True;
        reference = new ActivityResultReference(producerNodeId, isOptional);
        return true;
    }

    private static VariableReference? GetVariableWriteTarget(ActivityNode node)
    {
        if (node.Intrinsic?.Kind is AuthoredWorkflowIntrinsicKind.Set or
            AuthoredWorkflowIntrinsicKind.Merge or
            AuthoredWorkflowIntrinsicKind.Reduce)
            return node.Intrinsic.Variable;

        if (!StringComparer.Ordinal.Equals(node.ActivityVersionId, "elsa.intrinsic.set@1"))
            return null;

        var variable = node.Inputs.FirstOrDefault(input => StringComparer.Ordinal.Equals(input.ReferenceKey, "variable"));
        return variable is not null && VariableReference.TryParse(variable.Value.Value, out var target) ? target : null;
    }

    private static bool IsReturn(ActivityNode node) =>
        node.Intrinsic?.Kind == AuthoredWorkflowIntrinsicKind.Return ||
        StringComparer.Ordinal.Equals(node.ActivityVersionId, "elsa.intrinsic.return@1");

    private static NodeInfo? FindNearestCommonContainer(NodeInfo left, NodeInfo right, string? requiredKind = null)
    {
        var leftAncestors = new HashSet<NodeInfo>();
        for (var current = left.Parent; current is not null; current = current.Parent)
        {
            if (requiredKind is null || IsStructure(current, requiredKind))
                leftAncestors.Add(current);
        }

        for (var current = right.Parent; current is not null; current = current.Parent)
        {
            if (leftAncestors.Contains(current) && (requiredKind is null || IsStructure(current, requiredKind)))
                return current;
        }

        return null;
    }

    private static NodeInfo ChildBelow(NodeInfo node, NodeInfo ancestor)
    {
        var current = node;
        while (current.Parent is not null && !ReferenceEquals(current.Parent, ancestor))
            current = current.Parent;
        return current;
    }

    private static bool IsAncestorOrSelf(NodeInfo ancestor, NodeInfo node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    private static bool IsStructure(NodeInfo info, string kind) =>
        StringComparer.Ordinal.Equals(info.Node.Structure?.Kind, kind);

    private static string NormalizeScopeId(string? scopeId) =>
        string.IsNullOrWhiteSpace(scopeId) ||
        StringComparer.Ordinal.Equals(scopeId, VariableReference.WorkflowScopeId) ||
        StringComparer.Ordinal.Equals(scopeId, "$workflow")
            ? VariableReference.WorkflowScopeId
            : scopeId;

    private static string VariablePath(VariableKey key) =>
        StringComparer.Ordinal.Equals(key.ScopeId, VariableReference.WorkflowScopeId)
            ? $"$workflow/variables/{key.ReferenceKey}"
            : $"{key.ScopeId}/variables/{key.ReferenceKey}";

    private static void AddOnce(ICollection<ValidationError> errors, ValidationError error)
    {
        if (!errors.Any(existing => StringComparer.Ordinal.Equals(existing.Path, error.Path) && StringComparer.Ordinal.Equals(existing.Type, error.Type)))
            errors.Add(error);
    }

    private static bool TryAsJsonObject(object? value, out JsonElement json)
    {
        if (value is JsonElement element)
        {
            json = element;
            return json.ValueKind == JsonValueKind.Object;
        }

        try
        {
            json = JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object));
            return json.ValueKind == JsonValueKind.Object;
        }
        catch (NotSupportedException)
        {
            json = default;
            return false;
        }
    }

    private static bool TryGetArray(JsonElement value, string propertyName, out JsonElement array)
    {
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(propertyName, out array) && array.ValueKind == JsonValueKind.Array)
            return true;
        array = default;
        return false;
    }

    private static string? TryGetString(JsonElement value, string propertyName) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private sealed class ValueFlowGraph
    {
        private readonly IActivityStructureService _structureService;
        private readonly Dictionary<NodeInfo, FlowchartControlFlow?> _flowcharts = [];
        private readonly List<NodeInfo> _nodes = [];
        private readonly Dictionary<string, NodeInfo> _byNodeId = new(StringComparer.Ordinal);

        private ValueFlowGraph(IActivityStructureService structureService) => _structureService = structureService;

        public IReadOnlyList<NodeInfo> Nodes => _nodes;
        public IReadOnlyDictionary<string, NodeInfo> ByNodeId => _byNodeId;

        public static ValueFlowGraph Create(ActivityNode root, IActivityStructureService structureService)
        {
            var graph = new ValueFlowGraph(structureService);
            graph.Add(root);
            return graph;
        }

        public FlowchartControlFlow? Flowchart(NodeInfo container)
        {
            if (_flowcharts.TryGetValue(container, out var cached))
                return cached;

            var flow = FlowchartControlFlow.TryCreate(container.Node.Structure?.Payload);
            _flowcharts[container] = flow;
            return flow;
        }

        private void Add(ActivityNode root)
        {
            var pending = new Stack<(ActivityNode Node, NodeInfo? Parent, int Order)>();
            pending.Push((root, null, 0));
            while (pending.Count > 0)
            {
                var (node, parent, order) = pending.Pop();
                var info = new NodeInfo(node, parent, order);
                _nodes.Add(info);
                if (!string.IsNullOrWhiteSpace(node.NodeId))
                    _byNodeId.TryAdd(node.NodeId, info);

                var children = _structureService.ProjectChildren(node)
                    .SelectMany(slot => slot.Activities)
                    .Select((child, childOrder) => (Child: child, Order: childOrder))
                    .ToArray();
                foreach (var child in children.Reverse())
                    pending.Push((child.Child, info, child.Order));
            }
        }
    }

    private sealed record NodeInfo(ActivityNode Node, NodeInfo? Parent, int Order);
    private readonly record struct VariableKey(string ScopeId, string ReferenceKey);
    private readonly record struct ActivityResultReference(string ProducerNodeId, bool IsOptional);

    private sealed class FlowchartControlFlow(
        string startNodeId,
        IReadOnlyCollection<string> nodeIds,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> successors,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> predecessors)
    {
        public bool IsReachable(string source, string target, bool requireEdge = false)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>();
            pending.Push(source);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (!visited.Add(current))
                    continue;

                foreach (var next in successors.GetValueOrDefault(current, []))
                {
                    if (StringComparer.Ordinal.Equals(next, target) && (!StringComparer.Ordinal.Equals(source, target) || requireEdge))
                        return true;
                    pending.Push(next);
                }
            }

            return !requireEdge && StringComparer.Ordinal.Equals(source, target);
        }

        public bool Dominates(string candidate, string node)
        {
            var reachable = nodeIds.Where(id => IsReachable(startNodeId, id)).ToHashSet(StringComparer.Ordinal);
            if (!reachable.Contains(candidate) || !reachable.Contains(node))
                return false;

            var dominators = reachable.ToDictionary(
                id => id,
                id => StringComparer.Ordinal.Equals(id, startNodeId)
                    ? new HashSet<string>([startNodeId], StringComparer.Ordinal)
                    : new HashSet<string>(reachable, StringComparer.Ordinal),
                StringComparer.Ordinal);

            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var current in reachable.Where(id => !StringComparer.Ordinal.Equals(id, startNodeId)))
                {
                    var incoming = predecessors.GetValueOrDefault(current, []).Where(reachable.Contains).ToArray();
                    var next = incoming.Length == 0
                        ? []
                        : new HashSet<string>(dominators[incoming[0]], StringComparer.Ordinal);
                    foreach (var predecessor in incoming.Skip(1))
                        next.IntersectWith(dominators[predecessor]);
                    next.Add(current);
                    if (dominators[current].SetEquals(next))
                        continue;
                    dominators[current] = next;
                    changed = true;
                }
            }

            return dominators[node].Contains(candidate);
        }

        public static FlowchartControlFlow? TryCreate(JsonElement? payload)
        {
            if (payload is not { ValueKind: JsonValueKind.Object } value ||
                !TryGetArray(value, "connections", out var connections))
                return null;

            var startNodeId = TryGetString(value, "startNodeId");
            if (string.IsNullOrWhiteSpace(startNodeId))
                return null;

            var edges = new List<(string Source, string Target)>();
            foreach (var connection in connections.EnumerateArray())
            {
                if (!connection.TryGetProperty("source", out var source) ||
                    !connection.TryGetProperty("target", out var target))
                    continue;
                var sourceId = TryGetString(source, "nodeId");
                var targetId = TryGetString(target, "nodeId");
                if (!string.IsNullOrWhiteSpace(sourceId) && !string.IsNullOrWhiteSpace(targetId))
                    edges.Add((sourceId, targetId));
            }

            var nodeIds = edges.SelectMany(edge => new[] { edge.Source, edge.Target }).Append(startNodeId).ToHashSet(StringComparer.Ordinal);
            var successors = nodeIds.ToDictionary(
                id => id,
                id => (IReadOnlyCollection<string>)edges.Where(edge => StringComparer.Ordinal.Equals(edge.Source, id)).Select(edge => edge.Target).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
            var predecessors = nodeIds.ToDictionary(
                id => id,
                id => (IReadOnlyCollection<string>)edges.Where(edge => StringComparer.Ordinal.Equals(edge.Target, id)).Select(edge => edge.Source).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
            return new FlowchartControlFlow(startNodeId, nodeIds, successors, predecessors);
        }
    }
}
