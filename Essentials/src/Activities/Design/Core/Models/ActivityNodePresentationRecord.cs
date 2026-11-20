using System.Text.Json.Serialization;

namespace Elsa.Activities.Design.Core.Models;

/// <summary>Author-facing node metadata, stored independently of provider and executable material.</summary>
public sealed record ActivityNodePresentationRecord
{
    public const int MaxDisplayNameLength = 200;
    public const int MaxDescriptionLength = 2_000;

    public ActivityNodePresentationRecord(string nodeId, string? displayName = null, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        NodeId = nodeId;
        DisplayName = Normalize(displayName, MaxDisplayNameLength, nameof(displayName));
        Description = Normalize(description, MaxDescriptionLength, nameof(description));
    }

    public string NodeId { get; }
    public string? DisplayName { get; }
    public string? Description { get; }

    [JsonIgnore]
    public bool IsEmpty => DisplayName is null && Description is null;

    public static IReadOnlyList<ActivityNodePresentationRecord> NormalizeCollection(IEnumerable<ActivityNodePresentationRecord>? records)
    {
        var result = new List<ActivityNodePresentationRecord>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records ?? [])
        {
            ArgumentNullException.ThrowIfNull(record);
            if (!identities.Add(record.NodeId))
                throw new ArgumentException($"Duplicate activity presentation for node '{record.NodeId}'.", nameof(records));
            if (!record.IsEmpty)
                result.Add(record);
        }
        return result;
    }

    private static string? Normalize(string? value, int maximumLength, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return null;
        if (normalized.Length > maximumLength)
            throw new ArgumentOutOfRangeException(parameterName, $"The value cannot exceed {maximumLength} characters.");
        return normalized;
    }
}
