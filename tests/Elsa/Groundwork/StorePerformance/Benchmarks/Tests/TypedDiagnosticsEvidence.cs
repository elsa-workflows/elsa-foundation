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
        "metrics-by-last-seen" => "elsa-otel-metric-points-v2",
        "logs-by-last-seen" => "elsa-otel-logs-v2",
        "resources-by-last-seen" or "resources-by-status" or "resources-by-service" => "elsa-otel-resources-v2",
        _ => throw new ArgumentOutOfRangeException(nameof(routeIdentity), routeIdentity, null)
    };
}
