using System.Globalization;
using System.Text.Json;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Tests;

/// <summary>
/// Keeps the legacy in-memory/standalone SQLite fixtures focused on Publishing behavior while production
/// hosts execute the same <see cref="DocumentQuery"/> requests through certified physical runtimes.
/// </summary>
internal sealed class PublishingTestBoundedDocumentStore(IDocumentEnumerationSource documents) : IBoundedDocumentStore
{
    private static readonly IReadOnlyDictionary<string, (string Index, string Path)> Queries =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            [PublishingGroundworkStorageManifest.ListBySlotQuery] =
                (PublishingGroundworkStorageManifest.BySlotIndex, PublishingGroundworkStorageManifest.SlotIdField),
            [PublishingGroundworkStorageManifest.DeleteExpiredQuery] =
                (PublishingGroundworkStorageManifest.ByExpiresAtIndex, PublishingGroundworkStorageManifest.ExpiresAtField)
        };

    public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
    {
        var declaration = Resolve(query);
        var isExpiry = query.QueryIdentity == PublishingGroundworkStorageManifest.DeleteExpiredQuery;
        var comparison = AssertShape(
            query,
            declaration.Path,
            isExpiry ? QueryComparisonOperator.LessThanOrEqual : QueryComparisonOperator.Equal);
        if (isExpiry &&
            (query.Order.Count != 1 ||
             query.Order[0].Path != declaration.Path ||
             query.Order[0].Direction != global::Groundwork.Core.PhysicalStorage.PhysicalSortDirection.Ascending))
        {
            throw new InvalidOperationException(
                $"Publishing test query '{query.QueryIdentity}' must order by '{declaration.Path}' ascending.");
        }

        // The declared surface carries no kind-wide read path, and standing in for one is exactly this
        // double's job — so it enumerates the double's own contents through the test-only enumeration seam
        // and evaluates the declared predicate here, the same predicate the physical runtimes push down to
        // their projected columns in production.
        var all = documents.Snapshot(query.DocumentKind);
        var matches = isExpiry
            ? MatchExpired(all, declaration.Path, comparison.Values[0]!)
            : all
                .Where(document => string.Equals(
                    ReadPath(document, declaration.Path),
                    comparison.Values[0],
                    StringComparison.Ordinal))
                .OrderBy(document => document.Id, StringComparer.Ordinal)
                .ToArray();

        var window = matches.Skip(query.Skip ?? 0);
        if (query.Take is { } take)
            window = window.Take(take);
        return Task.FromResult(new DocumentQueryResult(window.ToArray(), matches.Length));
    }

    private static DocumentEnvelope[] MatchExpired(
        IReadOnlyCollection<DocumentEnvelope> documents,
        string path,
        string cutoffValue)
    {
        var cutoff = DateTimeOffset.Parse(cutoffValue, CultureInfo.InvariantCulture);
        return documents
            .Select(document => (
                Document: document,
                ExpiresAt: DateTimeOffset.Parse(ReadPath(document, path)!, CultureInfo.InvariantCulture)))
            .Where(entry => entry.ExpiresAt <= cutoff)
            .OrderBy(entry => entry.ExpiresAt)
            .ThenBy(entry => entry.Document.Id, StringComparer.Ordinal)
            .Select(entry => entry.Document)
            .ToArray();
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

    /// <summary>Reads a canonical dotted JSON path, matching how the declared indexes address their fields.</summary>
    private static string? ReadPath(DocumentEnvelope envelope, string path)
    {
        using var document = JsonDocument.Parse(envelope.ContentJson);
        var element = document.RootElement;
        foreach (var segment in path.Split('.'))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element))
                return null;
        }

        return element.ValueKind == JsonValueKind.Null ? null : element.GetString();
    }
}
