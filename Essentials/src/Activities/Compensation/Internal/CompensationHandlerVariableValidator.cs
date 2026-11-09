using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using CompensationScopeActivity = Elsa.Activities.Compensation.Activities.CompensationScope;

namespace Elsa.Activities.Compensation.Internal;

/// <summary>
/// Draft validator for handed-over compensation handlers (spec 258 FR-307, research R6 and R18). A scope that completes
/// <c>Done</c> hands its entries to the nearest enclosing scope, which may run the handler later as its own child. By
/// then the containers between the two scopes have completed and closed their variable frames, so a handler that uses a
/// variable one of them declares would fail when it runs. This validator refuses such a handler.
/// </summary>
/// <remarks>
/// <para>
/// A scope can hand over when it has an enclosing scope and its <c>confirmOnComplete</c> input is not the literal
/// <c>true</c>. Entries travel on outward while each receiving scope can hand over too, so the containers at risk are every container between the scope and the outermost scope the entries can reach. Containers
/// inside the handler itself, containers above that outermost scope, workflow variables and <c>ActivityResult</c>
/// references stay available. A workflow Draft never contains a reusable activity's body, so every scope this walk sees
/// is on the same side of every reusable-activity boundary.
/// </para>
/// <para>
/// Only structured variable references are checked: an input or output bound with the <c>Variable</c> expression type
/// whose declaring scope is a container, and the variable target of a Set, Merge or Reduce node. Script expressions
/// (JavaScript, Liquid and others) and loop iteration values such as <c>currentItem</c> are read by name at run time and
/// are not analyzed; such a handler still fails when it runs, takes its retries and waits for an operator. A reference
/// to a container that is not visible from the handler at all is reported by the general variable validator instead.
/// Errors do not block saving the Draft; the promotion gate refuses to publish it.
/// </para>
/// </remarks>
internal sealed class CompensationHandlerVariableValidator(IActivityStructureService activityStructureService) : IDraftValidator
{
    public const string HandlerUsesContainerVariableType = "Compensation/HandlerUsesContainerVariable";

    private const string ConfirmOnCompleteInputKey = "confirmOnComplete";
    private const string LiteralExpressionType = "Literal";
    private const string VariableExpressionType = "Variable";

    public ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        var errors = new List<ValidationError>();
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (scope, path) in WalkScopes(draft.State.RootActivity))
        {
            if (ContainersAtRisk(scope, path) is not { Count: > 0 } containers)
                continue;

            var receiver = path[NearestScopeIndex(path, path.Count)].NodeId;
            foreach (var slot in activityStructureService.ProjectChildren(scope))
            {
                if (!slot.Name.StartsWith(CompensationScopeActivity.CompensationSlotPrefix, StringComparison.Ordinal))
                    continue;

                foreach (var node in WalkSubtree(slot.Activities))
                foreach (var (location, reference) in VariableReferences(node))
                {
                    if (reference.IsWorkflowScope ||
                        !containers.TryGetValue(reference.DeclaringScopeId!, out var declared) ||
                        declared.FirstOrDefault(variable => StringComparer.Ordinal.Equals(variable.ReferenceKey, reference.ReferenceKey)) is not { } variable)
                        continue;

                    var errorPath = $"{node.NodeId}/{location}";
                    if (!reported.Add(errorPath))
                        continue;

                    errors.Add(new ValidationError(errorPath, HandlerUsesContainerVariableType,
                        $"Compensation '{node.NodeId}' of scope '{scope.NodeId}' uses variable '{variable.Name}' of '{reference.DeclaringScopeId}'. " +
                        $"Scope '{scope.NodeId}' hands its work to '{receiver}', which may run this compensation after '{reference.DeclaringScopeId}' has completed; " +
                        "use a workflow variable or the step's result instead."));
                }
            }
        }

        return ValueTask.FromResult<IEnumerable<ValidationError>>(errors);
    }

    // The containers whose variables a handed-over handler of this scope may outlive, keyed by node id, with the
    // variables each declares. Empty when the scope keeps its entries.
    private Dictionary<string, IReadOnlyCollection<VariableDefinition>>? ContainersAtRisk(ActivityNode scope, IReadOnlyList<ActivityNode> path)
    {
        var receiverIndex = NearestScopeIndex(path, path.Count);
        if (receiverIndex < 0 || ConfirmsOnComplete(scope))
            return null;

        // A receiving scope hands the entry on when it can hand over itself. An entry it receives in its own handler is
        // undone in that recovery and never handed on; following it outward anyway reports nothing new, because that
        // handler belongs to the receiving scope and is checked against the same containers.
        var outermost = receiverIndex;
        while (!ConfirmsOnComplete(path[outermost]) && NearestScopeIndex(path, outermost) is var next and >= 0)
            outermost = next;

        var containers = new Dictionary<string, IReadOnlyCollection<VariableDefinition>>(StringComparer.Ordinal);
        for (var index = outermost + 1; index < path.Count; index++)
        {
            var declared = activityStructureService.ProjectScopedVariables(path[index]);
            if (declared.Count > 0)
                containers[path[index].NodeId] = declared;
        }

        return containers;
    }

    // The index of the nearest scope in the path above the given index, or -1.
    private static int NearestScopeIndex(IReadOnlyList<ActivityNode> path, int below)
    {
        for (var index = below - 1; index >= 0; index--)
            if (IsScope(path[index]))
                return index;
        return -1;
    }

    private static bool IsScope(ActivityNode node) =>
        StringComparer.Ordinal.Equals(node.Structure?.Kind, CompensationScopeActivity.StructureKind);

    // Only a literal true keeps the entries; an expression may evaluate to false, so the scope may hand over.
    private static bool ConfirmsOnComplete(ActivityNode scope)
    {
        var input = scope.Inputs.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.ReferenceKey, ConfirmOnCompleteInputKey));
        if (input?.Value is not { } value ||
            !(string.IsNullOrWhiteSpace(value.ExpressionType) || string.Equals(value.ExpressionType, LiteralExpressionType, StringComparison.OrdinalIgnoreCase)))
            return false;

        return value.Value switch
        {
            bool literal => literal,
            string text => bool.TryParse(text.Trim(), out var parsed) && parsed,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.String } element => bool.TryParse(element.GetString()?.Trim(), out var parsed) && parsed,
            _ => false
        };
    }

    // Structured references the node reads or writes: Variable-bound inputs and outputs, and an intrinsic's target.
    private static IEnumerable<(string Location, VariableReference Reference)> VariableReferences(ActivityNode node)
    {
        foreach (var (bag, arguments) in new[] { ("inputs", node.Inputs), ("outputs", node.Outputs) })
        foreach (var argument in arguments)
        {
            if (argument.Value is { } value &&
                string.Equals(value.ExpressionType, VariableExpressionType, StringComparison.OrdinalIgnoreCase) &&
                VariableReference.TryParse(value.Value, out var reference))
                yield return ($"{bag}/{argument.ReferenceKey}", reference!);
        }

        if (node.Intrinsic?.Variable is { } target)
            yield return ("variable", target);
    }

    // Iterative depth-first walk yielding each scope with its ancestors, root first. A reference-identity visited set
    // guards against cyclic data.
    private IEnumerable<(ActivityNode Scope, IReadOnlyList<ActivityNode> Path)> WalkScopes(ActivityNode? root)
    {
        if (root is null)
            yield break;

        var stack = new Stack<(ActivityNode Node, IReadOnlyList<ActivityNode> Path)>();
        stack.Push((root, []));
        var visited = new HashSet<ActivityNode>(ReferenceEqualityComparer.Instance);

        while (stack.Count > 0)
        {
            var (node, path) = stack.Pop();
            if (!visited.Add(node))
                continue;

            if (IsScope(node))
                yield return (node, path);

            IReadOnlyList<ActivityNode> childPath = [.. path, node];
            foreach (var child in activityStructureService.ProjectChildren(node).SelectMany(slot => slot.Activities))
                stack.Push((child, childPath));
        }
    }

    private IEnumerable<ActivityNode> WalkSubtree(IEnumerable<ActivityNode> roots)
    {
        var stack = new Stack<ActivityNode>(roots);
        var visited = new HashSet<ActivityNode>(ReferenceEqualityComparer.Instance);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node))
                continue;

            yield return node;

            foreach (var child in activityStructureService.ProjectChildren(node).SelectMany(slot => slot.Activities))
                stack.Push(child);
        }
    }
}
