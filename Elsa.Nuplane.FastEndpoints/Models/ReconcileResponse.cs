using Nuplane.Reconciliation;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Nuplane.FastEndpoints.Models
{
    /// <summary>
    /// Response DTO for reconcile trigger endpoint.
    /// </summary>
    internal sealed record ReconcileResponse(
        string OutcomeCode,
        string CorrelationId,
        string? ReasonCode,
        bool? IsDegraded,
        IReadOnlyList<string>? FailedPackages)
    {
        /// <summary>
        /// Initializes from a <see cref="ManualReconcileOutcome"/>.
        /// </summary>
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
}
