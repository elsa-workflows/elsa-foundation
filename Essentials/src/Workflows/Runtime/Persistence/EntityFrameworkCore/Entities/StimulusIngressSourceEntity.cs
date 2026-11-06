namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

/// <summary>The immutable source and its canonical origin/key projection; delivery state follows separately.</summary>
internal sealed class StimulusIngressSourceEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string SourceId { get; set; } = null!;
    public string SourceIdHash { get; set; } = null!;
    public string OriginFingerprint { get; set; } = null!;
    public string SourceFingerprint { get; set; } = null!;
    public long AcceptedAtUtcTicks { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}
