using Groundwork.Core.Workloads;

namespace Elsa.Persistence.Groundwork.RuntimeEvaluation;

public sealed record RuntimeStoreEvaluation(
    RuntimeStoreCandidate Candidate,
    WorkloadCandidateCategory Recommendation,
    RuntimeStoreDecision Decision,
    string Reason,
    IReadOnlyList<RuntimeEvidenceGate> EvidenceGates);

public sealed record RuntimeEvidenceGate(RuntimeEvidenceGateKind Kind, string Description);

public enum RuntimeStoreDecision
{
    Go,
    BenchmarkGate,
    NoGo
}

public enum RuntimeEvidenceGateKind
{
    Benchmark,
    Concurrency,
    Retry,
    Diagnostics,
    Operations
}
