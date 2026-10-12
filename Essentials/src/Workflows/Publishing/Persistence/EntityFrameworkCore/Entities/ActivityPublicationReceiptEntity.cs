namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;

/// <summary>
/// One create-only activity-publication receipt: its lossless JSON material plus the identity projections a
/// lookup needs. The receipt tenant is the operation's own and is distinct from the persistence scope.
/// </summary>
public sealed class ActivityPublicationReceiptEntity
{
    public string Id { get; set; } = null!;
    public string ReceiptKeyHash { get; set; } = null!;
    public string IdempotencyKey { get; set; } = null!;
    public string? ReceiptTenantId { get; set; }
    public string Status { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public string Content { get; set; } = null!;
    public string? TenantId { get; set; }
    public string TenantIdHash { get; set; } = null!;
}
