using System.Text.Json;
using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using CompensationScopeActivity = Elsa.Activities.Compensation.Activities.CompensationScope;

namespace Elsa.Activities.Compensation.Internal;

/// <summary>
/// Draft validator for Confirm and Compensate (spec 258 FR-315): each needs an enclosing compensation scope, a named
/// target must be one of its enclosing scopes, and neither may sit in a scope's compensation handler, where the scope
/// ignores it. A workflow Draft never contains a reusable activity's body, so every scope this walk sees is on the
/// same side of every reusable-activity boundary; inside a reusable body the runtime refuses an unmatched target with
/// <c>runtime.ancestor-notification.target-not-found</c>. Errors do not block saving the Draft; the promotion gate
/// refuses to publish it.
/// </summary>
internal sealed class CompensationTargetValidator(
    IActivityStructureService activityStructureService,
    IActivityDefinitionLookup activityDefinitionLookup) : IDraftValidator
{
    public const string NoEnclosingScopeType = "Compensation/NoEnclosingScope";
    public const string TargetNotEnclosingType = "Compensation/TargetNotEnclosing";
    public const string InsideHandlerType = "Compensation/InsideHandler";

    private const string ScopeInputKey = "scope";
    private const string LiteralExpressionType = "Literal";

    private static readonly string ConfirmDefinitionId = ActivityCatalogStableIds.DefinitionId(typeof(Confirm).FullName!)!;
    private static readonly string CompensateDefinitionId = ActivityCatalogStableIds.DefinitionId(typeof(Compensate).FullName!)!;

    public async ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        var errors = new List<ValidationError>();
        foreach (var (node, scopes) in WalkTree(draft.State.RootActivity))
        {
            if (string.IsNullOrWhiteSpace(node.ActivityVersionId))
                continue;

            var version = await activityDefinitionLookup.FindVersion(node.ActivityVersionId, cancellationToken);
            var name = version?.DefinitionId switch
            {
                var id when StringComparer.Ordinal.Equals(id, ConfirmDefinitionId) => "Confirm",
                var id when StringComparer.Ordinal.Equals(id, CompensateDefinitionId) => "Compensate",
                _ => null
            };
            if (name is null)
                continue;

            if (scopes.Count == 0)
            {
                errors.Add(new ValidationError(node.NodeId, NoEnclosingScopeType,
                    $"{name} '{node.NodeId}' needs an enclosing compensation scope."));
                continue;
            }

            if (scopes[^1].InHandler)
                errors.Add(new ValidationError(node.NodeId, InsideHandlerType,
                    $"{name} '{node.NodeId}' sits in a compensation handler of scope '{scopes[^1].NodeId}', which ignores it during recovery."));

            if (LiteralTarget(node) is not { } target)
                continue;

            var named = scopes.LastOrDefault(scope => StringComparer.Ordinal.Equals(scope.NodeId, target));
            if (named is null)
                errors.Add(new ValidationError(node.NodeId, TargetNotEnclosingType,
                    $"{name} '{node.NodeId}' names scope '{target}', which does not enclose it."));
            else if (named.InHandler && !ReferenceEquals(named, scopes[^1]))
                errors.Add(new ValidationError(node.NodeId, InsideHandlerType,
                    $"{name} '{node.NodeId}' sits in a compensation handler of scope '{target}', which ignores it during recovery."));
        }

        return errors;
    }

    // The target when it is a literal; an expression is resolved at run time, where an unmatched target is refused.
    private static string? LiteralTarget(ActivityNode node)
    {
        var input = node.Inputs.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.ReferenceKey, ScopeInputKey));
        if (input?.Value is not { } value ||
            !(string.IsNullOrWhiteSpace(value.ExpressionType) || string.Equals(value.ExpressionType, LiteralExpressionType, StringComparison.OrdinalIgnoreCase)))
            return null;

        var text = value.Value switch
        {
            string literal => literal,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null
        };
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private sealed record EnclosingScope(string NodeId, bool InHandler);

    // Iterative depth-first walk that carries the enclosing scopes of each node, nearest last, and whether the node
    // sits in that scope's step or its compensation handler. A reference-identity visited set guards against cyclic data.
    private IEnumerable<(ActivityNode Node, IReadOnlyList<EnclosingScope> Scopes)> WalkTree(ActivityNode? root)
    {
        if (root is null)
            yield break;

        var stack = new Stack<(ActivityNode Node, IReadOnlyList<EnclosingScope> Scopes)>();
        stack.Push((root, []));
        var visited = new HashSet<ActivityNode>(ReferenceEqualityComparer.Instance);

        while (stack.Count > 0)
        {
            var (node, scopes) = stack.Pop();
            if (!visited.Add(node))
                continue;

            yield return (node, scopes);

            var isScope = StringComparer.Ordinal.Equals(node.Structure?.Kind, CompensationScopeActivity.StructureKind);
            foreach (var slot in activityStructureService.ProjectChildren(node))
            {
                IReadOnlyList<EnclosingScope> childScopes = isScope
                    ? [.. scopes, new EnclosingScope(node.NodeId, slot.Name.StartsWith(CompensationScopeActivity.CompensationSlotPrefix, StringComparison.Ordinal))]
                    : scopes;
                foreach (var child in slot.Activities)
                    stack.Push((child, childScopes));
            }
        }
    }
}
