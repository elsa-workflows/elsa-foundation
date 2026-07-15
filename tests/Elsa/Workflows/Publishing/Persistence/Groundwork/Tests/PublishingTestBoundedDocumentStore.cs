using Groundwork.Documents.Store;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Tests;

/// <summary>
/// Keeps the legacy in-memory/standalone SQLite fixtures focused on Publishing behavior while production
/// hosts execute the same <see cref="DocumentQuery"/> requests through certified physical runtimes.
/// </summary>
internal sealed class PublishingTestBoundedDocumentStore(IDocumentStore documents) : IBoundedDocumentStore
{
    private static readonly IReadOnlyDictionary<string, (string Index, string Path)> Queries =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            [PublishingGroundworkStorageManifest.ListByDefinitionQuery] =
                (PublishingGroundworkStorageManifest.ByDefinitionIndex, PublishingGroundworkStorageManifest.WorkflowDefinitionIdField),
            [PublishingGroundworkStorageManifest.FindByActivePublicationQuery] =
                (PublishingGroundworkStorageManifest.ByActivePublicationIndex, PublishingGroundworkStorageManifest.ActivePublicationIdField),
            [PublishingGroundworkStorageManifest.ListBySlotQuery] =
                (PublishingGroundworkStorageManifest.BySlotIndex, PublishingGroundworkStorageManifest.SlotIdField),
            [PublishingGroundworkStorageManifest.ListByPublicationQuery] =
                (PublishingGroundworkStorageManifest.ByPublicationIndex, PublishingGroundworkStorageManifest.PublicationIdField)
        };

    public async Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
    {
        var declaration = Resolve(query);
        var comparison = AssertShape(query, declaration.Path);
#pragma warning disable GW0004
        var result = await documents.QueryAsync(
            new DocumentStoreQuery(
                query.DocumentKind,
                declaration.Index,
                comparison.Values[0]!,
                query.Skip,
                query.Take),
            cancellationToken);
#pragma warning restore GW0004
        return new DocumentQueryResult(result, result.Count);
    }

    public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    private static (string Index, string Path) Resolve(DocumentQuery query)
    {
        if (!Queries.TryGetValue(query.QueryIdentity, out var declaration))
            throw new InvalidOperationException($"Undeclared Publishing test query '{query.QueryIdentity}'.");
        return declaration;
    }

    private static DocumentQueryComparison AssertShape(DocumentQuery query, string path)
    {
        var clause = query.Clauses.Count == 1
            ? query.Clauses[0]
            : throw new InvalidOperationException($"Publishing test query '{query.QueryIdentity}' must have one clause.");
        var comparison = clause.Comparisons.Count == 1
            ? clause.Comparisons[0]
            : throw new InvalidOperationException($"Publishing test query '{query.QueryIdentity}' must have one comparison.");
        if (comparison.Path != path || comparison.Operator != QueryComparisonOperator.Equal || comparison.Values.Count != 1)
            throw new InvalidOperationException($"Publishing test query '{query.QueryIdentity}' has an unexpected shape.");
        return comparison;
    }
}
