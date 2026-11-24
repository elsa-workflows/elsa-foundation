using Kernel.Foundation.Contracts;

namespace Kernel.Model.Contracts;

/// <summary>The workflow identity and model data visible to an execution model.</summary>
public sealed record InstanceInfo(ActorRef Ref, string Tenant, string Model, string Definition, ActorRef? Parent)
{
    public string Tenant { get; } = Tenant;
}
