using Kernel.Foundation.Contracts;

namespace Kernel.Model.Contracts;

/// <summary>The workflow identity and model data visible to an execution model.</summary>
/// <param name="Workflow">The workflow the instance runs: on a publishing host, the published artifact.</param>
public sealed record InstanceInfo(ActorRef Ref, string Tenant, string Model, string Workflow, ActorRef? Parent)
{
    public string Tenant { get; } = Tenant;
}
