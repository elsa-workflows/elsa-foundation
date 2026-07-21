using System.Text;
using System.Text.Json;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>
/// Keyset continuation paging for the bounded-query test doubles, mirroring the real providers' cursor
/// semantics: the token carries the last returned row's order-field values plus its document id, and the
/// next page resumes strictly after that boundary. Offset tokens are forbidden here on purpose — an offset
/// re-serves already-returned rows when concurrent writers insert before the boundary, which the
/// dependency-graph loader correctly rejects as a conflicting identity.
/// </summary>
internal static class TestKeysetContinuations
{
    /// <summary>
    /// Windows <paramref name="sortedMatches"/> (already sorted by <c>query.Order</c> with a trailing
    /// ordinal document-id tie-break) into one page: resumes after the token boundary, applies
    /// skip/take, and emits the next keyset token when more rows remain.
    /// </summary>
    public static DocumentQueryResult Page(
        DocumentQuery query,
        string tokenPrefix,
        IReadOnlyList<DocumentEnvelope> sortedMatches,
        Func<DocumentEnvelope, string, string?> readComparable,
        string invalidTokenMessage)
    {
        var boundary = Decode(query, tokenPrefix, invalidTokenMessage);
        var remaining = (boundary is null
                ? sortedMatches
                : sortedMatches.Where(document => IsAfter(document, query, readComparable, boundary)))
            .Skip(query.Skip ?? 0)
            .ToArray();
        var page = query.Take is { } take ? remaining.Take(take).ToArray() : remaining;
        var nextContinuation = query.Take is not null && page.Length != 0 && page.Length < remaining.Length
            ? Encode(query, tokenPrefix, page[^1], readComparable)
            : null;
        return new DocumentQueryResult(page, sortedMatches.Count, nextContinuation);
    }

    private static bool IsAfter(
        DocumentEnvelope document,
        DocumentQuery query,
        Func<DocumentEnvelope, string, string?> readComparable,
        Boundary boundary)
    {
        for (var index = 0; index < query.Order.Count; index++)
        {
            var order = query.Order[index];
            var compared = StringComparer.Ordinal.Compare(
                readComparable(document, order.Path),
                boundary.Values[index]);
            if (order.Direction == PhysicalSortDirection.Descending)
                compared = -compared;
            if (compared != 0)
                return compared > 0;
        }

        return StringComparer.Ordinal.Compare(document.Id, boundary.Id) > 0;
    }

    private static string Encode(
        DocumentQuery query,
        string tokenPrefix,
        DocumentEnvelope lastReturned,
        Func<DocumentEnvelope, string, string?> readComparable)
    {
        var boundary = new Boundary(
            query.Order.Select(order => readComparable(lastReturned, order.Path)).ToArray(),
            lastReturned.Id);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(boundary)));
        return $"{TokenPrefix(query, tokenPrefix)}{payload}";
    }

    private static Boundary? Decode(DocumentQuery query, string tokenPrefix, string invalidTokenMessage)
    {
        if (query.Continuation is null)
            return null;

        var prefix = TokenPrefix(query, tokenPrefix);
        if (query.Continuation.StartsWith(prefix, StringComparison.Ordinal))
        {
            try
            {
                var boundary = JsonSerializer.Deserialize<Boundary>(
                    Encoding.UTF8.GetString(Convert.FromBase64String(query.Continuation[prefix.Length..])));
                if (boundary is { Id: not null } && boundary.Values?.Length == query.Order.Count)
                    return boundary;
            }
            catch (Exception exception) when (exception is FormatException or JsonException)
            {
                // Fall through to the shared rejection below.
            }
        }

        throw new InvalidDocumentQueryContinuationException(invalidTokenMessage);
    }

    private static string TokenPrefix(DocumentQuery query, string tokenPrefix) =>
        $"{tokenPrefix}:{query.DocumentKind}:{query.QueryIdentity}:";

    private sealed record Boundary(string?[] Values, string Id);
}
