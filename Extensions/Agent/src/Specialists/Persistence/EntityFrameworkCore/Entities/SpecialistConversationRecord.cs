namespace Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Entities;

internal sealed class SpecialistConversationRecord
{
    public string ScopeKey { get; set; } = "";
    public string Id { get; set; } = "";
    public string SchemaVersion { get; set; } = "";
    public string Payload { get; set; } = "";
    public long Version { get; set; }
}
