using System.Text.Json;

namespace Elsa.Activities.Runtime.Core.Models;

public sealed class ActivityBookmarkRequest
{
    public ActivityBookmarkRequest(
        string bookmarkId,
        string resumeTargetId,
        string stimulusType,
        string stimulusHash,
        JsonElement? payload = null,
        DateTimeOffset? expiresAt = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookmarkId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resumeTargetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);

        BookmarkId = bookmarkId;
        ResumeTargetId = resumeTargetId;
        StimulusType = stimulusType;
        StimulusHash = stimulusHash;
        Payload = payload?.Clone();
        ExpiresAt = expiresAt;
        Metadata = Snapshot(metadata);
    }

    public string BookmarkId { get; }
    public string ResumeTargetId { get; }
    public string StimulusType { get; }
    public string StimulusHash { get; }
    public JsonElement? Payload { get; }
    public DateTimeOffset? ExpiresAt { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    private static IReadOnlyDictionary<string, string> Snapshot(IReadOnlyDictionary<string, string>? metadata)
    {
        var snapshot = metadata?.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)
                       ?? new Dictionary<string, string>(StringComparer.Ordinal);

        if (snapshot.Keys.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Bookmark request metadata keys cannot be blank.", nameof(metadata));

        if (snapshot.Values.Any(value => value is null))
            throw new ArgumentException("Bookmark request metadata values cannot be null.", nameof(metadata));

        return snapshot;
    }
}
