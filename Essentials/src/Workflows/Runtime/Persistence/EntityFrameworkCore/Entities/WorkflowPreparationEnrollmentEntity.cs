namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

// One permanent barrier per EXISTING preparation identity, not per retry/nonce. No material FK or TTL.
internal sealed class WorkflowPreparationEnrollmentEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKeyHash { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string IncarnationId { get; set; } = null!;
    public long Revision { get; set; }
}
