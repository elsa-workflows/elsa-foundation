using Kernel.Durable;

namespace Kernel.Workflows;

// The historical experiment harnesses invoke these methods by reflection.
internal static class ChildRegistry
{
    internal static List<CreationAddress> Read(string? json) => Kernel.Durable.ChildRegistry.Read(json);
    internal static string? Write(List<CreationAddress> children) => Kernel.Durable.ChildRegistry.Write(children);
}
