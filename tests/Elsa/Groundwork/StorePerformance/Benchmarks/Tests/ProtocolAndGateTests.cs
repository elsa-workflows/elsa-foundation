using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class ProtocolAndGateTests
{
    [Fact]
    public void Acceptance_protocol_is_fixed_to_one_warmup_and_three_independent_measured_processes()
    {
        Assert.Equal(1, BenchmarkProtocol.Acceptance.WarmupProcessCount);
        Assert.Equal(3, BenchmarkProtocol.Acceptance.MeasuredProcessCount);
        Assert.Equal(100, BenchmarkProtocol.Acceptance.MinimumOperations);
        Assert.Equal(TimeSpan.FromSeconds(30), BenchmarkProtocol.Acceptance.MinimumSteadyState);

        var plan = MatrixPlan.Create(Workload(), Request());
        Assert.Equal([ProcessKind.Warmup, ProcessKind.Measured, ProcessKind.Measured, ProcessKind.Measured], plan.Runs.Select(run => run.ProcessKind));
        Assert.Equal([0, 1, 2, 3], plan.Runs.Select(run => run.ProcessIndex));
    }

    [Fact]
    public void Host_fingerprint_is_opaque_and_repository_provenance_rejects_false_or_dirty_heads()
    {
        Assert.Matches("^[0-9a-f]{64}$", HostFingerprint.CaptureSha256());
        Assert.Matches("^[0-9a-f]{64}$", SourceProvenance.HarnessAssemblySha256());
        Assert.Throws<PerformanceContractException>(() =>
            SourceProvenance.Validate(new string('a', 40), new string('b', 40), isClean: true));
        Assert.Throws<PerformanceContractException>(() =>
            SourceProvenance.Validate(new string('a', 40), new string('a', 40), isClean: false));
        SourceProvenance.Validate(new string('a', 40), new string('a', 40), isClean: true);
        Assert.Throws<PerformanceContractException>(() =>
            SourceProvenance.RequireHarnessAssembly(new string('f', 64)));
    }

    [Fact]
    public async Task Adapter_child_rejects_a_different_host_before_preparation_or_timing()
    {
        var actualHost = HostFingerprint.CaptureSha256();
        var differentHost = actualHost == new string('f', 64) ? new string('e', 64) : new string('f', 64);
        var request = MatrixPlan.Create(Workload(), Request()).Runs[0] with { HostFingerprintSha256 = differentHost };
        await using var adapter = new NeverPreparedAdapter();

        var error = await Assert.ThrowsAsync<PerformanceContractException>(() =>
            ProcessMeasurement.ExecuteAsync(Workload(), request, BenchmarkProtocol.Acceptance, adapter, Path.GetTempPath(), CancellationToken.None));

        Assert.Contains("not running on the matrix host", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(adapter.PrepareCalled);
    }

    [Fact]
    public async Task Adapter_child_rejects_false_source_provenance_before_preparation_or_timing()
    {
        var request = MatrixPlan.Create(Workload(), Request()).Runs[0] with
        {
            HostFingerprintSha256 = HostFingerprint.CaptureSha256(),
            CommitSha = new string('f', 40),
            HarnessAssemblySha256 = SourceProvenance.HarnessAssemblySha256()
        };
        await using var adapter = new NeverPreparedAdapter();

        var error = await Assert.ThrowsAsync<PerformanceContractException>(() =>
            ProcessMeasurement.ExecuteAsync(Workload(), request, BenchmarkProtocol.Acceptance, adapter, Path.GetTempPath(), CancellationToken.None));

        Assert.Contains("must equal the current repository HEAD", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(adapter.PrepareCalled);
    }

    [Fact]
    public async Task Public_matrix_runner_rejects_false_source_provenance_before_starting_a_child()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"elsa646-matrix-{Guid.NewGuid():N}");
        try
        {
            var plan = MatrixPlan.Create(Workload(), Request() with
            {
                CommitSha = new string('f', 40),
                HarnessAssemblySha256 = SourceProvenance.HarnessAssemblySha256()
            });

            var error = await Assert.ThrowsAsync<PerformanceContractException>(() =>
                ProcessMatrixRunner.RunAsync(plan, Environment.ProcessPath!, directory, CancellationToken.None));

            Assert.Contains("must equal the current repository HEAD", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Matrix_rejects_provider_and_physical_form_outside_the_frozen_workload()
    {
        var workload = Workload();

        Assert.Throws<PerformanceContractException>(() =>
            MatrixPlan.Create(workload, Request() with { Provider = "oracle" }));
        Assert.Throws<PerformanceContractException>(() =>
            MatrixPlan.Create(workload, Request() with { PhysicalForm = "unreviewed-form" }));
        Assert.Throws<PerformanceContractException>(() =>
            MatrixPlan.Create(workload, Request() with { ProviderConfiguration = new Dictionary<string, string> { ["host"] = "db.internal" } }));
    }

    [Fact]
    public async Task Matrix_rejects_a_preexisting_planned_artifact_from_a_noop_child()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"elsa646-matrix-{Guid.NewGuid():N}");
        try
        {
            var plan = MatrixPlan.Create(Workload(), Request());
            ArtifactStore.Write(directory, ArtifactFor(plan.Runs[0]));

            var error = await Assert.ThrowsAsync<PerformanceContractException>(() =>
                ProcessMatrixRunner.RunForTestAsync(plan, directory, (_, _, _) => Task.FromResult(0), CancellationToken.None));

            Assert.Contains("preexisting", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Matrix_rejects_a_fresh_noop_child_without_writing_a_manifest()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"elsa646-matrix-{Guid.NewGuid():N}");
        try
        {
            var plan = MatrixPlan.Create(Workload(), Request());

            var error = await Assert.ThrowsAsync<PerformanceContractException>(() =>
                ProcessMatrixRunner.RunForTestAsync(plan, directory, (_, _, _) => Task.FromResult(0), CancellationToken.None));

            Assert.Contains("did not emit", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(directory) && Directory.EnumerateFiles(directory, "artifact-manifest.*.json").Any());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Matrix_manifest_binds_exactly_four_fresh_validated_artifacts()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"elsa646-matrix-{Guid.NewGuid():N}");
        try
        {
            var plan = MatrixPlan.Create(Workload(), Request());

            await ProcessMatrixRunner.RunForTestAsync(
                plan,
                directory,
                (run, output, _) =>
                {
                    WriteArtifact(output, run);
                    return Task.FromResult(0);
                },
                CancellationToken.None);

            var artifactSet = ArtifactStore.ReadAll(directory);
            Assert.Equal(4, artifactSet.Artifacts.Count);
            Assert.Equal(
                plan.Runs.Select(run => (run.ProcessKind, run.ProcessIndex)).OrderBy(run => run.ProcessKind).ThenBy(run => run.ProcessIndex),
                artifactSet.Artifacts.Select(artifact => (artifact.Request.ProcessKind, artifact.Request.ProcessIndex)).OrderBy(run => run.ProcessKind).ThenBy(run => run.ProcessIndex));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Matrix_rejects_a_child_artifact_that_does_not_match_the_complete_request()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"elsa646-matrix-{Guid.NewGuid():N}");
        try
        {
            var plan = MatrixPlan.Create(Workload(), Request());

            var error = await Assert.ThrowsAsync<PerformanceContractException>(() =>
                ProcessMatrixRunner.RunForTestAsync(
                    plan,
                    directory,
                    (run, output, _) =>
                    {
                        WriteArtifact(output, run with { PackageVersions = new Dictionary<string, string> { ["Groundwork.Sqlite"] = "0.0.1-preview.82" } });
                        return Task.FromResult(0);
                    },
                    CancellationToken.None));

            Assert.Contains("exactly match", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Matrix_rejects_a_child_that_emits_a_future_planned_artifact_early()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"elsa646-matrix-{Guid.NewGuid():N}");
        try
        {
            var plan = MatrixPlan.Create(Workload(), Request());
            var first = true;

            var error = await Assert.ThrowsAsync<PerformanceContractException>(() =>
                ProcessMatrixRunner.RunForTestAsync(
                    plan,
                    directory,
                    (run, output, _) =>
                    {
                        WriteArtifact(output, run);
                        if (first)
                        {
                            first = false;
                            WriteArtifact(output, plan.Runs[1]);
                        }
                        return Task.FromResult(0);
                    },
                    CancellationToken.None));

            Assert.Contains("unexpected artifact", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Two_fresh_measurement_sets_in_one_cohort_can_compare()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"elsa646-matrix-{Guid.NewGuid():N}");
        try
        {
            await RunMatrixAsync(MatrixPlan.Create(Workload(), Request("ef", "ef-set")), directory);
            await RunMatrixAsync(MatrixPlan.Create(Workload(), Request("groundwork", "groundwork-set", compositionFingerprint: new string('f', 64))), directory);

            var result = Comparison.Compare(
                directory,
                "sqlite/ef/document-type-specific-tables",
                "sqlite/groundwork/document-type-specific-tables",
                WorkloadCatalog.Load(Repository.Root()));

            Assert.True(result.Complete);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Matrix_rejects_a_duplicate_measurement_set_or_mixed_cohort()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"elsa646-matrix-{Guid.NewGuid():N}");
        try
        {
            var plan = MatrixPlan.Create(Workload(), Request("ef", "ef-set"));
            await RunMatrixAsync(plan, directory);

            var duplicate = await Assert.ThrowsAsync<PerformanceContractException>(() => RunMatrixAsync(plan, directory));
            Assert.Contains("distinct measurement set", duplicate.Message, StringComparison.OrdinalIgnoreCase);

            var mixed = MatrixPlan.Create(Workload(), Request("groundwork", "groundwork-set", "other-cohort"));
            var mixedError = await Assert.ThrowsAsync<PerformanceContractException>(() => RunMatrixAsync(mixed, directory));
            Assert.Contains("existing cohort", mixedError.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Matrix_rejects_traversal_in_native_plan_evidence_reference()
    {
        var request = Request() with { NativePlanEvidenceReference = "../native-plan.json" };

        Assert.Throws<PerformanceContractException>(() => MatrixPlan.Create(Workload(), request));
    }

    [Fact]
    public async Task Matrix_rejects_missing_or_hash_mismatched_native_plan_evidence_files()
    {
        var missingDirectory = Path.Combine(Path.GetTempPath(), $"elsa646-matrix-{Guid.NewGuid():N}");
        var mismatchDirectory = Path.Combine(Path.GetTempPath(), $"elsa646-matrix-{Guid.NewGuid():N}");
        try
        {
            var plan = MatrixPlan.Create(Workload(), Request());
            var missing = await Assert.ThrowsAsync<PerformanceContractException>(() =>
                ProcessMatrixRunner.RunForTestAsync(
                    plan,
                    missingDirectory,
                    (run, output, _) =>
                    {
                        ArtifactStore.Write(output, ArtifactFor(run));
                        return Task.FromResult(0);
                    },
                    CancellationToken.None));
            Assert.Contains("evidence", missing.Message, StringComparison.OrdinalIgnoreCase);

            var mismatch = await Assert.ThrowsAsync<PerformanceContractException>(() =>
                ProcessMatrixRunner.RunForTestAsync(
                    plan,
                    mismatchDirectory,
                    (run, output, _) =>
                    {
                        Directory.CreateDirectory(output);
                        File.WriteAllText(Path.Combine(output, run.NativePlanEvidenceReference), "tampered");
                        ArtifactStore.Write(output, ArtifactFor(run));
                        return Task.FromResult(0);
                    },
                    CancellationToken.None));
            Assert.Contains("evidence", mismatch.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(missingDirectory)) Directory.Delete(missingDirectory, recursive: true);
            if (Directory.Exists(mismatchDirectory)) Directory.Delete(mismatchDirectory, recursive: true);
        }
    }

    [Fact]
    public void Gate_defaults_to_the_performance_handoff_ratios()
    {
        var gate = GatePolicy.DefaultFor(GateClass.RuntimeHotPath);

        Assert.Equal(1.10, gate.MaxP95Ratio);
        Assert.Equal(0.90, gate.MinThroughputRatio);
        Assert.Equal(2.0, gate.MaxP99Ratio);
        Assert.Null(gate.Review);
    }

    [Fact]
    public void Replacement_gate_requires_an_independent_review_record()
    {
        var error = Assert.Throws<PerformanceContractException>(() => GatePolicy.Replacement(
            GateClass.OrdinaryStore,
            1.5,
            0.7,
            2.5,
            new GateReview("bookmark-lookup", "1.0.0", "#646", "#646", "same author", "2026-07-24")));

        Assert.Contains("independent", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Incomplete_or_nonmatching_evidence_fails_closed()
    {
        var comparison = new ComparisonResult(1, new string('c', 64), "bookmark-lookup", "1.0.0", "sqlite", "100k", "sqlite/ef/store", "sqlite/groundwork/store", false, false, [], [], "incomplete");

        var verdict = GateEvaluator.Evaluate(GatePolicy.DefaultFor(GateClass.OrdinaryStore), comparison);

        Assert.Equal(PerformanceVerdict.Blocked, verdict.Verdict);
    }

    [Fact]
    public void Durable_artifact_metadata_rejects_secret_bearing_fields()
    {
        var request = Request() with { PackageVersions = new Dictionary<string, string> { ["connection-string"] = "safe-looking-value" } };

        var error = Assert.Throws<PerformanceContractException>(() => MatrixPlan.Create(Workload(), request));

        Assert.Contains("sensitive", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reviewed_replacement_gate_is_versioned_and_bound_to_one_workload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa646-gate-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
                { "SchemaVersion": 1, "WorkloadId": "bookmark-lookup", "WorkloadVersion": "1.0.0", "GateClass": 1, "MaxP95Ratio": 1.3, "MinThroughputRatio": 0.8, "MaxP99Ratio": 2.0, "Review": { "WorkloadId": "bookmark-lookup", "WorkloadVersion": "1.0.0", "ProposedBy": "#645", "ReviewedBy": "#646", "ReviewReference": "review-42", "ReviewedAtUtc": "2026-07-24T00:00:00Z" } }
                """);

            var policy = GatePolicyFile.Load(path, "bookmark-lookup", "1.0.0");
            Assert.NotNull(policy.Review);
            Assert.Throws<PerformanceContractException>(() => GatePolicyFile.Load(path, "bookmark-lookup", "1.0.1"));
        }
        finally { File.Delete(path); }
    }

    private static MatrixRequest Request(string adapter = "groundwork", string measurementSet = "groundwork-set", string cohort = "cohort-646", string? compositionFingerprint = null)
    {
        var workload = BookmarkScenario;
        var request = new MatrixRequest(
            cohort, measurementSet, workload.WorkloadId, workload.Version, "sqlite", adapter, "document-type-specific-tables", "100k",
            new string('a', 40), new string('9', 64), new Dictionary<string, string> { [adapter == "ef" ? "Microsoft.EntityFrameworkCore" : "Groundwork.Sqlite"] = adapter == "ef" ? "10.0.8" : "0.0.1-preview.81" }, compositionFingerprint ?? new string('b', 64), new string('d', 64), "3.46.0", "file-backed-distinct-connections", ProviderConfiguration(adapter), workload.Seed, workload.ComputeInputFingerprint(), "list-by-stimulus-and-type", $"{measurementSet}.native-plan.json", new string('0', 64));
        return request with { NativePlanContentSha256 = Hash(NativePlanPayload(request)) };
    }

    private static PerformanceWorkload Workload() =>
        WorkloadCatalog.Load(Repository.Root()).Workloads["bookmark-lookup"];

    private static ProcessArtifact ArtifactFor(RunRequest request)
    {
        var raw = Enumerable.Repeat(1d, 100).ToArray();
        IReadOnlyList<OperationSample> operations = request.ProcessKind == ProcessKind.Warmup
            ? []
            :
            [
                new OperationSample(
                    "read",
                    raw.Length,
                    30,
                    raw.Length / 30d,
                    Statistics.Percentile(raw, 50),
                    Statistics.Percentile(raw, 95),
                    Statistics.Percentile(raw, 99),
                    raw)
            ];
        return new ProcessArtifact(
            2,
            request,
            BenchmarkProtocol.Acceptance,
            true,
            new CorrectnessEvidence(
                BookmarkScenario.ComputeResultDigest(),
                request.ProviderVersion,
                request.ProviderTopology,
                request.ProviderConfiguration,
                new NativePlanEvidence(
                    "list-by-stimulus-and-type",
                    request.NativePlanEvidenceReference,
                    request.NativePlanContentSha256,
                    NativeRoutes(request.MeasurementSetId))),
            operations,
            new MachineMetadata("test-os", "test-runtime", "X64", "X64", 1, request.HostFingerprintSha256, "2026-07-24T00:00:00Z"));
    }

    private static void WriteArtifact(string outputDirectory, RunRequest request)
    {
        Directory.CreateDirectory(outputDirectory);
        foreach (var route in NativeRoutes(request.MeasurementSetId))
        {
            var rawPlanPath = Path.Combine(outputDirectory, route.RawPlanReference);
            if (!File.Exists(rawPlanPath)) File.WriteAllText(rawPlanPath, RawPlanPayload(route.RouteIdentity));
        }
        var evidencePath = Path.Combine(outputDirectory, request.NativePlanEvidenceReference);
        if (!File.Exists(evidencePath)) File.WriteAllText(evidencePath, NativePlanPayload(request));
        ArtifactStore.Write(outputDirectory, ArtifactFor(request));
    }

    private static Task RunMatrixAsync(MatrixPlan plan, string outputDirectory) =>
        ProcessMatrixRunner.RunForTestAsync(
            plan,
            outputDirectory,
            (run, output, _) =>
            {
                WriteArtifact(output, run);
                return Task.FromResult(0);
            },
            CancellationToken.None);

    private static NativeRouteEvidence[] NativeRoutes(string measurementSet) =>
    [
        Route(measurementSet, "list-by-stimulus-and-type"),
        Route(measurementSet, "list-by-stimulus-type")
    ];
    private static NativeRouteEvidence Route(string measurementSet, string identity) =>
        new(identity, $"{measurementSet}.{identity}.raw-plan.json", Hash(RawPlanPayload(identity)), "bounded-index-seek", "ix-bookmarks", 100_000, true, true, 25, 1);
    private static string RawPlanPayload(string route) => JsonSerializer.Serialize(new { SchemaVersion = 1, Route = route, ProviderPlan = $"EXPLAIN {route} USING ix-bookmarks" });
    private static IReadOnlyDictionary<string, string> ProviderConfiguration(string adapter) =>
        new Dictionary<string, string> { ["journal_mode"] = adapter == "ef" ? "delete" : "wal", ["synchronous"] = adapter == "ef" ? "full" : "normal" };
    private static ReproducibleWorkloadScenario BookmarkScenario =>
        ReproducibleWorkloadScenarioCatalog.Get("bookmark-lookup");
    private static string NativePlanPayload(MatrixRequest request) => JsonSerializer.Serialize(new NativePlanEvidenceDocument(
        2, request.ComparisonCohortId, request.MeasurementSetId, request.WorkloadId, request.WorkloadVersion,
        request.Provider, request.Adapter, request.PhysicalForm, request.Scale, request.CommitSha, request.HarnessAssemblySha256,
        request.CompositionFingerprint, request.HostFingerprintSha256, request.ProviderVersion, request.ProviderTopology,
        request.ProviderConfiguration, request.Seed, request.InputFingerprintSha256, request.NativePlanIdentity, NativeRoutes(request.MeasurementSetId)));
    private static string NativePlanPayload(RunRequest request) => JsonSerializer.Serialize(new NativePlanEvidenceDocument(
        2, request.ComparisonCohortId, request.MeasurementSetId, request.WorkloadId, request.WorkloadVersion,
        request.Provider, request.Adapter, request.PhysicalForm, request.Scale, request.CommitSha, request.HarnessAssemblySha256,
        request.CompositionFingerprint, request.HostFingerprintSha256, request.ProviderVersion, request.ProviderTopology,
        request.ProviderConfiguration, request.Seed, request.InputFingerprintSha256, request.NativePlanIdentity, NativeRoutes(request.MeasurementSetId)));
    private static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private sealed class NeverPreparedAdapter : IBenchmarkAdapter
    {
        public bool PrepareCalled { get; private set; }
        public IReadOnlyList<IBenchmarkOperation> Operations => [];
        public Task PrepareAsync(CancellationToken cancellationToken)
        {
            PrepareCalled = true;
            return Task.CompletedTask;
        }
        public Task<CorrectnessEvidence> VerifyCorrectnessAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Correctness must not run for a mismatched host.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
