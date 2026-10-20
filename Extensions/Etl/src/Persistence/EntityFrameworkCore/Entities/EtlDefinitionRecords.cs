namespace Elsa.Etl.Persistence.EntityFrameworkCore.Entities;

/// <summary>Mutable authoring source. Its revision is independent of the immutable publication history.</summary>
internal sealed class EtlDefinitionDraftRecord
{
    public string ScopeKey { get; set; } = null!;
    public string TenantId { get; set; } = null!;
    public string PersistencePartition { get; set; } = null!;
    public string DefinitionKey { get; set; } = null!;
    public string DefinitionId { get; set; } = null!;
    public long Revision { get; set; }
    public int LastPublishedVersion { get; set; }
    public string SchemaVersion { get; set; } = null!;
    public string PipelineJson { get; set; } = null!;
    public string PipelineHash { get; set; } = null!;
    public long UpdatedAtUtcTicks { get; set; }
}

/// <summary>Append-only published runnable artifact. No update or delete path is exposed by the store.</summary>
internal sealed class EtlPublishedArtifactRecord
{
    public string ScopeKey { get; set; } = null!;
    public string TenantId { get; set; } = null!;
    public string PersistencePartition { get; set; } = null!;
    public string DefinitionKey { get; set; } = null!;
    public string DefinitionId { get; set; } = null!;
    public int DefinitionVersion { get; set; }
    public string SchemaVersion { get; set; } = null!;
    public string ArtifactJson { get; set; } = null!;
    public string ArtifactEnvelopeHash { get; set; } = null!;
    public string PipelineHash { get; set; } = null!;
    public long PublishedAtUtcTicks { get; set; }
}
