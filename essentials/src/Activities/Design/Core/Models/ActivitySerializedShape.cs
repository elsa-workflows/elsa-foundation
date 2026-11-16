using System.Collections.Immutable;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Design.Core.Models;

/// <summary>How much of the outgoing serialized contract the source can establish without executing code.</summary>
public enum ActivitySerializedShapeState
{
    Known,
    Unknown,
    Truncated,
    Unavailable
}

/// <summary>The serialized JSON shape, independent of its CLR alias and value representation.</summary>
public enum ActivitySerializedValueKind
{
    Unknown,
    Scalar,
    Object,
    Map,
    Collection
}

/// <summary>Member presence in the declared outgoing contract, not producer occurrence availability.</summary>
public enum ActivitySerializedPresence
{
    Unknown,
    Required,
    Optional
}

/// <summary>Immutable outgoing member facts. Serialized names are literal, case-sensitive JSON keys.</summary>
public sealed record ActivitySerializedMember(
    string SerializedName,
    string DisplayName,
    ActivitySerializedShape Shape);

/// <summary>
/// A bounded metadata-only view of an admitted outgoing value contract. Null facts mean unknown,
/// rather than non-nullable, unprotected, or absent. These facts never authorize a nested write.
/// </summary>
/// <remarks>
/// Revisions describe the queried source contract, activity-value profile, and actual metadata asset
/// independently. They never become fields in an existing catalog definition or its content hash.
/// CollectionKind remains on Type even when the JSON shape is Map or Collection.
/// </remarks>
public sealed record ActivitySerializedShape(
    ActivitySerializedShapeState State,
    ActivitySerializedValueKind Kind,
    TypeReference? Type,
    string SourceContractRevision,
    string ProfileRevision,
    string MetadataRevision)
{
    public bool? IsNullable { get; init; }

    public ActivitySerializedPresence Presence { get; init; } = ActivitySerializedPresence.Unknown;

    public ValueRepresentation? Representation { get; init; }

    public bool? IsSensitive { get; init; }

    public bool? IsCredential { get; init; }

    public ImmutableArray<ActivitySerializedMember> Members { get; init; } = [];

    public ActivitySerializedShape? Element { get; init; }

    public ActivitySerializedShape? MapValue { get; init; }

    /// <summary>A safe stable reason code, without paths, CLR implementation names or exception text.</summary>
    public string? ReasonCode { get; init; }
}

/// <summary>Bounds the complete returned tree, including collection elements and map values.</summary>
/// <remarks>
/// MaximumMembers is one global budget for the entire query, not a separate allowance for each node.
/// A literal member path is never split on dots or truncated to satisfy these response limits.
/// </remarks>
public sealed record ActivitySerializedShapeLimits(int MaximumDepth = 4, int MaximumMembers = 128);
