using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Read-only observation of active scheduler claims for safety checks that must not race executing work.</summary>
public interface IWorkflowSchedulerWorkClaimInspection
{
    ValueTask<IReadOnlyCollection<RuntimeSchedulerWorkClaim>> ListActiveClaimsAsync(
        string workflowExecutionId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
