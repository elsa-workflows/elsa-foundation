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

    /// <summary>
    /// The CLR model type of the authored structure payload for this kind/schema-version, used by
    /// the design API to publish a JSON Schema per structure kind so headless clients can author
    /// payloads without reverse-engineering them. <c>null</c> keeps the payload opaque/undocumented.
    /// </summary>
    Type? AuthoredPayloadType => null;

    IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity);

    /// <summary>
    /// Projects public-contract members referenced by structure-owned relationships such as
    /// Flowchart outcome ports. Values and expressions must never be included.
    /// </summary>
    IReadOnlyCollection<ActivityChildContractMemberUsage> ProjectChildContractMemberUsage(ActivityNode activity) => [];

    ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections);

    ActivityNodeStructure CompileExecutableStructure(ActivityNode activity);

    /// <summary>
    /// Rewrites the semantic child-node references in an already compiled structure after its
    /// authored children have received placement-specific executable identities.
    /// </summary>
    /// <remarks>
    /// Structure payloads are activity-owned. Implementations must rewrite only fields that
    /// semantically identify projected activity children; arbitrary JSON string replacement is
    /// unsafe because payloads may also contain unrelated identifiers and metadata.
    /// </remarks>
    ActivityNodeStructure RemapExecutableStructure(
        ActivityNodeStructure structure,
        IReadOnlyDictionary<string, string> authoredToExecutableNodeIds)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(authoredToExecutableNodeIds);
        if (authoredToExecutableNodeIds.Count == 0)
            return structure;

        throw new NotSupportedException($"Structure handler '{Kind}@{SchemaVersion}' does not support executable child-node remapping.");
    }

    /// <summary>
    /// Projects the container-scoped variables declared by this activity node, if any. Container
    /// activities (e.g. <c>Sequence</c>, <c>Flowchart</c>) own variable declarations that are
    /// visible to their descendant activities. Non-container activities declare none.
    /// </summary>
    IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
}
