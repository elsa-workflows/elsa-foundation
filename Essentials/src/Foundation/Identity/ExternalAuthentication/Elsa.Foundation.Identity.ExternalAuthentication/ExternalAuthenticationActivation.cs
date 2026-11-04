using CShells.Lifecycle;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.ExternalAuthentication.Connections;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.ExternalAuthentication;

/// <summary>Validates deployment policy and optional persistence registration at hosted/shell activation.</summary>
internal sealed class ExternalAuthenticationActivation(IOptions<ExternalAuthenticationOptions> options, IServiceScopeFactory scopes)
    : IShellInitializer, IHostedService
{
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configured = options.Value;
        _ = ConnectionSelection.Select(configured, new ConnectionCatalog([]));
        if (configured.AllowDatabaseManagement)
        {
            using var scope = scopes.CreateScope();
            var catalog = scope.ServiceProvider.GetRequiredService<IConnectionCatalogStore>();
            if (catalog is ProviderConnectionCatalog && scope.ServiceProvider.GetService<IRevisionAwareProviderConfigurationStore>() is null)
                throw new InvalidOperationException("Database-managed external connections require a provider-configuration persistence backend or a custom connection catalog.");
        }
        // Schema readiness is owned by the selected persistence feature; do not query tables before its migrations.
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => InitializeAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
