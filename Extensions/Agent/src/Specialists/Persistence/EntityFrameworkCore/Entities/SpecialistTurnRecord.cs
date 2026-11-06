namespace Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Entities;

internal sealed class SpecialistTurnRecord
{
    public string ScopeKey { get; set; } = "";
    public string Handle { get; set; } = "";
    public string OperationKey { get; set; } = "";
    public string SchemaVersion { get; set; } = "";
    public string Payload { get; set; } = "";
    public long Version { get; set; }
    public int Status { get; set; }
    public string? Fence { get; set; }
    public long DeadlineUtcTicks { get; set; }
}
