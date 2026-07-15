using System.Globalization;
using System.Text.Json;
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
                (PublishingGroundworkStorageManifest.ByPublicationIndex, PublishingGroundworkStorageManifest.PublicationIdField),
            [PublishingGroundworkStorageManifest.DeleteExpiredQuery] =
                (PublishingGroundworkStorageManifest.ByExpiresAtIndex, PublishingGroundworkStorageManifest.ExpiresAtField)
        };

    public async Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
    {
        var declaration = Resolve(query);
        if (query.QueryIdentity == PublishingGroundworkStorageManifest.DeleteExpiredQuery)
            return await QueryExpiredAsync(query, declaration, cancellationToken);

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

    private async Task<DocumentQueryResult> QueryExpiredAsync(
        DocumentQuery query,
        (string Index, string Path) declaration,
        CancellationToken cancellationToken)
    {
        var comparison = AssertShape(
            query,
            declaration.Path,
            QueryComparisonOperator.LessThanOrEqual);
        if (query.Order.Count != 1 ||
            query.Order[0].Path != declaration.Path ||
            query.Order[0].Direction != global::Groundwork.Core.PhysicalStorage.PhysicalSortDirection.Ascending)
        {
            throw new InvalidOperationException(
                $"Publishing test query '{query.QueryIdentity}' must order by '{declaration.Path}' ascending.");
        }

#pragma warning disable GW0004
        var all = await documents.QueryAsync(new PortableDocumentQuery(query.DocumentKind), cancellationToken);
#pragma warning restore GW0004
        var cutoff = DateTimeOffset.Parse(comparison.Values[0]!, CultureInfo.InvariantCulture);
        var matches = all.Documents
            .Select(document => (Document: document, ExpiresAt: ReadExpiresAt(document)))
            .Where(entry => entry.ExpiresAt <= cutoff)
            .OrderBy(entry => entry.ExpiresAt)
            .ThenBy(entry => entry.Document.Id, StringComparer.Ordinal)
            .ToArray();
        var window = matches.Skip(query.Skip ?? 0);
        if (query.Take is { } take)
            window = window.Take(take);
        return new DocumentQueryResult(window.Select(entry => entry.Document).ToArray(), matches.Length);
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

    private static DocumentQueryComparison AssertShape(
        DocumentQuery query,
        string path,
        QueryComparisonOperator @operator = QueryComparisonOperator.Equal)
    {
        var clause = query.Clauses.Count == 1
            ? query.Clauses[0]
            : throw new InvalidOperationException($"Publishing test query '{query.QueryIdentity}' must have one clause.");
        var comparison = clause.Comparisons.Count == 1
            ? clause.Comparisons[0]
            : throw new InvalidOperationException($"Publishing test query '{query.QueryIdentity}' must have one comparison.");
        if (comparison.Path != path || comparison.Operator != @operator || comparison.Values.Count != 1)
            throw new InvalidOperationException($"Publishing test query '{query.QueryIdentity}' has an unexpected shape.");
        return comparison;
    }

    private static DateTimeOffset ReadExpiresAt(DocumentEnvelope envelope)
    {
        using var document = JsonDocument.Parse(envelope.ContentJson);
        return document.RootElement
            .GetProperty(PublishingGroundworkStorageManifest.ExpiresAtField)
            .GetDateTimeOffset();
    }
}
