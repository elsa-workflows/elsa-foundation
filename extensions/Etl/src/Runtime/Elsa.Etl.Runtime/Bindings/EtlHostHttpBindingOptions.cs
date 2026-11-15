namespace Elsa.Etl.Runtime;

/// <summary>A trusted host HTTP revision available to ETL runtime requests.</summary>
public sealed class EtlHostHttpBindingOptions
{
    /// <summary>The trusted tenant identifier, or null for the explicit null-tenant scope.</summary>
    public string? TenantId { get; set; }

    /// <summary>The required opaque persistence partition for this binding.</summary>
    public string PersistencePartition { get; set; } = string.Empty;

    /// <summary>The bounded alias referenced by a published ETL pipeline.</summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>The HTTP connector role: Source, MainOutput, or ErrorOutput.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>The immutable host revision identifier.</summary>
    public string Revision { get; set; } = string.Empty;

    /// <summary>The absolute host-controlled endpoint URI; its exact path must be allowlisted.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Exact allowed request paths. A nonempty list is strict; when empty, source seed paths or the output endpoint path provide the default.</summary>
    public IList<string> AllowedPaths { get; set; } = new List<string>();

    /// <summary>Whether this revision may use cleartext HTTP. HTTPS is required by default.</summary>
    public bool AllowHttp { get; set; }

    /// <summary>Whether this revision is offered for new requests. Defaults to true; set false for retained revisions.</summary>
    public bool IsCurrent { get; set; } = true;

    /// <summary>The tenant connection name used to authenticate requests, or null for anonymous requests.</summary>
    public string? ConnectionName { get; set; }

    /// <summary>The HTTP header used as the idempotency key when this binding is used as an output.</summary>
    public string IdempotencyHeader { get; set; } = "Idempotency-Key";

    /// <summary>The bounded source shards and their host-approved starting request URIs.</summary>
    public IList<EtlHostHttpShardOptions> Shards { get; set; } = new List<EtlHostHttpShardOptions>();
}

/// <summary>A host-approved HTTP source shard and its starting request URI.</summary>
public sealed class EtlHostHttpShardOptions
{
    /// <summary>The bounded unique shard identifier retained by an ETL run.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The absolute host-approved starting request URI.</summary>
    public string SeedUri { get; set; } = string.Empty;
}
