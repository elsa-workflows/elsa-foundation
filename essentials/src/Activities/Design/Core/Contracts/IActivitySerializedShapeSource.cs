using System.Collections.Immutable;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Design.Core.Contracts;

/// <summary>Contributes bounded, metadata-only outgoing serialized shapes for admitted source contracts.</summary>
/// <remarks>
/// Callers own resource, catalog and disclosure authorization. A source must independently bind its
/// configured metadata to the exact admitted contract; a client alias is never CLR reflection authority.
/// Null means this contribution does not own the source. An owned opaque or missing contract returns
/// Unknown or Unavailable facts. The coordinator refuses multiple matching owners.
/// For a nonempty member path, the selected shape includes effective nullability, presence and protection
/// through every traversed prefix; subtree discovery does not discard a nullable or optional parent.
/// </remarks>
public interface IActivitySerializedShapeSource
{
    /// <summary>
    /// Describes the exact authorized catalog version's projection. The literal <c>$result</c> projection
    /// denotes its typed whole result and does not require a named OutputDefinition row.
    /// </summary>
    /// <param name="admittedVersion">An admitted immutable catalog version snapshot, including its identity and descriptor.</param>
    /// <param name="projectionKey">The whole-result key or exact declared output reference key.</param>
    /// <param name="memberPath">Literal serialized member names, including empty names and names containing dots.</param>
    /// <param name="limits">Bounds for the complete returned tree.</param>
    /// <param name="cancellationToken">Cancels metadata work.</param>
    ValueTask<ActivitySerializedShape?> DescribeResultAsync(
        IActivityDefinitionVersion admittedVersion,
        string projectionKey,
        ImmutableArray<string> memberPath,
        ActivitySerializedShapeLimits limits,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Describes a boundary/variable/owner-exposed item type only through actual activated design-time
    /// type-contract admission. Missing or ambiguous admission yields honest unknown facts; runtime
    /// alias registries, arbitrary type lookup and alias installation are not permitted substitutes.
    /// A selectable CLR alias alone does not establish a boundary value's serialized profile. An
    /// unbound profile returns Unknown, and a nonempty path cannot assert a selected member type.
    /// </summary>
    /// <param name="declaredType">The type resolved from the actual accepted source declaration, never a client expected-type claim.</param>
    /// <param name="ownerContractIdentity">Server-resolved identity of that declaration's exact document/contract revision.</param>
    /// <param name="memberPath">Literal serialized member names relative to the admitted root.</param>
    /// <param name="limits">Bounds for the complete returned tree.</param>
    /// <param name="cancellationToken">Cancels metadata work.</param>
    ValueTask<ActivitySerializedShape?> DescribeDeclaredTypeAsync(
        TypeReference declaredType,
        string ownerContractIdentity,
        ImmutableArray<string> memberPath,
        ActivitySerializedShapeLimits limits,
        CancellationToken cancellationToken = default);
}
