namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class BookmarkStimulusLookupRequest
{
    public BookmarkStimulusLookupRequest(
        string workflowExecutionId,
        string stimulusType,
        string stimulusHash,
        DateTimeOffset evaluatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);

        WorkflowExecutionId = workflowExecutionId;
        StimulusType = stimulusType;
        StimulusHash = stimulusHash;
        EvaluatedAt = evaluatedAt;
    }

    public string WorkflowExecutionId { get; }
    public string StimulusType { get; }
    public string StimulusHash { get; }
    public DateTimeOffset EvaluatedAt { get; }
}

public sealed class BookmarkStimulusLookupResult
{
    public BookmarkStimulusLookupResult(
        BookmarkStimulusLookupStatus status,
        BookmarkState? bookmark = null,
        string? reason = null,
        IReadOnlyCollection<string>? ambiguousBookmarkIds = null)
    {
        if (status == BookmarkStimulusLookupStatus.Found)
        {
            ArgumentNullException.ThrowIfNull(bookmark);

            if (reason is not null)
                throw new ArgumentException("Successful bookmark stimulus lookup results cannot carry a reason.", nameof(reason));
        }
        else
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);

            if (bookmark is not null)
                throw new ArgumentException("Failed bookmark stimulus lookup results cannot carry a bookmark.", nameof(bookmark));
        }

        var ambiguousBookmarkIdSnapshot = (ambiguousBookmarkIds ?? []).ToArray();
        if (ambiguousBookmarkIdSnapshot.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Ambiguous bookmark IDs cannot contain blank values.", nameof(ambiguousBookmarkIds));

        if (status == BookmarkStimulusLookupStatus.Ambiguous && ambiguousBookmarkIdSnapshot.Length < 2)
            throw new ArgumentException("Ambiguous bookmark stimulus lookup results require at least two bookmark IDs.", nameof(ambiguousBookmarkIds));

        if (status != BookmarkStimulusLookupStatus.Ambiguous && ambiguousBookmarkIdSnapshot.Length != 0)
            throw new ArgumentException("Only ambiguous bookmark stimulus lookup results can carry ambiguous bookmark IDs.", nameof(ambiguousBookmarkIds));

        Status = status;
        Bookmark = bookmark;
        Reason = reason;
        AmbiguousBookmarkIds = ambiguousBookmarkIdSnapshot;
    }

    public BookmarkStimulusLookupStatus Status { get; }
    public BookmarkState? Bookmark { get; }
    public string? Reason { get; }
    public IReadOnlyCollection<string> AmbiguousBookmarkIds { get; }
}

public enum BookmarkStimulusLookupStatus
{
    Found,
    NotFound,
    Ambiguous
}
