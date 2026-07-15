using System.Text.Json;

namespace Elsa.Activities.Runtime.Core.Models;

/// <summary>
/// Runtime-owned, stable description of how an executable node is activated. The consumer key and
/// schema version are durable wire identities; the payload remains opaque outside the matching
/// Runtime consumer.
/// </summary>
public sealed record RuntimeActivityDescriptor
{
    public const string InitialSchemaVersion = "1";

    public RuntimeActivityDescriptor(string consumerKey, string schemaVersion, JsonElement payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);

        ConsumerKey = consumerKey;
        SchemaVersion = schemaVersion;
        Payload = payload.Clone();
    }

    public string ConsumerKey { get; }
    public string SchemaVersion { get; }
    public JsonElement Payload { get; }
}

/// <summary>An exact Runtime consumer/schema pair required to activate retained executable material.</summary>
public sealed record RuntimeRequirement
{
    public RuntimeRequirement(string consumerKey, string schemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);

        ConsumerKey = consumerKey;
        SchemaVersion = schemaVersion;
    }

    public string ConsumerKey { get; }
    public string SchemaVersion { get; }
}

/// <summary>Stable first-party Runtime consumer keys. These values are independent of CLR names.</summary>
public static class WellKnownRuntimeActivityConsumers
{
    public const string ClrActivity = "elsa.clr-activity";
    public const string WorkflowDefinitionActivity = "elsa.workflow-definition-activity";
    public const string GraphActivity = "elsa.graph-activity";
}

/// <summary>Classifies an artifact activation failure without treating deployment recovery as activity retry.</summary>
public enum ActivityActivationFailureKind
{
    MissingConsumer,
    UnsupportedSchema,
    InvalidDescriptor
}

/// <summary>Safe Runtime evidence describing why one executable node could not be activated.</summary>
public sealed record ActivityActivationFailure(
    ActivityActivationFailureKind Kind,
    string ConsumerKey,
    string SchemaVersion,
    string? ArtifactId = null,
    string? ExecutableNodeId = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        Metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(Metadata, StringComparer.Ordinal);
}
