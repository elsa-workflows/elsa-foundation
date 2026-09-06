using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Groundwork.Kernel;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Maps the immutable Groundwork callback into the benchmark's versioned artifact shape. The mapper
/// deliberately accepts only the callback: it never sees a query request or native command text.
/// </summary>
internal static class StructuredEvidenceMapper
{
    internal static StructuredExecutionEvidence Map(ProviderExecutionEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        return new(
            1,
            evidence.Provider.Name,
            evidence.Provider.Version,
            evidence.Operation.ToString(),
            evidence.CommandKind.ToString(),
            evidence.Role.ToString(),
            Map(evidence.Identity),
            Map(evidence.Target),
            evidence.Outcome.ToString(),
            evidence.FailureCategory?.ToString(),
            evidence.ShapeAvailability.ToString(),
            evidence.BoundedQuery is null ? null : Map(evidence.BoundedQuery),
            Map(evidence.Plan))
        {
            PointRead = evidence.PointRead is null ? null : Map(evidence.PointRead)
        };
    }

    private static StructuredPointReadEvidence Map(ProviderPointReadEvidence point) => new(
        point.KeyBounds.Select(bound => new StructuredPointReadKeyBound(
            bound.LogicalColumn,
            bound.ValueType.ToString(),
            bound.BindingRole.ToString(),
            bound.BindingId.Value)).ToArray(),
        new(point.Uniqueness.Status.ToString(),
            point.Uniqueness.EnforcedKeyColumns.ToArray(),
            point.Uniqueness.IncludesScopeBinding),
        Map(point.NativeLimit),
        point.MaterializerReadsAtMostOne,
        point.LockMode.ToString());

    private static StructuredExecutionIdentity Map(ProviderExecutionIdentity identity) => new(
        identity.CaptureId.Value,
        identity.InvocationId.Value,
        identity.CommandId.Value,
        identity.StatementId.Value,
        identity.CommandOrdinal,
        identity.StatementOrdinal);

    private static StructuredExecutionTarget Map(ProviderExecutionTarget target) => new(
        target.LogicalUnitId.Value,
        target.PhysicalTargetId.Value,
        target.ScopeBinding.ToString());

    private static StructuredBoundedQueryEvidence Map(ProviderBoundedQueryEvidence query) => new(
        new StructuredConjunctionPredicate(query.Predicate.Facts.Select(Map).ToArray()),
        query.Ordering.Select(Map).ToArray(),
        new StructuredProjection(
            query.Projection.AllColumns,
            query.Projection.LogicalColumns.ToArray()),
        Map(query.NativeOffset),
        Map(query.NativeLimit),
        query.HasContinuation,
        query.HasLookahead,
        query.IncludesTotalCount);

    private static StructuredPredicateFact Map(ProviderPredicateFact fact) => new(
        fact.LogicalColumn,
        fact.Operator.ToString(),
        fact.ValueType.ToString(),
        fact.Comparison.ToString(),
        fact.BoundInclusivity.ToString(),
        fact.BindingRole.ToString(),
        fact.BindingId.Value);

    private static StructuredOrderTerm Map(ProviderOrderTerm term) => new(
        term.LogicalColumn,
        term.Direction.ToString(),
        term.NullPlacement?.ToString(),
        term.Transforms.Select(transform => transform.ToString()).ToArray(),
        term.Comparison.ToString());

    private static StructuredNativeBound Map(ProviderNativeBound bound) => new(
        bound.Kind.ToString(),
        bound.Value);

    private static StructuredPlanEvidence Map(ProviderPlanEvidence plan) => new(
        plan.Availability.ToString(),
        plan.Provenance?.ToString(),
        plan.ChoseExpectedIndex,
        plan.ExpectedLogicalIndex,
        plan.ChosenPhysicalIndexId?.Value,
        plan.FailureCategory?.ToString(),
        plan.CollectionCommandCount,
        plan.WinningPlan?.Nodes.Select(Map).ToArray());

    private static StructuredPlanNode Map(ProviderPlanNode node) => new(
        node.Id,
        node.ParentId,
        node.Operation.ToString(),
        node.TargetId?.Value,
        node.IndexId?.Value,
        node.LogicalIndexName,
        node.IsCovering,
        node.SortPurpose?.ToString());
}
