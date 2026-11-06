using Elsa.Jobs.Core;

namespace Elsa.Jobs.Api.Authorization;

/// <summary>Payload-free current-subject authorization resource, always built from the selected tenant and retained authority.</summary>
public sealed class JobApiResource(string tenantId, string kind, string id, string? queue = null, string? jobType = null, JobGateKey? gate = null)
{
    public string TenantId { get; } = tenantId;
    public string Kind { get; } = kind;
    public string Id { get; } = id;
    public string? Queue { get; } = queue;
    public string? JobType { get; } = jobType;
    public JobGateKey? Gate { get; } = gate;
    public JobOrigin? Origin { get; init; }
}
