using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>A validated, strategy-safe intent request. Runtime derives all durable identity and correlation fields.</summary>
public sealed class IncidentStrategySafePostCommitIntent
{
    public IncidentStrategySafePostCommitIntent(
        string kind,
        JsonElement? payload = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (payload is { } value && value.GetRawText().Length > 65_536)
            throw new ArgumentOutOfRangeException(nameof(payload), "Incident-strategy intent payload exceeds the 64 KiB safe limit.");

        Kind = kind;
        Payload = payload?.Clone();
        Metadata = IncidentStrategyMetadata.Snapshot(metadata);
    }

    public string Kind { get; }
    public JsonElement? Payload { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

/// <summary>Declares one namespaced post-commit intent kind safe for incident strategies to request.</summary>
public sealed class IncidentStrategySafeIntentDescriptor
{
    public IncidentStrategySafeIntentDescriptor(string kind, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (description is { Length: 0 })
            throw new ArgumentException("Description cannot be empty when supplied.", nameof(description));

        Kind = kind;
        Description = description;
    }

    public string Kind { get; }
    public string? Description { get; }
}

/// <summary>Immutable registration contribution for a strategy-safe intent kind.</summary>
public sealed class IncidentStrategySafeIntentRegistration(IncidentStrategySafeIntentDescriptor descriptor)
{
    public IncidentStrategySafeIntentDescriptor Descriptor { get; } = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
}
