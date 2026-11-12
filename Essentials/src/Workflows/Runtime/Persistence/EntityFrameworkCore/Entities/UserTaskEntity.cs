namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

internal sealed class UserTaskEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeHash { get; set; } = null!;
    public string TenantHash { get; set; } = null!;
    public string TenantKey { get; set; } = null!;
    public string TaskId { get; set; } = null!;
    public string ExecutionHash { get; set; } = null!;
    public string? AssigneeHash { get; set; }
    public string? GroupHash { get; set; }
    public string? ClaimantHash { get; set; }
    public string ExecutionKey { get; set; } = null!;
    public string? AssigneeKey { get; set; }
    public string? GroupKey { get; set; }
    public string? ClaimantKey { get; set; }
    public string SearchTitle { get; set; } = null!;
    public int Status { get; set; }
    public long Revision { get; set; }
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = UserTasksEfModule.SchemaVersion;
}

internal sealed class UserTaskFormEntity
{
    public string Id { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string ScopeHash { get; set; } = null!;
    public string TenantHash { get; set; } = null!;
    public string TenantKey { get; set; } = null!;
    public string FormKey { get; set; } = null!;
    public string VersionKey { get; set; } = null!;
    public string ContentJson { get; set; } = null!;
    public string SchemaVersion { get; set; } = UserTasksEfModule.SchemaVersion;
}
