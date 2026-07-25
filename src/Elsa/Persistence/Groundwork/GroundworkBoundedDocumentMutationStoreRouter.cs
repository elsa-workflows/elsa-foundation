using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork;

/// <summary>
/// Routes a bounded document mutation to the provider runtime admitted for the mutation's document kind.
/// Mutation identities remain scoped to one storage unit, so different units may safely reuse the same name.
/// </summary>
public sealed class GroundworkBoundedDocumentMutationStoreRouter : IBoundedDocumentMutationStore
{
    private readonly IReadOnlyDictionary<string, Lazy<IBoundedDocumentMutationStore>> stores;

    public GroundworkBoundedDocumentMutationStoreRouter(
        IEnumerable<KeyValuePair<string, IBoundedDocumentMutationStore>> stores)
        : this(Project(stores))
    {
    }

    public static GroundworkBoundedDocumentMutationStoreRouter CreateLazy(
        IEnumerable<KeyValuePair<string, Func<IBoundedDocumentMutationStore>>> routeFactories) =>
        new(Project(routeFactories));

    private GroundworkBoundedDocumentMutationStoreRouter(
        IReadOnlyList<KeyValuePair<string, Lazy<IBoundedDocumentMutationStore>>> entries)
    {
        if (entries.Any(entry => string.IsNullOrWhiteSpace(entry.Key) || entry.Value is null))
            throw new ArgumentException("Bounded document-mutation routes require a document kind and runtime.", nameof(entries));

        var duplicates = entries
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (duplicates.Length != 0)
        {
            throw new ArgumentException(
                $"Bounded document-mutation routes must be unique by document kind: {string.Join(", ", duplicates)}.",
                nameof(entries));
        }

        stores = entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
    }

    public Task<BoundedMutationResult> ExecuteAsync(
        DocumentMutation mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        return stores.TryGetValue(mutation.DocumentKind, out var store)
            ? store.Value.ExecuteAsync(mutation, cancellationToken)
            : throw new InvalidOperationException(
                $"No bounded document-mutation runtime was admitted for document kind '{mutation.DocumentKind}'.");
    }

    private static IReadOnlyList<KeyValuePair<string, Lazy<IBoundedDocumentMutationStore>>> Project(
        IEnumerable<KeyValuePair<string, IBoundedDocumentMutationStore>> stores)
    {
        ArgumentNullException.ThrowIfNull(stores);
        return stores
            .Select(entry => KeyValuePair.Create(
                entry.Key,
                entry.Value is null ? null! : new Lazy<IBoundedDocumentMutationStore>(entry.Value)))
            .ToArray();
    }

    private static IReadOnlyList<KeyValuePair<string, Lazy<IBoundedDocumentMutationStore>>> Project(
        IEnumerable<KeyValuePair<string, Func<IBoundedDocumentMutationStore>>> routeFactories)
    {
        ArgumentNullException.ThrowIfNull(routeFactories);
        return routeFactories
            .Select(entry => KeyValuePair.Create(
                entry.Key,
                entry.Value is null
                    ? null!
                    : new Lazy<IBoundedDocumentMutationStore>(
                        entry.Value,
                        LazyThreadSafetyMode.ExecutionAndPublication)))
            .ToArray();
    }
}
