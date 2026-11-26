namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// One finite cross-execution bookmark lookup for an exact stimulus identity.
/// </summary>
public sealed record BookmarkStimulusPageQuery : RuntimeStorePageRequest
{
    public BookmarkStimulusPageQuery(
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
/// One finite cross-execution bookmark lookup for a stimulus type.
/// </summary>
public sealed record BookmarkStimulusTypePageQuery : RuntimeStorePageRequest
{
    public BookmarkStimulusTypePageQuery(
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
