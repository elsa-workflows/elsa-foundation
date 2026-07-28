namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Shared validation for one finite workflow trigger-binding lookup.
/// </summary>
public abstract record WorkflowTriggerBindingPageRequest
{
    public const int DefaultLimit = RuntimeStorePageRequest.DefaultLimit;
    public const int MaximumLimit = RuntimeStorePageRequest.MaximumLimit;
    public const int MaximumContinuationTokenLength = RuntimeStorePageRequest.MaximumContinuationTokenLength;

    protected WorkflowTriggerBindingPageRequest(
        int limit = DefaultLimit,
        string? continuationToken = null)
    {
        Limit = RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));
        ContinuationToken = RuntimeStorePageRequest.ValidateContinuationToken(
            continuationToken,
            nameof(continuationToken));
    }

    public int Limit { get; }

    /// <summary>
    /// Provider-owned continuation. Callers and provider-neutral runtime code must treat this value as opaque.
    /// </summary>
    public string? ContinuationToken { get; }

    internal static string? ValidateContinuationToken(string? continuationToken, string parameterName)
        => RuntimeStorePageRequest.ValidateContinuationToken(continuationToken, parameterName);
}

/// <summary>
/// One finite exact-stimulus lookup over active workflow trigger bindings.
/// </summary>
public sealed record WorkflowTriggerBindingPageQuery : WorkflowTriggerBindingPageRequest
{
    public WorkflowTriggerBindingPageQuery(
        string stimulusType,
        string stimulusHash,
        int limit = DefaultLimit,
        string? continuationToken = null)
        : base(limit, continuationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);
        StimulusType = stimulusType;
        StimulusHash = stimulusHash;
    }

    public string StimulusType { get; }
    public string StimulusHash { get; }
}

/// <summary>
/// One finite type-scoped lookup over active workflow trigger bindings.
/// </summary>
public sealed record WorkflowTriggerBindingTypePageQuery : WorkflowTriggerBindingPageRequest
{
    public WorkflowTriggerBindingTypePageQuery(
        string stimulusType,
        int limit = DefaultLimit,
        string? continuationToken = null)
        : base(limit, continuationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        StimulusType = stimulusType;
    }

    public string StimulusType { get; }
}

/// <summary>
/// One finite publication-scoped lookup over prepared or active workflow trigger bindings.
/// </summary>
public sealed record WorkflowTriggerBindingPublicationPageQuery : WorkflowTriggerBindingPageRequest
{
    public WorkflowTriggerBindingPublicationPageQuery(
        string publicationId,
        int limit = DefaultLimit,
        string? continuationToken = null)
        : base(limit, continuationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        PublicationId = publicationId;
    }

    public string PublicationId { get; }
}

/// <summary>
/// One finite artifact-scoped lookup over workflow trigger bindings.
/// </summary>
public sealed record WorkflowTriggerBindingArtifactPageQuery : WorkflowTriggerBindingPageRequest
{
    public WorkflowTriggerBindingArtifactPageQuery(
        string artifactId,
        int limit = DefaultLimit,
        string? continuationToken = null)
        : base(limit, continuationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArtifactId = artifactId;
    }

    public string ArtifactId { get; }
}

/// <summary>
/// One bounded, deterministically ordered page of workflow trigger bindings. A continuation resumes
/// the provider's live view after the last returned item; it does not promise a cross-request snapshot.
/// </summary>
public sealed record WorkflowTriggerBindingPage
{
    public WorkflowTriggerBindingPage(
        WorkflowTriggerBindingPageRequest query,
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

        nextContinuationToken = WorkflowTriggerBindingPageRequest.ValidateContinuationToken(
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
