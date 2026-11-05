namespace Elsa.Persistence.Core;

/// <summary>Trusted boundary selected by a domain adapter, never by authored payload fields.</summary>
public sealed record SourceScope(string Partition, string? TenantId, string Kind, string Authority);

/// <summary>One editable source copy. Revision is a concurrency token, not a release version.</summary>
public sealed class SourceWorkingCopy
{
    public string Key { get; set; } = "";
    public string ScopeKey { get; set; } = "";
    /// <summary>Optional domain single-copy uniqueness claim; not a release identity.</summary>
    public string? SourceIdentity { get; set; }
    public string Partition { get; set; } = "";
    public string? TenantId { get; set; }
    public string Kind { get; set; } = "";
    public string Authority { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string CopyId { get; set; } = "";
    public string Name { get; set; } = "";
    public string NameSearchKey { get; set; } = "";
    public string Profile { get; set; } = "";
    public string Content { get; set; } = "";
    public long Revision { get; set; }
    public long Fence { get; set; }
    public bool Discarded { get; set; }
    public string? BaseSnapshotId { get; set; }
}

/// <summary>Write-once source content; publication operations reference this rather than copy it.</summary>
public sealed class SourceSnapshot
{
    public string Key { get; set; } = "";
    public string WorkingCopyKey { get; set; } = "";
    public string OperationId { get; set; } = "";
    public string Profile { get; set; } = "";
    public string Content { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public long Revision { get; set; }
    public string? BaseSnapshotId { get; set; }
}

/// <summary>A retained reference, including the capture intent of an interrupted operation.</summary>
public sealed class SourceSnapshotPin
{
    public string Key { get; set; } = "";
    public string SnapshotKey { get; set; } = "";
    public string Owner { get; set; } = "";
}

/// <summary>Only domain-authorized, bounded source queries reach the persistence reader.</summary>
public sealed record SourcePage(int Total, IReadOnlyList<SourceWorkingCopy> Items);

/// <summary>An immutable snapshot with its original copy reference, resolved within trusted source scope.</summary>
public sealed record SourceSnapshotRead(string CopyId, SourceSnapshot Snapshot);
public sealed record SourceSnapshotPage(int Total, IReadOnlyList<SourceSnapshotRead> Items);

/// <summary>A source persistence refusal, with no protected payload in its diagnostic.</summary>
public sealed class SourceRevisionException(string code, Exception? innerException = null)
    : Exception("The source revision operation was refused (" + code + ").", innerException)
{
    public string Code { get; } = code;
}

public interface ISourceRevisionReader
{
    Task<SourceWorkingCopy?> FindAsync(string sourceId, string copyId, CancellationToken cancellationToken = default);
    Task<SourceSnapshot?> FindSnapshotAsync(string sourceId, string copyId, string snapshotId, CancellationToken cancellationToken = default);
    Task<SourceSnapshotRead?> FindSourceSnapshotAsync(string sourceId, string snapshotId, CancellationToken cancellationToken = default);
    /// <summary>All retained copies of one source, in stable snapshot identity order; revisions remain copy-local CAS numbers.</summary>
    Task<SourceSnapshotPage> HistorySourceAsync(string sourceId, int skip = 0, int take = 50, CancellationToken cancellationToken = default);
    /// <summary>One representative copy per source, including discarded sources. Prefers an active copy, then stable copy identity.</summary>
    Task<SourcePage> ListSourcesAsync(string? search, int skip, int take, CancellationToken cancellationToken = default);
    Task<SourcePage> ListAsync(string? search, int skip, int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SourceSnapshot>> HistoryAsync(string sourceId, string copyId, int skip = 0, int take = 100, CancellationToken cancellationToken = default);
}

public interface ISourceRevisionWriter
{
    Task<SourceWorkingCopy> CreateAsync(string sourceId, string copyId, string name, string profile, string content, CancellationToken cancellationToken = default);
    Task<SourceWorkingCopy> ForkAsync(string sourceId, string copyId, string snapshotId, string newSourceId, string newCopyId, string name, CancellationToken cancellationToken = default);
    Task<SourceWorkingCopy> SaveAsync(string sourceId, string copyId, long expectedRevision, string name, string profile, string content, CancellationToken cancellationToken = default);
    Task<SourceSnapshot> CaptureAsync(string sourceId, string copyId, long expectedRevision, string operationId, CancellationToken cancellationToken = default);
    Task PinAsync(string sourceId, string copyId, string snapshotId, string owner, CancellationToken cancellationToken = default);
    Task UnpinAsync(string sourceId, string copyId, string snapshotId, string owner, CancellationToken cancellationToken = default);
    Task DeleteSnapshotAsync(string sourceId, string copyId, string snapshotId, CancellationToken cancellationToken = default);
    Task DiscardAsync(string sourceId, string copyId, long expectedRevision, CancellationToken cancellationToken = default);
}
