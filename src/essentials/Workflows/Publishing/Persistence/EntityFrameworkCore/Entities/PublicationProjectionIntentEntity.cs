namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;

/// <summary>Lossless relational projection of one scoped, idempotent publication projection intent.</summary>
public sealed class PublicationProjectionIntentEntity
{
    public string Id { get; set; } = null!;
    public string IntentId { get; set; } = null!;
    public string IntentIdHash { get; set; } = null!;
    public byte[] IntentIdOrderKey { get; set; } = [];
    public string PublicationId { get; set; } = null!;
    public string PublicationIdHash { get; set; } = null!;
    public string ProjectionKind { get; set; } = null!;
    public string ProjectionKindHash { get; set; } = null!;
    public string Operation { get; set; } = null!;
    public string Status { get; set; } = null!;
    public int AttemptCount { get; set; }
    public long? NextAttemptAtUtcTicks { get; set; }
    public int? NextAttemptAtOffsetMinutes { get; set; }
    public string? LastFailureCode { get; set; }
    public string? LastFailureMessage { get; set; }
    public string? TenantId { get; set; }
    public string TenantIdHash { get; set; } = null!;
    public long Revision { get; set; }
}
