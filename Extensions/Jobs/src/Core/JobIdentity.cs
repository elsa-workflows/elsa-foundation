using System.Text.Json;

namespace Elsa.Jobs.Core;

/// <summary>Trusted exact persistence partition and optional tenant; null tenant is a distinct scope.</summary>
public sealed record JobScope(
    string PersistencePartition,
    string? TenantId);

/// <summary>Stable logical job type and semantic handler version; no CLR type identity.</summary>
public sealed record JobHandlerIdentity(
    string JobType,
    string Version);

/// <summary>Declared payload alias and positive schema version.</summary>
public sealed record JobSchema(
    string Alias,
    int Version);

/// <summary>Trusted durable sender identity and immutable source digest; recognition survives ordinary producer-key expiry.</summary>
public sealed record JobOrigin(
    string Kind,
    string OwnerId,
    string OriginId,
    string PayloadSha256);

/// <summary>Adapter-validated model address; the model owns the versioned continuation payload.</summary>
public sealed record JobAssociation(
    string ModelKind,
    string DefinitionId,
    string DefinitionVersion,
    string ExecutionId,
    string InvocationId,
    string MaterialId,
    string MaterialSha256,
    JobAssociationMode Mode,
    JobPayload? Continuation);

/// <summary>Parent ownership and result consumption policy.</summary>
public enum JobAssociationMode
{
    Awaited = 0,
    Independent = 1,
    Detached = 2
}

/// <summary>Versioned JSON value snapshotted away from a caller-owned JsonDocument lifetime.</summary>
public sealed class JobPayload
{
    public JobPayload(JobSchema schema, JsonElement value)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (value.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException("A Jobs payload requires a defined JSON value; JSON null remains supported.", nameof(value));
        Schema = schema;
        Value = value.Clone();
    }

    public JobSchema Schema { get; }
    public JsonElement Value { get; }
}
