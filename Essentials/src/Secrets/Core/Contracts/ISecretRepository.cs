using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Core.Contracts;

/// <summary>
/// Stores secrets by tenant and normalized name. A name is unique only among secrets that are not deleted: deleting a
/// secret (saving it with <see cref="SecretStatus.Deleted"/>) frees its name, and the deleted secret stays stored, with its
/// versions, beside whatever secret takes the name next. A secret that takes a freed name is a new secret with its own id
/// and versions; nothing of the deleted one becomes visible through it.
/// </summary>
public interface ISecretRepository : IPagedSecretRepository
{
    /// <summary>
    /// The secret that holds the name, or when none does, a secret deleted under it, so a caller can tell a deleted
    /// secret from one that never existed. A deleted secret is never returned while another secret holds the name.
    /// </summary>
    ValueTask<Secret?> FindAsync(string tenantId, string normalizedName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds <paramref name="secret"/>, or returns <c>false</c> when a secret that is not deleted already holds its name.
    /// Secrets deleted under the name do not block it.
    /// </summary>
    ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes <paramref name="secret"/> unconditionally. A secret that is not deleted replaces the one holding its name.
    /// A deleted one is stored by its id and frees the name when it held it; it never removes another secret that
    /// holds the name by now.
    /// </summary>
    ValueTask SaveAsync(Secret secret, CancellationToken cancellationToken = default);
}

public interface IRevisionAwareSecretRepository
{
    /// <summary>The same secret <see cref="ISecretRepository.FindAsync"/> returns, with its revision.</summary>
    ValueTask<SecretRevisionedRecord?> FindWithRevisionAsync(string tenantId, string normalizedName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves <paramref name="secret"/> when <paramref name="expectedRevision"/> is the revision of the secret holding its
    /// name, or creates it when that is <c>null</c> and nothing holds its row yet. A deleted secret's revision saves
    /// nothing: deleted secrets are history, and a save against one reports <see cref="SecretRevisionSaveStatus.NotFound"/>
    /// or <see cref="SecretRevisionSaveStatus.Conflict"/>.
    /// </summary>
    ValueTask<SecretRevisionSaveResult> SaveWithRevisionAsync(Secret secret, string? expectedRevision, CancellationToken cancellationToken = default);
}

public interface IPagedSecretRepository
{
    ValueTask<SecretRepositoryPage> ListPageAsync(string tenantId, SecretRepositoryListRequest request, CancellationToken cancellationToken = default);
}

public sealed record SecretRevisionedRecord(Secret Secret, string Revision);

public sealed record SecretRevisionSaveResult(SecretRevisionSaveStatus Status, string? Revision = null);

public enum SecretRevisionSaveStatus
{
    Saved,
    Conflict,
    NotFound
}

public sealed record SecretRepositoryListRequest
{
    public const int MaximumTake = 250;
    public const int MaximumFacetValues = 64;
    public const int MaximumSearchLength = 256;

    public SecretRepositoryListRequest(
        string? search = null,
        string? typeName = null,
        IReadOnlyCollection<string>? typeNames = null,
        string? storeName = null,
        IReadOnlyCollection<string>? storeNames = null,
        string? scope = null,
        SecretStatus? status = null,
        SecretStatus? excludedStatus = null,
        bool activeOnly = false,
        DateTimeOffset? now = null,
        int skip = 0,
        int take = 50)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        if (take is <= 0 or > MaximumTake)
        {
            throw new ArgumentOutOfRangeException(
                nameof(take),
                take,
                $"Secret repository page size must be between 1 and {MaximumTake}.");
        }

        Search = NormalizeSearch(search);
        TypeName = OptionalValue(typeName);
        TypeNames = CopyFacet(typeNames, nameof(typeNames));
        StoreName = OptionalValue(storeName);
        StoreNames = CopyFacet(storeNames, nameof(storeNames));
        Scope = OptionalValue(scope);
        Status = status;
        ExcludedStatus = excludedStatus;
        ActiveOnly = activeOnly;
        if (activeOnly && now is null)
            throw new ArgumentException("An explicit evaluation time is required for active-only secret queries.", nameof(now));
        Now = now;
        Skip = skip;
        Take = take;
    }

    public string? Search { get; }
    public string? TypeName { get; }
    public IReadOnlyList<string> TypeNames { get; }
    public string? StoreName { get; }
    public IReadOnlyList<string> StoreNames { get; }
    public string? Scope { get; }
    public SecretStatus? Status { get; }
    public SecretStatus? ExcludedStatus { get; }
    public bool ActiveOnly { get; }
    public DateTimeOffset? Now { get; }
    public int Skip { get; }
    public int Take { get; }

    private static string? NormalizeSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        if (normalized.Length > MaximumSearchLength)
        {
            throw new ArgumentException(
                $"Secret search text cannot exceed {MaximumSearchLength} characters.",
                nameof(value));
        }

        return normalized;
    }

    private static string? OptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static IReadOnlyList<string> CopyFacet(
        IReadOnlyCollection<string>? values,
        string parameterName)
    {
        if (values is null || values.Count == 0)
            return [];
        if (values.Count > MaximumFacetValues)
        {
            throw new ArgumentException(
                $"A secret repository query cannot contain more than {MaximumFacetValues} values for one facet.",
                parameterName);
        }

        var normalized = values
            .Select(value => string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Secret repository facet values cannot be blank.", parameterName)
                : value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Array.AsReadOnly(normalized);
    }
}

/// <summary>
/// A bounded, deterministically ordered read-committed page. Items and count describe the provider's
/// live read observations; callers must not treat separate offset pages as one immutable snapshot.
/// </summary>
public sealed record SecretRepositoryPage
{
    public SecretRepositoryPage(IReadOnlyList<Secret> items, long totalCount)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        if (totalCount < items.Count)
            throw new ArgumentOutOfRangeException(nameof(totalCount), totalCount, "Total count cannot be smaller than the returned page.");

        Items = items;
        TotalCount = totalCount;
    }

    public IReadOnlyList<Secret> Items { get; }
    public long TotalCount { get; }
}
