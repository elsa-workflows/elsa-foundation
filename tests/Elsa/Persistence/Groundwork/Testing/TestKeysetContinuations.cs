using System.Text;
using System.Text.Json;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>
/// Keyset continuation paging for the bounded-query test doubles, mirroring the real providers' cursor
/// semantics: the token carries the last returned row's sort-key values (document-id tie-broken), and the
/// next page resumes strictly after that boundary. Offset tokens are forbidden here on purpose — an offset
/// re-serves already-returned rows when concurrent writers insert before the boundary, which the
/// dependency-graph loader correctly rejects as a conflicting identity.
/// </summary>
public static class TestKeysetContinuations
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
        string invalidTokenMessage) =>
        Page(
            query,
            tokenPrefix,
            sortedMatches,
            document => [.. query.Order.Select(order => readComparable(document, order.Path)), document.Id],
            document => document,
            [.. query.Order.Select(order => order.Direction), PhysicalSortDirection.Ascending],
            invalidTokenMessage);

    /// <summary>
    /// As above, for callers that key on precomputed candidate tuples instead of reading order fields
    /// from the envelope JSON: <paramref name="keyOf"/> returns the full sort key — one value per
    /// <paramref name="directions"/> entry, matching the order <paramref name="sortedMatches"/> is
    /// sorted by — whose last element must uniquely identify the row (e.g. the document id).
    /// </summary>
    public static DocumentQueryResult Page<TCandidate>(
        DocumentQuery query,
        string tokenPrefix,
        IReadOnlyList<TCandidate> sortedMatches,
        Func<TCandidate, string?[]> keyOf,
        Func<TCandidate, DocumentEnvelope> documentOf,
        IReadOnlyList<PhysicalSortDirection> directions,
        string invalidTokenMessage)
    {
        var boundary = Decode(query, tokenPrefix, directions.Count, invalidTokenMessage);
        var remaining = (boundary is null
                ? sortedMatches
                : sortedMatches.Where(candidate => IsAfter(keyOf(candidate), boundary, directions)))
            .Skip(query.Skip ?? 0)
            .ToArray();
        var page = query.Take is { } take ? remaining.Take(take).ToArray() : remaining;
        var nextContinuation = query.Take is not null && page.Length != 0 && page.Length < remaining.Length
            ? Encode(query, tokenPrefix, keyOf(page[^1]))
            : null;
        return new DocumentQueryResult(page.Select(documentOf).ToArray(), sortedMatches.Count, nextContinuation);
    }

    private static bool IsAfter(string?[] key, string?[] boundary, IReadOnlyList<PhysicalSortDirection> directions)
    {
        for (var index = 0; index < boundary.Length; index++)
        {
            var compared = StringComparer.Ordinal.Compare(key[index], boundary[index]);
            if (directions[index] == PhysicalSortDirection.Descending)
                compared = -compared;
            if (compared != 0)
                return compared > 0;
        }

        return false;
    }

    private static string Encode(DocumentQuery query, string tokenPrefix, string?[] key)
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(key)));
        return $"{TokenPrefix(query, tokenPrefix)}{payload}";
    }

    private static string?[]? Decode(DocumentQuery query, string tokenPrefix, int keyLength, string invalidTokenMessage)
    {
        if (query.Continuation is null)
            return null;

        var prefix = TokenPrefix(query, tokenPrefix);
        if (query.Continuation.StartsWith(prefix, StringComparison.Ordinal))
        {
            try
            {
                var boundary = JsonSerializer.Deserialize<string?[]>(
                    Encoding.UTF8.GetString(Convert.FromBase64String(query.Continuation[prefix.Length..])));
                if (boundary?.Length == keyLength && boundary[^1] is not null)
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
}
