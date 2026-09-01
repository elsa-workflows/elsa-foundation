using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
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
    public void Manifest_rejects_different_hosts_with_identical_generic_machine_metadata()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        fixture.WriteTarget("groundwork", "store", operations: ["read"], hostFingerprintSha256: new string('f', 64));

        var error = Assert.Throws<PerformanceContractException>(fixture.Bind);

        Assert.Contains("host fingerprint", error.Message, StringComparison.OrdinalIgnoreCase);
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
        var gate = GateEvaluator.Evaluate(GatePolicy.DefaultFor(GateClass.OrdinaryStore, comparison.WorkloadId), comparison);

        Assert.False(comparison.Complete);
        Assert.Equal(PerformanceVerdict.Blocked, gate.Verdict);
    }

    [Fact]
    public void Comparison_rejects_forged_secret_artifacts_even_when_the_measurement_sets_are_complete()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"], workloadId: ReproducibleWorkloadScenarioCatalog.BlockedWorkloadId);
        fixture.WriteTarget("groundwork", "store", operations: ["read"], workloadId: ReproducibleWorkloadScenarioCatalog.BlockedWorkloadId);
        fixture.Bind();

        var comparison = fixture.Compare();
        var gate = GateEvaluator.Evaluate(GatePolicy.DefaultFor(GateClass.OrdinaryStore, comparison.WorkloadId), comparison);

        Assert.False(comparison.Complete);
        Assert.Contains(ReproducibleWorkloadScenarioCatalog.BlockedReasonCode, comparison.BlockReason);
        Assert.Equal(PerformanceVerdict.Blocked, gate.Verdict);
        Assert.Contains(ReproducibleWorkloadScenarioCatalog.BlockedReasonCode, gate.Reason);
    }

    [Fact]
    public void Comparison_rejects_forged_complete_iam_artifacts_without_a_ratified_adapter_form_mapping()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        fixture.WriteTarget("groundwork", "store", operations: ["read"]);
        fixture.ForgeWorkloadIdentity(
            "iam-normalized-lookup-update",
            "1.1.0",
            "ef-aspnetcore-identity",
            "ef-identity-relational-schema",
            "groundwork-aspnetcore-identity",
            "entity-type-specific-physical-tables-current-identity-shape");

        var result = fixture.Compare(
            "sqlite/ef-aspnetcore-identity/ef-identity-relational-schema",
            "sqlite/groundwork-aspnetcore-identity/entity-type-specific-physical-tables-current-identity-shape");

        Assert.False(result.Complete);
        Assert.Contains("iam.adapter-form.ratification-required", result.BlockReason, StringComparison.Ordinal);
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
    public void Comparison_rejects_native_plan_document_captured_for_different_input()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        fixture.WriteTarget("groundwork", "store", operations: ["read"], evidenceDocumentInputFingerprint: new string('f', 64));
        fixture.Bind();

        var result = fixture.Compare();

        Assert.False(result.Complete);
        Assert.Contains("target, provenance", result.BlockReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Comparison_rejects_provider_version_not_observed_by_the_adapter()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        fixture.WriteTarget("groundwork", "store", operations: ["read"], observedProviderVersion: "3.45.0");
        fixture.Bind();

        var result = fixture.Compare();

        Assert.False(result.Complete);
        Assert.Contains("correctness", result.BlockReason!, StringComparison.OrdinalIgnoreCase);
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
    public void Artifact_store_rejects_raw_provider_plan_tampered_after_manifest()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        fixture.WriteTarget("groundwork", "store", operations: ["read"]);
        fixture.Bind();
        var rawPlan = Directory.EnumerateFiles(fixture.Directory, "groundwork-set.*.raw-plan.json").First();
        File.WriteAllText(rawPlan, "tampered");

        var error = Assert.Throws<PerformanceContractException>(() => ArtifactStore.ReadAll(fixture.Directory));

        Assert.Contains("integrity hash", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manifest_rejects_raw_provider_plan_reused_across_measurement_sets()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        fixture.WriteTarget("groundwork", "store", operations: ["read"], rawPlanReferenceOwner: "ef-set");

        var error = Assert.Throws<PerformanceContractException>(fixture.Bind);

        Assert.Contains("must own one distinct raw provider-plan", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manifest_rejects_secret_bearing_raw_provider_plan_json()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        var rawPlan = Directory.EnumerateFiles(fixture.Directory, "ef-set.*.raw-plan.json").First();
        File.WriteAllText(rawPlan, """{"connectionString":"Server=db;User ID=sa;Pwd=pwned","token":"abc"}""");

        var error = Assert.Throws<PerformanceContractException>(fixture.Bind);

        Assert.Contains("connection values or credentials", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manifest_rejects_endpoint_bearing_raw_provider_plan_json()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        var rawPlan = Directory.EnumerateFiles(fixture.Directory, "ef-set.*.raw-plan.json").First();
        File.WriteAllText(rawPlan, """{"server":"db.internal","database":"elsa","user":"sa"}""");

        var error = Assert.Throws<PerformanceContractException>(fixture.Bind);

        Assert.Contains("connection values or credentials", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Raw_plan_text_allows_parameterized_endpoints_but_rejects_retained_values()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa646-raw-plan-{Guid.NewGuid():N}.txt");
        try
        {
            foreach (var content in new[] { "server = @p", "server: $1", "database = :database", "host = ?" })
            {
                File.WriteAllText(path, content);
                ArtifactStore.ValidateRawPlanFile(path);
            }

            File.WriteAllText(path, "Host=db.internal;Database=elsa");
            var error = Assert.Throws<PerformanceContractException>(() => ArtifactStore.ValidateRawPlanFile(path));
            Assert.Contains("connection values or credentials", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Raw_plan_text_and_json_reject_credential_free_connection_uris()
    {
        var paths = new[]
        {
            (Path.Combine(Path.GetTempPath(), $"elsa646-raw-plan-{Guid.NewGuid():N}.txt"), "mongodb://mongo.internal:27017/elsa"),
            (Path.Combine(Path.GetTempPath(), $"elsa646-raw-plan-{Guid.NewGuid():N}.json"), """{"uri":"mongodb://mongo.internal:27017/elsa"}""")
        };
        try
        {
            foreach (var (path, content) in paths)
            {
                File.WriteAllText(path, content);
                var error = Assert.Throws<PerformanceContractException>(() => ArtifactStore.ValidateRawPlanFile(path));
                Assert.Contains("connection values or credentials", error.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            foreach (var (path, _) in paths)
                if (File.Exists(path)) File.Delete(path);
        }
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

        var comparison = fixture.Compare();
        var verdict = GateEvaluator.Evaluate(GatePolicy.DefaultFor(comparison.WorkloadId), comparison);

        var row = Assert.Single(verdict.Rows);
        Assert.True(double.IsFinite(row.P50Ratio));
        Assert.True(double.IsFinite(row.P95RatioCi.Low));
        Assert.True(double.IsFinite(row.P99RatioCi.High));
    }

    [Fact]
    public void Bounded_read_absolute_ceiling_rejects_a_ratio_neutral_140_millisecond_regression()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"], transform: At140Milliseconds);
        fixture.WriteTarget("groundwork", "store", operations: ["read"], transform: At140Milliseconds);
        fixture.Bind();

        var comparison = fixture.Compare();
        var verdict = GateEvaluator.Evaluate(
            GatePolicy.DefaultFor(GateClass.RuntimeHotPath, comparison.WorkloadId),
            comparison);

        Assert.True(comparison.Complete);
        Assert.True(comparison.CorrectnessEqual);
        Assert.Equal(PerformanceVerdict.Redesign, verdict.Verdict);
        var row = Assert.Single(verdict.Rows);
        Assert.Equal(1d, row.P95Ratio);
        Assert.Equal(GatePolicy.RatifiedBoundedReadPathP95Milliseconds, row.MaxP95Milliseconds);
        Assert.Equal(140d, row.P95Milliseconds);
        Assert.False(row.Pass);
    }

    [Fact]
    public void Gate_evaluator_rejects_a_forged_ordinary_policy_for_a_bounded_read()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"], transform: At140Milliseconds);
        fixture.WriteTarget("groundwork", "store", operations: ["read"], transform: At140Milliseconds);
        fixture.Bind();

        var comparison = fixture.Compare();
        var verdict = GateEvaluator.Evaluate(
            new GatePolicy(GateClass.OrdinaryStore, 1.25, .80, 2.0, null),
            comparison);

        Assert.Equal(PerformanceVerdict.Blocked, verdict.Verdict);
        Assert.Contains("gate class", verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Gate_evaluator_preserves_a_valid_reviewed_threshold_at_the_comparison_boundary()
    {
        using var fixture = ArtifactFixture.Create();
        fixture.WriteTarget("ef", "store", operations: ["read"]);
        fixture.WriteTarget("groundwork", "store", operations: ["read"]);
        fixture.Bind();

        var comparison = fixture.Compare();
        var policy = new GatePolicy(
            GateClass.RuntimeHotPath,
            1.01,
            .99,
            1.10,
            new GateReview(comparison.WorkloadId, comparison.WorkloadVersion, "proposer", "reviewer", "review-42", "2026-07-24T00:00:00Z"),
            17d);
        var verdict = GateEvaluator.Evaluate(policy, comparison);

        Assert.Equal(PerformanceVerdict.Pass, verdict.Verdict);
        Assert.Equal(17d, Assert.Single(verdict.Rows).MaxP95Milliseconds);
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

    private static OperationSample At140Milliseconds(OperationSample operation)
    {
        var latencies = Enumerable.Repeat(140d, operation.RawLatenciesMilliseconds.Count).ToArray();
        return operation with
        {
            P50Milliseconds = 140d,
            P95Milliseconds = 140d,
            P99Milliseconds = 140d,
            RawLatenciesMilliseconds = latencies
        };
    }
}

internal sealed class ArtifactFixture : IDisposable
{
    private ArtifactFixture(string directory) => Directory = directory;
    public string Directory { get; }
    public static ArtifactFixture Create() => new(Path.Combine(Path.GetTempPath(), "elsa646-artifacts-", Guid.NewGuid().ToString("N")));
    public void WriteTarget(string adapter, string form, string[] operations, (int Run, string Operation)? omitFromRun = null, int? alternateInputOnRun = null, Func<OperationSample, OperationSample>? transform = null, string? commitSha = null, int processorCount = 1, string timestampUtc = "2026-07-24T00:00:00Z", string? evidencePlanContentSha = null, Func<NativeRouteEvidence, NativeRouteEvidence>? routeTransform = null, string? omitNativeRoute = null, string? evidenceDocumentAdapter = null, string? hostFingerprintSha256 = null, string? evidenceDocumentInputFingerprint = null, string? observedProviderVersion = null, string? rawPlanReferenceOwner = null, string? workloadId = null)
    {
        Write(adapter, form, ProcessKind.Warmup, 0, [], transform: transform, commitSha: commitSha, processorCount: processorCount, timestampUtc: timestampUtc, evidencePlanContentSha: evidencePlanContentSha, routeTransform: routeTransform, omitNativeRoute: omitNativeRoute, evidenceDocumentAdapter: evidenceDocumentAdapter, hostFingerprintSha256: hostFingerprintSha256, evidenceDocumentInputFingerprint: evidenceDocumentInputFingerprint, observedProviderVersion: observedProviderVersion, rawPlanReferenceOwner: rawPlanReferenceOwner, workloadId: workloadId);
        foreach (var index in Enumerable.Range(1, 3))
            Write(adapter, form, ProcessKind.Measured, index, operations.Where(operation => omitFromRun is not { } omitted || omitted.Run != index || omitted.Operation != operation).ToArray(), alternateInputOnRun == index ? new string('e', 64) : null, transform, commitSha, processorCount, timestampUtc, evidencePlanContentSha, routeTransform, omitNativeRoute, evidenceDocumentAdapter, hostFingerprintSha256, evidenceDocumentInputFingerprint, observedProviderVersion, rawPlanReferenceOwner, workloadId);
    }
    public void Bind() => ArtifactStore.WriteManifest(Directory);
    public void ForgeWorkloadIdentity(
        string workloadId,
        string workloadVersion,
        string oracleAdapter,
        string oraclePhysicalForm,
        string targetAdapter,
        string targetPhysicalForm)
    {
        var workload = WorkloadCatalog.Load(Repository.Root()).Workloads[workloadId];
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*.process.json").ToArray())
        {
            var artifact = JsonSerializer.Deserialize<ProcessArtifact>(File.ReadAllBytes(path), ArtifactStore.JsonOptions)!;
            var request = artifact.Request with
            {
                WorkloadId = workloadId,
                WorkloadVersion = workloadVersion,
                Adapter = artifact.Request.Adapter == "ef" ? oracleAdapter : targetAdapter,
                PhysicalForm = artifact.Request.Adapter == "ef" ? oraclePhysicalForm : targetPhysicalForm,
                Seed = workload.Input.Seed,
                InputFingerprintSha256 = workload.Input.FingerprintSha256
            };
            var forgedPath = ArtifactStore.PathFor(Directory, request);
            File.Move(path, forgedPath);
            File.WriteAllText(forgedPath, JsonSerializer.Serialize(artifact with { Request = request }, ArtifactStore.JsonOptions));
        }
        Bind();
    }
    public ComparisonResult Compare(string oracle = "sqlite/ef/document-type-specific-tables", string target = "sqlite/groundwork/document-type-specific-tables") =>
        Comparison.Compare(Directory, oracle, target, WorkloadCatalog.Load(Repository.Root()));
    public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);

    private void Write(string adapter, string form, ProcessKind kind, int index, string[] operations, string? input = null, Func<OperationSample, OperationSample>? transform = null, string? commitSha = null, int processorCount = 1, string timestampUtc = "2026-07-24T00:00:00Z", string? evidencePlanContentSha = null, Func<NativeRouteEvidence, NativeRouteEvidence>? routeTransform = null, string? omitNativeRoute = null, string? evidenceDocumentAdapter = null, string? hostFingerprintSha256 = null, string? evidenceDocumentInputFingerprint = null, string? observedProviderVersion = null, string? rawPlanReferenceOwner = null, string? workloadId = null)
    {
        var physicalForm = form == "store" ? "document-type-specific-tables" : form;
        var evidenceReference = $"{adapter}-set.native-plan.json";
        var workload = WorkloadCatalog.Load(Repository.Root()).Workloads[workloadId ?? BookmarkScenario.WorkloadId];
        var request = new RunRequest("cohort-646", $"{adapter}-set", workload.Id, workload.Version, "sqlite", adapter, physicalForm, "100k", commitSha ?? new string('a', 40), new string('9', 64), new Dictionary<string, string> { [adapter == "ef" ? "Microsoft.EntityFrameworkCore" : "Groundwork.Sqlite"] = adapter == "ef" ? "10.0.8" : "0.0.1-preview.103" }, new string('b', 64), hostFingerprintSha256 ?? new string('d', 64), "3.46.0", "file-backed-distinct-connections", ProviderConfiguration(adapter), workload.Input.Seed, input ?? workload.Input.FingerprintSha256, "list-by-stimulus-and-type", evidenceReference, new string('0', 64), kind, index);
        var planOwner = rawPlanReferenceOwner ?? request.MeasurementSetId;
        var payload = NativePlanPayload(request, evidenceDocumentAdapter ?? adapter, evidenceDocumentInputFingerprint ?? request.InputFingerprintSha256, planOwner);
        request = request with { NativePlanContentSha256 = Hash(payload) };
        System.IO.Directory.CreateDirectory(Directory);
        var evidencePath = Path.Combine(Directory, evidenceReference);
        if (!File.Exists(evidencePath)) File.WriteAllText(evidencePath, payload);
        var rawLatencies = Enumerable.Repeat(1d, 100).ToArray();
        var rawRoundTrips = Enumerable.Repeat(1L, rawLatencies.Length).ToArray();
        var samples = operations.Select(operation => new OperationSample(operation, rawLatencies.Length, 30, rawLatencies.Length / 30d, Statistics.Percentile(rawLatencies, 50), Statistics.Percentile(rawLatencies, 95), Statistics.Percentile(rawLatencies, 99), rawLatencies)
        {
            RoundTrips = rawRoundTrips.Sum(),
            RawRoundTrips = rawRoundTrips
        }).Select(operation => transform?.Invoke(operation) ?? operation).ToArray();
        var routes = NativeRoutes(planOwner)
            .Where(route => route.RouteIdentity != omitNativeRoute)
            .Select(route => routeTransform?.Invoke(route) ?? route)
            .ToArray();
        foreach (var route in routes)
        {
            var rawPlanPath = Path.Combine(Directory, route.RawPlanReference);
            if (!File.Exists(rawPlanPath)) File.WriteAllText(rawPlanPath, RawPlanPayload(route.RouteIdentity));
        }
        var evidence = new CorrectnessEvidence(
            workload.Correctness.ResultDigestSha256,
            observedProviderVersion ?? request.ProviderVersion,
            request.ProviderTopology,
            request.ProviderConfiguration,
            new NativePlanEvidence("list-by-stimulus-and-type", evidenceReference, evidencePlanContentSha ?? request.NativePlanContentSha256, routes));
        ArtifactStore.Write(Directory, new ProcessArtifact(2, request, BenchmarkProtocol.Acceptance, true, evidence, samples, new MachineMetadata("test-os", "test-runtime", "X64", "X64", processorCount, request.HostFingerprintSha256, timestampUtc))
        {
            RoundTripInstrumentation = kind == ProcessKind.Measured ? "test-observer" : null
        });
    }

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
    private static string NativePlanPayload(RunRequest request, string documentAdapter, string documentInputFingerprint, string rawPlanReferenceOwner) => JsonSerializer.Serialize(new NativePlanEvidenceDocument(
        2, request.ComparisonCohortId, request.MeasurementSetId, request.WorkloadId, request.WorkloadVersion,
        request.Provider, documentAdapter, request.PhysicalForm, request.Scale, request.CommitSha, request.HarnessAssemblySha256,
        request.CompositionFingerprint, request.HostFingerprintSha256, request.ProviderVersion, request.ProviderTopology,
        request.ProviderConfiguration, request.Seed, documentInputFingerprint, request.NativePlanIdentity, NativeRoutes(rawPlanReferenceOwner)));
    private static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
