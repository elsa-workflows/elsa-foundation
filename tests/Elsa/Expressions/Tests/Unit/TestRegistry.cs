using Elsa.Expressions.Services;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;

namespace Elsa.Expressions.Tests.Unit;

/// <summary>
/// Shared test setup: a <see cref="IWellKnownTypeRegistry"/> seeded from the framework
/// <see cref="DefaultVariableTypeDescriptorProvider"/> — the same alias↔CLR-type set the production
/// seed startup task installs, so mapper tests resolve aliases exactly as the runtime would.
/// </summary>
internal static class TestRegistry
{
    public static IWellKnownTypeRegistry SeededWithPrimitives()
    {
        var registry = new WellKnownTypeRegistry();
        foreach (var descriptor in new DefaultVariableTypeDescriptorProvider().GetDescriptors())
            registry.RegisterType(descriptor.ClrType, descriptor.Alias);
        return registry;
    }
}
