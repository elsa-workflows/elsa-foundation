namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

/// <summary>The exact first replacement time of a slot revision with a continuing schedule.</summary>
internal sealed class RuntimeKernelScheduleCutoverEntity
{
    public string Id { get; set; } = null!;
    public long ActivatedAtUtcTicks { get; set; }
    public string SchemaVersion { get; set; } = null!;
}
