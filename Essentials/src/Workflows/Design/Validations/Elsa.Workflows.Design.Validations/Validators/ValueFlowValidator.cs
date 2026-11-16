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
public sealed class ValueFlowValidator(
    IActivityStructureService activityStructureService,
    IActivityDataSelectionAnalyzer sourceAnalyzer) : IDraftValidator
{
    /// <summary>Preserves the legacy call shape; validation fails closed until shared authority is supplied.</summary>
    public ValueFlowValidator(IActivityStructureService activityStructureService) : this(activityStructureService, null!) { }

    private const string ParallelStructureKind = "elsa.parallel.structure";
    private const string FlowchartStructureKind = "elsa.flowchart.structure";

    public async ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();
        if (sourceAnalyzer is null)
            throw new InvalidOperationException("Value-flow validation requires the shared activity data selection analyzer.");

        if (draft.State.RootActivity is null)
            return [];

        var graph = ValueFlowGraph.Create(draft.State.RootActivity, activityStructureService);
        var errors = new List<ValidationError>();
        ValidateConcurrentWrites(graph, errors);
        var analysis = await sourceAnalyzer.AnalyzeAsync(new ActivityDataSelectionDocument(
            "WorkflowDefinition", draft.Id, $"{draft.Id}/{draft.LastModifiedAt:O}", draft.State.RootActivity,
            [.. draft.State.Inputs], [.. draft.State.Variables], [.. draft.State.Outputs]), cancellationToken: cancellationToken);
        errors.AddRange(analysis.Findings);
        ValidateStableCollectionIdentities(graph, errors);
        return errors;
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

        var forward = graph.AssessChildResult(common, leftChild.Node.NodeId, rightChild.Node.NodeId);
        var reverse = graph.AssessChildResult(common, rightChild.Node.NodeId, leftChild.Node.NodeId);
        if (forward.MayRunConcurrently == true || reverse.MayRunConcurrently == true)
            return true;
        if (forward.MayRunConcurrently == false || reverse.MayRunConcurrently == false)
            return false;

        // Legacy owners may supply only completion order. Unknown facts cannot certify two
        // colliding writes as safe; explicit ordering or exclusion can establish that safety.
        return forward.Availability != ActivityChildResultAvailability.Definite &&
               reverse.Availability != ActivityChildResultAvailability.Definite &&
               forward.AreMutuallyExclusive != true && reverse.AreMutuallyExclusive != true;
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
        private readonly Dictionary<(NodeInfo Container, string Producer, string Consumer), ActivityChildResultFacts> _childResultFacts = [];
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

        public ActivityChildResultFacts AssessChildResult(NodeInfo container, string producerChildNodeId, string consumerChildNodeId)
        {
            var key = (container, producerChildNodeId, consumerChildNodeId);
            if (_childResultFacts.TryGetValue(key, out var cached))
                return cached;

            var facts = _structureService.AssessChildResult(container.Node, producerChildNodeId, consumerChildNodeId);
            _childResultFacts[key] = facts;
            return facts;
        }

        private void Add(ActivityNode root)
        {
            var pending = new Stack<(ActivityNode Node, NodeInfo? Parent, int Order)>();
            pending.Push((root, null, 0));
            while (pending.Count > 0)
            {
                var (node, parent, order) = pending.Pop();
                var effective = node with { Inputs = _structureService.ProjectInputs(node).ApplyToInputs(node.Inputs) };
                var info = new NodeInfo(effective, parent, order);
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
}
