using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Single retained-policy owner with no default. The Runtime gate supplies the exact revalidated entry.
/// Implementations validate captured module profiles and current authority; admission/receipts atomically fence
/// against normal test-scope closure. A registration or authored profile alone never grants effect permission.
/// </summary>
public interface IRuntimeTestEffectPolicySource
{
    ValueTask ValidateNodeAsync(WorkflowTestEntry entry, RuntimeTestNodeRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new RuntimeTestEffectUnavailableException("runtime.test-effects.node-unavailable", "The node has no qualified test effect profile."));

    ValueTask ValidateActivityAsync(WorkflowTestEntry entry, RuntimeTestInvocation invocation,
        CancellationToken cancellationToken = default);

    ValueTask<RuntimeSecretReference> MapSecretAsync(WorkflowTestEntry entry, RuntimeTestInvocation invocation,
        string inputKey, RuntimeSecretReference reference, CancellationToken cancellationToken = default);

    ValueTask<RuntimeTestEffectAdmission> BeginAsync(WorkflowTestEntry entry, RuntimeTestEffectRequest request,
        CancellationToken cancellationToken = default);

    ValueTask RevalidateAsync(WorkflowTestEntry entry, RuntimeTestEffectAdmission admission,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Settlement remains owed after scope closure or cancellation. Accept only the exact retained admission;
    /// completing a receipt cannot grant new work or revive an execution.
    /// </summary>
    ValueTask CompleteAsync(WorkflowTestEntry entry, RuntimeTestEffectCompletion completion,
        CancellationToken cancellationToken = default);
}
