namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

/// <summary>
/// One permanent preparation document. It has no material foreign key: terminal evidence survives
/// collection of every executable and template it once retained.
/// </summary>
internal sealed class WorkflowPreparationRetentionEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string OwnerAddress { get; set; } = null!;
    public string OperationId { get; set; } = null!;
    public string PreparationOrderKey { get; set; } = null!;
    public string IncarnationId { get; set; } = null!;
    public long Revision { get; set; }
    public string State { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
}
