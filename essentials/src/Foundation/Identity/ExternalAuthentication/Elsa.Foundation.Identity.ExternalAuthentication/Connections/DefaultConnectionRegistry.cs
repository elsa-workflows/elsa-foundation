using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Connections;

internal sealed class DefaultConnectionRegistry(IOptions<ExternalAuthenticationOptions> options, IConnectionCatalogStore catalog) : IConnectionRegistry
{
    public async ValueTask<ConnectionRegistryView> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = options.Value.AllowDatabaseManagement
            ? await catalog.ReadAsync(cancellationToken)
            : new ConnectionCatalog([]);
        return ConnectionSelection.Select(options.Value, snapshot);
    }
}
