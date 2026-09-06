namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

/// <summary>
/// Versioned, value-free snapshot of one Groundwork execution observation retained by the Elsa
/// diagnostics artifact. This is a consumer-owned persistence shape, not a replacement for the
/// provider-neutral Groundwork observation contract.
/// </summary>
public sealed record StructuredExecutionEvidence(
    int SchemaVersion,
    string Provider,
    string ProviderVersion,
    string Operation,
    string CommandKind,
    string Role,
    StructuredExecutionIdentity Identity,
    StructuredExecutionTarget Target,
    string Outcome,
    string? FailureCategory,
    string ShapeAvailability,
    StructuredBoundedQueryEvidence? BoundedQuery,
    StructuredPlanEvidence Plan)
{
    public StructuredPointReadEvidence? PointRead { get; init; }
}

/// <summary>Value-free key equality emitted for a point read, including opaque scope bindings.</summary>
public sealed record StructuredPointReadKeyBound(
    string? LogicalColumn,
    string ValueType,
    string BindingRole,
    Guid BindingId);

/// <summary>Only provider-observed uniqueness may carry enforced key columns.</summary>
public sealed record StructuredPointReadUniqueness(
    string Status,
    IReadOnlyList<string> EnforcedKeyColumns,
    bool IncludesScopeBinding);

/// <summary>Native bounds and materializer behavior are distinct facts, not interchangeable limits.</summary>
public sealed record StructuredPointReadEvidence(
    IReadOnlyList<StructuredPointReadKeyBound> KeyBounds,
    StructuredPointReadUniqueness Uniqueness,
    StructuredNativeBound NativeLimit,
    bool MaterializerReadsAtMostOne,
    string LockMode);

/// <summary>
/// Opaque capture-local command and statement identity retained for correlation. The capture boundary
/// associates a callback with its route; these identifiers and the artifact digest preserve that
/// recorded association across reloads, but do not authenticate the origin of an arbitrary artifact.
/// </summary>
public sealed record StructuredExecutionIdentity(
    Guid CaptureId,
    Guid InvocationId,
    Guid CommandId,
    Guid StatementId,
    int CommandOrdinal,
    int StatementOrdinal);

/// <summary>Logical target plus opaque physical target identity and scope binding mode.</summary>
public sealed record StructuredExecutionTarget(
    string LogicalUnitId,
    Guid PhysicalTargetId,
    string ScopeBinding);

/// <summary>One value-free fact in the complete conjunction emitted by the provider.</summary>
public sealed record StructuredPredicateFact(
    string LogicalColumn,
    string Operator,
    string ValueType,
    string Comparison,
    string BoundInclusivity,
    string BindingRole,
    Guid BindingId);

/// <summary>Closed conjunction shape; an omitted value means the whole shape is unavailable.</summary>
public sealed record StructuredConjunctionPredicate(
    IReadOnlyList<StructuredPredicateFact> Facts);

/// <summary>One logical ordering term and the transforms actually emitted by the provider.</summary>
public sealed record StructuredOrderTerm(
    string LogicalColumn,
    string Direction,
    string? NullPlacement,
    IReadOnlyList<string> Transforms,
    string Comparison);

/// <summary>Projection facts emitted by the actual provider renderer.</summary>
public sealed record StructuredProjection(
    bool AllColumns,
    IReadOnlyList<string> LogicalColumns);

/// <summary>Explicit, absent, or unknown native paging fact. Value is never a query value.</summary>
public sealed record StructuredNativeBound(
    string Kind,
    int? Value);

/// <summary>First-slice bounded-query facts retained for route admission.</summary>
public sealed record StructuredBoundedQueryEvidence(
    StructuredConjunctionPredicate Predicate,
    IReadOnlyList<StructuredOrderTerm> Ordering,
    StructuredProjection Projection,
    StructuredNativeBound NativeOffset,
    StructuredNativeBound NativeLimit,
    bool HasContinuation,
    bool HasLookahead,
    bool IncludesTotalCount);

/// <summary>One mapped native plan node, retaining only typed operators and opaque physical IDs.</summary>
public sealed record StructuredPlanNode(
    int Id,
    int? ParentId,
    string Operation,
    Guid? TargetId,
    Guid? IndexId,
    string? LogicalIndexName,
    bool? IsCovering,
    string? SortPurpose);

/// <summary>Independent plan availability/provenance and the mapped winning-plan facts.</summary>
public sealed record StructuredPlanEvidence(
    string Availability,
    string? Provenance,
    bool? ChoseExpectedIndex,
    string? ExpectedLogicalIndex,
    Guid? ChosenPhysicalIndexId,
    string? FailureCategory,
    int? CollectionCommandCount,
    IReadOnlyList<StructuredPlanNode>? Nodes);
