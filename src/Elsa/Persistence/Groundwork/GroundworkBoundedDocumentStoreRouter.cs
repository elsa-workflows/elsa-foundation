using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork;

/// <summary>
/// Routes a bounded document query to the provider runtime compiled for that document kind's exact
/// admitted physical route. Query identities are intentionally resolved inside the selected route,
/// so different storage units may safely use the same stable identity.
/// </summary>
public sealed class GroundworkBoundedDocumentStoreRouter : IBoundedDocumentStore
{
    private readonly IReadOnlyDictionary<string, IBoundedDocumentStore> stores;

    public GroundworkBoundedDocumentStoreRouter(
        IEnumerable<KeyValuePair<string, IBoundedDocumentStore>> stores)
    {
        ArgumentNullException.ThrowIfNull(stores);
        var entries = stores.ToArray();
        if (entries.Any(entry => string.IsNullOrWhiteSpace(entry.Key) || entry.Value is null))
            throw new ArgumentException("Bounded document-store routes require a document kind and runtime.", nameof(stores));

        var duplicates = entries
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (duplicates.Length != 0)
        {
            throw new ArgumentException(
                $"Bounded document-store routes must be unique by document kind: {string.Join(", ", duplicates)}.",
                nameof(stores));
        }

        this.stores = entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
    }

    public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        Resolve(query).QueryAsync(query, cancellationToken);

    public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        Resolve(query).CountAsync(query, cancellationToken);

    public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        Resolve(query).FirstOrDefaultAsync(query, cancellationToken);

    public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        Resolve(query).AnyAsync(query, cancellationToken);

    private IBoundedDocumentStore Resolve(DocumentQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return stores.TryGetValue(query.DocumentKind, out var store)
            ? store
            : throw new InvalidOperationException(
                $"No bounded document-query runtime was admitted for document kind '{query.DocumentKind}'.");
    }
}
