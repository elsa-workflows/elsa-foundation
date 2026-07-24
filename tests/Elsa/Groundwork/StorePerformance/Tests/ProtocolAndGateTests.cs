using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
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

        var plan = MatrixPlan.Create(Request());
        Assert.Equal([ProcessKind.Warmup, ProcessKind.Measured, ProcessKind.Measured, ProcessKind.Measured], plan.Runs.Select(run => run.ProcessKind));
        Assert.Equal([0, 1, 2, 3], plan.Runs.Select(run => run.ProcessIndex));
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

        var error = Assert.Throws<PerformanceContractException>(() => MatrixPlan.Create(request));

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

    private static MatrixRequest Request() => new(
        "bookmark-lookup", "1.0.0", "sqlite", "groundwork", "document-type-specific-tables", "100k",
        new string('a', 40), new Dictionary<string, string> { ["Groundwork.Sqlite"] = "0.0.1-preview.81" }, new string('b', 64), "spec094-bookmark-lookup-v1", new string('c', 64), "list-by-stimulus-and-type", "native-plan.json");
}
