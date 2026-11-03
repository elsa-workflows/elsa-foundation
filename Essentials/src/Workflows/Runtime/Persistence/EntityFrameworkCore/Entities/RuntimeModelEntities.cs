namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

internal sealed class RuntimeModelExecutionEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string ExecutionId { get; set; } = null!;
    public string ExecutionIdHash { get; set; } = null!;
    public string ExecutionIdOrderKey { get; set; } = null!;
    public string ArtifactIdHash { get; set; } = null!;
    public int Lifecycle { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}

internal sealed class RuntimeModelInputEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string ExecutionId { get; set; } = null!;
    public string ExecutionIdHash { get; set; } = null!;
    public string InputId { get; set; } = null!;
    public string InputIdHash { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}
