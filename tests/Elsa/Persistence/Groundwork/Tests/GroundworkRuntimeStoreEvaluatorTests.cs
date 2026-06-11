using Elsa.Persistence.Groundwork.RuntimeEvaluation;
using Groundwork.Core.Workloads;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkRuntimeStoreEvaluatorTests
{
    private readonly GroundworkRuntimeStoreEvaluator evaluator = new();

    [Fact]
    public void DefaultMatrixCoversRuntimeStoreCandidates()
    {
        var evaluations = evaluator.EvaluateDefaults();

        Assert.True(evaluations.Count >= 7);
        Assert.Contains(evaluations, evaluation => evaluation.Candidate.Name == "Runtime-defined business data");
        Assert.Contains(evaluations, evaluation => evaluation.Candidate.Name == "Workflow checkpoint state");
        Assert.Contains(evaluations, evaluation => evaluation.Candidate.Name == "Bookmark state");
        Assert.Contains(evaluations, evaluation => evaluation.Candidate.Name == "Workflow execution mailbox and agent ownership");
        Assert.Contains(evaluations, evaluation => evaluation.Candidate.Name == "Post-commit intents and outbox");
        Assert.Contains(evaluations, evaluation => evaluation.Candidate.Name == "Execution logs and audit stream");
        Assert.Contains(evaluations, evaluation => evaluation.Candidate.Name == "Distributed locks and leases");
    }

    [Fact]
    public void RuntimeDefinedBusinessDataIsGroundworkDefault()
    {
        var evaluation = Evaluate("Runtime-defined business data");

        Assert.Equal(WorkloadCandidateCategory.GroundworkDefault, evaluation.Recommendation);
        Assert.Equal(RuntimeStoreDecision.Go, evaluation.Decision);
        Assert.Empty(evaluation.EvidenceGates);
    }

    [Theory]
    [InlineData("Workflow checkpoint state")]
    [InlineData("Bookmark state")]
    [InlineData("Durable value and scheduler continuation state")]
    public void RuntimeContinuationStateIsBenchmarkGated(string candidateName)
    {
        var evaluation = Evaluate(candidateName);

        Assert.Equal(WorkloadCandidateCategory.BenchmarkGated, evaluation.Recommendation);
        Assert.Equal(RuntimeStoreDecision.BenchmarkGate, evaluation.Decision);
        Assert.Contains(evaluation.EvidenceGates, gate => gate.Kind == RuntimeEvidenceGateKind.Benchmark);
        Assert.Contains(evaluation.EvidenceGates, gate => gate.Kind == RuntimeEvidenceGateKind.Concurrency);
    }

    [Theory]
    [InlineData("Workflow execution mailbox and agent ownership")]
    [InlineData("Post-commit intents and outbox")]
    [InlineData("Execution logs and audit stream")]
    [InlineData("Distributed locks and leases")]
    public void OperationalRuntimeStoresAreSpecializedProviderCandidates(string candidateName)
    {
        var evaluation = Evaluate(candidateName);

        Assert.Equal(WorkloadCandidateCategory.SpecializedProvider, evaluation.Recommendation);
        Assert.Equal(RuntimeStoreDecision.NoGo, evaluation.Decision);
        Assert.Contains(evaluation.EvidenceGates, gate => gate.Kind == RuntimeEvidenceGateKind.Operations);
        Assert.Contains("outside the portable document-store contract", evaluation.Reason);
    }

    [Fact]
    public void WorkflowRuntimeHotPathsAreNotGroundworkDefault()
    {
        var hotPathEvaluations = evaluator.EvaluateDefaults()
            .Where(evaluation => evaluation.Candidate.Name != "Runtime-defined business data")
            .ToList();

        Assert.All(hotPathEvaluations, evaluation => Assert.NotEqual(WorkloadCandidateCategory.GroundworkDefault, evaluation.Recommendation));
    }

    private RuntimeStoreEvaluation Evaluate(string candidateName) =>
        evaluator.EvaluateDefaults().Single(evaluation => evaluation.Candidate.Name == candidateName);
}
