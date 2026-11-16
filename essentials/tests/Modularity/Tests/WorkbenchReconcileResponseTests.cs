using Elsa.Workbench;
using Microsoft.Extensions.Logging;
using Nuplane.Reconciliation;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>
/// The reconcile answer of the Workbench's module-management API: what Nuplane's reconcile service reports when it fails can hold
/// paths, feed addresses or connection details, so the answer carries a fixed reason and the host logs the report.
/// </summary>
public sealed class WorkbenchReconcileResponseTests
{
    private readonly CapturingLogger _log = new();

    [Fact]
    public void An_unavailable_outcome_answers_a_fixed_reason_and_logs_the_failure_with_the_correlation_id()
    {
        const string failure = @"The feed at https://user:secret@feeds.internal/nuget could not be read.";

        var response = ModuleManagementReconcileResponse.FromOutcome(new ManualReconcileOutcome(ManualReconcileOutcomeCode.Unavailable, "corr-1", null, failure), _log);

        Assert.Equal(("Unavailable", "reconcile-service-failed", "corr-1"), (response.Outcome, response.ReasonCode, response.CorrelationId));
        var logged = Assert.Single(_log.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains(failure, logged.Message, StringComparison.Ordinal);
        Assert.Contains("corr-1", logged.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ManualReconcileOutcomeCode.Rejected, "single-flight-active")]
    [InlineData(ManualReconcileOutcomeCode.Rejected, "store-lock-unavailable")]
    [InlineData(ManualReconcileOutcomeCode.Completed, null)]
    public void Any_other_outcome_answers_the_reason_it_carries(ManualReconcileOutcomeCode code, string? reason)
    {
        var response = ModuleManagementReconcileResponse.FromOutcome(new ManualReconcileOutcome(code, "corr-2", null, reason), _log);

        Assert.Equal((code.ToString(), reason), (response.Outcome, response.ReasonCode));
        Assert.Empty(_log.Entries);
    }
}
