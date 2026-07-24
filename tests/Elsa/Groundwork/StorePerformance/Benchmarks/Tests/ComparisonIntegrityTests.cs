using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class ComparisonIntegrityTests
{
    [Fact]
    public void Comparison_rejects_a_missing_operation_from_one_measured_run()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read", "write"], omitFromRun: (3, "write"));
        fixture.WriteTarget("groundwork", "store", operations: ["read", "write"]);
        fixture.Bind();

        var result = fixture.Compare();

        Assert.False(result.Complete);
        Assert.Contains("identical", result.BlockReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Comparison_rejects_a_target_with_different_frozen_input_between_processes()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        fixture.WriteTarget("groundwork", "store", operations: ["read"], alternateInputOnRun: 2);
        fixture.Bind();

        var result = fixture.Compare();

        Assert.False(result.Complete);
        Assert.Contains("immutable", result.BlockReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Comparison_rejects_targets_from_different_commits()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        fixture.WriteTarget("groundwork", "store", operations: ["read"], commitSha: new string('f', 40));

        var error = Assert.Throws<PerformanceContractException>(fixture.Bind);

        Assert.Contains("expected commit", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Comparison_rejects_targets_from_different_machine_fingerprints()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        fixture.WriteTarget("groundwork", "store", operations: ["read"], processorCount: 2);
        fixture.Bind();

        var result = fixture.Compare();

        Assert.False(result.Complete);
        Assert.Contains("machine", result.BlockReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Comparison_machine_fingerprint_excludes_capture_timestamps()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"], timestampUtc: "2026-07-24T00:00:00Z");
        fixture.WriteTarget("groundwork", "store", operations: ["read"], timestampUtc: "2026-07-24T00:01:00Z");
        fixture.Bind();

        Assert.True(fixture.Compare().Complete);
    }

    [Fact]
    public void Comparison_rejects_nonpositive_raw_latency_samples()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        fixture.WriteTarget("groundwork", "store", operations: ["read"], transform: operation => operation with { RawLatenciesMilliseconds = [.. operation.RawLatenciesMilliseconds.Skip(1), 0] });
        fixture.Bind();

        var result = fixture.Compare();

        Assert.False(result.Complete);
        Assert.Contains("raw samples", result.BlockReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Comparison_rejects_summaries_that_do_not_match_raw_samples()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        fixture.WriteTarget("groundwork", "store", operations: ["read"], transform: operation => operation with { P50Milliseconds = 2, P95Milliseconds = 2, P99Milliseconds = 2, ThroughputPerSecond = 1000 });
        fixture.Bind();

        var result = fixture.Compare();

        Assert.False(result.Complete);
        Assert.Contains("summaries reproduced", result.BlockReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Comparison_aggregates_recomputed_raw_metrics_instead_of_tolerated_stored_summaries()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        fixture.WriteTarget("groundwork", "store", operations: ["read"], transform: operation => operation with { P50Milliseconds = operation.P50Milliseconds + 5e-13 });
        fixture.Bind();

        var result = fixture.Compare();

        Assert.True(result.Complete);
        Assert.All(Assert.Single(result.TargetOperations).P50Milliseconds, value => Assert.Equal(1d, value));
    }

    [Fact]
    public void Comparison_rejects_nonpositive_operation_count_or_duration()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        fixture.WriteTarget("groundwork", "store", operations: ["read"], transform: operation => operation with { Count = 0, SteadyStateSeconds = 0 });
        fixture.Bind();

        var result = fixture.Compare();

        Assert.False(result.Complete);
        Assert.Contains("finite positive", result.BlockReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Comparison_and_gate_reject_oracle_only_operations()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read", "oracle-only"]);
        fixture.WriteTarget("groundwork", "store", operations: ["read"]);
        fixture.Bind();

        var comparison = fixture.Compare();
        var gate = GateEvaluator.Evaluate(GatePolicy.DefaultFor(GateClass.OrdinaryStore), comparison);

        Assert.False(comparison.Complete);
        Assert.Equal(PerformanceVerdict.Blocked, gate.Verdict);
    }

    [Fact]
    public void Comparison_rejects_direct_artifacts_outside_the_frozen_physical_forms()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "unreviewed-form", operations: ["read"]);
        fixture.WriteTarget("groundwork", "unreviewed-form", operations: ["read"]);
        fixture.Bind();

        var result = fixture.Compare("sqlite/ef/unreviewed-form", "sqlite/groundwork/unreviewed-form");

        Assert.False(result.Complete);
        Assert.Contains("frozen", result.BlockReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Comparison_rejects_native_plan_evidence_not_bound_to_the_requested_content()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        fixture.WriteTarget("groundwork", "store", operations: ["read"], evidencePlanContentSha: new string('f', 64));
        fixture.Bind();

        var result = fixture.Compare();

        Assert.False(result.Complete);
        Assert.Contains("native-plan", result.BlockReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Comparison_rejects_native_plan_document_bound_to_a_different_target()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        fixture.WriteTarget("groundwork", "store", operations: ["read"], evidenceDocumentAdapter: "ef");
        fixture.Bind();

        var result = fixture.Compare();

        Assert.False(result.Complete);
        Assert.Contains("target, provenance", result.BlockReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Comparison_rejects_native_route_evidence_without_admitted_bounded_facts()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        fixture.WriteTarget(
            "groundwork",
            "store",
            operations: ["read"],
            routeTransform: route => route with { HasRoutePredicate = false, FiniteLimit = 0 });
        fixture.Bind();

        var result = fixture.Compare();

        Assert.False(result.Complete);
        Assert.Contains("bounded cardinality", result.BlockReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Comparison_rejects_missing_required_native_route_evidence()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        fixture.WriteTarget("groundwork", "store", operations: ["read"], omitNativeRoute: "list-by-stimulus-type");
        fixture.Bind();

        var result = fixture.Compare();

        Assert.False(result.Complete);
        Assert.Contains("every required route", result.BlockReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Artifact_store_rejects_a_duplicate_identity_and_unknown_json_field()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        fixture.WriteTarget("groundwork", "store", operations: ["read"]);
        fixture.Bind();
        var original = Directory.EnumerateFiles(fixture.Directory, "*.process.json").First();
        File.Copy(original, Path.Combine(fixture.Directory, "duplicate.process.json"));

        Assert.Throws<PerformanceContractException>(() => ArtifactStore.ReadAll(fixture.Directory));

        File.Delete(Path.Combine(fixture.Directory, "duplicate.process.json"));
        var artifact = Directory.EnumerateFiles(fixture.Directory, "*.process.json").First();
        File.WriteAllText(artifact, File.ReadAllText(artifact).Replace("\"CorrectnessPassed\": true", "\"CorrectnessPassed\": true, \"Unknown\": true", StringComparison.Ordinal));

        Assert.Throws<PerformanceContractException>(() => ArtifactStore.WriteManifest(fixture.Directory));
    }

    [Fact]
    public void Artifact_store_rejects_native_plan_evidence_tampered_after_manifest()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        fixture.WriteTarget("groundwork", "store", operations: ["read"]);
        fixture.Bind();
        File.WriteAllText(Path.Combine(fixture.Directory, "ef-set.native-plan.json"), "tampered");

        var error = Assert.Throws<PerformanceContractException>(() => ArtifactStore.ReadAll(fixture.Directory));

        Assert.Contains("integrity hash", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Artifact_schema_v2_rejects_v1_manifests_and_missing_required_process_fields()
    {
        using var legacy = ArtifactFixture.Create();
        legacy.WriteTarget("ef", "store", operations: ["read"]);
        legacy.WriteTarget("groundwork", "store", operations: ["read"]);
        legacy.Bind();
        var legacyManifest = Path.Combine(legacy.Directory, "artifact-manifest.v2.json");
        File.WriteAllText(legacyManifest, File.ReadAllText(legacyManifest).Replace("\"SchemaVersion\": 2", "\"SchemaVersion\": 1", StringComparison.Ordinal));
        Assert.Throws<PerformanceContractException>(() => ArtifactStore.ReadAll(legacy.Directory));

        using var missing = ArtifactFixture.Create();
        missing.WriteTarget("ef", "store", operations: ["read"]);
        var artifact = Directory.EnumerateFiles(missing.Directory, "*.process.json").First();
        File.WriteAllText(artifact, File.ReadAllText(artifact).Replace("  \"ComparisonCohortId\": \"cohort-646\",\n", "", StringComparison.Ordinal));
        Assert.Throws<PerformanceContractException>(() => ArtifactStore.WriteManifest(missing.Directory));
    }

    [Fact]
    public void Gate_rows_include_p50_and_honest_ratio_confidence_intervals()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        fixture.WriteTarget("groundwork", "store", operations: ["read"]);
        fixture.Bind();

        var verdict = GateEvaluator.Evaluate(GatePolicy.DefaultFor(GateClass.OrdinaryStore), fixture.Compare());

        var row = Assert.Single(verdict.Rows);
        Assert.True(double.IsFinite(row.P50Ratio));
        Assert.True(double.IsFinite(row.P95RatioCi.Low));
        Assert.True(double.IsFinite(row.P99RatioCi.High));
    }

    [Fact]
    public void Hierarchical_ratio_bootstrap_preserves_process_medians_instead_of_flattening_samples()
    {
        var oracle = new Dictionary<int, IReadOnlyList<double>>
        {
            [1] = Enumerable.Repeat(1d, 100).ToArray(),
            [2] = Enumerable.Repeat(1d, 100).ToArray(),
            [3] = Enumerable.Repeat(100d, 100).ToArray()
        };
        var target = new Dictionary<int, IReadOnlyList<double>>
        {
            [1] = Enumerable.Repeat(1d, 100).ToArray(),
            [2] = Enumerable.Repeat(1d, 100).ToArray(),
            [3] = Enumerable.Repeat(1d, 100).ToArray()
        };

        var interval = Statistics.BootstrapPercentileRatioCi(oracle, target, 95, resamples: 200, seed: 646);
        var flattenedRatio = Statistics.Percentile(target.SelectMany(pair => pair.Value).ToArray(), 95) / Statistics.Percentile(oracle.SelectMany(pair => pair.Value).ToArray(), 95);

        Assert.Equal(1d, interval.Low);
        Assert.Equal(1d, interval.High);
        Assert.Equal(.01d, flattenedRatio, precision: 8);
    }

    [Fact]
    public void Result_store_persists_versioned_payload_with_integrity_hash()
    {
        using var fixture = ArtifactFixture.Create();
        var comparison = new ComparisonResult(1, new string('d', 64), "bookmark-lookup", "1.0.0", "sqlite", "100k", "sqlite/ef/store", "sqlite/groundwork/store", false, false, [], [], "blocked");
        var path = ResultStore.Write(Path.Combine(fixture.Directory, "comparison.json"), comparison);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(1, document.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.Matches("^[0-9a-f]{64}$", document.RootElement.GetProperty("PayloadSha256").GetString()!);
    }
}

internal sealed class ArtifactFixture : IDisposable
{
    private ArtifactFixture(string directory) => Directory = directory;
    public string Directory { get; }
    public static ArtifactFixture Create() => new(Path.Combine(Path.GetTempPath(), "elsa646-artifacts-", Guid.NewGuid().ToString("N")));
    public void WriteTarget(string adapter, string form, string[] operations, (int Run, string Operation)? omitFromRun = null, int? alternateInputOnRun = null, Func<OperationSample, OperationSample>? transform = null, string? commitSha = null, int processorCount = 1, string timestampUtc = "2026-07-24T00:00:00Z", string? evidencePlanContentSha = null, Func<NativeRouteEvidence, NativeRouteEvidence>? routeTransform = null, string? omitNativeRoute = null, string? evidenceDocumentAdapter = null)
    {
        Write(adapter, form, ProcessKind.Warmup, 0, [], transform: transform, commitSha: commitSha, processorCount: processorCount, timestampUtc: timestampUtc, evidencePlanContentSha: evidencePlanContentSha, routeTransform: routeTransform, omitNativeRoute: omitNativeRoute, evidenceDocumentAdapter: evidenceDocumentAdapter);
        foreach (var index in Enumerable.Range(1, 3))
            Write(adapter, form, ProcessKind.Measured, index, operations.Where(operation => omitFromRun is not { } omitted || omitted.Run != index || omitted.Operation != operation).ToArray(), alternateInputOnRun == index ? new string('e', 64) : null, transform, commitSha, processorCount, timestampUtc, evidencePlanContentSha, routeTransform, omitNativeRoute, evidenceDocumentAdapter);
    }
    public void Bind() => ArtifactStore.WriteManifest(Directory);
    public ComparisonResult Compare(string oracle = "sqlite/ef/document-type-specific-tables", string target = "sqlite/groundwork/document-type-specific-tables") =>
        Comparison.Compare(Directory, oracle, target, WorkloadCatalog.Load(Repository.Root()));
    public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);

    private void Write(string adapter, string form, ProcessKind kind, int index, string[] operations, string? input = null, Func<OperationSample, OperationSample>? transform = null, string? commitSha = null, int processorCount = 1, string timestampUtc = "2026-07-24T00:00:00Z", string? evidencePlanContentSha = null, Func<NativeRouteEvidence, NativeRouteEvidence>? routeTransform = null, string? omitNativeRoute = null, string? evidenceDocumentAdapter = null)
    {
        var physicalForm = form == "store" ? "document-type-specific-tables" : form;
        var evidenceReference = $"{adapter}-set.native-plan.json";
        var request = new RunRequest("cohort-646", $"{adapter}-set", "bookmark-lookup", "1.0.0", "sqlite", adapter, physicalForm, "100k", commitSha ?? new string('a', 40), new Dictionary<string, string> { [adapter == "ef" ? "Microsoft.EntityFrameworkCore" : "Groundwork.Sqlite"] = adapter == "ef" ? "10.0.8" : "0.0.1-preview.81" }, new string('b', 64), "spec094-bookmark-lookup-v1", input ?? "c1b8a142e22e7c47449edc25c79cc2a83c5edb6dbbe4a884a730751038f3ae9a", "list-by-stimulus-and-type", evidenceReference, new string('0', 64), kind, index);
        var payload = NativePlanPayload(request, evidenceDocumentAdapter ?? adapter);
        request = request with { NativePlanContentSha256 = Hash(payload) };
        System.IO.Directory.CreateDirectory(Directory);
        var evidencePath = Path.Combine(Directory, evidenceReference);
        if (!File.Exists(evidencePath)) File.WriteAllText(evidencePath, payload);
        var rawLatencies = Enumerable.Repeat(1d, 100).ToArray();
        var samples = operations.Select(operation => new OperationSample(operation, rawLatencies.Length, 30, rawLatencies.Length / 30d, Statistics.Percentile(rawLatencies, 50), Statistics.Percentile(rawLatencies, 95), Statistics.Percentile(rawLatencies, 99), rawLatencies)).Select(operation => transform?.Invoke(operation) ?? operation).ToArray();
        var routes = new[] { "list-by-stimulus-and-type", "list-by-stimulus-type" }
            .Where(route => route != omitNativeRoute)
            .Select(route => new NativeRouteEvidence(route, new string('e', 64), "bounded-index-seek", "ix-bookmarks", 100_000, true, true, 25, 1))
            .Select(route => routeTransform?.Invoke(route) ?? route)
            .ToArray();
        var evidence = new CorrectnessEvidence(
            "9f3d29edc4c3e64409f3fb9b64b4ec3e7d5e5064d8233be8afd92215ec3d680e",
            "file-backed-distinct-connections",
            new NativePlanEvidence("list-by-stimulus-and-type", evidenceReference, evidencePlanContentSha ?? request.NativePlanContentSha256, routes));
        ArtifactStore.Write(Directory, new ProcessArtifact(2, request, BenchmarkProtocol.Acceptance, true, evidence, samples, new MachineMetadata("test-os", "test-runtime", "X64", "X64", processorCount, timestampUtc)));
    }

    private static readonly NativeRouteEvidence[] NativeRoutes =
    [
        new("list-by-stimulus-and-type", new string('e', 64), "bounded-index-seek", "ix-bookmarks", 100_000, true, true, 25, 1),
        new("list-by-stimulus-type", new string('e', 64), "bounded-index-seek", "ix-bookmarks", 100_000, true, true, 25, 1)
    ];
    private static string NativePlanPayload(RunRequest request, string documentAdapter) => JsonSerializer.Serialize(new NativePlanEvidenceDocument(
        2, request.ComparisonCohortId, request.MeasurementSetId, request.WorkloadId, request.WorkloadVersion,
        request.Provider, documentAdapter, request.PhysicalForm, request.Scale, request.CommitSha,
        request.CompositionFingerprint, request.NativePlanIdentity, NativeRoutes));
    private static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
