using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts;

public interface IActivityStructureService
{
    IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity);

    /// <summary>
    /// Projects public-contract members referenced by structure-owned relationships.
    /// </summary>
    IReadOnlyCollection<ActivityChildContractMemberUsage> ProjectChildContractMemberUsage(ActivityNode activity) => [];

    ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections);

    ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity);

    /// <summary>
    /// Rewrites activity-owned references in a compiled structure from authored child node IDs to
    /// their placement-specific executable node IDs.
    /// </summary>
    ActivityNodeStructure RemapExecutableStructure(
        ActivityNodeStructure structure,
        IReadOnlyDictionary<string, string> authoredToExecutableNodeIds)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(authoredToExecutableNodeIds);
        if (authoredToExecutableNodeIds.Count == 0)
            return structure;

        throw new NotSupportedException("The activity structure service does not support executable child-node remapping.");
    }

    /// <summary>
    /// Whether a structure handler is registered for <paramref name="structure"/>'s kind and schema
    /// version. Lets publish-time guards distinguish a legitimately opaque structure (spec 071
    /// FR-008: unknown kinds are copied as opaque non-composite structure) from a composite
    /// structure whose owning feature is missing from the shell. Defaults to <see langword="true"/>
    /// (assume handled) so custom/stub implementations that always project their children never
    /// trip the guard.
    /// </summary>
    bool HasHandler(ActivityNodeStructure structure) => true;

    /// <summary>
    /// Projects the container-scoped variables declared by <paramref name="activity"/>. Returns an
    /// empty collection when the node has no structure handler or declares no container variables.
    /// </summary>
    IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity);

    /// <summary>
    /// Whether <paramref name="activity"/> is a container scope that can own container-scoped
    /// variable declarations (ADR 0027). Used by authoring tooling to discover which activities can
    /// declare container variables.
    /// </summary>
    bool SupportsScopedVariables(ActivityNode activity);
}
