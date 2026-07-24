using System.Text.Json;
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
    public void WriteTarget(string adapter, string form, string[] operations, (int Run, string Operation)? omitFromRun = null, int? alternateInputOnRun = null)
    {
        Write(adapter, form, ProcessKind.Warmup, 0, []);
        foreach (var index in Enumerable.Range(1, 3))
            Write(adapter, form, ProcessKind.Measured, index, operations.Where(operation => omitFromRun is not { } omitted || omitted.Run != index || omitted.Operation != operation).ToArray(), alternateInputOnRun == index ? new string('e', 64) : new string('c', 64));
    }
    public void Bind() => ArtifactStore.WriteManifest(Directory);
    public ComparisonResult Compare() => Comparison.Compare(Directory, "sqlite/ef/store", "sqlite/groundwork/store");
    public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);

    private void Write(string adapter, string form, ProcessKind kind, int index, string[] operations, string? input = null)
    {
        var request = new RunRequest("bookmark-lookup", "1.0.0", "sqlite", adapter, form, "100k", new string('a', 40), new Dictionary<string, string> { [adapter == "ef" ? "Microsoft.EntityFrameworkCore" : "Groundwork.Sqlite"] = "10.0.8" }, new string('b', 64), "spec094-bookmark-lookup-v1", input ?? new string('c', 64), "list-by-stimulus-and-type", "plan-evidence.json", kind, index);
        var samples = operations.Select(operation => new OperationSample(operation, 100, 30, 1000, 1, 2, 3, Enumerable.Repeat(1d, 100).ToArray())).ToArray();
        ArtifactStore.Write(Directory, new ProcessArtifact(request, BenchmarkProtocol.Acceptance, true, new CorrectnessEvidence("9f3d29edc4c3e64409f3fb9b64b4ec3e7d5e5064d8233be8afd92215ec3d680e", "file-backed-distinct-connections", ["list-by-stimulus-and-type", "list-by-stimulus-type"], ["plan-evidence.json"]), samples, new MachineMetadata("test-os", "test-runtime", "X64", "X64", 1, "2026-07-24T00:00:00Z")));
    }
}
