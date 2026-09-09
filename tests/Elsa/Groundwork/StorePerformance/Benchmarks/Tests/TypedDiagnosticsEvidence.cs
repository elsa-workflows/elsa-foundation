using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

/// <summary>
/// Builds the typed callback evidence a migrated diagnostics route carries, derived from the route
/// specification the same way admission derives its checks, so fixtures never hand-copy a route shape.
/// </summary>
internal static class TypedDiagnosticsEvidence
{
    public static readonly Guid CaptureId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid TargetId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid IndexId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static NativeRouteEvidence Route(
        string provider,
        string routeIdentity,
        string rawPlanReference = "raw.json",
        string? rawPlanSha256 = null,
        string providerVersion = "3.46.0")
    {
        var specification = DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, routeIdentity);
        return new NativeRouteEvidence(
            specification.RouteIdentity,
            rawPlanReference,
            rawPlanSha256 ?? new string('a', 64),
            DiagnosticsNativePlanContract.IndexSearchPlanClassification,
            DiagnosticsNativePlanContract.ExpectedPhysicalIndexName(provider, specification),
            specification.PhysicalCardinality,
            DiagnosticsNativePlanContract.ExpectedStorageScopePredicate(provider, specification),
            specification.PredicateColumn is not null,
            specification.FiniteLimit,
            specification.FiniteLimit)
        {
            NativeFetchLimit = DiagnosticsNativePlanContract.ExpectedNativeFetchLimit(specification),
            StructuredEvidence = Build(provider, routeIdentity, providerVersion)
        };
    }

    /// <summary>
    /// The bounded resource catalog shape a provider reports when it scans (or reads an index that does not
    /// carry the ordering) and sorts the frozen 128-row catalog: PostgreSQL computes ordinal keys in
    /// aggregate-over-function subplans under a table scan, MongoDB in compute stages before a top-N sort.
    /// </summary>
    public static NativeRouteEvidence BoundedScanSortRoute(string provider, string routeIdentity, string? scanIndex = null, string providerVersion = "3.46.0")
    {
        var route = Route(provider, routeIdentity, providerVersion: providerVersion);
        var specification = DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, routeIdentity);
        var evidence = route.StructuredEvidence!;
        var keys = specification.EffectiveOrdering
            .Select(term => DiagnosticsNativePlanContract.IsOrdinalStringOrderColumn(term.Column)
                ? new StructuredOrderTerm(term.Column, Direction(term), null, ["OrdinalStringKey"], "Ordinal")
                : new StructuredOrderTerm(term.Column, Direction(term), null, [], "Unknown"))
            .ToArray();
        var lookahead = DiagnosticsNativePlanContract.ExpectedNativeFetchLimit(specification);
        var sortDetails = new StructuredPlanNodeDetails(keys, new("Explicit", lookahead), new StructuredPlanSpill(false, null, null));
        StructuredPlanNode[] nodes = provider == "sqlserver"
            ?
            [
                new(0, null, "Limit", null, null, null, null, null),
                new(1, 0, "TopNSort", null, null, null, null, null, sortDetails),
                new(2, 1, "Filter", null, null, null, null, null),
                new(3, 2, "Compute", null, null, null, null, null),
                new(4, 3, "TableScan", TargetId, null, null, null, null)
            ]
            : provider == "mongodb"
            ?
            [
                scanIndex is null
                    ? new(0, null, "TableScan", TargetId, null, null, null, null)
                    : new(0, null, "IndexScan", TargetId, IndexId, scanIndex, false, null),
                new(1, null, "Compute", null, null, null, null, null),
                new(2, null, "Compute", null, null, null, null, null),
                new(3, null, "TopNSort", null, null, null, null, null, sortDetails),
                new(4, null, "Projection", null, null, null, null, null)
            ]
            :
            [
                new(0, null, "Limit", null, null, null, null, null),
                new(1, 0, "Sort", null, null, null, null, null, sortDetails with { NativeLimit = new("Unknown", null) }),
                new(2, 1, "TableScan", TargetId, null, null, null, null),
                new(3, 2, "Aggregate", null, null, null, null, null),
                new(4, 3, "FunctionScan", null, null, null, null, null),
                new(5, 2, "Aggregate", null, null, null, null, null),
                new(6, 5, "FunctionScan", null, null, null, null, null)
            ];
        var plan = evidence.Plan with
        {
            ChoseExpectedIndex = scanIndex is not null && scanIndex == specification.IndexName,
            ChosenPhysicalIndexId = scanIndex is null ? null : IndexId,
            Nodes = nodes,
            ObservedRootOrder = provider == "mongodb" ? [0, 1, 2, 3, 4] : null
        };
        return route with
        {
            PlanClassification = DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
            StructuredEvidence = evidence with { Plan = plan }
        };
    }

    public static StructuredExecutionEvidence Build(string provider, string routeIdentity, string providerVersion = "3.46.0")
    {
        var specification = DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, routeIdentity);
        var scopePredicate = DiagnosticsNativePlanContract.ExpectedStorageScopePredicate(provider, specification);
        var nativeFetchLimit = DiagnosticsNativePlanContract.ExpectedNativeFetchLimit(specification);
        var facts = new List<StructuredPredicateFact>();
        if (scopePredicate)
            facts.Add(new("__groundwork_scope", "Equal", "String", "Ordinal", "NotApplicable", "Scope", Guid.Parse("77777777-7777-7777-7777-777777777777")));
        if (specification.PredicateColumn is { } column)
            facts.Add(new(column, "Equal", "String", "Ordinal", "NotApplicable", "Caller", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")));
        if (routeIdentity == "structured-log-replay")
        {
            facts.Add(new("sequence", "LowerBound", "Int64", "Exact", "Exclusive", "Caller", Guid.Parse("88888888-8888-8888-8888-888888888888")));
            facts.Add(new("sequence", "UpperBound", "Int64", "Exact", "Inclusive", "Caller", Guid.Parse("99999999-9999-9999-9999-999999999999")));
        }

        var ordering = specification.EffectiveOrdering
            .Select(term => DiagnosticsNativePlanContract.IsOrdinalStringOrderColumn(term.Column)
                ? new StructuredOrderTerm(term.Column, Direction(term), null, ["OrdinalStringKey"], "Ordinal")
                : new StructuredOrderTerm(term.Column, Direction(term), null, [], "Exact"))
            .ToArray();
        var (provenance, displayName) = provider switch
        {
            "sqlite" => ("EstimatedExplain", "SQLite"),
            "postgresql" => ("EstimatedExplain", "PostgreSQL"),
            "sqlserver" => ("ExplainReplay", "SQL Server"),
            "mongodb" => ("ExplainReplay", "MongoDB"),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };
        return new StructuredExecutionEvidence(
            1,
            displayName,
            providerVersion,
            "BoundedQuery",
            "Read",
            "Statement",
            new(
                CaptureId,
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                Guid.Parse("66666666-6666-6666-6666-666666666666"),
                0,
                0),
            new(LogicalUnitId(routeIdentity), TargetId, scopePredicate ? "Predicate" : "PhysicalTarget"),
            "Succeeded",
            null,
            "Collected",
            new(
                new StructuredConjunctionPredicate(facts),
                ordering,
                new(true, []),
                new("Absent", null),
                new("Explicit", nativeFetchLimit),
                false,
                true,
                false),
            new(
                "Collected",
                provenance,
                true,
                specification.IndexName,
                IndexId,
                null,
                1,
                [new(0, null, "IndexSearch", TargetId, IndexId, specification.IndexName, false, null)]));
    }

    private static string Direction(RuntimeNativeOrderTerm term) =>
        term.Direction == RuntimeNativeOrderDirection.Descending ? "Descending" : "Ascending";

    private static string LogicalUnitId(string routeIdentity) => routeIdentity switch
    {
        "structured-log-recent" or "structured-log-replay" => "elsa-structured-logs",
        "traces-by-last-seen" => "elsa-otel-trace-summaries-v3",
        "metrics-by-last-seen" => "elsa-otel-metric-points-v2",
        "logs-by-last-seen" => "elsa-otel-logs-v2",
        "resources-by-last-seen" or "resources-by-status" or "resources-by-service" => "elsa-otel-resources-v2",
        _ => throw new ArgumentOutOfRangeException(nameof(routeIdentity), routeIdentity, null)
    };
}
