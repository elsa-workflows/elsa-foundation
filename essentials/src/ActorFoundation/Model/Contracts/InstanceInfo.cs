using System.Text.Json.Serialization;
using Kernel.Foundation.Contracts;

namespace Kernel.Model.Contracts;

/// <summary>The workflow identity and model data visible to an execution model.</summary>
/// <param name="Workflow">The workflow the instance runs: on a publishing host, the published artifact.</param>
public sealed record InstanceInfo(ActorRef Ref, string Tenant, string Model, string Workflow, ActorRef? Parent)
{
    public string Tenant { get; } = Tenant;

    /// <summary>The publishing host's optional exact executable identity for this run.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExecutableSelection { get; init; }
}
