using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Services;

namespace Elsa.Workflows.Design.Validations.Validators;

/// <summary>
/// Rejects authored value flows whose producer or scope cannot be resolved before an executable is published, and
/// warns about writes to one variable whose order the definition does not fix (spec 266).
/// </summary>
public sealed class ValueFlowValidator(
    IActivityStructureService activityStructureService,
    IActivityDataSelectionAnalyzer sourceAnalyzer,
    IUnorderedVariableWriteAnalyzer? writeOrderAnalyzer = null) : IDraftValidator
{
    private readonly IUnorderedVariableWriteAnalyzer _writeOrderAnalyzer = writeOrderAnalyzer ?? new UnorderedVariableWriteAnalyzer();

    /// <summary>Preserves the legacy call shape; validation fails closed until shared authority is supplied.</summary>
    public ValueFlowValidator(IActivityStructureService activityStructureService) : this(activityStructureService, null!) { }

    private const string ParallelStructureKind = "elsa.parallel.structure";

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
        ValidateUnorderedWrites(draft.State.RootActivity, errors, cancellationToken);
        var analysis = await sourceAnalyzer.AnalyzeAsync(new ActivityDataSelectionDocument(
            "WorkflowDefinition", draft.Id, $"{draft.Id}/{draft.LastModifiedAt:O}", draft.State.RootActivity,
            [.. draft.State.Inputs], [.. draft.State.Variables], [.. draft.State.Outputs]), cancellationToken: cancellationToken);
        errors.AddRange(analysis.Findings);
        ValidateStableCollectionIdentities(graph, errors);
        return errors;
    }

    // Spec 266: declared writes to one variable whose order the definition does not fix are a warning, never a
    // refusal. The ordering knowledge comes only from the structures' declarations.
    private void ValidateUnorderedWrites(ActivityNode root, ICollection<ValidationError> errors, CancellationToken cancellationToken)
    {
        var result = _writeOrderAnalyzer.Analyze(root, activityStructureService, includeIntrinsics: true, cancellationToken);
        foreach (var finding in result.UnorderedWrites)
        {
            AddOnce(errors, new ValidationError(
                VariablePath(new VariableKey(finding.ScopeId, finding.ReferenceKey)),
                "ValueFlow/ConcurrentWrite",
                finding.Message) { Severity = ValidationSeverities.Warning });
        }

        foreach (var duplicate in result.DuplicateCaptureTargets)
        {
            AddOnce(errors, new ValidationError(
                $"{duplicate.NodeId}/outputs/{duplicate.OutputKeys[^1]}",
                "ValueFlow/DuplicateCaptureTarget",
                duplicate.Message) { Severity = ValidationSeverities.Warning });
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

    private static bool IsStructure(NodeInfo info, string kind) =>
        StringComparer.Ordinal.Equals(info.Node.Structure?.Kind, kind);

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
