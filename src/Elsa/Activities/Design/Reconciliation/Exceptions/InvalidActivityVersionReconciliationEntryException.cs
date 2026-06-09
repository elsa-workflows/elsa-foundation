namespace Elsa.Activities.Design.Reconciliation.Exceptions;

/// <summary>
/// Raised when a reconciliation entry cannot be validated or mapped into the activity model —
/// missing <c>descriptorType</c>, missing/empty descriptor payload, or other missing required field.
/// Carries enough context (entry index, descriptor type, activity-type-key) to localise the failure
/// in the source. Replaces raw <c>JsonException</c> / <c>InvalidOperationException</c> escaping a source.
/// </summary>
public sealed class InvalidActivityVersionReconciliationEntryException : Exception
{
    public int EntryIndex { get; }
    public string? ActivityTypeKey { get; }
    public string? DescriptorType { get; }

    public InvalidActivityVersionReconciliationEntryException(int entryIndex, string? activityTypeKey, string? descriptorType, string message)
        : base(BuildMessage(entryIndex, activityTypeKey, descriptorType, message))
    {
        EntryIndex = entryIndex;
        ActivityTypeKey = activityTypeKey;
        DescriptorType = descriptorType;
    }

    public InvalidActivityVersionReconciliationEntryException(int entryIndex, string? activityTypeKey, string? descriptorType, string message, Exception inner)
        : base(BuildMessage(entryIndex, activityTypeKey, descriptorType, message), inner)
    {
        EntryIndex = entryIndex;
        ActivityTypeKey = activityTypeKey;
        DescriptorType = descriptorType;
    }

    private static string BuildMessage(int entryIndex, string? activityTypeKey, string? descriptorType, string message)
    {
        var context = string.Join(", ", new[]
        {
            $"entryIndex={entryIndex}",
            activityTypeKey is null ? null : $"activityTypeKey='{activityTypeKey}'",
            descriptorType is null ? null : $"descriptorType='{descriptorType}'",
        }.Where(x => x is not null));

        return $"Invalid activity version reconciliation entry ({context}): {message}";
    }
}
