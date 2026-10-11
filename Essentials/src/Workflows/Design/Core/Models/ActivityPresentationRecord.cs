using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Models;

/// <summary>
/// Optional author-facing presentation for one activity occurrence.
/// </summary>
public sealed record ActivityPresentationRecord : IActivityPresentationRecord
{
    public const int MaxDisplayNameLength = 200;
    public const int MaxDescriptionLength = 2_000;

    public ActivityPresentationRecord(string nodeId, string? displayName = null, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        NodeId = nodeId;
        DisplayName = Normalize(displayName, MaxDisplayNameLength, nameof(displayName));
        Description = Normalize(description, MaxDescriptionLength, nameof(description));
    }

    public string NodeId { get; }
    public string? DisplayName { get; }
    public string? Description { get; }

    public bool IsEmpty => DisplayName is null && Description is null;

    public static IReadOnlyCollection<ActivityPresentationRecord> NormalizeCollection(
        IEnumerable<ActivityPresentationRecord>? records)
    {
        var normalized = (records ?? []).Where(x => !x.IsEmpty).ToArray();
        var duplicate = normalized
            .GroupBy(x => x.NodeId, StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException(
                $"Activity presentation contains duplicate node id '{duplicate.Key}'.",
                nameof(records));
        return normalized;
    }

    private static string? Normalize(string? value, int maximumLength, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return null;
        if (normalized.Length > maximumLength)
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Activity {parameterName} cannot exceed {maximumLength} characters.");
        return normalized;
    }
}
