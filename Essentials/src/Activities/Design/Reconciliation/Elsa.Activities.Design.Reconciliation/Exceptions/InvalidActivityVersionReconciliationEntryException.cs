namespace Elsa.Activities.Design.Reconciliation.Exceptions;

/// <summary>
/// Raised when a reconciliation entry cannot be validated or mapped into the activity model —
/// missing stable consumer identity, missing/empty descriptor payload, or other required field.
/// Carries enough context (entry index, consumer key, activity-type-key) to localise the failure
/// in the source. Replaces raw <c>JsonException</c> / <c>InvalidOperationException</c> escaping a source.
/// </summary>
public sealed class InvalidActivityVersionReconciliationEntryException : Exception
{
    public int EntryIndex { get; }
    public string? ActivityTypeKey { get; }
    public string? ConsumerKey { get; }

    public InvalidActivityVersionReconciliationEntryException(int entryIndex, string? activityTypeKey, string? consumerKey, string message)
        : base(BuildMessage(entryIndex, activityTypeKey, consumerKey, message))
    {
        EntryIndex = entryIndex;
        ActivityTypeKey = activityTypeKey;
        ConsumerKey = consumerKey;
    }

    public InvalidActivityVersionReconciliationEntryException(int entryIndex, string? activityTypeKey, string? consumerKey, string message, Exception inner)
        : base(BuildMessage(entryIndex, activityTypeKey, consumerKey, message), inner)
    {
        EntryIndex = entryIndex;
        ActivityTypeKey = activityTypeKey;
        ConsumerKey = consumerKey;
    }

    private static string BuildMessage(int entryIndex, string? activityTypeKey, string? consumerKey, string message)
    {
        var context = string.Join(", ", new[]
        {
            $"entryIndex={entryIndex}",
            activityTypeKey is null ? null : $"activityTypeKey='{activityTypeKey}'",
            consumerKey is null ? null : $"consumerKey='{consumerKey}'",
        }.Where(x => x is not null));

        return $"Invalid activity version reconciliation entry ({context}): {message}";
    }
}
