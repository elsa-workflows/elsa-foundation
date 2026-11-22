namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Entities;

// Keys and lookups use fixed-width hashes of opaque identities, so provider collations can never fold
// two identities together. The encoded residual columns and canonical JSON are the lossless source
// material every read verifies before it returns a domain value.

/// <summary>L01: one immutable, expiring collection upload in its exact tenant-plus-user scope.</summary>
public sealed class Elsa3ImportCollectionRecord
{
    public string TenantKey { get; set; } = null!;
    public string UserIdHash { get; set; } = null!;
    public string HandleHash { get; set; } = null!;
    public string Handle { get; set; } = null!;
    public string? TenantId { get; set; }
    public string UserId { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long CreatedAtUtcTicks { get; set; }
    public long ExpiresAtUtcTicks { get; set; }
    public long ContentLength { get; set; }
    public string ContentJson { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
}

/// <summary>L02: one durable, immutable apply receipt for an idempotency key in its exact tenant-plus-user scope.</summary>
public sealed class Elsa3ImportReceiptRecord
{
    public string TenantKey { get; set; } = null!;
    public string UserIdHash { get; set; } = null!;
    public string ReceiptIdHash { get; set; } = null!;
    public string ReceiptId { get; set; } = null!;
    public string IdempotencyKey { get; set; } = null!;
    public string? TenantId { get; set; }
    public string UserId { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long CompletedAtUtcTicks { get; set; }

    /// <summary>Identifies the commit attempt that wrote the receipt, so reconciliation after an uncertain commit can tell its own commit from a concurrent one.</summary>
    public string CommitAttemptId { get; set; } = null!;

    public string ContentJson { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
}

/// <summary>L03: tenant-owned provenance binding one imported Design definition to its Elsa 3 source lineage.</summary>
public sealed class Elsa3ImportDefinitionBindingRecord
{
    public string TenantKey { get; set; } = null!;
    public string BindingIdHash { get; set; } = null!;
    public string BindingId { get; set; } = null!;
    public string TargetDocumentKind { get; set; } = null!;
    public string TargetDefinitionIdHash { get; set; } = null!;
    public string TargetDefinitionId { get; set; } = null!;
    public string SourceKind { get; set; } = null!;
    public string SourceDefinitionId { get; set; } = null!;
    public string? TenantId { get; set; }
    public string SchemaVersion { get; set; } = null!;
    public long CreatedAtUtcTicks { get; set; }
    public int CreatedAtOffsetMinutes { get; set; }
    public string ContentHash { get; set; } = null!;
}
