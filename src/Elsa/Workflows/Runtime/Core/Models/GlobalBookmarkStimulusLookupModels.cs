using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Request to find every waiting bookmark across all workflow executions that matches an external
/// stimulus identity (W7, E3-5). An optional correlation id narrows the set to instances that were
/// created or suspended under the same correlation.
/// </summary>
public sealed class GlobalBookmarkStimulusLookupRequest
{
    public GlobalBookmarkStimulusLookupRequest(
        string stimulusType,
        string stimulusHash,
        DateTimeOffset evaluatedAt,
        string? correlationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);

        if (correlationId is not null && string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("Correlation id cannot be blank when provided.", nameof(correlationId));

        StimulusType = stimulusType;
        StimulusHash = stimulusHash;
        EvaluatedAt = evaluatedAt;
        CorrelationId = correlationId;
    }

    public string StimulusType { get; }
    public string StimulusHash { get; }
    public DateTimeOffset EvaluatedAt { get; }
    public string? CorrelationId { get; }
}

/// <summary>
/// Request to find every non-expired waiting bookmark of a stimulus type across all workflow executions
/// (spec 089 D), regardless of hash. Used by the mid-flow HttpEndpoint route-table resolver and middleware to
/// discover waiting endpoint bookmarks and read their durable route template + endpoint options metadata.
/// </summary>
public sealed class GlobalBookmarkStimulusTypeLookupRequest
{
    public GlobalBookmarkStimulusTypeLookupRequest(string stimulusType, DateTimeOffset evaluatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);

        StimulusType = stimulusType;
        EvaluatedAt = evaluatedAt;
    }

    public string StimulusType { get; }
    public DateTimeOffset EvaluatedAt { get; }
}

/// <summary>
/// The set of waiting bookmarks (deterministically ordered) that match a stimulus across executions.
/// </summary>
public sealed class GlobalBookmarkStimulusLookupResult
{
    public GlobalBookmarkStimulusLookupResult(IReadOnlyList<BookmarkState> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        Matches = matches;
    }

    public IReadOnlyList<BookmarkState> Matches { get; }

    public bool HasMatches => Matches.Count > 0;

    /// <summary>The distinct workflow execution ids the matched bookmarks belong to, in match order.</summary>
    public IReadOnlyList<string> WorkflowExecutionIds =>
        Matches.Select(match => match.WorkflowExecutionId).Distinct(StringComparer.Ordinal).ToArray();

    public static bool CorrelationMatches(BookmarkState bookmark, string? correlationId)
    {
        if (correlationId is null)
            return true;

        return bookmark.Metadata.TryGetValue(RuntimeMetadataKeys.CorrelationId, out var value) &&
               StringComparer.Ordinal.Equals(value, correlationId);
    }
}
