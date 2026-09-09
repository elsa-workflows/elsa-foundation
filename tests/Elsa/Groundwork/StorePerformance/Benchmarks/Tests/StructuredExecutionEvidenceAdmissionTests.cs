using System.Text.Json;
using System.Security.Cryptography;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class StructuredExecutionEvidenceAdmissionTests
{
    [Theory]
    [InlineData(127, true)]
    [InlineData(129, true)]
    [InlineData(128, false)]
    public void SQLite_structured_log_recent_rejects_wrong_fetch_bound_or_missing_lookahead(int nativeLimit, bool lookahead)
    {
        var route = ValidRecentRoute();
        var evidence = route.StructuredEvidence!;
        var query = evidence.BoundedQuery!;

        Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateStructuredEvidence(
                "sqlite", DiagnosticsNativePlanContract.GroundworkAdapter, route with
                {
                    StructuredEvidence = evidence with
                    {
                        BoundedQuery = query with
                        {
                            NativeLimit = new StructuredNativeBound("Explicit", nativeLimit),
                            HasLookahead = lookahead
                        }
                    }
                }));
    }

    [Fact]
    public void SQLite_structured_log_recent_accepts_scope_only_descending_typed_evidence()
    {
        var route = ValidRecentRoute();

        DiagnosticsNativePlanContract.ValidateStructuredEvidence(
            "sqlite", DiagnosticsNativePlanContract.GroundworkAdapter, route);

        var evidence = route.StructuredEvidence!;
        var query = evidence.BoundedQuery!;
        Assert.Single(query.Predicate.Facts);
        Assert.Equal("__groundwork_scope", query.Predicate.Facts[0].LogicalColumn);
        Assert.Equal("Descending", Assert.Single(query.Ordering).Direction);
        Assert.Equal("Explicit", query.NativeLimit.Kind);
        Assert.Equal(128, query.NativeLimit.Value);
        Assert.True(query.HasLookahead);
        Assert.Equal(DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("sqlite",
            DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, route.RouteIdentity)), route.IndexName);
        Assert.Equal("IndexSearch", Assert.Single(evidence.Plan.Nodes!).Operation);
    }

    [Fact]
    public void SQLite_structured_log_recent_rejects_replay_bounds_or_ascending_order()
    {
        var route = ValidRecentRoute();
        var evidence = route.StructuredEvidence!;
        var query = evidence.BoundedQuery!;
        var scope = query.Predicate.Facts[0];
        var replayBound = ValidRoute().StructuredEvidence!.BoundedQuery!.Predicate.Facts[1];

        Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateStructuredEvidence(
                "sqlite",
                DiagnosticsNativePlanContract.GroundworkAdapter,
                route with
                {
                    StructuredEvidence = evidence with
                    {
                        BoundedQuery = query with
                        {
                            Predicate = new StructuredConjunctionPredicate([scope, replayBound])
                        }
                    }
                }));

        Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateStructuredEvidence(
                "sqlite",
                DiagnosticsNativePlanContract.GroundworkAdapter,
                route with
                {
                    StructuredEvidence = evidence with
                    {
                        BoundedQuery = query with
                        {
                            Ordering = [query.Ordering[0] with { Direction = "Ascending" }]
                        }
                    }
                }));
    }

    [Theory]
    [InlineData("table-scan")]
    [InlineData("unknown")]
    public void SQLite_structured_log_replay_rejects_contradictory_plan_classification(string classification)
    {
        var route = ValidRoute() with { PlanClassification = classification };

        Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateStructuredEvidence(
                "sqlite", DiagnosticsNativePlanContract.GroundworkAdapter, route));
    }

    [Fact]
    public void Artifact_admission_rejects_null_route_elements_with_a_controlled_error()
    {
        using var fixture = AdmissionFixture.Create(
            ValidRoute() with { RawPlanReference = string.Empty, RawPlanSha256 = string.Empty });
        var changed = fixture.Evidence with
        {
            NativePlan = fixture.Evidence.NativePlan with { Routes = [null!] }
        };

        Assert.Throws<PerformanceContractException>(() => fixture.Validate(changed));
    }

    [Fact]
    public void Artifact_admission_rejects_a_typed_provider_version_different_from_the_request()
    {
        var route = ValidRoute();
        using var fixture = AdmissionFixture.Create(route with
        {
            RawPlanReference = string.Empty,
            RawPlanSha256 = string.Empty,
            StructuredEvidence = route.StructuredEvidence! with { ProviderVersion = "0.0.0-wrong" }
        });

        var exception = Assert.Throws<PerformanceContractException>(fixture.Validate);

        Assert.Contains("provider version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SQLite_structured_log_replay_dto_roundtrip_preserves_semantic_facts()
    {
        var route = ValidRoute();
        var serialized = JsonSerializer.Serialize(route, ArtifactStore.JsonOptions);
        var reloadedRoute = JsonSerializer.Deserialize<NativeRouteEvidence>(serialized, ArtifactStore.JsonOptions);

        DiagnosticsNativePlanContract.ValidateStructuredEvidence(
            "sqlite",
            DiagnosticsNativePlanContract.GroundworkAdapter,
            reloadedRoute!);

        var reloadedEvidence = reloadedRoute!.StructuredEvidence!;
        Assert.Equal(128, reloadedEvidence.BoundedQuery!.NativeLimit.Value);
        Assert.Equal("Explicit", reloadedEvidence.BoundedQuery.NativeLimit.Kind);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), reloadedEvidence.Identity.CaptureId);
        Assert.DoesNotContain("SELECT", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SQLite_structured_log_replay_rejects_missing_scope_or_extra_predicate_fact()
    {
        var route = ValidRoute();
        var evidence = route.StructuredEvidence!;
        var facts = evidence.BoundedQuery!.Predicate.Facts;

        Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateStructuredEvidence(
                "sqlite",
                DiagnosticsNativePlanContract.GroundworkAdapter,
                route with
                {
                    StructuredEvidence = evidence with
                    {
                        BoundedQuery = evidence.BoundedQuery with
                        {
                            Predicate = new StructuredConjunctionPredicate(facts.Skip(1).ToArray())
                        }
                    }
                }));

        Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateStructuredEvidence(
                "sqlite",
                DiagnosticsNativePlanContract.GroundworkAdapter,
                route with
                {
                    StructuredEvidence = evidence with
                    {
                        BoundedQuery = evidence.BoundedQuery with
                        {
                            Predicate = new StructuredConjunctionPredicate(
                                facts.Append(facts[0] with { BindingId = Guid.NewGuid() }).ToArray())
                        }
                    }
                }));
    }

    [Fact]
    public void SQLite_structured_log_replay_rejects_unknown_native_limit()
    {
        var route = ValidRoute();
        var evidence = route.StructuredEvidence!;
        Assert.NotNull(evidence.BoundedQuery);

        Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateStructuredEvidence(
                "sqlite",
                DiagnosticsNativePlanContract.GroundworkAdapter,
                route with
                {
                    StructuredEvidence = evidence with
                    {
                        BoundedQuery = evidence.BoundedQuery with
                        {
                            NativeLimit = new StructuredNativeBound("Unknown", null)
                        }
                    }
                }));
    }

    [Fact]
    public void SQLite_structured_log_replay_rejects_wrong_selected_index_or_non_bounded_operation()
    {
        var route = ValidRoute();
        var evidence = route.StructuredEvidence!;

        Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateStructuredEvidence(
                "sqlite",
                DiagnosticsNativePlanContract.GroundworkAdapter,
                route with
                {
                    StructuredEvidence = evidence with
                    {
                        Plan = evidence.Plan with
                        {
                            ExpectedLogicalIndex = "wrong-index"
                        }
                    }
                }));

        Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateStructuredEvidence(
                "sqlite",
                DiagnosticsNativePlanContract.GroundworkAdapter,
                route with
                {
                    StructuredEvidence = evidence with { Operation = "PointRead" }
                }));
    }

    [Fact]
    public void SQLite_structured_log_replay_rejects_null_persisted_collections()
    {
        var route = ValidRoute();
        var evidence = route.StructuredEvidence!;
        Assert.NotNull(evidence.BoundedQuery);

        Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateStructuredEvidence(
                "sqlite",
                DiagnosticsNativePlanContract.GroundworkAdapter,
                route with
                {
                    StructuredEvidence = evidence with
                    {
                        BoundedQuery = evidence.BoundedQuery with
                        {
                            Projection = evidence.BoundedQuery.Projection with { LogicalColumns = null! }
                        }
                    }
                }));

        Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateStructuredEvidence(
                "sqlite",
                DiagnosticsNativePlanContract.GroundworkAdapter,
                route with
                {
                    StructuredEvidence = evidence with
                    {
                        BoundedQuery = evidence.BoundedQuery with
                        {
                            Ordering = [evidence.BoundedQuery.Ordering[0] with { Transforms = null! }]
                        }
                    }
                }));
    }

    [Fact]
    public void SQLite_structured_log_replay_rejects_contradictory_point_read_evidence()
    {
        var route = ValidRoute();
        var evidence = route.StructuredEvidence!;

        Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateStructuredEvidence(
                "sqlite",
                DiagnosticsNativePlanContract.GroundworkAdapter,
                route with
                {
                    StructuredEvidence = evidence with
                    {
                        PointRead = new StructuredPointReadEvidence(
                            [],
                            new StructuredPointReadUniqueness("NotObserved", [], false),
                            new StructuredNativeBound("Absent", null),
                            true,
                            "None")
                    }
                }));
    }

    [Fact]
    public void Artifact_admission_accepts_typed_sqlite_replay_without_a_raw_plan_artifact_with_complete_route_accounting()
    {
        using var fixture = AdmissionFixture.Create(
            ValidRoute() with { RawPlanReference = string.Empty, RawPlanSha256 = string.Empty });

        fixture.Validate();
        var admittedRouteIdentities = fixture.Evidence.NativePlan.Routes
            .Select(route => route.RouteIdentity)
            .Concat(fixture.Evidence.NativePlan.BlockedRoutes)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(fixture.Workload.RequiredNativeRoutes.Order(StringComparer.Ordinal), admittedRouteIdentities);
        Assert.Contains("resources-by-last-seen", fixture.Evidence.NativePlan.Routes.Select(route => route.RouteIdentity));
        Assert.Contains("traces-by-last-seen", fixture.Evidence.NativePlan.Routes.Select(route => route.RouteIdentity));
        Assert.Equal(6, fixture.Evidence.NativePlan.BlockedRoutes.Count);
    }

    [Fact]
    public void Artifact_admission_requires_typed_evidence_for_the_migrated_route()
    {
        using var fixture = AdmissionFixture.Create(
            ValidRoute() with
            {
                RawPlanReference = string.Empty,
                RawPlanSha256 = string.Empty,
                StructuredEvidence = null
            });

        var exception = Assert.Throws<PerformanceContractException>(fixture.Validate);

        Assert.Contains("Structured execution evidence is missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Artifact_admission_does_not_allow_a_legacy_raw_plan_to_substitute_for_typed_evidence()
    {
        using var fixture = AdmissionFixture.Create(
            ValidRoute() with { StructuredEvidence = null, RawPlanReference = "legacy.json" });

        var exception = Assert.Throws<PerformanceContractException>(fixture.Validate);

        Assert.Contains("Structured execution evidence is missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Artifact_admission_rejects_a_malformed_optional_raw_plan_pair()
    {
        using var fixture = AdmissionFixture.Create(
            ValidRoute() with { RawPlanReference = "legacy.json", RawPlanSha256 = string.Empty });

        Assert.Throws<PerformanceContractException>(fixture.Validate);

        using var reverseFixture = AdmissionFixture.Create(
            ValidRoute() with { RawPlanReference = string.Empty, RawPlanSha256 = new string('a', 64) });

        Assert.Throws<PerformanceContractException>(reverseFixture.Validate);
    }

    [Fact]
    public void Artifact_admission_rejects_explicitly_null_optional_raw_plan_fields_after_persistence()
    {
        using var fixture = AdmissionFixture.Create(
            ValidRoute() with { RawPlanReference = null!, RawPlanSha256 = null! });

        var persistedRoute = fixture.Evidence.NativePlan.Routes.Single(route => route.RouteIdentity == "structured-log-replay");
        Assert.Null(persistedRoute.RawPlanReference);
        Assert.Null(persistedRoute.RawPlanSha256);

        Assert.Throws<PerformanceContractException>(fixture.Validate);
    }

    [Fact]
    public void Artifact_admission_still_validates_an_optional_raw_plan_for_the_migrated_route()
    {
        using var fixture = AdmissionFixture.Create(ValidRoute());

        var exception = Assert.Throws<PerformanceContractException>(fixture.Validate);

        Assert.Contains("Raw provider-plan evidence is missing", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Artifact_store_manifest_roundtrip_preserves_rawless_typed_structured_logs(bool recent)
    {
        var route = recent ? ValidRecentRoute() : ValidRoute();
        using var fixture = AdmissionFixture.Create(
            route with { RawPlanReference = string.Empty, RawPlanSha256 = string.Empty });

        fixture.Validate();
        fixture.WriteAndReadManifest(route.RouteIdentity);
    }

    [Fact]
    public void Artifact_admission_keeps_raw_plan_mandatory_for_unmigrated_routes()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "traces-by-last-seen");
        using var fixture = AdmissionFixture.CreateUnmigrated(new NativeRouteEvidence(
            specification.RouteIdentity,
            "missing.json",
            new string('a', 64),
            DiagnosticsNativePlanContract.IndexSearchPlanClassification,
            DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("sqlite", specification),
            specification.PhysicalCardinality,
            true,
            false,
            specification.FiniteLimit,
            specification.FiniteLimit));

        Assert.Throws<PerformanceContractException>(fixture.Validate);
    }

    [Fact]
    public void Artifact_admission_rejects_changed_typed_evidence_after_persistence()
    {
        using var fixture = AdmissionFixture.Create(
            ValidRoute() with { RawPlanReference = string.Empty, RawPlanSha256 = string.Empty });
        var changed = fixture.Evidence with
        {
            NativePlan = fixture.Evidence.NativePlan with
            {
                Routes = fixture.Evidence.NativePlan.Routes.Select(route => route.StructuredEvidence is { } typed
                    ? route with { StructuredEvidence = typed with { Identity = typed.Identity with { CaptureId = Guid.NewGuid() } } }
                    : route).ToArray()
            }
        };

        var exception = Assert.Throws<PerformanceContractException>(() => fixture.Validate(changed));

        Assert.Contains("does not match", exception.Message, StringComparison.Ordinal);
    }

    private sealed class AdmissionFixture : IDisposable
    {
        private readonly DiagnosticsArtifactFixture source;
        private readonly ProcessArtifact artifact;

        private AdmissionFixture(
            DiagnosticsArtifactFixture source,
            ProcessArtifact artifact)
        {
            this.source = source;
            this.artifact = artifact;
            Workload = WorkloadCatalog.Load(Repository.Root()).Workloads[artifact.Request.WorkloadId];
            Request = artifact.Request;
            Evidence = artifact.Correctness;
        }

        public PerformanceWorkload Workload { get; }
        public RunRequest Request { get; }
        public CorrectnessEvidence Evidence { get; }

        public static AdmissionFixture Create(NativeRouteEvidence route)
        {
            return CreateCore(route, appendTypedRoute: true);
        }

        public static AdmissionFixture CreateUnmigrated(NativeRouteEvidence route) =>
            CreateCore(route, appendTypedRoute: false);

        private static AdmissionFixture CreateCore(NativeRouteEvidence route, bool appendTypedRoute)
        {
            var source = DiagnosticsArtifactFixture.Create();
            try
            {
                var artifacts = ArtifactStore.LoadProcessArtifactsWithoutManifest(source.Directory)
                    .Select(entry => entry.Artifact)
                    .ToArray();
                var anchor = artifacts[0];
                var nativePlan = anchor.Correctness.NativePlan;
                var evidencePath = ArtifactStore.EvidencePath(
                    source.Directory,
                    anchor.Request.NativePlanEvidenceReference);
                var document = JsonSerializer.Deserialize<NativePlanEvidenceDocument>(
                    File.ReadAllBytes(evidencePath),
                    ArtifactStore.JsonOptions) ??
                    throw new InvalidOperationException("Diagnostics fixture evidence could not be loaded.");
                var routes = appendTypedRoute
                    ? nativePlan.Routes.Append(route).ToArray()
                    : nativePlan.Routes
                        .Select(existing => existing.RouteIdentity == route.RouteIdentity ? route : existing)
                        .ToArray();
                var blockedRoutes = appendTypedRoute
                    ? nativePlan.BlockedRoutes.Where(identity => identity != route.RouteIdentity).ToArray()
                    : nativePlan.BlockedRoutes.ToArray();
                var evidenceBytes = JsonSerializer.SerializeToUtf8Bytes(
                    document with { Routes = routes, BlockedRoutes = blockedRoutes },
                    ArtifactStore.JsonOptions);
                var evidenceSha = Convert.ToHexString(SHA256.HashData(evidenceBytes)).ToLowerInvariant();
                File.WriteAllBytes(evidencePath, evidenceBytes);

                var revisedNativePlan = nativePlan with
                {
                    ContentSha256 = evidenceSha,
                    Routes = routes,
                    BlockedRoutes = blockedRoutes
                };
                foreach (var artifact in artifacts)
                {
                    ArtifactStore.Write(
                        source.Directory,
                        artifact with
                        {
                            Request = artifact.Request with { NativePlanContentSha256 = evidenceSha },
                            Correctness = artifact.Correctness with { NativePlan = revisedNativePlan }
                        });
                }

                var updatedArtifact = ArtifactStore.LoadProcessArtifactsWithoutManifest(source.Directory)
                    .Select(entry => entry.Artifact)
                    .Single(item => item.Request.ProcessKind == ProcessKind.Measured && item.Request.ProcessIndex == 1);
                return new AdmissionFixture(source, updatedArtifact);
            }
            catch
            {
                source.Dispose();
                throw;
            }
        }

        public void Validate() => ArtifactAdmission.ValidateCorrectness(Workload, Request, Evidence, source.Directory);

        public void Validate(CorrectnessEvidence evidence) =>
            ArtifactAdmission.ValidateCorrectness(Workload, Request, evidence, source.Directory);

        public void WriteAndReadManifest(string routeIdentity)
        {
            ArtifactStore.Write(
                source.Directory,
                new ProcessArtifact(
                    2,
                    Request,
                    BenchmarkProtocol.Acceptance,
                    true,
                    Evidence,
                    [],
                    new MachineMetadata(
                        "test-os",
                        "test-runtime",
                        "X64",
                        "X64",
                        1,
                        Request.HostFingerprintSha256,
                        "2026-09-06T00:00:00Z")));
            ArtifactStore.WriteManifest(source.Directory);
            var roundtripped = ArtifactStore.ReadAll(source.Directory);

            Assert.Equal(4, roundtripped.Artifacts.Count);
            var artifact = roundtripped.Artifacts.Single(item =>
                item.Request.ProcessKind == ProcessKind.Measured && item.Request.ProcessIndex == 1);
            ArtifactAdmission.ValidateCorrectness(Workload, artifact.Request, artifact.Correctness, source.Directory);
            var route = artifact.Correctness.NativePlan.Routes.Single(item => item.RouteIdentity == routeIdentity);
            Assert.Empty(route.RawPlanReference);
            Assert.Empty(route.RawPlanSha256);
        }

        public void Dispose() => source.Dispose();
    }

    [Fact]
    public void An_explicit_zero_native_offset_is_the_same_fact_as_no_offset_and_a_real_offset_is_not()
    {
        // SQL Server states OFFSET 0 ROWS before FETCH NEXT and reports an explicit zero offset.
        var route = TypedDiagnosticsEvidence.Route("sqlserver", "structured-log-recent");
        var query = route.StructuredEvidence!.BoundedQuery!;
        var zero = route with { StructuredEvidence = route.StructuredEvidence with { BoundedQuery = query with { NativeOffset = new("Explicit", 0) } } };
        DiagnosticsNativePlanContract.ValidateStructuredEvidence("sqlserver", DiagnosticsNativePlanContract.GroundworkAdapter, zero);

        var five = route with { StructuredEvidence = route.StructuredEvidence with { BoundedQuery = query with { NativeOffset = new("Explicit", 5) } } };
        Assert.Contains("paging or projection", Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanContract.ValidateStructuredEvidence("sqlserver", DiagnosticsNativePlanContract.GroundworkAdapter, five)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("postgresql", "resources-by-last-seen", null)]
    [InlineData("postgresql", "resources-by-status", null)]
    [InlineData("mongodb", "resources-by-last-seen", null)]
    [InlineData("mongodb", "resources-by-status", "elsa_otel_resources_status")]
    [InlineData("mongodb", "resources-by-service", "elsa_otel_resources_service_last_seen")]
    public void Bounded_resource_routes_admit_a_typed_scan_and_sort_over_the_frozen_catalog(string provider, string route, string? scanIndex)
    {
        var evidence = TypedDiagnosticsEvidence.BoundedScanSortRoute(provider, route, scanIndex);

        DiagnosticsNativePlanContract.ValidateStructuredEvidence(provider, DiagnosticsNativePlanContract.GroundworkAdapter, evidence);
        Assert.Equal(
            DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
            DiagnosticsNativePlanContract.ClassifyStructuredPlan(provider, DiagnosticsNativePlanContract.GroundworkAdapter,
                DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, route), evidence.StructuredEvidence!.Plan));
    }

    [Fact]
    public void Bounded_resource_scan_sort_rejects_wrong_keys_spill_foreign_index_and_index_search_classification()
    {
        var accepted = TypedDiagnosticsEvidence.BoundedScanSortRoute("mongodb", "resources-by-last-seen");
        var plan = accepted.StructuredEvidence!.Plan;
        var sort = plan.Nodes!.Single(node => node.Operation == "TopNSort");

        var wrongKeys = accepted with { StructuredEvidence = accepted.StructuredEvidence with { Plan = plan with { Nodes = plan.Nodes!.Select(node => node == sort ? node with { Details = node.Details! with { NativeSortKeys = node.Details!.NativeSortKeys!.Take(2).ToArray() } } : node).ToArray() } } };
        Assert.Contains("complete ordering", Assert.Throws<PerformanceContractException>(() => Validate(wrongKeys)).Message, StringComparison.Ordinal);

        var spilled = accepted with { StructuredEvidence = accepted.StructuredEvidence with { Plan = plan with { Nodes = plan.Nodes!.Select(node => node == sort ? node with { Details = node.Details! with { Spill = new StructuredPlanSpill(true, 1, 1) } } : node).ToArray() } } };
        Assert.Contains("spill", Assert.Throws<PerformanceContractException>(() => Validate(spilled)).Message, StringComparison.Ordinal);

        var foreignIndex = TypedDiagnosticsEvidence.BoundedScanSortRoute("mongodb", "resources-by-last-seen", "elsa_otel_resources_status");
        Assert.Contains("admitted index", Assert.Throws<PerformanceContractException>(() => Validate(foreignIndex)).Message, StringComparison.Ordinal);

        var misclassified = accepted with { PlanClassification = DiagnosticsNativePlanContract.IndexSearchPlanClassification };
        Assert.Contains("plan classification", Assert.Throws<PerformanceContractException>(() => Validate(misclassified)).Message, StringComparison.Ordinal);

        // An index-search route never admits a sort, so the same node set on a stream route fails closed.
        var stream = TypedDiagnosticsEvidence.Route("mongodb", "traces-by-last-seen");
        var sortedStream = stream with { StructuredEvidence = stream.StructuredEvidence! with { Plan = stream.StructuredEvidence.Plan with { Nodes = [.. stream.StructuredEvidence.Plan.Nodes!, sort] } } };
        Assert.Throws<PerformanceContractException>(() => Validate(sortedStream));

        static void Validate(NativeRouteEvidence route) =>
            DiagnosticsNativePlanContract.ValidateStructuredEvidence("mongodb", DiagnosticsNativePlanContract.GroundworkAdapter, route);
    }

    private static NativeRouteEvidence ValidRoute() => TypedDiagnosticsEvidence.Route("sqlite", "structured-log-replay");

    private static NativeRouteEvidence ValidRecentRoute() => TypedDiagnosticsEvidence.Route("sqlite", "structured-log-recent");
}
