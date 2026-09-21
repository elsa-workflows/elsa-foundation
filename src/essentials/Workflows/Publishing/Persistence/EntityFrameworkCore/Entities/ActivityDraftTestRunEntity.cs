namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;

/// <summary>
/// One activity draft test-run receipt: its lossless JSON material, the expiry projection cleanup orders by,
/// and the receipt revision that every update compares and swaps.
/// </summary>
public sealed class ActivityDraftTestRunEntity
{
    public string Id { get; set; } = null!;
    public string TestRunId { get; set; } = null!;
    public string TestRunIdHash { get; set; } = null!;
    public byte[] TestRunIdOrderKey { get; set; } = [];
    public long ReceiptExpiresAtUtcTicks { get; set; }
    public int ReceiptExpiresAtOffsetMinutes { get; set; }
    public string Status { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public string Content { get; set; } = null!;
    public string? TenantId { get; set; }
    public string TenantIdHash { get; set; } = null!;
    public long Revision { get; set; }
}
