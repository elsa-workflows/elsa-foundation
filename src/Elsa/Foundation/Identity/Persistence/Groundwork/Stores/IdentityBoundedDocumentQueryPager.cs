using Groundwork.Core.Queries;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Exhausts one admitted Identity cursor route without ever changing its route, scope, predicates,
/// or declared order. Groundwork validates those bindings inside its opaque continuation; this
/// helper additionally guards page count, aggregate size, repeated tokens, and no-progress pages.
/// </summary>
public static class IdentityBoundedDocumentQueryPager
{
    public static DocumentQuery CreatePageQuery(
        string documentKind,
        string queryIdentity,
        IReadOnlyList<DocumentQueryClause> clauses,
        int take,
        string? continuation = null) =>
        new(
            documentKind,
            queryIdentity,
            clauses,
            IdentityStorageManifest.GetDeclaredOrder(queryIdentity),
            skip: null,
            take: take,
            continuation: continuation,
            resultOperation: BoundedQueryResultOperation.Documents);

    public static async ValueTask<IReadOnlyList<DocumentEnvelope>> ReadAllPagesAsync(
        IBoundedDocumentStore store,
        string documentKind,
        string queryIdentity,
        IReadOnlyList<DocumentQueryClause> clauses,
        int pageSize,
        int maximumMaterialization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(queryIdentity);
        ArgumentNullException.ThrowIfNull(clauses);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumMaterialization);

        var documents = new List<DocumentEnvelope>();
        var seenDocumentIdentities = new HashSet<(string? Scope, string Id)>();
        var seenContinuations = new HashSet<string>(StringComparer.Ordinal);
        // Providers may return fewer documents than requested while still issuing a valid
        // continuation. One distinct document is therefore the only portable progress unit.
        var maximumPages = maximumMaterialization;
        string? continuation = null;

        for (var pageNumber = 0; pageNumber < maximumPages; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await store.QueryAsync(
                CreatePageQuery(documentKind, queryIdentity, clauses, pageSize, continuation),
                cancellationToken);
            if (result.Documents.Count > pageSize)
            {
                throw new InvalidOperationException(
                    $"Groundwork Identity route '{queryIdentity}' provider page exceeded its requested bound.");
            }

            var madeProgress = false;
            foreach (var document in result.Documents)
            {
                if (!seenDocumentIdentities.Add((document.Scope?.Value, document.Id)))
                {
                    throw new InvalidOperationException(
                        $"Groundwork Identity route '{queryIdentity}' repeated a storage identity while following a continuation.");
                }

                documents.Add(document);
                madeProgress = true;
                if (documents.Count > maximumMaterialization)
                {
                    throw new InvalidOperationException(
                        $"Groundwork Identity route '{queryIdentity}' exceeded the bounded relationship materialization limit.");
                }
            }

            if (result.NextContinuation is null)
                return documents;
            if (string.IsNullOrWhiteSpace(result.NextContinuation))
            {
                throw new InvalidOperationException(
                    $"Groundwork Identity route '{queryIdentity}' returned an empty continuation.");
            }
            if (!madeProgress)
            {
                throw new InvalidOperationException(
                    $"Groundwork Identity route '{queryIdentity}' returned a continuation without forward progress.");
            }
            if (!seenContinuations.Add(result.NextContinuation))
            {
                throw new InvalidOperationException(
                    $"Groundwork Identity route '{queryIdentity}' repeated a previously seen continuation.");
            }

            continuation = result.NextContinuation;
        }

        throw new InvalidOperationException(
            $"Groundwork Identity route '{queryIdentity}' exceeded the bounded relationship page limit.");
    }
}
