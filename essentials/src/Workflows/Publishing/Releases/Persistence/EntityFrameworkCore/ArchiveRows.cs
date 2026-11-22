namespace Elsa.Workflows.Publishing.Releases.Persistence.EntityFrameworkCore;

internal sealed class ReleaseArchiveRow
{
    public string Key { get; set; } = "";
    public string ScopeKey { get; set; } = "";
    public string Authority { get; set; } = "";
    public string Partition { get; set; } = "";
    public string? TenantId { get; set; }
    public string Kind { get; set; } = "";
    public string CaptureId { get; set; } = "";
    public string ReleaseDigest { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public byte[] Manifest { get; set; } = [];
    public string ManifestDigest { get; set; } = "";
    public long ManifestByteLength { get; set; }
    public int BlobCount { get; set; }
    public int AttachmentCount { get; set; }
    public string SchemaVersion { get; set; } = ReleaseArchiveModelBuilderExtensions.SchemaVersion;
}

internal sealed class ReleaseArchiveEntryRow
{
    public string Key { get; set; } = "";
    public string ArchiveKey { get; set; } = "";
    public string ScopeKey { get; set; } = "";
    public string Authority { get; set; } = "";
    public string Partition { get; set; } = "";
    public string? TenantId { get; set; }
    public string Kind { get; set; } = "";
    public string CaptureId { get; set; } = "";
    public int EntryKind { get; set; }
    public string Name { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public string ContentDigest { get; set; } = "";
    public long ContentByteLength { get; set; }
    public string SchemaVersion { get; set; } = ReleaseArchiveModelBuilderExtensions.SchemaVersion;
}
