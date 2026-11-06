namespace Acme.Processing.Core;

public sealed record ProcessingActor(string TenantId, string SubjectId);

/// <summary>One scoped replacement supplied by the host's trusted identity integration.</summary>
public interface IProcessingActorContext
{
    ValueTask<ProcessingActor> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Module-owned business data; customer category is deliberately part of this public business contract.</summary>
public sealed record ProcessingRecord(string Id, string TenantId, string OwnerId, IReadOnlySet<string> ReaderIds,
    string CustomerCategory, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, string Status);

public sealed record ProcessingCreate(string CustomerCategory, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
    string Status, IReadOnlyList<string>? ReaderIds = null);

public sealed record ProcessingRecordView(string Id, string? CustomerCategory, bool CategoryAvailable,
    DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, string Status);

public sealed record ProcessingRecordPage(IReadOnlyList<ProcessingRecord> Items, string? NextId);
public sealed record ProcessingRecordViewPage(IReadOnlyList<ProcessingRecordView> Items, string? NextCursor);

/// <summary>Trusted persistence read port; caller-facing reads use IProcessingRecords and current module authority.</summary>
public interface IProcessingRecordReader
{
    ValueTask<ProcessingRecord?> FindAsync(string tenantId, string id, CancellationToken cancellationToken = default);
    ValueTask<ProcessingRecordPage> ReadPageAsync(string tenantId, int take, string? afterId = null, CancellationToken cancellationToken = default);
}

/// <summary>Single persistence command port; records and readers remain tenant/owner bound.</summary>
public interface IProcessingRecordWriter
{
    ValueTask AddAsync(ProcessingRecord record, CancellationToken cancellationToken = default);
}

public interface IProcessingRecords
{
    ValueTask<string> CreateAsync(ProcessingCreate input, CancellationToken cancellationToken = default);
    ValueTask<ProcessingRecordView?> FindAsync(string id, CancellationToken cancellationToken = default);
    ValueTask<ProcessingRecordViewPage> ReadAsync(int take = 25, string? cursor = null, CancellationToken cancellationToken = default);
}

public sealed class ProcessingException(string code, string message, Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}
