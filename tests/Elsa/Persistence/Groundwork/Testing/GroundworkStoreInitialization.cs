using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>
/// Drives Groundwork startup for a bare <see cref="IServiceProvider"/>.
/// </summary>
/// <remarks>
/// A real host materializes storage through a hosted service / CShells shell initializer before the first
/// consumer resolves a session; a bare provider built in a test has no such lifecycle, so the initializers
/// have to be run explicitly. Under Groundwork v2 that is the whole story — the storage session source is
/// itself a shell initializer and admits every declared unit — so there is no longer a per-provider
/// schema-application helper to call first. Idempotent.
/// </remarks>
public static class GroundworkStoreInitialization
{
    public static async Task InitializeGroundworkStoreAsync(
        this IServiceProvider provider,
        CancellationToken cancellationToken = default)
    {
        foreach (var initializer in provider.GetServices<IShellInitializer>())
            await initializer.InitializeAsync(cancellationToken);
    }
}
