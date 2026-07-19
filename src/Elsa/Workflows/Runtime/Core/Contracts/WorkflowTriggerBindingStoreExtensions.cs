using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Bounded-page traversal helpers for operations whose business semantics require every matching trigger binding.
/// </summary>
public static class WorkflowTriggerBindingStoreExtensions
{
    public static ValueTask<IReadOnlyList<WorkflowTriggerBinding>> ListAllByPublicationAsync(
        this IWorkflowTriggerBindingStore store,
        string publicationId,
        CancellationToken cancellationToken = default) =>
        TraverseAsync(
            store,
            continuationToken => store.ListByPublicationAsync(
                new WorkflowTriggerBindingPublicationPageQuery(
                    publicationId,
                    WorkflowTriggerBindingPublicationPageQuery.MaximumLimit,
                    continuationToken),
                cancellationToken));

    public static ValueTask<IReadOnlyList<WorkflowTriggerBinding>> ListAllByArtifactAsync(
        this IWorkflowTriggerBindingStore store,
        string artifactId,
        CancellationToken cancellationToken = default) =>
        TraverseAsync(
            store,
            continuationToken => store.ListByArtifactAsync(
                new WorkflowTriggerBindingArtifactPageQuery(
                    artifactId,
                    WorkflowTriggerBindingArtifactPageQuery.MaximumLimit,
                    continuationToken),
                cancellationToken));

    public static async ValueTask<IReadOnlyList<WorkflowTriggerBinding>> ListAllByStimulusAsync(
        this IWorkflowTriggerBindingStore store,
        string stimulusType,
        string stimulusHash,
        CancellationToken cancellationToken = default)
    {
        return await TraverseAsync(
            store,
            continuationToken => store.ListByStimulusAsync(
                new WorkflowTriggerBindingPageQuery(
                    stimulusType,
                    stimulusHash,
                    WorkflowTriggerBindingPageQuery.MaximumLimit,
                    continuationToken),
                cancellationToken));
    }

    public static async ValueTask<IReadOnlyList<WorkflowTriggerBinding>> ListAllByStimulusTypeAsync(
        this IWorkflowTriggerBindingStore store,
        string stimulusType,
        CancellationToken cancellationToken = default)
    {
        return await TraverseAsync(
            store,
            continuationToken => store.ListByStimulusTypeAsync(
                new WorkflowTriggerBindingTypePageQuery(
                    stimulusType,
                    WorkflowTriggerBindingTypePageQuery.MaximumLimit,
                    continuationToken),
                cancellationToken));
    }

    private static async ValueTask<IReadOnlyList<WorkflowTriggerBinding>> TraverseAsync(
        IWorkflowTriggerBindingStore store,
        Func<string?, ValueTask<WorkflowTriggerBindingPage>> getPage)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(getPage);
        var bindings = new List<WorkflowTriggerBinding>();
        string? continuationToken = null;

        do
        {
            var page = await getPage(continuationToken);
            bindings.AddRange(page.Items);
            continuationToken = page.NextContinuationToken;
        } while (continuationToken is not null);

        return bindings;
    }
}
