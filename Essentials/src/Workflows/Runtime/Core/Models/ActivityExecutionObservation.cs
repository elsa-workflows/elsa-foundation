namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>An immutable inspection fact. It never affects activity continuation, incidents or retry policy.</summary>
public sealed record ActivityExecutionObservation(
    string ObservationId,
    string Kind,
    string Code,
    IncidentSeverity Severity,
    string Message,
    DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, string> Metadata)
{
    /// <summary>Combines retained facts without losing history or silently accepting a conflicting identity.</summary>
    public static IReadOnlyList<ActivityExecutionObservation> Merge(
        IEnumerable<ActivityExecutionObservation> retained,
        IEnumerable<ActivityExecutionObservation> incoming)
    {
        var facts = new Dictionary<string, ActivityExecutionObservation>(StringComparer.Ordinal);
        foreach (var fact in retained.Concat(incoming))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fact.ObservationId);
            ArgumentException.ThrowIfNullOrWhiteSpace(fact.Kind);
            ArgumentException.ThrowIfNullOrWhiteSpace(fact.Code);
            ArgumentException.ThrowIfNullOrWhiteSpace(fact.Message);
            if (fact.ObservationId.Length > 512 || fact.Kind.Length > 128 || fact.Code.Length > 128 || fact.Message.Length > 2048)
                throw new ArgumentException("Observation identity, kind, code or message exceeds its bounded length.");
            var metadata = RuntimeModelMetadata.Snapshot(fact.Metadata);
            if (metadata.Count > 16 || metadata.Any(pair => pair.Key.Length > 128 || pair.Value.Length > 4096))
                throw new ArgumentException("Observation metadata exceeds its bounded size.");
            if (facts.TryGetValue(fact.ObservationId, out var previous))
            {
                if (previous.Kind != fact.Kind || previous.Code != fact.Code || previous.Severity != fact.Severity ||
                    previous.Message != fact.Message || previous.OccurredAt != fact.OccurredAt ||
                    !previous.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .SequenceEqual(metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal)))
                    throw new InvalidOperationException("A retained observation identity cannot be rewritten.");
            }
            else facts.Add(fact.ObservationId, fact with { Metadata = metadata });
        }
        return facts.Values.OrderBy(fact => fact.OccurredAt).ThenBy(fact => fact.ObservationId, StringComparer.Ordinal).ToArray();
    }
}
