using Elsa.Expressions.Core.Contracts;
using Elsa.Serialization.Core;
using Elsa.Tasks.Core;

namespace Elsa.Serialization.SystemText.Startup;

/// <summary>
/// Startup bridge (research D5): seeds the runtime <see cref="IWellKnownTypeRegistry"/> from the shared
/// <c>TypeDescriptor</c> set every <see cref="IVariableTypeDescriptorProvider"/> contributes, registering
/// each <c>(Alias, ClrType)</c> pair. This is the single registration site for the framework primitives,
/// so the design-time picker catalog and the runtime resolution authority can never drift.
/// </summary>
public sealed class SeedWellKnownTypesStartupTask(
    IEnumerable<IVariableTypeDescriptorProvider> providers,
    IWellKnownTypeRegistry wellKnownTypeRegistry)
    : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in providers.SelectMany(p => p.GetDescriptors()))
        {
            // In this slice RegisterType still silently overwrites (US3 makes it throw); guard here so
            // each alias is registered exactly once regardless of provider overlap.
            if (seen.Add(descriptor.Alias))
                wellKnownTypeRegistry.RegisterType(descriptor.ClrType, descriptor.Alias);
        }

        return Task.CompletedTask;
    }
}
