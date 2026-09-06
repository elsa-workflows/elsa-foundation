using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Groundwork.Kernel;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class DiagnosticsDurableHistoryAdapterTests
{
    [Fact]
    public async Task Dispatches_to_the_exact_groundwork_diagnostics_form()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request(),
            "unused",
            "unused");

        Assert.IsType<DiagnosticsDurableHistoryAdapter>(adapter);
    }

    [Fact]
    public async Task Timed_operations_remain_closed_before_correctness_preparation()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(Request(), "unused", "unused");

        var exception = Assert.Throws<PerformanceContractException>(() => adapter.Operations);

        Assert.Contains("before correctness preparation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SQLite_scoped_clients_share_the_one_process_connection_but_remain_distinct()
    {
        var root = Directory.CreateTempSubdirectory("diagnostics-shared-sqlite-");
        var connectionString = $"Data Source={Path.Combine(root.FullName, "diagnostics.db")};Mode=ReadWriteCreate;Cache=Shared";
        try
        {
            var observed = await ProviderProbe.ReadAsync("sqlite", connectionString, CancellationToken.None);
            var request = Request() with
            {
                ProviderVersion = observed.Version,
                ProviderTopology = observed.Topology,
                ProviderConfiguration = observed.Configuration
            };
            await using var adapter = new DiagnosticsDurableHistoryAdapter(request, connectionString, root.FullName);

            await adapter.PrepareAsync(CancellationToken.None);
            var scopes = await adapter.OpenScopedClientsAsync(CancellationToken.None);

            Assert.NotSame(scopes.Primary, scopes.Secondary);
            Assert.NotSame(scopes.Primary.OpenTelemetry, scopes.Secondary.OpenTelemetry);
            Assert.NotSame(scopes.Primary.StructuredLogs, scopes.Secondary.StructuredLogs);

            await scopes.Secondary.StructuredLogs.AppendAsync(
                new StructuredLogEntry { Message = "secondary", Category = "scope", SourceId = "secondary" },
                CancellationToken.None);
            var primary = await scopes.Primary.StructuredLogs.AppendAsync(
                new StructuredLogEntry { Message = "primary", Category = "scope", SourceId = "primary" },
                CancellationToken.None);
            Assert.Equal(primary.Sequence, await scopes.Primary.StructuredLogs.GetHighWaterMarkAsync());

            adapter.CommandObserver.ClearCommands();
            await scopes.Primary.OpenTelemetry.QueryResourcesAsync(
                new OpenTelemetryResourceFilter { Take = 1 },
                CancellationToken.None);
            await scopes.Primary.StructuredLogs.GetRecentAsync(
                new StructuredLogFilter { MaxCount = 1 },
                CancellationToken.None);

            Assert.Collection(
                adapter.CommandObserver.Commands,
                command => Assert.Contains("elsa_otel_resources_v2", command.CommandText, StringComparison.OrdinalIgnoreCase),
                command => Assert.Contains("elsa_structured_logs", command.CommandText, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public async Task SQLite_ReadAfter_null_127_emits_one_typed_terminal_observation_and_preserves_legacy_count()
    {
        var root = Directory.CreateTempSubdirectory("structured-evidence-read-after-");
        var connectionString = $"Data Source={Path.Combine(root.FullName, "diagnostics.db")};Mode=ReadWriteCreate;Cache=Shared";
        try
        {
            var observed = await ProviderProbe.ReadAsync("sqlite", connectionString, CancellationToken.None);
            var request = Request() with
            {
                ProviderVersion = observed.Version,
                ProviderTopology = observed.Topology,
                ProviderConfiguration = observed.Configuration
            };
            await using var adapter = new DiagnosticsDurableHistoryAdapter(
                request,
                connectionString,
                root.FullName,
                captureStructuredEvidence: true);

            await adapter.PrepareAsync(CancellationToken.None);
            var scopes = await adapter.OpenScopedClientsAsync(CancellationToken.None);
            await scopes.Primary.StructuredLogs.AppendAsync(
                new StructuredLogEntry
                {
                    Message = "structured-evidence-proof",
                    Category = "structured-evidence",
                    SourceId = "structured-evidence-test"
                },
                CancellationToken.None);
            await adapter.FlushAsync(CancellationToken.None);

            adapter.CommandObserver.ClearCommands();
            adapter.ClearStructuredEvidence();
            var page = await scopes.Primary.StructuredLogs.ReadAfterAsync(
                afterCursor: null,
                filter: StructuredLogFilter.None,
                maxCount: 127,
                cancellationToken: CancellationToken.None);

            var evidence = Assert.Single(adapter.StructuredEvidence);
            Assert.Single(page.Entries);
            Assert.Single(adapter.CommandObserver.Commands);
            Assert.Equal("SQLite", evidence.Provider);
            Assert.Equal("BoundedQuery", evidence.Operation);
            Assert.Equal("Read", evidence.CommandKind);
            Assert.Equal("Succeeded", evidence.Outcome);
            Assert.Equal("Collected", evidence.ShapeAvailability);
            Assert.Equal("elsa-structured-logs", evidence.Target.LogicalUnitId);
            Assert.Equal("Predicate", evidence.Target.ScopeBinding);
            var predicateFacts = evidence.BoundedQuery!.Predicate.Facts;
            Assert.Equal(3, predicateFacts.Count);
            Assert.Contains(predicateFacts, fact =>
                fact.LogicalColumn == "__groundwork_scope" &&
                fact.Operator == "Equal" &&
                fact.ValueType == "String" &&
                fact.Comparison == "Ordinal" &&
                fact.BoundInclusivity == "NotApplicable" &&
                fact.BindingRole == "Scope");
            Assert.Contains(predicateFacts, fact =>
                fact.LogicalColumn == "sequence" &&
                fact.Operator == "LowerBound" &&
                fact.ValueType == "Int64" &&
                fact.Comparison == "Exact" &&
                fact.BoundInclusivity == "Exclusive" &&
                fact.BindingRole == "Caller");
            Assert.Contains(predicateFacts, fact =>
                fact.LogicalColumn == "sequence" &&
                fact.Operator == "UpperBound" &&
                fact.ValueType == "Int64" &&
                fact.Comparison == "Exact" &&
                fact.BoundInclusivity == "Inclusive" &&
                fact.BindingRole == "Caller");
            Assert.Equal("Explicit", evidence.BoundedQuery.NativeLimit.Kind);
            Assert.Equal(128, evidence.BoundedQuery.NativeLimit.Value);
            Assert.True(evidence.BoundedQuery.HasLookahead);
            Assert.False(evidence.BoundedQuery.HasContinuation);
            Assert.Equal("sequence", Assert.Single(evidence.BoundedQuery.Ordering).LogicalColumn);
            Assert.Equal("Ascending", evidence.BoundedQuery.Ordering[0].Direction);
            Assert.Null(evidence.BoundedQuery.Ordering[0].NullPlacement);
            Assert.True(evidence.BoundedQuery.Projection.AllColumns);
            Assert.Empty(evidence.BoundedQuery.Projection.LogicalColumns);
            Assert.Equal("Collected", evidence.Plan.Availability);
            Assert.Equal("EstimatedExplain", evidence.Plan.Provenance);
            Assert.True(evidence.Plan.ChoseExpectedIndex);
            Assert.NotNull(evidence.Plan.ChosenPhysicalIndexId);
            var node = Assert.Single(evidence.Plan.Nodes!);
            Assert.Equal("IndexSearch", node.Operation);
            Assert.Equal("elsa_structured_logs_sequence_order", node.LogicalIndexName);
            Assert.Equal(evidence.Plan.ChosenPhysicalIndexId, node.IndexId);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public async Task SQLite_ReadAfter_callback_evidence_survives_file_reload_and_typed_admission()
    {
        var root = Directory.CreateTempSubdirectory("structured-evidence-capture-");
        var connectionString = $"Data Source={Path.Combine(root.FullName, "diagnostics.db")};Mode=ReadWriteCreate;Cache=Shared";
        try
        {
            var observed = await ProviderProbe.ReadAsync("sqlite", connectionString, CancellationToken.None);
            var repositoryRoot = SourceProvenance.FindRepositoryRoot();
            var request = Request() with
            {
                MeasurementSetId = "structured-evidence-capture",
                CommitSha = SourceProvenance.CurrentHead(repositoryRoot),
                HarnessAssemblySha256 = SourceProvenance.HarnessAssemblySha256(),
                PackageVersions = ProviderPackageProvenance.CurrentVersions(
                    repositoryRoot, DiagnosticsDurableHistoryAdapter.AdapterId, "sqlite"),
                NativePlanEvidenceReference = NativePlanEvidenceStaging.ReferenceFor(
                    DiagnosticsDurableHistoryWorkload.WorkloadId,
                    "sqlite",
                    "structured-evidence-capture"),
                ProviderVersion = observed.Version,
                ProviderTopology = observed.Topology,
                ProviderConfiguration = observed.Configuration
            };
            request = request with { CompositionFingerprint = BenchmarkCompositionFingerprint.Describe(request).Fingerprint };
            ArtifactSafety.ValidateRequest(request);

            await using var adapter = new DiagnosticsDurableHistoryAdapter(
                request,
                connectionString,
                root.FullName,
                captureStructuredEvidence: true);
            await adapter.PrepareAsync(CancellationToken.None);
            var scopes = await adapter.OpenScopedClientsAsync(CancellationToken.None);
            var maximumCommittedSequence = 0L;
            const int acknowledgementWindow = 1_000;
            var acknowledgements = new List<Task<StructuredLogEntry>>(acknowledgementWindow);
            for (var index = 0; index < DiagnosticsDurableHistoryWorkload.AppendedRecordsPerStream; index++)
            {
                acknowledgements.Add(scopes.Primary.StructuredLogs.AppendAsync(
                    new StructuredLogEntry
                    {
                        Sequence = index + 1,
                        Message = $"scope-secret-{index}",
                        Category = "structured-evidence",
                        SourceId = "scope-secret"
                    },
                    CancellationToken.None).AsTask());
                if (acknowledgements.Count != acknowledgementWindow)
                    continue;

                maximumCommittedSequence = Math.Max(
                    maximumCommittedSequence,
                    (await Task.WhenAll(acknowledgements)).Max(entry => entry.Sequence));
                acknowledgements.Clear();
            }
            if (acknowledgements.Count != 0)
                maximumCommittedSequence = Math.Max(
                    maximumCommittedSequence,
                    (await Task.WhenAll(acknowledgements)).Max(entry => entry.Sequence));
            await adapter.FlushAsync(CancellationToken.None);
            var highWater = await scopes.Primary.StructuredLogs.GetHighWaterMarkAsync(CancellationToken.None);
            Assert.Equal(DiagnosticsDurableHistoryWorkload.AppendedRecordsPerStream, highWater);
            Assert.Equal(highWater, maximumCommittedSequence);

            adapter.CommandObserver.ClearCommands();
            adapter.ClearStructuredEvidence();
            var page = await scopes.Primary.StructuredLogs.ReadAfterAsync(
                afterCursor: null,
                filter: StructuredLogFilter.None,
                maxCount: DiagnosticsDurableHistoryWorkload.QueryLimit,
                cancellationToken: CancellationToken.None);
            var callbackEvidence = Assert.Single(adapter.StructuredEvidence);
            Assert.Equal("SQLite", callbackEvidence.Provider);
            Assert.Equal(observed.Version, callbackEvidence.ProviderVersion);
            Assert.Equal(request.ProviderVersion, callbackEvidence.ProviderVersion);
            Assert.Equal(DiagnosticsDurableHistoryWorkload.QueryLimit, page.Entries.Count);
            Assert.All(page.Entries, entry =>
            {
                Assert.StartsWith("scope-secret-", entry.Message, StringComparison.Ordinal);
                Assert.Equal("scope-secret", entry.SourceId);
            });
            Assert.Single(adapter.CommandObserver.Commands);

            var specification = DiagnosticsNativePlanContract.For(
                DiagnosticsNativePlanContract.GroundworkAdapter,
                "structured-log-replay");
            var route = new NativeRouteEvidence(
                "structured-log-replay",
                string.Empty,
                string.Empty,
                DiagnosticsNativePlanContract.IndexSearchPlanClassification,
                DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("sqlite", specification),
                checked((int)highWater),
                true,
                false,
                DiagnosticsDurableHistoryWorkload.QueryLimit,
                page.Entries.Count)
            {
                NativeFetchLimit = callbackEvidence.BoundedQuery?.NativeLimit.Value ?? 0,
                StructuredEvidence = callbackEvidence
            };

            var routeIdentities = DiagnosticsDurableHistoryWorkload.NativeRouteCardinalities.Keys
                .Order(StringComparer.Ordinal)
                .ToArray();
            var blockedRoutes = routeIdentities
                .Where(identity => !string.Equals(identity, route.RouteIdentity, StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(
                routeIdentities,
                new[] { route.RouteIdentity }
                    .Concat(blockedRoutes)
                    .Order(StringComparer.Ordinal)
                    .ToArray());

            var artifactDirectory = Path.Combine(root.FullName, "artifacts");
            Directory.CreateDirectory(artifactDirectory);
            var evidenceDocument = new NativePlanEvidenceDocument(
                2,
                request.ComparisonCohortId,
                request.MeasurementSetId,
                request.WorkloadId,
                request.WorkloadVersion,
                request.Provider,
                request.Adapter,
                request.PhysicalForm,
                request.Scale,
                request.CommitSha,
                request.HarnessAssemblySha256,
                request.CompositionFingerprint,
                request.HostFingerprintSha256,
                request.ProviderVersion,
                request.ProviderTopology,
                request.ProviderConfiguration,
                request.Seed,
                request.InputFingerprintSha256,
                request.NativePlanIdentity,
                [route],
                DiagnosticsNativePlanContract.BlockedRouteContract,
                blockedRoutes);
            var evidenceBytes = JsonSerializer.SerializeToUtf8Bytes(evidenceDocument, ArtifactStore.JsonOptions);
            var evidenceDigest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(evidenceBytes)).ToLowerInvariant();
            var evidencePath = ArtifactStore.EvidencePath(artifactDirectory, request.NativePlanEvidenceReference);
            await File.WriteAllBytesAsync(evidencePath, evidenceBytes);

            var artifactRequest = request with
            {
                NativePlanContentSha256 = evidenceDigest,
                ProcessKind = ProcessKind.Warmup,
                ProcessIndex = 0
            };
            var correctness = new CorrectnessEvidence(
                DiagnosticsDurableHistoryWorkload.ExpectedResultDigest,
                observed.Version,
                observed.Topology,
                observed.Configuration,
                new NativePlanEvidence(
                    artifactRequest.NativePlanIdentity,
                    artifactRequest.NativePlanEvidenceReference,
                    evidenceDigest,
                    [route])
                {
                    RouteContract = DiagnosticsNativePlanContract.BlockedRouteContract,
                    BlockedRoutes = blockedRoutes
                });

            // This envelope proves persistence and correctness admission only. Route cardinality,
            // page count, native bound, and plan facts come from the actual callback/read above; no
            // timing or comparison verdict is claimed here.
            var processArtifact = new ProcessArtifact(
                2,
                artifactRequest,
                BenchmarkProtocol.Acceptance,
                true,
                correctness,
                [],
                new MachineMetadata(
                    "test-os",
                    "test-runtime",
                    "X64",
                    "X64",
                    1,
                    artifactRequest.HostFingerprintSha256,
                    "2026-09-06T00:00:00Z"));
            ArtifactStore.Write(artifactDirectory, processArtifact);
            ArtifactStore.WriteManifest(artifactDirectory);
            var artifactSet = ArtifactStore.ReadAll(artifactDirectory);
            var reloadedArtifact = Assert.Single(artifactSet.Artifacts);
            ArtifactAdmission.ValidateCorrectness(
                WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot()).Workloads[
                    DiagnosticsDurableHistoryWorkload.WorkloadId],
                reloadedArtifact.Request,
                reloadedArtifact.Correctness,
                artifactDirectory);

            var reloadedRoute = Assert.Single(reloadedArtifact.Correctness.NativePlan.Routes);
            Assert.Empty(reloadedRoute.RawPlanReference);
            Assert.Empty(reloadedRoute.RawPlanSha256);
            Assert.Equal(callbackEvidence.Identity, reloadedRoute.StructuredEvidence!.Identity);
            Assert.Equal(callbackEvidence.Target, reloadedRoute.StructuredEvidence.Target);
            var structuredJson = JsonSerializer.Serialize(reloadedRoute.StructuredEvidence, ArtifactStore.JsonOptions);
            Assert.DoesNotContain("spec094-primary", structuredJson, StringComparison.Ordinal);
            Assert.DoesNotContain("spec094-diagnostics", structuredJson, StringComparison.Ordinal);
            var persistedEvidence = await File.ReadAllTextAsync(evidencePath);
            Assert.DoesNotContain("scope-secret", persistedEvidence, StringComparison.Ordinal);
            Assert.DoesNotContain(root.FullName, persistedEvidence, StringComparison.Ordinal);
            Assert.DoesNotContain(connectionString, persistedEvidence, StringComparison.Ordinal);
            Assert.DoesNotContain("SELECT", persistedEvidence, StringComparison.OrdinalIgnoreCase);

            var tamperedCorrectness = reloadedArtifact.Correctness with
            {
                NativePlan = reloadedArtifact.Correctness.NativePlan with
                {
                    Routes = reloadedArtifact.Correctness.NativePlan.Routes.Select(item =>
                        item with
                        {
                            StructuredEvidence = item.StructuredEvidence! with
                            {
                                Identity = item.StructuredEvidence.Identity with { CaptureId = Guid.NewGuid() }
                            }
                        }).ToArray()
                }
            };
            var tamper = Assert.Throws<PerformanceContractException>(() => ArtifactAdmission.ValidateCorrectness(
                WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot()).Workloads[
                    DiagnosticsDurableHistoryWorkload.WorkloadId],
                reloadedArtifact.Request,
                tamperedCorrectness,
                artifactDirectory));
            Assert.Contains("does not match", tamper.Message, StringComparison.Ordinal);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public async Task SQLite_native_capture_helper_uses_typed_callbacks_for_recent_and_replay_without_raw_plan_references()
    {
        var root = Directory.CreateTempSubdirectory("structured-evidence-native-capture-");
        var connectionString = $"Data Source={Path.Combine(root.FullName, "diagnostics.db")};Mode=ReadWriteCreate;Cache=Shared";
        try
        {
            var observed = await ProviderProbe.ReadAsync("sqlite", connectionString, CancellationToken.None);
            var request = Request() with
            {
                MeasurementSetId = "structured-evidence-native-capture",
                NativePlanEvidenceReference = NativePlanEvidenceStaging.ReferenceFor(
                    DiagnosticsDurableHistoryWorkload.WorkloadId,
                    "sqlite",
                    "structured-evidence-native-capture"),
                ProviderVersion = observed.Version,
                ProviderTopology = observed.Topology,
                ProviderConfiguration = observed.Configuration
            };
            await using var adapter = new DiagnosticsDurableHistoryAdapter(
                request,
                connectionString,
                root.FullName,
                captureStructuredEvidence: true);
            await adapter.PrepareAsync(CancellationToken.None);
            var scopes = await adapter.OpenScopedClientsAsync(CancellationToken.None);
            await DiagnosticsNativePlanCapture.SeedStructuredLogFixtureAsync(
                scopes.Primary.StructuredLogs,
                CancellationToken.None);
            await adapter.FlushAsync(CancellationToken.None);

            var routes = new List<NativeRouteEvidence>(2);
            foreach (var route in new[] { "structured-log-recent", "structured-log-replay" })
            {
                var captured = await DiagnosticsNativePlanCapture.CaptureStructuredEvidenceRouteAsync(
                    adapter,
                    scopes.Primary,
                    request,
                    observed,
                    route,
                    DiagnosticsDurableHistoryWorkload.QueryLimit,
                    CancellationToken.None);
                routes.Add(captured);

                Assert.Empty(captured.RawPlanReference);
                Assert.Empty(captured.RawPlanSha256);
                Assert.Equal(127, captured.FiniteLimit);
                Assert.Equal(127, captured.MaterializedCandidateCount);
                Assert.Equal(128, captured.NativeFetchLimit);
                Assert.NotNull(captured.StructuredEvidence);
                Assert.Equal(observed.Version, captured.StructuredEvidence!.ProviderVersion);
                Assert.Equal("Succeeded", captured.StructuredEvidence.Outcome);
                Assert.Equal("Collected", captured.StructuredEvidence.ShapeAvailability);
                Assert.Equal("Collected", captured.StructuredEvidence.Plan.Availability);
                Assert.Equal("EstimatedExplain", captured.StructuredEvidence.Plan.Provenance);
            }

            var outputDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "capture")).FullName;
            var digest = NativePlanEvidenceStaging.Write(
                outputDirectory,
                DiagnosticsNativePlanCapture.CreateDocument(request, observed, routes));

            Assert.Matches("^[0-9a-f]{64}$", digest);
            var document = NativePlanEvidenceStaging.Read(
                Path.Combine(outputDirectory, request.NativePlanEvidenceReference));
            Assert.Equal(
                new[] { "structured-log-recent", "structured-log-replay" },
                document.Routes.Select(route => route.RouteIdentity).Order(StringComparer.Ordinal));
            Assert.All(document.Routes, route =>
            {
                Assert.Empty(route.RawPlanReference);
                Assert.Empty(route.RawPlanSha256);
                Assert.Equal(127, route.FiniteLimit);
                Assert.Equal(127, route.MaterializedCandidateCount);
                Assert.Equal(128, route.NativeFetchLimit);
                Assert.Equal(observed.Version, route.StructuredEvidence!.ProviderVersion);
            });
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public async Task SQLite_reopen_samples_release_prior_restart_compositions_outside_timing()
    {
        var root = Directory.CreateTempSubdirectory("diagnostics-reopen-reset-sqlite-");
        var connectionString = $"Data Source={Path.Combine(root.FullName, "diagnostics.db")};Mode=ReadWriteCreate;Cache=Shared";
        try
        {
            var observed = await ProviderProbe.ReadAsync("sqlite", connectionString, CancellationToken.None);
            var request = Request() with
            {
                ProviderVersion = observed.Version,
                ProviderTopology = observed.Topology,
                ProviderConfiguration = observed.Configuration
            };
            await using var adapter = new DiagnosticsDurableHistoryAdapter(request, connectionString, root.FullName);
            await adapter.PrepareAsync(CancellationToken.None);

            await adapter.ReopenClientAsync(CancellationToken.None);
            await adapter.ReopenClientAsync(CancellationToken.None);
            Assert.Equal(4, adapter.ActiveCompositionCount);

            await adapter.ResetReopenedClientsAsync(CancellationToken.None);

            Assert.Equal(2, adapter.ActiveCompositionCount);
            var scopes = await adapter.OpenScopedClientsAsync(CancellationToken.None);
            Assert.NotSame(scopes.Primary, scopes.Secondary);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public async Task SQLite_durability_poll_retries_a_transient_schema_lock()
    {
        var store = new ProbeOpenTelemetryStore(throwTransientLock: true);
        var tracking = new DiagnosticsDurableHistoryAdapter.TrackingOpenTelemetryStore(store);

        await tracking.WaitForDurabilityAsync(CancellationToken.None);

        Assert.Equal(2, store.DiagnosticsReadCount);
    }

    [Fact]
    public async Task Durability_poll_fails_immediately_when_the_background_drain_dropped_records()
    {
        var store = new ProbeOpenTelemetryStore(
            diagnostics: EmptyDiagnostics() with
            {
                DroppedTraceCount = 2,
                DroppedSpanCount = 3,
                DroppedMetricPointCount = 5,
                DroppedLogRecordCount = 7
            });
        var tracking = new DiagnosticsDurableHistoryAdapter.TrackingOpenTelemetryStore(store);

        var exception = await Assert.ThrowsAsync<PerformanceContractException>(
            () => tracking.WaitForDurabilityAsync(CancellationToken.None));

        Assert.Equal(1, store.DiagnosticsReadCount);
        Assert.Contains("traces: 2, spans: 3, metric points: 5, logs: 7", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Durability_poll_timeout_reports_the_last_observed_and_required_counts()
    {
        var store = new ProbeOpenTelemetryStore();
        var tracking = new DiagnosticsDurableHistoryAdapter.TrackingOpenTelemetryStore(
            store,
            TimeSpan.FromMilliseconds(20));
        await tracking.WriteAsync(new OpenTelemetryBatch(
            Resources:
            [
                new TelemetryResource(
                    "resource-1",
                    "service-1",
                    null,
                    null,
                    new Dictionary<string, string?>(),
                    DateTimeOffset.UtcNow,
                    TelemetryResourceStatus.Active)
            ],
            Traces: [],
            Spans: [],
            Instruments: [],
            MetricPoints: [],
            Logs: []));

        var exception = await Assert.ThrowsAsync<PerformanceContractException>(
            () => tracking.WaitForDurabilityAsync(CancellationToken.None));

        Assert.InRange(store.DiagnosticsReadCount, 1, 2);
        Assert.Contains("resources 0/1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("trace probe not required", exception.Message, StringComparison.Ordinal);
        Assert.Contains("diagnostics counts insufficient", exception.Message, StringComparison.Ordinal);
        Assert.Contains("attempt:", exception.Message, StringComparison.Ordinal);
        Assert.Contains("duration:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Durability_poll_counts_repeated_catalog_upserts_once()
    {
        var diagnostics = EmptyDiagnostics() with
        {
            ResourceCount = 1,
            MetricInstrumentCount = 1
        };
        var tracking = new DiagnosticsDurableHistoryAdapter.TrackingOpenTelemetryStore(
            new ProbeOpenTelemetryStore(diagnostics),
            TimeSpan.FromMilliseconds(20));
        var resource = new TelemetryResource(
            "resource-1",
            "service-1",
            null,
            null,
            new Dictionary<string, string?>(),
            DateTimeOffset.UtcNow,
            TelemetryResourceStatus.Active);
        var instrument = new MetricInstrument(
            "instrument-1",
            resource.Id,
            "duration",
            "ms",
            null,
            MetricKind.Gauge,
            new Dictionary<string, string?>());
        var batch = new OpenTelemetryBatch([resource], [], [], [instrument], [], []);

        await tracking.WriteAsync(batch);
        await tracking.WriteAsync(batch);
        await tracking.WaitForDurabilityAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Durability_poll_enforces_its_deadline_when_a_provider_probe_never_completes()
    {
        var store = new ProbeOpenTelemetryStore(blockDiagnostics: true);
        var tracking = new DiagnosticsDurableHistoryAdapter.TrackingOpenTelemetryStore(
            store,
            TimeSpan.FromMilliseconds(20));

        var exception = await Assert.ThrowsAsync<PerformanceContractException>(
            () => tracking.WaitForDurabilityAsync(CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Contains("untimed flush budget", exception.Message, StringComparison.Ordinal);
        Assert.Contains("diagnostics read in progress", exception.Message, StringComparison.Ordinal);
        Assert.Contains("attempt: 1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("duration:", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, store.DiagnosticsReadCount);
    }

    [Fact]
    public async Task Durability_poll_reports_a_missing_trace_after_counts_become_visible()
    {
        var trace = Trace("trace-missing");
        var diagnostics = EmptyDiagnostics() with
        {
            TraceCapacity = 1,
            TraceCount = 1
        };
        var store = new ProbeOpenTelemetryStore(diagnostics, blockTrace: false, returnMissingTrace: true);
        var tracking = new DiagnosticsDurableHistoryAdapter.TrackingOpenTelemetryStore(
            store,
            TimeSpan.FromMilliseconds(20));

        await tracking.WriteAsync(new OpenTelemetryBatch([], [trace], [], [], [], []));
        var exception = await Assert.ThrowsAsync<PerformanceContractException>(
            () => tracking.WaitForDurabilityAsync(CancellationToken.None));

        Assert.Contains("trace read missing", exception.Message, StringComparison.Ordinal);
        Assert.Contains("attempt:", exception.Message, StringComparison.Ordinal);
        Assert.Contains("duration:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Durability_poll_reports_a_trace_read_in_progress_when_detail_never_completes()
    {
        var trace = Trace("trace-blocked");
        var diagnostics = EmptyDiagnostics() with
        {
            TraceCapacity = 1,
            TraceCount = 1
        };
        var store = new ProbeOpenTelemetryStore(diagnostics, blockTrace: true);
        var tracking = new DiagnosticsDurableHistoryAdapter.TrackingOpenTelemetryStore(
            store,
            TimeSpan.FromMilliseconds(20));

        await tracking.WriteAsync(new OpenTelemetryBatch([], [trace], [], [], [], []));
        var exception = await Assert.ThrowsAsync<PerformanceContractException>(
            () => tracking.WaitForDurabilityAsync(CancellationToken.None));

        Assert.Contains("trace read in progress", exception.Message, StringComparison.Ordinal);
        Assert.Contains("attempt:", exception.Message, StringComparison.Ordinal);
        Assert.Contains("duration:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Durability_poll_uses_a_retained_trace_without_the_native_plan_fanout()
    {
        var diagnostics = EmptyDiagnostics() with
        {
            TraceCapacity = 2,
            SpanCapacity = 1,
            LogRecordCapacity = 1,
            TraceCount = 2,
            SpanCount = 1,
            LogRecordCount = 1
        };
        var store = new ProbeOpenTelemetryStore(diagnostics);
        var tracking = new DiagnosticsDurableHistoryAdapter.TrackingOpenTelemetryStore(store);
        var cheapTrace = Trace("trace-cheap");
        var fanoutTrace = Trace("TRACE-FANOUT");
        var span = Span("trace-fanout");
        var log = Log("trace-fanout");

        await tracking.WriteAsync(new OpenTelemetryBatch([], [cheapTrace, fanoutTrace], [span], [], [], [log]));
        await tracking.WaitForDurabilityAsync(CancellationToken.None);

        Assert.Equal([cheapTrace.TraceId], store.TraceReads);
    }

    [Fact]
    public void Frozen_sequence_and_native_route_cardinalities_match_the_catalog()
    {
        var workload = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot()).Workloads[
            DiagnosticsDurableHistoryWorkload.WorkloadId];

        Assert.Equal(workload.OperationSequence, DiagnosticsDurableHistoryWorkload.OperationIds);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.ExpectedInputFingerprint, workload.Input.FingerprintSha256);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.ExpectedResultDigest, workload.Correctness.ResultDigestSha256);
        Assert.Equal(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["resources-by-last-seen"] = DiagnosticsDurableHistoryWorkload.ResourceCount,
                ["resources-by-status"] = DiagnosticsDurableHistoryWorkload.ResourceCount,
                ["resources-by-service"] = DiagnosticsDurableHistoryWorkload.ResourceCount,
                ["traces-by-last-seen"] = DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
                ["trace-detail"] = DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
                ["metrics-by-last-seen"] = DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
                ["logs-by-last-seen"] = DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
                ["structured-log-recent"] = DiagnosticsDurableHistoryWorkload.AppendedRecordsPerStream,
                ["structured-log-replay"] = DiagnosticsDurableHistoryWorkload.AppendedRecordsPerStream
            },
            DiagnosticsDurableHistoryWorkload.NativeRouteCardinalities);
    }

    [Fact]
    public void Diagnostics_admission_retains_the_unratified_absolute_budget_reason()
    {
        var workload = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot()).Workloads[
            DiagnosticsDurableHistoryWorkload.WorkloadId];

        Assert.True(BenchmarkAdmissionGuard.TryGetBlockedReason(workload, out var reason));
        Assert.Equal(ReproducibleWorkloadScenarioCatalog.DiagnosticsBlockedReasonCode, reason);
    }

    [Fact]
    public void Trace_detail_artifacts_are_partitioned_by_their_exact_logical_index()
    {
        var directory = Directory.CreateTempSubdirectory("diagnostics-native-pages-");
        try
        {
            var before = Path.Combine(directory.FullName, "000001-sqlite-optimizer-selected-before.txt");
            var spanFirst = Path.Combine(directory.FullName, "000002-sqlite-optimizer-selected-elsa_otel_spans_trace_detail.txt");
            var log = Path.Combine(directory.FullName, "000003-sqlite-optimizer-selected-elsa_otel_logs_trace_detail.txt");
            var spanSecond = Path.Combine(directory.FullName, "000004-sqlite-optimizer-selected-elsa_otel_spans_trace_detail.txt");
            File.WriteAllText(before, "before");
            File.WriteAllText(spanFirst, "span one");
            File.WriteAllText(log, "log");
            File.WriteAllText(spanSecond, "span two");

            var beforeTraceDetail = new HashSet<string>(StringComparer.Ordinal) { before };
            var spans = DiagnosticsNativePlanCapture.RequireNativeArtifacts(
                directory.FullName,
                beforeTraceDetail,
                "sqlite",
                "elsa_otel_spans_trace_detail",
                2);
            var logs = DiagnosticsNativePlanCapture.RequireNativeArtifacts(
                directory.FullName,
                beforeTraceDetail,
                "sqlite",
                "elsa_otel_logs_trace_detail",
                1);

            Assert.Equal([spanFirst, spanSecond], spans);
            Assert.Equal([log], logs);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void PostgreSql_diagnostics_fixture_refreshes_statistics_for_every_distinct_plan_table()
    {
        var commands = DiagnosticsNativePlanCapture.PostgreSqlAnalyzeCommands(
            DiagnosticsNativePlanContract.GroundworkAdapter);

        Assert.NotEmpty(commands);
        Assert.Equal(commands.Count, commands.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("ANALYZE \"elsa_otel_trace_summaries_v3\"", commands);
        Assert.Contains("ANALYZE \"elsa_otel_spans_v2\"", commands);
        Assert.Contains("ANALYZE \"elsa_otel_logs_v2\"", commands);
        Assert.All(commands, command => Assert.Matches("^ANALYZE \\\"[A-Za-z0-9_]+\\\"$", command));
    }

    [Fact]
    public void Failed_explain_artifacts_are_copied_out_before_the_temporary_capture_directory_is_deleted()
    {
        var explain = Directory.CreateTempSubdirectory("diagnostics-failed-explain-");
        var output = Directory.CreateTempSubdirectory("diagnostics-failed-output-");
        try
        {
            File.WriteAllText(Path.Combine(explain.FullName, "000002-postgresql-plan.json"), "{\"plan\":\"second\"}");
            File.WriteAllText(Path.Combine(explain.FullName, "000001-postgresql-plan.json"), "{\"plan\":\"first\"}");
            File.WriteAllText(Path.Combine(explain.FullName, "ignored.txt"), "ignored");

            var retained = DiagnosticsNativePlanCapture.PreserveFailedExplainArtifacts(
                explain.FullName,
                output.FullName,
                "postgresql",
                "diagnostics-set");

            Assert.Equal(
                [
                    "diagnostics.postgresql.diagnostics-set.failed-explain-1.json",
                    "diagnostics.postgresql.diagnostics-set.failed-explain-2.json"
                ],
                retained);
            using var first = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.FullName, retained[0])));
            using var second = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.FullName, retained[1])));
            Assert.Equal("first", first.RootElement.GetProperty("plan").GetString());
            Assert.Equal("second", second.RootElement.GetProperty("plan").GetString());
            Assert.False(File.Exists(Path.Combine(output.FullName, "ignored.txt")));
        }
        finally
        {
            explain.Delete(true);
            output.Delete(true);
        }
    }

    [Fact]
    public async Task Failed_explain_capture_retains_raw_provider_output_restores_environment_and_rethrows_original_failure()
    {
        var explain = Directory.CreateTempSubdirectory("diagnostics-capture-boundary-explain-");
        var output = Directory.CreateTempSubdirectory("diagnostics-capture-boundary-output-");
        var failure = new PerformanceContractException("injected capture failure");
        var previousFlag = Environment.GetEnvironmentVariable("GW_EXPLAIN_ASSERT");
        var previousDirectory = Environment.GetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR");
        Environment.SetEnvironmentVariable("GW_EXPLAIN_ASSERT", "previous-flag");
        Environment.SetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR", "previous-directory");
        try
        {
            var exception = await Assert.ThrowsAsync<PerformanceContractException>(() =>
                DiagnosticsNativePlanCapture.ExecuteExplainCaptureAsync<string>(
                    explain.FullName,
                    output.FullName,
                    "sqlserver",
                    "capture-set",
                    async () =>
                    {
                        Environment.SetEnvironmentVariable("GW_EXPLAIN_ASSERT", "callback-flag");
                        Environment.SetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR", "callback-directory");
                        await File.WriteAllTextAsync(
                            Path.Combine(explain.FullName, "000001-sqlserver-plan.xml"),
                            "<ShowPlanXML xmlns=\"http://schemas.microsoft.com/sqlserver/2004/07/showplan\"><RelOp PhysicalOp=\"Index Scan\" /></ShowPlanXML>");
                        await File.WriteAllTextAsync(Path.Combine(explain.FullName, "000002-malformed.xml"), "<invalid");
                        await File.WriteAllTextAsync(Path.Combine(explain.FullName, "000003-unsafe.xml"),
                            "<ShowPlanXML><RelOp Note=\"https://example.invalid/private\" /></ShowPlanXML>");
                        await File.WriteAllTextAsync(Path.Combine(explain.FullName, "000004-oversized.xml"),
                            new string('x', 16 * 1024 * 1024 + 1));
                        throw failure;
                    }));

            Assert.Same(failure, exception);
            var retained = Path.Combine(output.FullName, "diagnostics.sqlserver.capture-set.failed-explain-1.xml");
            Assert.True(File.Exists(retained));
            Assert.Single(Directory.EnumerateFiles(output.FullName));
            Assert.DoesNotContain("http://", File.ReadAllText(retained), StringComparison.Ordinal);
            ArtifactStore.ValidateRawPlanFile(retained);
            Assert.Empty(Directory.EnumerateFiles(output.FullName, "*.native-plan.json"));
            Assert.False(Directory.Exists(explain.FullName));
            Assert.Equal("previous-flag", Environment.GetEnvironmentVariable("GW_EXPLAIN_ASSERT"));
            Assert.Equal("previous-directory", Environment.GetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ASSERT", previousFlag);
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR", previousDirectory);
            if (explain.Exists)
                explain.Delete(true);
            if (output.Exists)
                output.Delete(true);
        }
    }

    [Fact]
    public void Blocked_explain_artifacts_are_copied_with_value_free_route_diagnostics()
    {
        var explain = Directory.CreateTempSubdirectory("diagnostics-blocked-explain-");
        var output = Directory.CreateTempSubdirectory("diagnostics-blocked-output-");
        var xmlExplain = Directory.CreateTempSubdirectory("diagnostics-blocked-xml-explain-");
        var xmlOutput = Directory.CreateTempSubdirectory("diagnostics-blocked-xml-output-");
        try
        {
            var beforePath = Path.Combine(explain.FullName, "000001-postgresql-before.json");
            var afterPath = Path.Combine(explain.FullName, "000002-postgresql-plan.json");
            File.WriteAllText(beforePath, "{\"plan\":\"before\"}");
            File.WriteAllText(afterPath, "{\"plan\":\"scan\"}");

            var retained = DiagnosticsNativePlanCapture.PreserveBlockedExplainArtifacts(
                explain.FullName,
                new HashSet<string>(StringComparer.Ordinal) { beforePath },
                output.FullName,
                "postgresql",
                "diagnostics-set",
                "resources-by-last-seen");

            Assert.Single(retained);
            Assert.Equal("{\"plan\":\"scan\"}", File.ReadAllText(Path.Combine(output.FullName, retained[0].Reference)));
            Assert.Matches("^[0-9a-f]{64}$", retained[0].Sha256);

            var xmlPath = Path.Combine(xmlExplain.FullName, "000001-sqlserver-plan.xml");
            File.WriteAllText(
                xmlPath,
                "<ShowPlanXML xmlns=\"http://schemas.microsoft.com/sqlserver/2004/07/showplan\"><RelOp PhysicalOp=\"Index Scan\" /></ShowPlanXML>");
            File.WriteAllText(Path.Combine(xmlExplain.FullName, "000002-malformed.xml"), "<invalid");
            File.WriteAllText(
                Path.Combine(xmlExplain.FullName, "000003-unsafe.xml"),
                "<ShowPlanXML><RelOp Note=\"https://example.invalid/private\" /></ShowPlanXML>");
            File.WriteAllText(
                Path.Combine(xmlExplain.FullName, "000004-oversized.xml"),
                new string('x', 16 * 1024 * 1024 + 1));
            var xmlRetained = DiagnosticsNativePlanCapture.PreserveBlockedExplainArtifacts(
                xmlExplain.FullName,
                new HashSet<string>(StringComparer.Ordinal),
                xmlOutput.FullName,
                "sqlserver",
                "diagnostics-set",
                "resources-by-last-seen");

            Assert.Single(xmlRetained);
            var normalizedXml = File.ReadAllText(Path.Combine(xmlOutput.FullName, xmlRetained[0].Reference));
            Assert.DoesNotContain("http://", normalizedXml, StringComparison.Ordinal);
            Assert.Contains("<ShowPlanXML>", normalizedXml, StringComparison.Ordinal);
            Assert.Single(Directory.EnumerateFiles(xmlOutput.FullName));

            var request = Request() with { Provider = "sqlserver", MeasurementSetId = "diagnostics-set" };
            var blockedRoute = new DiagnosticsBlockedRouteEvidence(
                "resources-by-last-seen", "route-plan-validation", "native-plan.index-scan", xmlRetained);
            var digest = NativePlanEvidenceStaging.WriteBlockedCapture(xmlOutput.FullName, request, [blockedRoute]);
            var diagnosticPath = Path.Combine(xmlOutput.FullName, "diagnostics.sqlserver.diagnostics-set.blocked-capture.json");
            var document = JsonSerializer.Deserialize<DiagnosticsBlockedCaptureDocument>(
                File.ReadAllText(diagnosticPath), ArtifactStore.JsonOptions)!;
            Assert.Equal(digest, ArtifactStore.HashFile(diagnosticPath));
            Assert.Equal("native-plan.index-scan", Assert.Single(document.Routes).ReasonCode);
            Assert.Equal("route-plan-validation", Assert.Single(document.Routes).FailurePhase);
            Assert.Equal(xmlRetained[0], Assert.Single(document.Routes[0].RawPlans));
            Assert.Empty(Directory.EnumerateFiles(xmlOutput.FullName, "*.native-plan.json"));
        }
        finally
        {
            explain.Delete(true);
            output.Delete(true);
            xmlExplain.Delete(true);
            xmlOutput.Delete(true);
        }
    }

    [Fact]
    public void Trace_detail_page_is_hashed_before_validation_and_published_only_after_admission()
    {
        var specification = DiagnosticsNativePlanContract.TraceDetailConstituents(
                DiagnosticsNativePlanContract.GroundworkAdapter)
            .Single(item => item.RouteIdentity == "trace-detail/spans-by-trace-key-start-id");
        var route = new DiagnosticsNativeRouteSpec(
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            specification.Ordering[0].Column,
            specification.PredicateColumn,
            specification.PhysicalCardinality,
            specification.FiniteLimit,
            specification.StorageScopeRequired,
            false,
            specification.Ordering);
        var physicalIndex = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("sqlite", route);
        var command =
            "SELECT * FROM elsa_otel_spans_v2 WHERE __groundwork_scope = @scope AND traceKey = @traceKey " +
            "ORDER BY startTime ASC, __groundwork_ordinal_spanId ASC, sequence ASC LIMIT 127";
        var artifact = new DiagnosticsNativePlanArtifact(
            1,
            "sqlite",
            DiagnosticsNativePlanContract.GroundworkAdapter,
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            physicalIndex,
            command,
            $"2 0 SEARCH elsa_otel_spans_v2 USING INDEX {physicalIndex} (__groundwork_scope=? AND traceKey=?)");
        var evidence = new DiagnosticsTraceDetailConstituentEvidence(
            specification.RouteIdentity,
            "page.raw.json",
            "",
            "index-search",
            physicalIndex,
            command,
            specification.PhysicalCardinality,
            true,
            true,
            specification.FiniteLimit,
            specification.PublicRowBound,
            specification.PublicRowBound,
            specification.MaxInvocationCount,
            specification.MaxInvocationCount);
        var directory = Directory.CreateTempSubdirectory("diagnostics-native-page-publish-");
        var path = Path.Combine(directory.FullName, evidence.RawPlanReference);
        try
        {
            var digest = DiagnosticsNativePlanCapture.ValidateAndPublishTraceDetailPage(
                "sqlite",
                DiagnosticsNativePlanContract.GroundworkAdapter,
                evidence,
                artifact,
                path);

            Assert.Equal(64, digest.Length);
            Assert.True(File.Exists(path));
            Assert.Equal(digest, ArtifactStore.HashFile(path));

            File.Delete(path);
            Assert.Throws<PerformanceContractException>(() =>
                DiagnosticsNativePlanCapture.ValidateAndPublishTraceDetailPage(
                    "sqlite",
                    DiagnosticsNativePlanContract.GroundworkAdapter,
                    evidence,
                    artifact with { PhysicalIndexName = "wrong-index" },
                    path));
            Assert.False(File.Exists(path));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void Mongo_trace_detail_rejects_an_unrecognized_non_probe_read_operation()
    {
        var commands = new[]
        {
            new ProviderCommandEvent("mongodb.query", "{}", ProviderCommandKind.Read, false),
            new ProviderCommandEvent("mongodb.lookup", "{}", ProviderCommandKind.Read, false)
        };

        var exception = Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanCapture.RequireKnownMongoReadOperations(commands));

        Assert.Contains("mongodb.lookup", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mongo_trace_detail_rejects_a_read_without_command_identity()
    {
        var commands = new[]
        {
            new ProviderCommandEvent("mongodb.read", " ", ProviderCommandKind.Read, false)
        };

        var exception = Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanCapture.RequireKnownMongoReadOperations(commands));

        Assert.Contains("without command identity evidence", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mongo_trace_detail_binds_point_reads_by_physical_collection_instead_of_observer_order()
    {
        var specifications = DiagnosticsNativePlanContract.TraceDetailConstituents(
            DiagnosticsNativePlanContract.GroundworkAdapter);
        var summary = specifications.Single(item => item.RouteIdentity == "trace-detail/summary-by-trace-key");
        var resource = specifications.Single(item => item.RouteIdentity == "trace-detail/resources-by-id");
        var summaryCommand = MongoPointCommand(summary.TableName);
        var resourceCommand = MongoPointCommand(resource.TableName);
        var commands = new[]
        {
            new ProviderCommandEvent("mongodb.read", resourceCommand, ProviderCommandKind.Read, false),
            new ProviderCommandEvent("mongodb.read", summaryCommand, ProviderCommandKind.Read, false)
        };

        var classified = DiagnosticsNativePlanCapture.ClassifyMongoPointReads(commands, specifications);

        Assert.Equal(summaryCommand, Assert.Single(classified[summary.RouteIdentity]).CommandText);
        Assert.Equal(resourceCommand, Assert.Single(classified[resource.RouteIdentity]).CommandText);
    }

    [Fact]
    public void Mongo_trace_detail_rejects_a_point_read_for_an_undeclared_collection()
    {
        var specifications = DiagnosticsNativePlanContract.TraceDetailConstituents(
            DiagnosticsNativePlanContract.GroundworkAdapter);
        var commands = new[]
        {
            new ProviderCommandEvent(
                "mongodb.read",
                MongoPointCommand("elsa_unknown"),
                ProviderCommandKind.Read,
                false)
        };

        var exception = Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanCapture.ClassifyMongoPointReads(commands, specifications));

        Assert.Contains("exactly one constituent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bounded_resource_page_validation_requires_the_canonical_page_and_scope_count()
    {
        var page = new OpenTelemetryResourceResult(
            Enumerable.Range(0, DiagnosticsDurableHistoryWorkload.ResourceCount)
                .Reverse()
                .Take(DiagnosticsDurableHistoryWorkload.QueryLimit)
                .Select(ordinal => DiagnosticsDurableHistoryWorkload.ResourceFor(
                    ordinal,
                    DiagnosticsDurableHistoryWorkload.ServiceNameFor(ordinal)))
                .ToArray(),
            0);
        var diagnostics = DiagnosticsWithResourceCount(DiagnosticsDurableHistoryWorkload.ResourceCount);

        DiagnosticsNativePlanCapture.ValidateBoundedResourcePage(
            "resources-by-last-seen",
            page,
            diagnostics,
            DiagnosticsDurableHistoryWorkload.QueryLimit);
    }

    [Theory]
    [InlineData(127, false)]
    [InlineData(128, true)]
    public void Bounded_resource_page_validation_rejects_stale_or_duplicate_pages(
        int resourceCount,
        bool duplicatePage)
    {
        var page = duplicatePage
            ? new OpenTelemetryResourceResult(
                Enumerable.Repeat(
                    DiagnosticsDurableHistoryWorkload.ResourceFor(
                        DiagnosticsDurableHistoryWorkload.ResourceCount - 1,
                        DiagnosticsDurableHistoryWorkload.ServiceNameFor(DiagnosticsDurableHistoryWorkload.ResourceCount - 1)),
                    DiagnosticsDurableHistoryWorkload.QueryLimit)
                    .ToArray(),
                0)
            : new OpenTelemetryResourceResult(
                Enumerable.Range(0, DiagnosticsDurableHistoryWorkload.ResourceCount)
                    .Reverse()
                    .Take(DiagnosticsDurableHistoryWorkload.QueryLimit)
                    .Select(ordinal => DiagnosticsDurableHistoryWorkload.ResourceFor(
                        ordinal,
                        DiagnosticsDurableHistoryWorkload.ServiceNameFor(ordinal)))
                    .ToArray(),
                0);
        var exception = Assert.Throws<PerformanceContractException>(() =>
            DiagnosticsNativePlanCapture.ValidateBoundedResourcePage(
                "resources-by-last-seen",
                page,
                DiagnosticsWithResourceCount(resourceCount),
                DiagnosticsDurableHistoryWorkload.QueryLimit));

        Assert.Contains(
            duplicatePage ? "identity/order" : "scoped resources",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Process_identity_is_bound_into_storage_and_diagnostic_scopes()
    {
        var first = Request(processIndex: 1);
        var second = Request(processIndex: 2);

        Assert.NotEqual(
            DiagnosticsDurableHistoryAdapter.PersistenceScopeForTesting(first),
            DiagnosticsDurableHistoryAdapter.PersistenceScopeForTesting(second));
        Assert.NotEqual(
            DiagnosticsDurableHistoryAdapter.BindingScopeForTesting(first, "primary"),
            DiagnosticsDurableHistoryAdapter.BindingScopeForTesting(second, "primary"));
        Assert.InRange(
            DiagnosticsDurableHistoryAdapter.BindingTenantForTesting(first, "primary").Length,
            1,
            64);
        Assert.InRange(
            DiagnosticsDurableHistoryAdapter.BindingStorageScopeForTesting(first, "primary").Length,
            1,
            64);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    [InlineData("mongodb")]
    public void Native_plan_phase_identity_separates_capture_and_correctness_requests_without_changing_matrix_isolation(
        string provider)
    {
        // Capture uses the placeholder digest while correctness binds the staged evidence digest;
        // both requests intentionally retain the runner's measured/zero process identity.
        var capture = Request(provider, processIndex: 0) with
        {
            NativePlanContentSha256 = new string('0', 64)
        };
        var correctness = capture with
        {
            NativePlanContentSha256 = new string('f', 64)
        };
        var correctnessRestart = correctness with { };
        var warmup = correctness with { ProcessKind = ProcessKind.Warmup };
        var measured = correctness with { ProcessKind = ProcessKind.Measured, ProcessIndex = 1 };

        Assert.Equal(ProcessKind.Measured, capture.ProcessKind);
        Assert.Equal(capture.ProcessKind, correctness.ProcessKind);
        Assert.Equal(capture.ProcessIndex, correctness.ProcessIndex);
        Assert.NotEqual(
            DiagnosticsDurableHistoryAdapter.PersistenceScopeForTesting(capture),
            DiagnosticsDurableHistoryAdapter.PersistenceScopeForTesting(correctness));
        Assert.NotEqual(
            DiagnosticsDurableHistoryAdapter.BindingTenantForTesting(capture, "primary"),
            DiagnosticsDurableHistoryAdapter.BindingTenantForTesting(correctness, "primary"));
        Assert.NotEqual(
            DiagnosticsDurableHistoryAdapter.BindingStorageScopeForTesting(capture, "primary"),
            DiagnosticsDurableHistoryAdapter.BindingStorageScopeForTesting(correctness, "primary"));

        Assert.Equal(
            DiagnosticsDurableHistoryAdapter.BindingScopeForTesting(correctness, "primary"),
            DiagnosticsDurableHistoryAdapter.BindingScopeForTesting(correctnessRestart, "primary"));
        Assert.NotEqual(
            DiagnosticsDurableHistoryAdapter.BindingScopeForTesting(warmup, "primary"),
            DiagnosticsDurableHistoryAdapter.BindingScopeForTesting(measured, "primary"));
        Assert.NotEqual(
            DiagnosticsDurableHistoryAdapter.PersistenceScopeForTesting(warmup),
            DiagnosticsDurableHistoryAdapter.PersistenceScopeForTesting(measured));
    }

    private static OpenTelemetryStorageDiagnostics DiagnosticsWithResourceCount(int resourceCount) => new(
        TraceCapacity: 0,
        SpanCapacity: 0,
        MetricPointCapacity: 0,
        LogRecordCapacity: 0,
        ResourceCount: resourceCount,
        TraceCount: 0,
        SpanCount: 0,
        MetricInstrumentCount: 0,
        MetricPointCount: 0,
        LogRecordCount: 0,
        DroppedTraceCount: 0,
        DroppedSpanCount: 0,
        DroppedMetricPointCount: 0,
        DroppedLogRecordCount: 0);

    private static TelemetryTrace Trace(string traceId) => new(
        traceId,
        null,
        null,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        TimeSpan.Zero,
        SpanStatus.Ok,
        [],
        [],
        0);

    private static TelemetrySpan Span(string traceId) => new(
        "span-row",
        traceId,
        "span-id",
        null,
        "resource-id",
        "span",
        "Internal",
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        SpanStatus.Ok,
        null,
        new Dictionary<string, string?>(),
        [],
        []);

    private static OtlpLogRecord Log(string traceId) => new(
        "log-id",
        "resource-id",
        DateTimeOffset.UnixEpoch,
        "Information",
        9,
        "body",
        traceId,
        "span-id",
        new Dictionary<string, string?>());

    private static OpenTelemetryStorageDiagnostics EmptyDiagnostics() => DiagnosticsWithResourceCount(0);

    private static RunRequest Request(
        string provider = "sqlite",
        string adapter = DiagnosticsDurableHistoryAdapter.AdapterId,
        string physicalForm = DiagnosticsDurableHistoryAdapter.PhysicalForm,
        int processIndex = 1) => new(
        ComparisonCohortId: "cohort",
        MeasurementSetId: "set",
        WorkloadId: DiagnosticsDurableHistoryWorkload.WorkloadId,
        WorkloadVersion: DiagnosticsDurableHistoryWorkload.Version,
        Provider: provider,
        ProviderVersion: "3.46.0",
        ProviderTopology: "file-backed-distinct-connections",
        ProviderConfiguration: new Dictionary<string, string>(StringComparer.Ordinal),
        Adapter: adapter,
        PhysicalForm: physicalForm,
        Scale: "small",
        CommitSha: new string('a', 40),
        HarnessAssemblySha256: new string('b', 64),
        PackageVersions: new Dictionary<string, string>(StringComparer.Ordinal),
        CompositionFingerprint: new string('c', 64),
        HostFingerprintSha256: new string('d', 64),
        Seed: DiagnosticsDurableHistoryWorkload.Seed,
        InputFingerprintSha256: DiagnosticsDurableHistoryWorkload.ExpectedInputFingerprint,
        NativePlanIdentity: "diagnostics-plan",
        NativePlanEvidenceReference: "diagnostics-plan.json",
        NativePlanContentSha256: new string('e', 64),
        ProcessKind: ProcessKind.Measured,
        ProcessIndex: processIndex);

    private static string MongoPointCommand(string tableName) => JsonSerializer.Serialize(new
    {
        collection = tableName + "__scope__" + new string('A', 64),
        filter = new Dictionary<string, object>
        {
            ["_id"] = new Dictionary<string, string> { ["$eq"] = "<redacted>" }
        },
        limit = 1
    });

    private sealed class ProbeOpenTelemetryStore(
        OpenTelemetryStorageDiagnostics? diagnostics = null,
        bool throwTransientLock = false,
        bool blockDiagnostics = false,
        bool blockTrace = false,
        bool returnMissingTrace = false) : IOpenTelemetryStore
    {
        private readonly TaskCompletionSource<OpenTelemetryStorageDiagnostics> blockedDiagnostics =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<OpenTelemetryTraceDetail?> blockedTrace =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DiagnosticsReadCount { get; private set; }
        public List<string> TraceReads { get; } = [];

        public ValueTask WriteAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<OpenTelemetryResourceResult> QueryResourcesAsync(OpenTelemetryResourceFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<OpenTelemetryTraceResult> QueryTracesAsync(OpenTelemetryTraceFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<OpenTelemetryTraceDetail?> GetTraceAsync(string traceId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TraceReads.Add(traceId);
            if (blockTrace)
                return new ValueTask<OpenTelemetryTraceDetail?>(blockedTrace.Task);
            if (returnMissingTrace)
                return ValueTask.FromResult<OpenTelemetryTraceDetail?>(null);
            return ValueTask.FromResult<OpenTelemetryTraceDetail?>(new(Trace(traceId), [], [], []));
        }
        public ValueTask<OpenTelemetryMetricResult> QueryMetricsAsync(OpenTelemetryMetricFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<OpenTelemetryLogResult> QueryLogsAsync(OpenTelemetryLogFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<OpenTelemetryStorageDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DiagnosticsReadCount++;
            if (blockDiagnostics)
                return new ValueTask<OpenTelemetryStorageDiagnostics>(blockedDiagnostics.Task);
            if (throwTransientLock && DiagnosticsReadCount == 1)
                throw new SqliteException("database schema is locked: main", 6, 6);

            return ValueTask.FromResult(diagnostics ?? EmptyDiagnostics());
        }
    }
}
