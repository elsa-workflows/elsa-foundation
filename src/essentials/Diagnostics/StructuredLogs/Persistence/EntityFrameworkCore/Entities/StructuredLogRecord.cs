namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Entities;

public sealed class StructuredLogRecord
{
    public string ScopeKey { get; set; } = "";
    public long Position { get; set; }
    public string TenantId { get; set; } = "";
    public string ScopeId { get; set; } = "";
    public string StreamId { get; set; } = "";
    public long TimestampTicks { get; set; }
    public short TimestampOffsetMinutes { get; set; }
    public int Level { get; set; }
    public string CategoryKey { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public string ReplayToken { get; set; } = "";
    public string PayloadJson { get; set; } = "";
}
