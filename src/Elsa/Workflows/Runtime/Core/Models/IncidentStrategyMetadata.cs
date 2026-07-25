using System.Collections.ObjectModel;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Applies the bounded string-only metadata contract shared by incident strategy inputs, outcomes,
/// and strategy-safe intents.
/// </summary>
public static class IncidentStrategyMetadata
{
    public const int MaxEntries = 32;
    public const int MaxKeyLength = 128;
    public const int MaxValueLength = 1024;

    private static readonly IReadOnlySet<string> PolicySafeIncidentKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        RuntimeMetadataKeys.CausalActivityExecutionId,
        RuntimeMetadataKeys.CausalExecutableNodeId,
        RuntimeMetadataKeys.CausalIncidentId,
        RuntimeMetadataKeys.CausationKind,
        RuntimeMetadataKeys.CompletedChildActivityExecutionId,
        RuntimeMetadataKeys.FaultSubStatus,
        RuntimeMetadataKeys.ParentActivityExecutionId
    };

    /// <summary>Validates and snapshots strategy-owned metadata.</summary>
    public static IReadOnlyDictionary<string, string> Snapshot(
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (metadata is null || metadata.Count == 0)
            return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));
        if (metadata.Count > MaxEntries)
            throw new ArgumentOutOfRangeException(nameof(metadata), $"Incident-strategy metadata may contain at most {MaxEntries} entries.");

        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            ValidateEntry(key, value);
            snapshot.Add(key, value);
        }

        return new ReadOnlyDictionary<string, string>(snapshot);
    }

    /// <summary>
    /// Projects only explicitly policy-safe incident metadata, excluding exception details,
    /// messages, payloads, variables, and other private runtime state.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ProjectIncident(
        IReadOnlyDictionary<string, string>? metadata) =>
        Snapshot(metadata?
            .Where(pair => PolicySafeIncidentKeys.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

    public static void ValidateEntry(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (key.Length > MaxKeyLength)
            throw new ArgumentOutOfRangeException(nameof(key), $"Incident-strategy metadata keys may contain at most {MaxKeyLength} characters.");
        if (value.Length > MaxValueLength)
            throw new ArgumentOutOfRangeException(nameof(value), $"Incident-strategy metadata values may contain at most {MaxValueLength} characters.");
        if (key.Any(char.IsControl) || value.Any(char.IsControl))
            throw new ArgumentException("Incident-strategy metadata cannot contain control characters.");
    }
}
