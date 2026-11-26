using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

/// <summary>An independently managed entry point. Its enabled state never grants target publication authority.</summary>
public sealed class StandaloneTrigger
{
    [JsonConstructor]
    public StandaloneTrigger(string id, string name, string typeKey, int schemaVersion, JsonElement configuration,
        long revision, long configurationRevision, long associationRevision, TriggerDesiredState desiredState,
        TriggerManagementOrigin origin, DateTimeOffset createdAt, DateTimeOffset updatedAt, string createdBy,
        string? description = null, bool hasActivated = false, string? tenantId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBy);
        if (id.Length > 128 || name.Length > 256 || typeKey.Length > 256 || description?.Length > 4000)
            throw new ArgumentException("Trigger identity, name, type or description exceeds its supported length.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(configurationRevision);
        ArgumentOutOfRangeException.ThrowIfNegative(associationRevision);
        if (configurationRevision > revision || associationRevision > revision)
            throw new ArgumentException("Resource subrevisions cannot exceed its aggregate revision.");
        if (!Enum.IsDefined(desiredState)) throw new ArgumentOutOfRangeException(nameof(desiredState));
        if (configuration.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Trigger configuration must be a JSON object.", nameof(configuration));
        if (updatedAt < createdAt) throw new ArgumentException("A trigger update cannot precede its creation.", nameof(updatedAt));
        ArgumentNullException.ThrowIfNull(origin);
        Id = id; Name = name; TypeKey = typeKey; SchemaVersion = schemaVersion;
        Configuration = configuration.Clone(); Revision = revision; ConfigurationRevision = configurationRevision;
        AssociationRevision = associationRevision; DesiredState = desiredState; Origin = origin;
        CreatedAt = createdAt; UpdatedAt = updatedAt; CreatedBy = createdBy;
        Description = description; HasActivated = hasActivated; TenantId = tenantId;
    }

    public string Id { get; }
    public string Name { get; }
    public string TypeKey { get; }
    public int SchemaVersion { get; }
    public JsonElement Configuration { get; }
    public long Revision { get; }
    public long ConfigurationRevision { get; }
    public long AssociationRevision { get; }
    public TriggerDesiredState DesiredState { get; }
    public TriggerManagementOrigin Origin { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }
    public string CreatedBy { get; }
    public string? Description { get; }
    public bool HasActivated { get; }
    public string? TenantId { get; }
}

public enum TriggerDesiredState { Paused, Enabled, Deleted }
public enum TriggerManagementKind { Studio, Provider }

/// <summary>Configuration ownership is separate from permitted local operational controls.</summary>
public sealed class TriggerManagementOrigin
{
    [JsonConstructor]
    public TriggerManagementOrigin(TriggerManagementKind kind, string? sourceId = null, bool configurationReadOnly = false)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == TriggerManagementKind.Provider) ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        if (kind == TriggerManagementKind.Studio && (sourceId is not null || configurationReadOnly))
            throw new ArgumentException("Studio-owned configuration cannot claim a provider or provider-only ownership.", nameof(sourceId));
        Kind = kind; SourceId = sourceId; ConfigurationReadOnly = configurationReadOnly;
    }

    public TriggerManagementKind Kind { get; }
    public string? SourceId { get; }
    public bool ConfigurationReadOnly { get; }
}
