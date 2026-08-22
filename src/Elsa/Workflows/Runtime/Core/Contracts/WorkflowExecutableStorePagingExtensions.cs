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

    public static async ValueTask<IReadOnlyCollection<WorkflowExecutableSourceReference>> ListAllByDefinitionVersionAsync(
        this IWorkflowExecutableSourceReferenceReader store,
        string definitionVersionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionVersionId);
        var items = new List<WorkflowExecutableSourceReference>();
        string? continuationToken = null;
        do
        {
            var page = await store.ListByDefinitionVersionPageAsync(
                new WorkflowExecutableSourceReferenceDefinitionVersionPageQuery(
                    definitionVersionId,
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

/// <summary>
/// The body behind
/// <see cref="IWorkflowExecutableSourceReferenceReader.ListByDefinitionVersionPageAsync"/>'s default: a residual
/// filter over the unnarrowed page, for readers that declare no by-definition-version route.
/// </summary>
/// <remarks>
/// A page may not be empty while a continuation is outstanding (<c>RuntimeStorePage</c> refuses that shape), so
/// the fallback keeps pulling underlying pages until one of them yields at least one match or the traversal ends.
/// Each underlying page carries at most <c>Limit</c> records, so the match set can never overflow the requested
/// limit, and the underlying continuation is handed straight back — it is the store's, and stays opaque.
/// </remarks>
internal static class WorkflowExecutableSourceReferenceReaderDefaults
{
    public static async ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByDefinitionVersionPageAsync(
        IWorkflowExecutableSourceReferenceReader store,
        WorkflowExecutableSourceReferenceDefinitionVersionPageQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(query);

        var matches = new List<WorkflowExecutableSourceReference>();
        var continuationToken = query.ContinuationToken;
        do
        {
            var page = await store.ListPageAsync(
                new WorkflowExecutableSourceReferencePageQuery(
                    limit: query.Limit,
                    continuationToken: continuationToken),
                cancellationToken);
            continuationToken = page.NextContinuationToken;
            matches.AddRange(page.Items.Where(reference =>
                StringComparer.Ordinal.Equals(reference.DefinitionVersionId, query.DefinitionVersionId)));
        } while (matches.Count == 0 && continuationToken is not null);

        return new RuntimeStorePage<WorkflowExecutableSourceReference>(query, matches, continuationToken);
    }
}
