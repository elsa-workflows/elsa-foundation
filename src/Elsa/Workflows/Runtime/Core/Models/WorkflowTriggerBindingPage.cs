namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// One finite exact-stimulus lookup over active workflow trigger bindings.
/// </summary>
public sealed record WorkflowTriggerBindingPageQuery
{
    public const int DefaultLimit = 100;
    public const int MaximumLimit = 500;
    public const int MaximumContinuationTokenLength = 16 * 1024;

    public WorkflowTriggerBindingPageQuery(
        string stimulusType,
        string stimulusHash,
        int limit = DefaultLimit,
        string? continuationToken = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);
        if (limit is <= 0 or > MaximumLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                limit,
                $"Trigger-binding page limit must be between 1 and {MaximumLimit}.");
        }

        StimulusType = stimulusType;
        StimulusHash = stimulusHash;
        Limit = limit;
        ContinuationToken = ValidateContinuationToken(continuationToken, nameof(continuationToken));
    }

    public string StimulusType { get; }
    public string StimulusHash { get; }
    public int Limit { get; }

    /// <summary>
    /// Provider-owned continuation. Callers and provider-neutral runtime code must treat this value as opaque.
    /// </summary>
    public string? ContinuationToken { get; }

    internal static string? ValidateContinuationToken(string? continuationToken, string parameterName)
    {
        if (continuationToken is null)
            return null;
        if (string.IsNullOrWhiteSpace(continuationToken))
            throw new ArgumentException("A trigger-binding continuation token cannot be blank.", parameterName);
        if (continuationToken.Length > MaximumContinuationTokenLength)
        {
            throw new ArgumentException(
                $"A trigger-binding continuation token cannot exceed {MaximumContinuationTokenLength} characters.",
                parameterName);
        }

        return continuationToken;
    }
}

/// <summary>
/// One bounded, deterministically ordered page of active trigger bindings. A continuation resumes
/// the provider's live view after the last returned item; it does not promise a cross-request snapshot.
/// </summary>
public sealed record WorkflowTriggerBindingPage
{
    public WorkflowTriggerBindingPage(
        WorkflowTriggerBindingPageQuery query,
        IReadOnlyList<WorkflowTriggerBinding> items,
        long totalCount,
        string? nextContinuationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count > query.Limit)
            throw new ArgumentException("A trigger-binding page cannot exceed its requested limit.", nameof(items));
        if (totalCount < items.Count)
            throw new ArgumentOutOfRangeException(nameof(totalCount), totalCount, "The total count cannot be smaller than the returned page.");

        nextContinuationToken = WorkflowTriggerBindingPageQuery.ValidateContinuationToken(
            nextContinuationToken,
            nameof(nextContinuationToken));
        if (items.Count == 0 && nextContinuationToken is not null)
            throw new ArgumentException("An empty trigger-binding page cannot expose a continuation.", nameof(nextContinuationToken));
        if (nextContinuationToken is not null &&
            StringComparer.Ordinal.Equals(query.ContinuationToken, nextContinuationToken))
            throw new ArgumentException("A trigger-binding continuation must advance the traversal.", nameof(nextContinuationToken));

        Items = items;
        TotalCount = totalCount;
        NextContinuationToken = nextContinuationToken;
    }

    public IReadOnlyList<WorkflowTriggerBinding> Items { get; }

    /// <summary>Predicate count observed while this page was evaluated.</summary>
    public long TotalCount { get; }

    /// <summary>Provider-owned token for the next live-view page, or <see langword="null"/> at the end.</summary>
    public string? NextContinuationToken { get; }
}
