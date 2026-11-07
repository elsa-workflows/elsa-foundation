using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Single-provider trusted source of immutable time policy in the selected entry's exact context.
/// Missing policy is unavailable; absence of mutable clock state never selects wall time.
/// </summary>
public interface IRuntimeTestTimePolicySource
{
    ValueTask<WorkflowTestTimePolicy?> FindAsync(string contextId, string contextFingerprint,
        CancellationToken cancellationToken = default);
}
