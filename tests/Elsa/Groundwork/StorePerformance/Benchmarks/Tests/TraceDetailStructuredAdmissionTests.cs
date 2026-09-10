using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

/// <summary>
/// Trace-detail constituents are admitted from typed Groundwork observations only; the observation must
/// be this run's provider and server version, and a continuation page must continue on the route's
/// ordering with the ordering's own value semantics.
/// </summary>
public sealed class TraceDetailStructuredAdmissionTests
{
    private const string Adapter = DiagnosticsNativePlanContract.GroundworkAdapter;
    private const string Version = "17.6";
    private static readonly Guid Target = TypedDiagnosticsEvidence.TargetId;
    private static readonly Guid Index = TypedDiagnosticsEvidence.IndexId;
    private static readonly Guid Binding = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Point_read_and_paged_query_from_this_provider_and_version_are_admitted()
    {
        DiagnosticsNativePlanContract.ValidateStructuredTraceDetailConstituent("postgresql", Adapter, Summary(PointRead("postgresql")), Version);
        DiagnosticsNativePlanContract.ValidateStructuredTraceDetailConstituent("postgresql", Adapter, Spans("postgresql"), Version);
    }

    [Fact]
    public void Evidence_observed_on_another_provider_is_rejected()
    {
        var rejected = Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateStructuredTraceDetailConstituent("sqlite", Adapter, Summary(PointRead("postgresql")), Version));
        Assert.Contains("was not observed on SQLite", rejected.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("17.5")]
    [InlineData("")]
    public void Evidence_observed_on_another_server_version_is_rejected(string expectedVersion)
    {
        var rejected = Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateStructuredTraceDetailConstituent("postgresql", Adapter, Spans("postgresql"), expectedVersion));
        Assert.Contains("observed provider version", rejected.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Int64", "Exact")]
    [InlineData("String", "Exact")]
    [InlineData("Int64", "Ordinal")]
    public void Continuation_fact_on_an_ordinal_identity_must_compare_strings_ordinally(string valueType, string comparison)
    {
        StructuredPredicateFact Rewrite(StructuredPredicateFact fact) =>
            fact.LogicalColumn == "spanId" ? fact with { ValueType = valueType, Comparison = comparison } : fact;
        var boundary = Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateStructuredTraceDetailConstituent("postgresql", Adapter, Spans("postgresql", Rewrite, rewriteEqualities: false), Version));
        Assert.Contains("is not the route's keyset branch", boundary.Message, StringComparison.Ordinal);
        var equality = Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateStructuredTraceDetailConstituent("postgresql", Adapter, Spans("postgresql", Rewrite, rewriteEqualities: true), Version));
        Assert.Contains("is not the route's keyset branch", equality.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Continuation_fact_on_an_exact_term_must_compare_exactly()
    {
        var rejected = Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateStructuredTraceDetailConstituent("postgresql", Adapter,
                Spans("postgresql", fact => fact.LogicalColumn == "startTime" ? fact with { Comparison = "Ordinal" } : fact, rewriteEqualities: false), Version));
        Assert.Contains("is not the route's keyset branch", rejected.Message, StringComparison.Ordinal);
    }

    private static DiagnosticsTraceDetailConstituentEvidence Summary(StructuredExecutionEvidence evidence) => new(
        "trace-detail/summary-by-trace-key", "", "", "primary-key-read", "", "",
        DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream, true, true, 1, 1, 1, 1, 1)
    {
        StructuredEvidence = evidence
    };

    private static StructuredExecutionEvidence PointRead(string provider) => new(
        1, DisplayName(provider), Version, "PointRead", "Read", "Statement", Identity(),
        new("elsa-otel-trace-summaries-v3", Target, "Predicate"), "Succeeded", null, "Collected", null,
        new("Collected", Provenance(provider), null, null, null, null, 1, [new(0, null, "PrimaryKeySearch", Target, null, null, null, null)]))
    {
        PointRead = new(
            [new("traceKey", "String", "Key", Binding), new("__groundwork_scope", "String", "Scope", Guid.Parse("77777777-7777-7777-7777-777777777777"))],
            new("Observed", ["traceKey"], true), new("Absent", null), true, "None")
    };

    /// <summary>The spans constituent: page 0 plus every continuation page of the frozen page sequence.</summary>
    private static DiagnosticsTraceDetailConstituentEvidence Spans(
        string provider,
        Func<StructuredPredicateFact, StructuredPredicateFact>? rewriteContinuation = null,
        bool rewriteEqualities = false)
    {
        var specification = DiagnosticsNativePlanContract.TraceDetailConstituents(Adapter)
            .Single(item => item.RouteIdentity == "trace-detail/spans-by-trace-key-start-id");
        var pages = specification.MaxInvocationCount;
        var route = DiagnosticsNativePlanContract.RouteSpecificationFor(specification);
        return new(
            specification.RouteIdentity, "", "", "index-search",
            DiagnosticsNativePlanContract.ExpectedPhysicalIndexName(provider, route), "",
            specification.PhysicalCardinality, true, true, specification.FiniteLimit, specification.PublicRowBound,
            specification.PublicRowBound, pages, pages,
            Enumerable.Range(1, pages - 1)
                .Select(page => new DiagnosticsTraceDetailPageEvidence(page, "", "", "")
                {
                    StructuredEvidence = Page(provider, specification, route, continuation: true, rewriteContinuation, rewriteEqualities)
                })
                .ToArray())
        {
            StructuredEvidence = Page(provider, specification, route, continuation: false, null, false)
        };
    }

    private static StructuredExecutionEvidence Page(
        string provider,
        DiagnosticsTraceDetailConstituentSpec specification,
        DiagnosticsNativeRouteSpec route,
        bool continuation,
        Func<StructuredPredicateFact, StructuredPredicateFact>? rewriteContinuation,
        bool rewriteEqualities)
    {
        var ordering = specification.Ordering;
        StructuredPredicateFact Fact(string column, string @operator) => new(
            column, @operator,
            DiagnosticsNativePlanContract.IsOrdinalStringOrderColumn(column) ? "String" : "Int64",
            DiagnosticsNativePlanContract.IsOrdinalStringOrderColumn(column) ? "Ordinal" : "Exact",
            @operator == "Equal" ? "NotApplicable" : "Exclusive", "Continuation", Binding);
        StructuredPredicateFact Rewrite(StructuredPredicateFact fact, bool equality) =>
            rewriteContinuation is null || equality != rewriteEqualities ? fact : rewriteContinuation(fact);
        var branches = ordering.Select((term, index) => new StructuredContinuationBranch(
                ordering.Take(index).Select(prefix => Rewrite(Fact(prefix.Column, "Equal"), equality: true)).ToArray(),
                Rewrite(Fact(term.Column, "LowerBound"), equality: false),
                false))
            .ToArray();
        return new StructuredExecutionEvidence(
            1, DisplayName(provider), Version, "BoundedQuery", "Read", "Statement", Identity(),
            new("elsa-otel-spans-v2", Target, "Predicate"), "Succeeded", null, "Collected",
            new(
                new StructuredConjunctionPredicate(
                [
                    new("__groundwork_scope", "Equal", "String", "Ordinal", "NotApplicable", "Scope", Guid.Parse("77777777-7777-7777-7777-777777777777")),
                    new("traceKey", "Equal", "String", "Ordinal", "NotApplicable", "Caller", Binding)
                ]),
                ordering.Select(term => DiagnosticsNativePlanContract.IsOrdinalStringOrderColumn(term.Column)
                    ? new StructuredOrderTerm(term.Column, "Ascending", null, ["OrdinalStringKey"], "Ordinal")
                    : new StructuredOrderTerm(term.Column, "Ascending", null, [], "Exact")).ToArray(),
                new(true, []),
                new("Absent", null),
                new("Explicit", DiagnosticsNativePlanContract.ExpectedNativeFetchLimit(route)),
                continuation,
                true,
                false)
            {
                Continuation = continuation ? new("Lexicographic", branches, []) : null
            },
            new("Collected", Provenance(provider), null, null, null, null, 1,
                [new(0, null, "IndexSearch", Target, Index, specification.IndexName, false, null)]));
    }

    private static StructuredExecutionIdentity Identity() => new(
        TypedDiagnosticsEvidence.CaptureId, Guid.Parse("44444444-4444-4444-4444-444444444444"),
        Guid.Parse("55555555-5555-5555-5555-555555555555"), Guid.Parse("66666666-6666-6666-6666-666666666666"), 0, 0);

    private static string DisplayName(string provider) => provider switch
    {
        "sqlite" => "SQLite",
        "postgresql" => "PostgreSQL",
        "sqlserver" => "SQL Server",
        _ => "MongoDB"
    };

    private static string Provenance(string provider) => provider is "sqlite" or "postgresql" ? "EstimatedExplain" : "ExplainReplay";
}
