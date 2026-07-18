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
