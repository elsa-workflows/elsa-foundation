namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Entities;

public sealed class StructuredLogAppendOperation
{
    public string ScopeKey { get; set; } = "";
    public string BatchId { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string ScopeId { get; set; } = "";
    public string StreamId { get; set; } = "";
    public long IssuedAtTicks { get; set; }
    public string Fingerprint { get; set; } = "";
    public string OutcomeJson { get; set; } = "";
}
