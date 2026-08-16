using Groundwork.Kernel;

namespace Elsa.Persistence.Groundwork.Composition;

/// <summary>Exposes the selected target's provider capabilities to v2 feature adapters.</summary>
public interface IGroundworkStorageCapabilitySource
{
    IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null);
}
