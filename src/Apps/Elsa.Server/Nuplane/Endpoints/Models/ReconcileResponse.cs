using Nuplane.Reconciliation;

namespace Elsa.Server.Nuplane.Endpoints.Models;

internal sealed record ReconcileResponse(
    string OutcomeCode,
    string CorrelationId,
    string? ReasonCode,
    bool? IsDegraded,
    IReadOnlyList<string>? FailedPackages)
{
    public ReconcileResponse(ManualReconcileOutcome outcome)
        : this(
            outcome.OutcomeCode.ToString(),
            outcome.CorrelationId,
            outcome.ReasonCode,
            outcome.RunResult?.IsDegraded,
            outcome.RunResult?.FailedPackages)
    {
    }
}
