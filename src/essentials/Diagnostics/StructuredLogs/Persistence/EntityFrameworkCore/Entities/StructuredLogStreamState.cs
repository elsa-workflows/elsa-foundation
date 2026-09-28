namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Entities;

public sealed class StructuredLogStreamState
{
    public string ScopeKey { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string ScopeId { get; set; } = "";
    public string StreamId { get; set; } = "";
    public long HighWater { get; set; }
    public long AppendOperationCutoffTicks { get; set; }
    public string Version { get; set; } = "";
    public long UpdatedAtTicks { get; set; }

    /// <summary>The persisted-schema version of this row: <see cref="StructuredLogsEfModule.SchemaVersion"/>.</summary>
    public string SchemaVersion { get; set; } = null!;
}
