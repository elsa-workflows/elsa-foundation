using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Explicit complete traversals over executable-material store pages. These helpers belong at business call sites;
/// providers only ever execute the finite page operations declared by their contracts.
/// </summary>
public static class WorkflowExecutableStorePagingExtensions
{
    public static async ValueTask<IReadOnlyCollection<WorkflowExecutable>> ListAllAsync(
        this IWorkflowExecutableStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var items = new List<WorkflowExecutable>();
        string? continuationToken = null;
        do
        {
            var page = await store.ListPageAsync(
                new RuntimeStorePageRequest(continuationToken: continuationToken),
                cancellationToken);
            items.AddRange(page.Items);
            continuationToken = page.NextContinuationToken;
        } while (continuationToken is not null);

        return items;
    }

    public static async ValueTask<IReadOnlyCollection<ExecutableActivityTemplate>> ListAllAsync(
        this IExecutableActivityTemplateStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var items = new List<ExecutableActivityTemplate>();
        string? continuationToken = null;
        do
        {
            var page = await store.ListPageAsync(
                new RuntimeStorePageRequest(continuationToken: continuationToken),
                cancellationToken);
            items.AddRange(page.Items);
            continuationToken = page.NextContinuationToken;
        } while (continuationToken is not null);

        return items;
    }

    public static async ValueTask<IReadOnlyCollection<WorkflowExecutableSourceReference>> ListAllAsync(
        this IWorkflowExecutableSourceReferenceReader store,
        WorkflowExecutableReferenceScope? scope = null,
        bool liveOnly = false,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var items = new List<WorkflowExecutableSourceReference>();
        string? continuationToken = null;
        do
        {
            var page = await store.ListPageAsync(
                new WorkflowExecutableSourceReferencePageQuery(
                    scope,
                    liveOnly,
                    now,
                    continuationToken: continuationToken),
                cancellationToken);
            items.AddRange(page.Items);
            continuationToken = page.NextContinuationToken;
        } while (continuationToken is not null);

        return items;
    }

    public static async ValueTask<IReadOnlyCollection<WorkflowExecutableSourceReference>> ListAllByArtifactAsync(
        this IWorkflowExecutableSourceReferenceReader store,
        string artifactId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        var items = new List<WorkflowExecutableSourceReference>();
        string? continuationToken = null;
        do
        {
            var page = await store.ListByArtifactPageAsync(
                new WorkflowExecutableSourceReferenceArtifactPageQuery(artifactId, continuationToken: continuationToken),
                cancellationToken);
            items.AddRange(page.Items);
            continuationToken = page.NextContinuationToken;
        } while (continuationToken is not null);

        return items;
    }
}
