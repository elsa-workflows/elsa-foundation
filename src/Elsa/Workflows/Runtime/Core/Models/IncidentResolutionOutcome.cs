using Elsa.Workflows.Primitives.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Immutable durable evidence of the action applied to an incident.
/// </summary>
public sealed record IncidentResolutionOutcome
{
    public IncidentResolutionOutcome(
        string actionKind,
        DateTimeOffset appliedAt,
        IncidentStrategyReference? strategy,
        string? systemSource,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionKind);

        if (strategy is null && string.IsNullOrWhiteSpace(systemSource))
            throw new ArgumentException("An incident resolution outcome requires strategy or system provenance.", nameof(systemSource));

        ActionKind = actionKind;
        AppliedAt = appliedAt;
        Strategy = strategy;
        SystemSource = systemSource;
        Metadata = IncidentStrategyMetadata.Snapshot(metadata);
    }

    public string ActionKind { get; }
    public DateTimeOffset AppliedAt { get; }
    public IncidentStrategyReference? Strategy { get; }
    public string? SystemSource { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}
