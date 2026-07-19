using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts;

/// <summary>
/// Activity-owned handler that lets generic design and publishing code project and compile
/// composite children without interpreting activity-specific structure payloads.
/// </summary>
public interface IActivityStructureHandler
{
    string Kind { get; }

    string SchemaVersion { get; }

    /// <summary>
    /// Whether this activity is a container scope that can own container-scoped variable
    /// declarations (ADR 0027). Container activities (e.g. <c>Sequence</c>, <c>Flowchart</c>)
    /// return <c>true</c>; ordinary activities use the default <c>false</c>.
    /// </summary>
    bool SupportsScopedVariables => false;

    IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity);

    /// <summary>
    /// Projects public-contract members referenced by structure-owned relationships such as
    /// Flowchart outcome ports. Values and expressions must never be included.
    /// </summary>
    IReadOnlyCollection<ActivityChildContractMemberUsage> ProjectChildContractMemberUsage(ActivityNode activity) => [];

    ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections);

    ActivityNodeStructure CompileExecutableStructure(ActivityNode activity);

    /// <summary>
    /// Projects the container-scoped variables declared by this activity node, if any. Container
    /// activities (e.g. <c>Sequence</c>, <c>Flowchart</c>) own variable declarations that are
    /// visible to their descendant activities. Non-container activities declare none.
    /// </summary>
    IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
}
