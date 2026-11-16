namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

/// <summary>Scoped relational projections and serialized content for one durable leaf invocation.</summary>
public sealed class BackgroundActivityWorkEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string WorkflowExecutionId { get; set; } = null!;
    public string WorkflowExecutionIdHash { get; set; } = null!;
    public string ActivityExecutionId { get; set; } = null!;
    public string ActivityExecutionIdHash { get; set; } = null!;
    public string WorkId { get; set; } = null!;
    public string WorkIdHash { get; set; } = null!;
    public int Status { get; set; }
    public long RecordedAtUtcTicks { get; set; }
    public long UpdatedAtUtcTicks { get; set; }
    public long ClaimableAtUtcTicks { get; set; }
    public bool ClaimableIsEligible { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = null!;
    public long Revision { get; set; }
}
