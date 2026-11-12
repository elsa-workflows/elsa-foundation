namespace Elsa.Agent.Persistence.EntityFrameworkCore.Entities;

// A row-level admission/cancellation guard, including operations whose turn is not staged yet.
internal sealed class AgentOperationRecord
{
    public string ScopeKey { get; set; } = "";
    public string OperationKey { get; set; } = "";
    public string SchemaVersion { get; set; } = "";
    public string Payload { get; set; } = "";
    public long Version { get; set; }
}
