namespace Elsa.Persistence.Spike;

/// <summary>Minimal Secrets-shaped row used by both spike variants.</summary>
public sealed class SecretRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public byte[] RowVersion { get; set; } = null!;
}
