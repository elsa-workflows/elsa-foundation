using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Services;

/// <summary>
/// Publish-time checks of a definition's step writes (spec 1231 FR-024, FR-026, FR-029) and the facts the
/// concurrent-write rule counts (FR-028):
/// <list type="bullet">
/// <item>A variable-reference input must be bound, by a <c>Variable</c> expression, to a variable declared in a scope
/// visible to the node; a writable one must not be bound to a loop item or index (read-only).</item>
/// <item>A node's may-write set is its writable bound references; an activity type marked
/// <c>[AccessesUndeclaredVariables]</c> may write every visible variable.</item>
/// <item>A may-write set that overlaps one of the node's own output captures is a warning: the capture applies last.</item>
/// </list>
/// </summary>
internal static class StepVariableWriteValidation
{
    public const string ReferenceRuleId = "ValueFlow/VariableReference";
    public const string OverlapRuleId = "ValueFlow/StepWriteOverlapsCapture";

    public static async Task<(StepVariableWriteFacts Facts, IReadOnlyList<ValidationError> Errors)> AnalyzeAsync(
        ActivityNode root,
        IReadOnlyCollection<VariableDefinition> workflowVariables,
        IActivityStructureService structures,
        IActivityDefinitionLookup catalog,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(catalog);
        var errors = new List<ValidationError>();
        var byNode = new Dictionary<string, StepVariableWriters>(StringComparer.Ordinal);
        var versions = new Dictionary<string, IActivityDefinitionVersion?>(StringComparer.Ordinal);
        var workflowKeys = workflowVariables.Select(variable => variable.ReferenceKey).ToHashSet(StringComparer.Ordinal);

        var pending = new Stack<(ActivityNode Node, IReadOnlyList<ActivityNode> Ancestors)>();
        pending.Push((root, []));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (node, ancestors) = pending.Pop();
            var below = ancestors.Append(node).ToArray();
            foreach (var child in structures.ProjectChildren(node).SelectMany(slot => slot.Activities))
                pending.Push((child, below));

            if (node.Intrinsic is not null || string.IsNullOrWhiteSpace(node.ActivityVersionId) || string.IsNullOrWhiteSpace(node.NodeId))
                continue;
            if (!versions.TryGetValue(node.ActivityVersionId, out var version))
                versions[node.ActivityVersionId] = version = await catalog.FindVersion(node.ActivityVersionId, cancellationToken);
            if (version is null)
                continue;

            var everyVisible = VariableAccessDesignFacet.AccessesUndeclaredVariables(version.DesignFacets);
            var targets = new List<StepVariableWriteTarget>();
            foreach (var input in version.Inputs.Where(input => !string.IsNullOrWhiteSpace(input.VariableAccess)))
            {
                var writable = StringComparer.Ordinal.Equals(input.VariableAccess, InputVariableAccess.ReadWrite);
                var path = $"{node.NodeId}/inputs/{input.ReferenceKey}";
                var state = node.Inputs?.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.ReferenceKey, input.ReferenceKey));
                if (state is null || !state.IsBound())
                {
                    errors.Add(new ValidationError(path, ReferenceRuleId,
                        $"Input '{input.Name}' of node '{node.NodeId}' is a variable reference and must be bound to a declared variable."));
                    continue;
                }

                if (!StringComparer.OrdinalIgnoreCase.Equals(state.Value!.ExpressionType, InputVariableAccess.ExpressionType) ||
                    !VariableReference.TryParse(state.Value.Value, out var reference) || reference is null)
                {
                    errors.Add(new ValidationError(path, ReferenceRuleId,
                        $"Input '{input.Name}' of node '{node.NodeId}' is a variable reference and must be bound to a declared variable, not to a '{state.Value.ExpressionType}' expression."));
                    continue;
                }

                var scopeId = UnorderedVariableWriteAnalyzer.NormalizeScopeId(reference.DeclaringScopeId);
                switch (Classify(scopeId, reference.ReferenceKey, ancestors, workflowKeys, structures))
                {
                    case TargetKind.Undeclared:
                        errors.Add(new ValidationError(path, ReferenceRuleId,
                            $"Input '{input.Name}' of node '{node.NodeId}' is bound to variable '{reference.ReferenceKey}' in scope '{scopeId}', which is not declared in a scope visible to the node. A step never creates a variable."));
                        continue;
                    case TargetKind.ReadOnly when writable:
                        errors.Add(new ValidationError(path, ReferenceRuleId,
                            $"Input '{input.Name}' of node '{node.NodeId}' can write the variable it names, but '{reference.ReferenceKey}' in scope '{scopeId}' is a loop item or index, which is read-only. Use a ReadOnlyVariableReference, or bind a declared variable."));
                        continue;
                }

                if (writable && !targets.Any(target => target.ScopeId == scopeId && target.ReferenceKey == reference.ReferenceKey))
                    targets.Add(new StepVariableWriteTarget(scopeId, reference.ReferenceKey));
            }

            if (targets.Count == 0 && !everyVisible)
                continue;
            byNode[node.NodeId] = new StepVariableWriters(targets, everyVisible);
            AddOverlapWarnings(node, targets, everyVisible, errors);
        }

        return (new StepVariableWriteFacts(byNode, workflowKeys.ToArray()), errors);
    }

    // FR-029: a step that writes a variable in its session and also captures an output into it. Both commit; the capture
    // applies last and wins (ADR 0090 decision 6), which is rarely what the author meant.
    private static void AddOverlapWarnings(ActivityNode node, IReadOnlyCollection<StepVariableWriteTarget> targets, bool everyVisible, ICollection<ValidationError> errors)
    {
        foreach (var capture in UnorderedVariableWriteAnalyzer.DeclaredWrites(node, includeIntrinsics: false))
        {
            if (!everyVisible && !targets.Any(target => target.ScopeId == capture.ScopeId && target.ReferenceKey == capture.ReferenceKey))
                continue;
            errors.Add(new ValidationError(
                $"{node.NodeId}/outputs/{capture.OutputKey}",
                OverlapRuleId,
                $"Node '{node.NodeId}' may write variable '{capture.ReferenceKey}' in its step and also captures output '{capture.OutputKey}' into it. " +
                "Both commit with the step: the step's own write applies first and the capture last, so the capture wins. Write the variable in one place.")
            {
                Severity = ValidationSeverities.Warning
            });
        }
    }

    private static TargetKind Classify(
        string scopeId,
        string referenceKey,
        IReadOnlyList<ActivityNode> ancestors,
        IReadOnlySet<string> workflowKeys,
        IActivityStructureService structures)
    {
        if (StringComparer.Ordinal.Equals(scopeId, VariableReference.WorkflowScopeId))
            return workflowKeys.Contains(referenceKey) ? TargetKind.Declared : TargetKind.Undeclared;

        var owner = ancestors.LastOrDefault(ancestor => StringComparer.Ordinal.Equals(ancestor.NodeId, scopeId));
        if (owner is null)
            return TargetKind.Undeclared;
        // An enclosing scope's own declared variables are writable; any other value it exposes to its body is a loop
        // iteration value (an item or an index), which is read-only (ADR 0028).
        return structures.ProjectScopedVariables(owner).Any(variable => StringComparer.Ordinal.Equals(variable.ReferenceKey, referenceKey))
            ? TargetKind.Declared
            : TargetKind.ReadOnly;
    }

    private enum TargetKind
    {
        Declared,
        ReadOnly,
        Undeclared
    }
}
