using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Core.Contracts;

public interface ISecretRepository : IPagedSecretRepository
{
    ValueTask<Secret?> FindAsync(string tenantId, string normalizedName, CancellationToken cancellationToken = default);
    ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default);
    ValueTask SaveAsync(Secret secret, CancellationToken cancellationToken = default);
}

public interface IRevisionAwareSecretRepository
{
    ValueTask<SecretRevisionedRecord?> FindWithRevisionAsync(string tenantId, string normalizedName, CancellationToken cancellationToken = default);
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
