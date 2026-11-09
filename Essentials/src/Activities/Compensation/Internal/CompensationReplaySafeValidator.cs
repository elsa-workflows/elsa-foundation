using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using CompensationScopeActivity = Elsa.Activities.Compensation.Activities.CompensationScope;

namespace Elsa.Activities.Compensation.Internal;

/// <summary>
/// Spec 250 FR-014 as spec 261 OD20 A narrows it: warns, without blocking publication, when a step that has a compensation
/// runs a <see cref="SideEffectProfile.ReplaySafe"/> leaf activity, or when a compensation handler is one. A replay-safe
/// activity's attempt is not committed before it runs, so after a crash recovery may not know that its effect happened,
/// or may run an undo again without a committed claim. Structural activities (any node with a structure, such as a
/// Sequence, a Flowchart or a nested scope) never warn, because their profile describes the container rather than the
/// work inside it; neither does a step without a compensation, which has nothing to undo, nor an activity whose profile
/// cannot be resolved. Without an <see cref="IActivitySideEffectProfileReader"/> (a host without Publishing) it reports
/// nothing.
/// </summary>
internal sealed class CompensationReplaySafeValidator(
    IActivityStructureService activityStructureService,
    IActivitySideEffectProfileReader? profileReader = null) : IDraftValidator
{
    public const string ReplaySafeStepType = "CompensationScope/ReplaySafeStep";
    public const string ReplaySafeHandlerType = "CompensationScope/ReplaySafeHandler";

    public async ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        if (profileReader is null)
            return [];

        var warnings = new List<ValidationError>();
        var profiles = new Dictionary<string, SideEffectProfile?>(StringComparer.Ordinal);
        foreach (var node in WalkTree(draft.State.RootActivity))
        {
            if (!StringComparer.Ordinal.Equals(node.Structure?.Kind, CompensationScopeActivity.StructureKind))
                continue;

            var steps = CompensationScopeAuthoredStructureReader.Read(node).Steps.ToArray();
            for (var index = 0; index < steps.Length; index++)
            {
                var step = steps[index];
                if (step.Compensation is null)
                    continue;

                var name = CompensationStepRules.Normalize(step.Name);
                if (await IsReplaySafeLeafAsync(step.Activity, profiles, cancellationToken))
                    warnings.Add(Warning(node, index, ReplaySafeStepType,
                        $"\"{name}\" is marked safe to repeat, so its call may not be committed before it runs; if the process stops, recovery may not know it happened. Mark it as an external effect unless it has none."));
                if (await IsReplaySafeLeafAsync(step.Compensation, profiles, cancellationToken))
                    warnings.Add(Warning(node, index, ReplaySafeHandlerType,
                        $"The compensation of \"{name}\" is marked safe to repeat, so an undo may run again after a restart without a committed claim. Mark it as an external effect unless it is idempotent and has no effect of its own."));
            }
        }

        return warnings;
    }

    // A leaf is a node with no structure and no engine intrinsic; its profile is read once per activity version.
    private async ValueTask<bool> IsReplaySafeLeafAsync(
        ActivityNode? node,
        Dictionary<string, SideEffectProfile?> profiles,
        CancellationToken cancellationToken)
    {
        if (node is null || node.Structure is not null || node.Intrinsic is not null || string.IsNullOrWhiteSpace(node.ActivityVersionId))
            return false;

        if (!profiles.TryGetValue(node.ActivityVersionId, out var profile))
            profiles[node.ActivityVersionId] = profile = await profileReader!.ReadAsync(node.ActivityVersionId, cancellationToken);
        return profile == SideEffectProfile.ReplaySafe;
    }

    // The scope's step row, as the scope validator reports it, so the designer highlights the row.
    private static ValidationError Warning(ActivityNode scope, int index, string type, string message) =>
        new($"{scope.NodeId}/steps/{index}", type, message) { Severity = ValidationSeverities.Warning };

    // Iterative depth-first walk over the activity tree; a reference-identity visited set guards against cyclic data.
    private IEnumerable<ActivityNode> WalkTree(ActivityNode? root)
    {
        if (root is null)
            yield break;

        var stack = new Stack<ActivityNode>();
        stack.Push(root);
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
