using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>
/// Test helper that drives Groundwork document-store startup for bare <see cref="IServiceProvider"/>s. In a real
/// host the store is materialized by a hosted service / CShells shell initializer before the first consumer
/// resolves <c>IDocumentStore</c>; a bare provider built in a test has no such lifecycle, so the store must be
/// initialized explicitly. This runs every registered <see cref="IShellInitializer"/> (which includes the
/// provider's document-store initializer), mirroring the shell-activation path. Idempotent.
/// </summary>
public static class GroundworkStoreInitialization
{
    /// <summary>Runs all registered shell initializers so the Groundwork document store is materialized and usable.</summary>
    public static async Task InitializeGroundworkStoreAsync(this IServiceProvider provider, CancellationToken cancellationToken = default)
    {
        foreach (var initializer in provider.GetServices<IShellInitializer>())
            await initializer.InitializeAsync(cancellationToken);
    }
}
