using Elsa.Expressions.Core.Constants;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Services;

/// <summary>
/// Rewrites scoped variable references when a container subtree is copied or imported (ADR 0027).
/// Given a map of old → new authored node identities for the nodes being copied, it reassigns each
/// node's identity and remaps every <em>internal</em> scoped reference (one whose declaring scope is
/// a node inside the copied subtree) to the copied node identity, while preserving reference keys.
/// References to scopes outside the copied subtree (external references) are left untouched — they
/// remain valid only when still visible from the new location, which design validation enforces
/// afterwards rather than this transform retargeting them by name.
/// </summary>
public sealed class ScopedVariableReferenceRemapper(IActivityStructureService structureService)
{
    /// <summary>
    /// Returns a copy of <paramref name="node"/> (and its subtree) with node identities and internal
    /// scoped references remapped per <paramref name="nodeIdRemap"/>.
    /// </summary>
    public ActivityNode Remap(ActivityNode node, IReadOnlyDictionary<string, string> nodeIdRemap)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(nodeIdRemap);

        var newNodeId = nodeIdRemap.TryGetValue(node.NodeId, out var mappedId) ? mappedId : node.NodeId;
        var remapped = node with
        {
            NodeId = newNodeId,
            Inputs = node.Inputs.Select(argument => RemapArgument(argument, nodeIdRemap)).ToArray(),
            Outputs = node.Outputs.Select(argument => RemapArgument(argument, nodeIdRemap)).ToArray()
        };

        var childProjections = structureService.ProjectChildren(remapped);
        if (childProjections.Count == 0 || childProjections.All(projection => !projection.Activities.Any()))
            return remapped;

        var remappedProjections = childProjections
            .Select(projection => new ActivityChildProjection(
                projection.Name,
                projection.Activities.Select(child => Remap(child, nodeIdRemap)).ToArray()))
            .ToArray();

        return structureService.ReplaceChildren(remapped, remappedProjections);
    }

    private static ArgumentState RemapArgument(ArgumentState argument, IReadOnlyDictionary<string, string> nodeIdRemap)
    {
        if (argument.Value.Value is null ||
            !string.Equals(argument.Value.ExpressionType, WellKnownExpressionDescriptorTypes.Variable, StringComparison.Ordinal))
            return argument;

        if (!VariableReference.TryParse(argument.Value.Value, out var reference) || reference is null || reference.IsWorkflowScope)
            return argument;

        // Only internal references (declaring scope inside the copied subtree) are remapped.
        if (reference.DeclaringScopeId is null || !nodeIdRemap.TryGetValue(reference.DeclaringScopeId, out var newScopeId))
            return argument;

        var remappedReference = reference with { DeclaringScopeId = newScopeId };
        return argument with { Value = argument.Value with { Value = remappedReference } };
    }
}
