using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.DesignConformance.Targets.Tests;

/// <summary>
/// Starts only the initializers owned by a legacy provider baseline. The clean-break v2 session source
/// remains fail-closed when a shipping host declares v2 units without a v2 provider connection; these
/// temporary v1 conformance hosts cannot load that same-ID provider graph and must not claim to exercise it.
/// </summary>
public static class LegacyGroundworkTestHost
{
    public static async Task InitializeAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        foreach (var initializer in services.GetServices<IShellInitializer>())
        {
            if (initializer is GroundworkStorageSessionSource)
                continue;
            await initializer.InitializeAsync(cancellationToken);
        }
    }
}
