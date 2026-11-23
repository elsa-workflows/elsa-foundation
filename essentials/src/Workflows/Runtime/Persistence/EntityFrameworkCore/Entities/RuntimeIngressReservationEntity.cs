namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

internal sealed class RuntimeIngressReservationEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeHash { get; set; } = null!;
    public string TenantKey { get; set; } = null!;
    public string StimulusType { get; set; } = null!;
    public string StimulusTypeHash { get; set; } = null!;
    public string StimulusHash { get; set; } = null!;
    public string StimulusHashHash { get; set; } = null!;
    public string OwnerKind { get; set; } = null!;
    public string OwnerId { get; set; } = null!;
    public string OwnerHash { get; set; } = null!;
    public long Revision { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = StandaloneTriggersEfModule.SchemaVersion;
}
