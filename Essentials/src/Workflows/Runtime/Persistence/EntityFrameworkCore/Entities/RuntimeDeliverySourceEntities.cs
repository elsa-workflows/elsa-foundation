namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

/// <summary>Canonical source for BusinessMessage and explicitly recognized new families only.</summary>
/// <remarks>Existing StimulusIngress and native outbox sources keep their owning canonical rows; never mirror
/// them here. ContentJson holds the immutable header/full authority/test context and family progress. HeaderRevision
/// stays fixed while Revision fences mutable progress/proof CAS. Checked reads validate every projection against
/// canonical content; fingerprints, projections and construction grant no source or participant authority.</remarks>
internal sealed class RuntimeIngressSourceEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string SourceFamily { get; set; } = null!;
    public string SourceFamilyHash { get; set; } = null!;
    public string SourceId { get; set; } = null!;
    public string SourceIdHash { get; set; } = null!;
    public string? TenantId { get; set; }
    public string? TenantIdHash { get; set; }
    public int RunKind { get; set; }
    public string? TestScopeId { get; set; }
    public string? TestScopeIdHash { get; set; }
    public string AuthorityPartitionKey { get; set; } = null!;
    public string FullDiscoveryIdentity { get; set; } = null!;
    public string FullDiscoveryIdentityHash { get; set; } = null!;
    public int HeaderVersion { get; set; }
    public long HeaderRevision { get; set; }
    public int RequiredParticipantVersion { get; set; }
    public string HeaderFingerprint { get; set; } = null!;
    public int Status { get; set; }
    public long RecordedAtUtcTicks { get; set; }
    public string AcceptanceOperationId { get; set; } = null!;
    public string AcceptanceOperationFingerprint { get; set; } = null!;
    public string OperationProofJson { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}

/// <summary>Source-owned transport work and retained completion proof; status never asserts business delivery.</summary>
/// <remarks>The exact scope/family/source address resolves through its recognized canonical family adapter,
/// not a generic-source FK or header copy. SourceHeaderRevision/Fingerprint bind immutable header identity;
/// Revision fences mutable work/claim CAS. Full tenant/run/test/authority predicates and canonical source checks
/// apply before paging/claim and again at effects/commit. No database FK protects this cross-family link: the
/// selected provider must serialize source creation/deletion with owed work and retained proof roots in the same
/// owning transaction. Canonical target/group/delivery state stays with its family/checkpoint owner. Missing,
/// corrupt or unsupported canonical sources refuse before claim mutation.</remarks>
internal sealed class RuntimeDeliveryWorkEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string SourceFamily { get; set; } = null!;
    public string SourceFamilyHash { get; set; } = null!;
    public string SourceId { get; set; } = null!;
    public string SourceIdHash { get; set; } = null!;
    public string? TenantId { get; set; }
    public string? TenantIdHash { get; set; }
    public int RunKind { get; set; }
    public string? TestScopeId { get; set; }
    public string? TestScopeIdHash { get; set; }
    public string AuthorityPartitionKey { get; set; } = null!;
    public string FullDiscoveryIdentity { get; set; } = null!;
    public string FullDiscoveryIdentityHash { get; set; } = null!;
    public long SourceHeaderRevision { get; set; }
    public string SourceHeaderFingerprint { get; set; } = null!;
    public string WorkId { get; set; } = null!;
    public string WorkIdHash { get; set; } = null!;
    public string WorkKind { get; set; } = null!;
    public string WorkKindHash { get; set; } = null!;
    public int Role { get; set; }
    public int RequiredParticipantVersion { get; set; }
    public string ContentFingerprint { get; set; } = null!;
    public int Status { get; set; }
    public long RecordedAtUtcTicks { get; set; }
    public bool ClaimableIsEligible { get; set; }
    public long ClaimableAtUtcTicks { get; set; }
    public string? OwnerId { get; set; }
    public long FencingToken { get; set; }
    public long? ClaimedAtUtcTicks { get; set; }
    public long? VisibleAfterUtcTicks { get; set; }
    public string? CompletionOperationId { get; set; }
    public string? CompletionOperationFingerprint { get; set; }
    public string? OperationProofJson { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}
