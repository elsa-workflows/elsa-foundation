using System.Text.Json;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

/// <summary>
/// The pre-flight admission the operator runner delegates to (issue #1593). These cases are the rules
/// the Python runner used to carry on its own; they now have exactly one home.
/// </summary>
public sealed class NativePlanEvidenceAdmissionTests
{
    [Fact]
    public void Complete_provider_native_evidence_is_admitted_for_correctness_and_measurement()
    {
        using var fixture = DiagnosticsArtifactFixture.Create(fullNativePlan: true);
        var (workload, request) = Bind(fixture);

        var correctness = NativePlanEvidenceAdmission.ValidateCaptured(workload, request, fixture.Directory, BenchmarkPhase.Correctness, requireComplete: true);
        var measurement = NativePlanEvidenceAdmission.ValidateCaptured(workload, request, fixture.Directory, BenchmarkPhase.Measurement, requireComplete: false);

        Assert.Equal("provider-native-routes", correctness.RouteContract);
        Assert.Equal(request.NativePlanIdentity, measurement.Identity);
    }

    [Fact]
    public void Blocked_evidence_is_admitted_for_plain_correctness_but_never_promoted()
    {
        using var fixture = DiagnosticsArtifactFixture.Create(fullNativePlan: false);
        var (_, request) = Bind(fixture);
        // The fixture blocks every required route it does not index, against the real catalog.
        var workload = WorkloadCatalog.Load(Repository.Root()).Workloads[ReproducibleWorkloadScenarioCatalog.DiagnosticsWorkloadId];

        var document = NativePlanEvidenceAdmission.ValidateCaptured(workload, request, fixture.Directory, BenchmarkPhase.Correctness, requireComplete: false);
        Assert.Equal(DiagnosticsNativePlanContract.BlockedRouteContract, document.RouteContract);

        var complete = Assert.Throws<PerformanceContractException>(() =>
            NativePlanEvidenceAdmission.ValidateCaptured(workload, request, fixture.Directory, BenchmarkPhase.Correctness, requireComplete: true));
        Assert.Contains("blocked or incomplete", complete.Message, StringComparison.Ordinal);

        Assert.Throws<PerformanceContractException>(() =>
            NativePlanEvidenceAdmission.ValidateCaptured(workload, request, fixture.Directory, BenchmarkPhase.Measurement, requireComplete: false));
    }

    [Fact]
    public void Provenance_mismatch_names_the_field()
    {
        using var fixture = DiagnosticsArtifactFixture.Create(fullNativePlan: true);
        var (workload, request) = Bind(fixture);

        var exception = Assert.Throws<PerformanceContractException>(() =>
            NativePlanEvidenceAdmission.ValidateCaptured(workload, request with { ComparisonCohortId = "another-cohort" }, fixture.Directory, BenchmarkPhase.Correctness, requireComplete: true));

        Assert.Contains("provenance: ComparisonCohortId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Typed_sqlite_structured_log_routes_may_omit_the_raw_plan_only_with_structured_evidence()
    {
        // Cohort run 34088499054: the SQLite structured-log routes carry a paired-empty raw-plan reference
        // and digest backed by typed evidence; the Python copy of this rule rejected them (#1578).
        using var fixture = DiagnosticsArtifactFixture.Create(fullNativePlan: true);
        var (_, request) = Bind(fixture);
        var workload = DiagnosticsArtifactFixture.CatalogWithDeclaredRoutes(["structured-log-recent"])
            .Workloads[ReproducibleWorkloadScenarioCatalog.DiagnosticsWorkloadId];
        var template = Read(fixture, request).Routes[0];
        var typed = template with
        {
            RouteIdentity = "structured-log-recent",
            RawPlanReference = "",
            RawPlanSha256 = "",
            StructuredEvidence = StructuredEvidence()
        };

        Write(fixture, request, document => document with { Routes = [typed] });
        NativePlanEvidenceAdmission.ValidateCaptured(workload, request, fixture.Directory, BenchmarkPhase.Correctness, requireComplete: true);

        Write(fixture, request, document => document with { Routes = [typed with { StructuredEvidence = null }] });
        var missing = Assert.Throws<PerformanceContractException>(() =>
            NativePlanEvidenceAdmission.ValidateCaptured(workload, request, fixture.Directory, BenchmarkPhase.Correctness, requireComplete: true));
        Assert.Contains("without structured execution evidence", missing.Message, StringComparison.Ordinal);

        Write(fixture, request, document => document with { Routes = [typed with { RawPlanSha256 = new string('0', 64) }] });
        var half = Assert.Throws<PerformanceContractException>(() =>
            NativePlanEvidenceAdmission.ValidateCaptured(workload, request, fixture.Directory, BenchmarkPhase.Correctness, requireComplete: true));
        Assert.Contains("both optional raw-plan reference and digest, or neither", half.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Trace_detail_constituents_require_the_provider_scope_predicate()
    {
        // Relational providers render a synthetic __groundwork_scope predicate; the fixture is SQLite, so a
        // constituent without it is refused, and one with it passes the predicate rule (and then fails the
        // completeness rule, which proves the predicate check was what let it through).
        using var fixture = DiagnosticsArtifactFixture.Create(fullNativePlan: true);
        var (_, request) = Bind(fixture);
        var workload = DiagnosticsArtifactFixture.CatalogWithDeclaredRoutes(
                [.. DiagnosticsArtifactFixture.IndexedDiagnosticsRoutes, "trace-detail"])
            .Workloads[ReproducibleWorkloadScenarioCatalog.DiagnosticsWorkloadId];
        DiagnosticsTraceDetailConstituentEvidence PointRead(bool scopePredicate) => new(
            "trace-detail/resources-by-id", "", "", "primary-key-read", "", "find", 1, scopePredicate, true, 1, 1, 1, 1, 1);

        Write(fixture, request, document => document with { TraceDetailConstituents = [PointRead(scopePredicate: false)] });
        var withoutScope = Assert.Throws<PerformanceContractException>(() =>
            NativePlanEvidenceAdmission.ValidateCaptured(workload, request, fixture.Directory, BenchmarkPhase.Correctness, requireComplete: true));
        Assert.Contains("without required predicates", withoutScope.Message, StringComparison.Ordinal);

        Write(fixture, request, document => document with { TraceDetailConstituents = [PointRead(scopePredicate: true)] });
        var incomplete = Assert.Throws<PerformanceContractException>(() =>
            NativePlanEvidenceAdmission.ValidateCaptured(workload, request, fixture.Directory, BenchmarkPhase.Correctness, requireComplete: true));
        Assert.Contains("every trace-detail constituent exactly once", incomplete.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Composite_trace_detail_evidence_accounts_for_pages_contracts_and_blocked_routes()
    {
        // Ported from the operator runner's former Python self-check: a complete four-constituent
        // trace-detail composite with a continuation page is admitted for measurement; unknown or
        // contradictory route contracts and a blocked composite are handled like the runner did.
        using var fixture = DiagnosticsArtifactFixture.Create(fullNativePlan: true);
        var (_, request) = Bind(fixture);
        var workload = DiagnosticsArtifactFixture.CatalogWithDeclaredRoutes(["trace-detail"])
            .Workloads[ReproducibleWorkloadScenarioCatalog.DiagnosticsWorkloadId];
        string Raw(string name, string content)
        {
            File.WriteAllText(Path.Combine(fixture.Directory, name), content);
            return ArtifactStore.HashFile(Path.Combine(fixture.Directory, name));
        }
        var spans = new DiagnosticsTraceDetailConstituentEvidence(
            "trace-detail/spans-by-trace-key-start-id", "trace.raw.json", Raw("trace.raw.json", "{\"plan\":\"initial\"}"),
            "index-search", "trace-index", "SELECT page", 100_000, true, true, 1, 2, 2, 2, 2,
            [new DiagnosticsTraceDetailPageEvidence(1, "trace-page.raw.json", Raw("trace-page.raw.json", "{\"plan\":\"continuation\"}"), "SELECT continuation page")]);
        var logs = spans with
        {
            RouteIdentity = "trace-detail/logs-by-trace-key-timestamp-id",
            RawPlanReference = "trace-log.raw.json",
            RawPlanSha256 = Raw("trace-log.raw.json", "{\"plan\":\"log\"}"),
            PublicRowBound = 1, MaterializedCandidateCount = 1, ObservedCommandCount = 1, MaxInvocationCount = 1,
            Pages = []
        };
        DiagnosticsTraceDetailConstituentEvidence PointRead(string route) => spans with
        {
            RouteIdentity = route, RawPlanReference = "", RawPlanSha256 = "", PlanClassification = "primary-key-read",
            PhysicalIndexName = "", FiniteLimit = 1, PublicRowBound = 1, MaterializedCandidateCount = 1,
            ObservedCommandCount = 1, MaxInvocationCount = 1, Pages = null
        };
        var composite = new[] { PointRead("trace-detail/summary-by-trace-key"), spans, logs, PointRead("trace-detail/resources-by-id") };

        Write(fixture, request, document => document with { Routes = [], TraceDetailConstituents = composite });
        NativePlanEvidenceAdmission.ValidateCaptured(workload, request, fixture.Directory, BenchmarkPhase.Measurement, requireComplete: false);

        foreach (var contract in new[] { DiagnosticsNativePlanContract.BlockedRouteContract, "unknown" })
        {
            Write(fixture, request, document => document with { Routes = [], TraceDetailConstituents = composite, RouteContract = contract });
            var exception = Assert.Throws<PerformanceContractException>(() =>
                NativePlanEvidenceAdmission.ValidateCaptured(workload, request, fixture.Directory, BenchmarkPhase.Correctness, requireComplete: false));
            Assert.Contains("route contract", exception.Message, StringComparison.Ordinal);
        }

        Write(fixture, request, document => document with { Routes = [], TraceDetailConstituents = [], RouteContract = DiagnosticsNativePlanContract.BlockedRouteContract, BlockedRoutes = ["trace-detail"] });
        NativePlanEvidenceAdmission.ValidateCaptured(workload, request, fixture.Directory, BenchmarkPhase.Correctness, requireComplete: false);
        var incomplete = Assert.Throws<PerformanceContractException>(() =>
            NativePlanEvidenceAdmission.ValidateCaptured(workload, request, fixture.Directory, BenchmarkPhase.Correctness, requireComplete: true));
        Assert.Contains("blocked or incomplete", incomplete.Message, StringComparison.Ordinal);

        Write(fixture, request, document => document with { Routes = [], TraceDetailConstituents = [], RouteContract = "provider-native-routes", BlockedRoutes = ["trace-detail"] });
        var contradictory = Assert.Throws<PerformanceContractException>(() =>
            NativePlanEvidenceAdmission.ValidateCaptured(workload, request, fixture.Directory, BenchmarkPhase.Correctness, requireComplete: false));
        Assert.Contains("declares blocked routes", contradictory.Message, StringComparison.Ordinal);

        Write(fixture, request, document => document with { Routes = [], RouteContract = "provider-native-routes", BlockedRoutes = [], TraceDetailConstituents = [PointRead("trace-detail/summary-by-trace-key"), spans with { Pages = [] }, logs, PointRead("trace-detail/resources-by-id")] });
        var pages = Assert.Throws<PerformanceContractException>(() =>
            NativePlanEvidenceAdmission.ValidateCaptured(workload, request, fixture.Directory, BenchmarkPhase.Measurement, requireComplete: false));
        Assert.Contains("non-sequential trace-detail continuation page indexes", pages.Message, StringComparison.Ordinal);
    }

    private static (PerformanceWorkload Workload, RunRequest Request) Bind(DiagnosticsArtifactFixture fixture)
    {
        fixture.Bind();
        var request = ArtifactStore.ReadAll(fixture.Directory).Artifacts.First().Request;
        var workload = DiagnosticsArtifactFixture.CatalogWithDeclaredRoutes(DiagnosticsArtifactFixture.IndexedDiagnosticsRoutes)
            .Workloads[ReproducibleWorkloadScenarioCatalog.DiagnosticsWorkloadId];
        return (workload, request);
    }

    private static NativePlanEvidenceDocument Read(DiagnosticsArtifactFixture fixture, RunRequest request) =>
        JsonSerializer.Deserialize<NativePlanEvidenceDocument>(
            File.ReadAllText(ArtifactStore.EvidencePath(fixture.Directory, request.NativePlanEvidenceReference)),
            ArtifactStore.JsonOptions)!;

    private static void Write(DiagnosticsArtifactFixture fixture, RunRequest request, Func<NativePlanEvidenceDocument, NativePlanEvidenceDocument> mutate) =>
        File.WriteAllText(
            ArtifactStore.EvidencePath(fixture.Directory, request.NativePlanEvidenceReference),
            JsonSerializer.Serialize(mutate(Read(fixture, request)), ArtifactStore.JsonOptions));

    private static StructuredExecutionEvidence StructuredEvidence() => new(
        SchemaVersion: 1,
        Provider: "SQLite",
        ProviderVersion: "1.0.0",
        Operation: "BoundedQuery",
        CommandKind: "Read",
        Role: "Statement",
        Identity: new(
            CaptureId: Guid.Parse("00000000-0000-0000-0000-000000000001"),
            InvocationId: Guid.Parse("00000000-0000-0000-0000-000000000002"),
            CommandId: Guid.Parse("00000000-0000-0000-0000-000000000003"),
            StatementId: Guid.Parse("00000000-0000-0000-0000-000000000004"),
            CommandOrdinal: 0,
            StatementOrdinal: 0),
        Target: new(
            LogicalUnitId: "elsa-structured-logs",
            PhysicalTargetId: Guid.Parse("00000000-0000-0000-0000-000000000005"),
            ScopeBinding: "Predicate"),
        Outcome: "Succeeded",
        FailureCategory: null,
        ShapeAvailability: "Collected",
        BoundedQuery: null,
        Plan: new(
            Availability: "Collected",
            Provenance: "EstimatedExplain",
            ChoseExpectedIndex: true,
            ExpectedLogicalIndex: "index",
            ChosenPhysicalIndexId: Guid.Parse("00000000-0000-0000-0000-000000000006"),
            FailureCategory: null,
            CollectionCommandCount: 1,
            Nodes: []));
}
