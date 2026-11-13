namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

internal sealed class StandaloneTriggerEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeHash { get; set; } = null!;
    public string TenantKey { get; set; } = null!;
    public string TenantHash { get; set; } = null!;
    public string TriggerKey { get; set; } = null!;
    public string TriggerOrderKey { get; set; } = null!;
    public string TypeKey { get; set; } = null!;
    public string SearchName { get; set; } = null!;
    public int DesiredState { get; set; }
    public long Revision { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = StandaloneTriggersEfModule.SchemaVersion;
}

internal sealed class TriggerSubscriptionEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeHash { get; set; } = null!;
    public string TenantKey { get; set; } = null!;
    public string TenantHash { get; set; } = null!;
    public string TriggerRowId { get; set; } = null!;
    public string SubscriptionKey { get; set; } = null!;
    public string SubscriptionOrderKey { get; set; } = null!;
    public string DefinitionHash { get; set; } = null!;
    public string DefinitionKey { get; set; } = null!;
    public string SlotKey { get; set; } = null!;
    public long Revision { get; set; }
    public bool IsDeleted { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = StandaloneTriggersEfModule.SchemaVersion;
}

internal sealed class TriggerMutationEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string TenantKey { get; set; } = null!;
    public string ActorKey { get; set; } = null!;
    public string RequestKey { get; set; } = null!;
    public string TriggerRowId { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = StandaloneTriggersEfModule.SchemaVersion;
}
