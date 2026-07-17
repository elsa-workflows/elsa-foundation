using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Bounded-page traversal helpers for operations whose business semantics require every exact-stimulus match.
/// </summary>
public static class WorkflowTriggerBindingStoreExtensions
{
    public static async ValueTask<IReadOnlyList<WorkflowTriggerBinding>> ListAllByStimulusAsync(
        this IWorkflowTriggerBindingStore store,
        string stimulusType,
        string stimulusHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var bindings = new List<WorkflowTriggerBinding>();
        string? continuationToken = null;

        do
        {
            var page = await store.ListByStimulusAsync(
                new WorkflowTriggerBindingPageQuery(
                    stimulusType,
                    stimulusHash,
                    WorkflowTriggerBindingPageQuery.MaximumLimit,
                    continuationToken),
                cancellationToken);
            bindings.AddRange(page.Items);
            continuationToken = page.NextContinuationToken;
        } while (continuationToken is not null);

        return bindings;
    }
}
