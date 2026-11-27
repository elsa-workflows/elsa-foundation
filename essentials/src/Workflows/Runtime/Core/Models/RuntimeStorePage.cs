namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Provider-neutral request for one finite, forward-only live-view page from a runtime store.
/// </summary>
public record RuntimeStorePageRequest
{
    public const int DefaultLimit = 100;
    public const int MaximumLimit = 500;
    public const int MaximumContinuationTokenLength = 16 * 1024;

    public RuntimeStorePageRequest(
        int limit = DefaultLimit,
        string? continuationToken = null)
    {
        Limit = ValidateLimit(limit, nameof(limit));
        ContinuationToken = ValidateContinuationToken(continuationToken, nameof(continuationToken));
    }

    public int Limit { get; }

    /// <summary>
    /// Provider-owned continuation. Callers and provider-neutral runtime code must treat this value as opaque.
    /// </summary>
    public string? ContinuationToken { get; }

    public static int ValidateLimit(int limit, string parameterName)
    {
        if (limit is <= 0 or > MaximumLimit)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                limit,
                $"Runtime store page limit must be between 1 and {MaximumLimit}.");
        }

        return limit;
    }

    public static string? ValidateContinuationToken(string? continuationToken, string parameterName)
    {
        if (continuationToken is null)
            return null;
        if (string.IsNullOrWhiteSpace(continuationToken))
            throw new ArgumentException("A runtime store continuation token cannot be blank.", parameterName);
        if (continuationToken.Length > MaximumContinuationTokenLength)
        {
            throw new ArgumentException(
                $"A runtime store continuation token cannot exceed {MaximumContinuationTokenLength} characters.",
                parameterName);
        }

        return continuationToken;
    }
}

/// <summary>
/// One bounded result page. Its continuation resumes a provider's live view and does not promise a
/// cross-request snapshot.
/// </summary>
public sealed record RuntimeStorePage<T>
{
    public RuntimeStorePage(
        RuntimeStorePageRequest request,
        IReadOnlyList<T> items,
        string? nextContinuationToken = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count > request.Limit)
            throw new ArgumentException("A runtime store page cannot exceed its requested limit.", nameof(items));

        nextContinuationToken = RuntimeStorePageRequest.ValidateContinuationToken(
            nextContinuationToken,
            nameof(nextContinuationToken));
        if (items.Count == 0 && nextContinuationToken is not null)
            throw new ArgumentException("An empty runtime store page cannot expose a continuation.", nameof(nextContinuationToken));
        if (nextContinuationToken is not null &&
            StringComparer.Ordinal.Equals(request.ContinuationToken, nextContinuationToken))
            throw new ArgumentException("A runtime store continuation must advance the traversal.", nameof(nextContinuationToken));

        Items = items;
        NextContinuationToken = nextContinuationToken;
    }

    public IReadOnlyList<T> Items { get; }
    public string? NextContinuationToken { get; }
}
