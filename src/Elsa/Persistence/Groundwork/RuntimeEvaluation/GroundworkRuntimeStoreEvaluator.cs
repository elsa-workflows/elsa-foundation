using Groundwork.Core.Workloads;

namespace Elsa.Persistence.Groundwork.RuntimeEvaluation;

public sealed class GroundworkRuntimeStoreEvaluator
{
    public IReadOnlyList<RuntimeStoreEvaluation> EvaluateDefaults() =>
        DefaultCandidates.Select(Evaluate).ToList();

    public RuntimeStoreEvaluation Evaluate(RuntimeStoreCandidate candidate)
    {
        if (candidate.AllowsGroundworkDefault &&
            candidate is { RequiresAtomicCommit: false, RequiresPostCommitDispatch: false, IsOperationalStream: false, RequiresLeaseOrMailbox: false })
        {
            return new RuntimeStoreEvaluation(
                candidate,
                WorkloadCandidateCategory.GroundworkDefault,
                RuntimeStoreDecision.Go,
                "The candidate is business data with document/index access patterns and no workflow continuation or operational-stream semantics.",
                []);
        }

        if (candidate.IsOperationalStream || candidate.RequiresLeaseOrMailbox || candidate.RequiresPostCommitDispatch)
        {
            return new RuntimeStoreEvaluation(
                candidate,
                WorkloadCandidateCategory.SpecializedProvider,
                RuntimeStoreDecision.NoGo,
                "The candidate requires operational stream, ownership, lease, mailbox, or post-commit dispatch behavior outside the portable document-store contract.",
                SpecializedProviderGates);
        }

        if (candidate.Family == WorkloadFamily.RuntimeContinuationState || candidate.RequiresAtomicCommit)
        {
            return new RuntimeStoreEvaluation(
                candidate,
                WorkloadCandidateCategory.BenchmarkGated,
                RuntimeStoreDecision.BenchmarkGate,
                "The candidate is workflow continuation state and needs benchmark, concurrency, retry, and diagnostic evidence before any Groundwork-backed implementation.",
                BenchmarkGates);
        }

        return new RuntimeStoreEvaluation(
            candidate,
            WorkloadCandidateCategory.BenchmarkGated,
            RuntimeStoreDecision.BenchmarkGate,
            "The candidate is not a proven Groundwork-default workload and must be evaluated before migration.",
            BenchmarkGates);
    }

    public static IReadOnlyList<RuntimeStoreCandidate> DefaultCandidates { get; } =
    [
        new(
            "Runtime-defined business data",
            WorkloadFamily.RuntimeDefinedBusinessData,
            RequiresAtomicCommit: false,
            RequiresPostCommitDispatch: false,
            IsOperationalStream: false,
            RequiresLeaseOrMailbox: false,
            AllowsGroundworkDefault: true),
        new(
            "Workflow checkpoint state",
            WorkloadFamily.RuntimeContinuationState,
            RequiresAtomicCommit: true,
            RequiresPostCommitDispatch: false,
            IsOperationalStream: false,
            RequiresLeaseOrMailbox: false,
            AllowsGroundworkDefault: false),
        new(
            "Bookmark state",
            WorkloadFamily.RuntimeContinuationState,
            RequiresAtomicCommit: true,
            RequiresPostCommitDispatch: false,
            IsOperationalStream: false,
            RequiresLeaseOrMailbox: false,
            AllowsGroundworkDefault: false),
        new(
            "Durable value and scheduler continuation state",
            WorkloadFamily.RuntimeContinuationState,
            RequiresAtomicCommit: true,
            RequiresPostCommitDispatch: false,
            IsOperationalStream: false,
            RequiresLeaseOrMailbox: false,
            AllowsGroundworkDefault: false),
        new(
            "Workflow execution mailbox and agent ownership",
            WorkloadFamily.OperationalStream,
            RequiresAtomicCommit: false,
            RequiresPostCommitDispatch: false,
            IsOperationalStream: true,
            RequiresLeaseOrMailbox: true,
            AllowsGroundworkDefault: false),
        new(
            "Post-commit intents and outbox",
            WorkloadFamily.OperationalStream,
            RequiresAtomicCommit: false,
            RequiresPostCommitDispatch: true,
            IsOperationalStream: true,
            RequiresLeaseOrMailbox: false,
            AllowsGroundworkDefault: false),
        new(
            "Execution logs and audit stream",
            WorkloadFamily.AuditTrail,
            RequiresAtomicCommit: false,
            RequiresPostCommitDispatch: false,
            IsOperationalStream: true,
            RequiresLeaseOrMailbox: false,
            AllowsGroundworkDefault: false),
        new(
            "Distributed locks and leases",
            WorkloadFamily.OperationalStream,
            RequiresAtomicCommit: false,
            RequiresPostCommitDispatch: false,
            IsOperationalStream: true,
            RequiresLeaseOrMailbox: true,
            AllowsGroundworkDefault: false)
    ];

    private static IReadOnlyList<RuntimeEvidenceGate> BenchmarkGates { get; } =
    [
        new(RuntimeEvidenceGateKind.Benchmark, "Measure read/write p95 and p99 latency under expected runtime concurrency."),
        new(RuntimeEvidenceGateKind.Concurrency, "Prove optimistic concurrency behavior under parallel start, resume, and checkpoint attempts."),
        new(RuntimeEvidenceGateKind.Retry, "Prove idempotent retry behavior after failed writes and process restarts."),
        new(RuntimeEvidenceGateKind.Diagnostics, "Expose diagnostics for failed checkpoint writes, materialization, and migration state.")
    ];

    private static IReadOnlyList<RuntimeEvidenceGate> SpecializedProviderGates { get; } =
    [
        new(RuntimeEvidenceGateKind.Operations, "Define provider-specific ordering, ownership, lease, retention, or stream semantics."),
        new(RuntimeEvidenceGateKind.Retry, "Define retry and recovery behavior for partially processed work."),
        new(RuntimeEvidenceGateKind.Diagnostics, "Expose operational metrics, traces, and alert guidance.")
    ];
}
