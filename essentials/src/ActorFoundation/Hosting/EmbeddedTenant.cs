using Kernel.Foundation.Contracts;

namespace Elsa.ActorFoundation.Hosting;

/// <summary>Holds the configured tenant for an Embedded foundation host.</summary>
/// <remarks>Construct this during host composition using trusted configuration.</remarks>
public sealed class EmbeddedTenant(string configuredTenant)
{
    /// <summary>The fixed scope used by the host for ingress and reads.</summary>
    public BoundScope Scope { get; } = new(configuredTenant);
}
