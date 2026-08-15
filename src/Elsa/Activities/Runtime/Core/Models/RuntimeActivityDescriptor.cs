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

/// <summary>
/// An exact stable durable-value storage-driver capability required by retained executable material.
/// This is deliberately separate from <see cref="RuntimeRequirement"/>: storage drivers encode values,
/// while activity consumers construct executable nodes, and the two registries have different contracts.
/// </summary>
public sealed record RuntimeStorageDriverRequirement
{
    public RuntimeStorageDriverRequirement(string driverKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverKey);
        DriverKey = driverKey;
    }

    public string DriverKey { get; }
}

/// <summary>Stable first-party Runtime consumer keys. These values are independent of CLR names.</summary>
public static class WellKnownRuntimeActivityConsumers
{
    public const string ClrActivity = "elsa.clr-activity";
    public const string GraphActivity = "elsa.graph-activity";
}

/// <summary>Classifies an artifact activation failure without treating deployment recovery as activity retry.</summary>
public enum ActivityActivationFailureKind
{
    MissingConsumer,
    UnsupportedSchema,
    InvalidDescriptor,
    MissingStorageDriver,

    /// <summary>
    /// The node's CLR activity type is not registered in this host's well-known type registry.
    /// Reaching this at activation means an artifact got past the import gate (FR-B-005a), so it
    /// classifies as a non-retryable deployment incident like every sibling kind — defense in depth,
    /// not the primary detection path.
    /// </summary>
    MissingActivityType
}

public enum RuntimeActivationCapabilityKind
{
    ActivityConsumer,
    DurableValueStorageDriver
}

/// <summary>Safe Runtime evidence describing why one executable node could not be activated.</summary>
public sealed record ActivityActivationFailure
{
    public ActivityActivationFailure(
        ActivityActivationFailureKind kind,
        string consumerKey,
        string schemaVersion,
        string? artifactId = null,
        string? executableNodeId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
        : this(
            kind,
            RuntimeActivationCapabilityKind.ActivityConsumer,
            consumerKey,
            schemaVersion,
            artifactId,
            executableNodeId,
            metadata)
    {
    }

    public ActivityActivationFailure(
        ActivityActivationFailureKind kind,
        RuntimeActivationCapabilityKind capabilityKind,
        string capabilityKey,
        string? schemaVersion,
        string? artifactId = null,
        string? executableNodeId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityKey);
        if (capabilityKind == RuntimeActivationCapabilityKind.ActivityConsumer)
            ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);
        if (capabilityKind == RuntimeActivationCapabilityKind.DurableValueStorageDriver && schemaVersion is not null)
            throw new ArgumentException("Durable-value storage-driver capabilities do not use activity descriptor schemas.", nameof(schemaVersion));

        Kind = kind;
        CapabilityKind = capabilityKind;
        CapabilityKey = capabilityKey;
        SchemaVersion = schemaVersion;
        ArtifactId = artifactId;
        ExecutableNodeId = executableNodeId;
        Metadata = metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(metadata, StringComparer.Ordinal);
    }

    public ActivityActivationFailureKind Kind { get; }
    public RuntimeActivationCapabilityKind CapabilityKind { get; }
    public string CapabilityKey { get; }
    public string? ConsumerKey => CapabilityKind == RuntimeActivationCapabilityKind.ActivityConsumer ? CapabilityKey : null;
    public string? StorageDriverKey => CapabilityKind == RuntimeActivationCapabilityKind.DurableValueStorageDriver ? CapabilityKey : null;
    public string? SchemaVersion { get; }
    public string? ArtifactId { get; }
    public string? ExecutableNodeId { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
