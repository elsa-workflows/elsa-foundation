using Elsa.Foundation.Identity.OpenIddict.Extensions;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Composition;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Models;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Scoping;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.DependencyInjection;

/// <summary>
/// Composes the provider-neutral OpenIddict behavior (spec 106 T020) with the four Groundwork-backed
/// <c>IOpenIddict*Store</c> implementations under <c>Groundwork/Stores/</c>, in place of the transitional
/// EF Core store oracle registered by <see cref="OpenIddictIdentityServiceCollectionExtensions"/>.
/// </summary>
/// <remarks>
/// Unlike the EF registration, this extension configures no <c>DbContext</c> and runs no migrations: the
/// four Groundwork storage units (<see cref="OpenIddictGroundworkStorageManifest"/>) are provisioned
/// through the manifest/deployment path (<see cref="OpenIddictGroundworkDeploymentSchema"/>), not by an
/// <c>IHostedService</c>/<c>IShellInitializer</c> store initializer. There is accordingly no equivalent of
/// <c>OpenIddictIdentityStoreInitializer</c> here.
/// </remarks>
public static class OpenIddictGroundworkServiceCollectionExtensions
{
    public static IServiceCollection AddFoundationIdentityOpenIddictGroundwork(
        this IServiceCollection services,
        Action<OpenIddictIdentityOptions>? configure = null)
    {
        // Server/validation options, the scheme selector, ITokenService, and (when development/demo)
        // the ephemeral-key guard are all provider-neutral and are reused verbatim from the EF path.
        services.AddFoundationIdentityOpenIddictBehavior(configure);

        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IGroundworkStorageManifestSource, OpenIddictGroundworkStorageManifestSource>());

        // Mirrors GroundworkIdentityStoresRegistration.AddGroundworkIdentityStores: when a real Groundwork
        // provider has published an admitted IGroundworkStoreSessionFactory (and its session source), route
        // atomic mutations through the readiness-checked session wrapper; otherwise (e.g. a
        // directly-registered IDocumentStore test double with no provider session) fall back to the
        // internal direct-store constructor. OpenIddictGroundworkStoreSessionFactory is deliberately built
        // by hand here rather than registered as its own DI service: it requires GroundworkStoreSessionSource,
        // which only a real Groundwork provider registers, and ASP.NET Core's development-time service
        // validation would otherwise fail eagerly on hosts that never select one.
        services.TryAddScoped(serviceProvider =>
        {
            var sessionFactory = serviceProvider.GetService<IGroundworkStoreSessionFactory>();
            var sessionSource = serviceProvider.GetService<GroundworkStoreSessionSource>();
            return sessionFactory is not null && sessionSource is not null
                ? new OpenIddictGroundworkAtomicWrite(new OpenIddictGroundworkStoreSessionFactory(sessionSource, sessionFactory))
                : new OpenIddictGroundworkAtomicWrite(serviceProvider.GetRequiredService<IDocumentStore>());
        });

        services.AddOpenIddict().AddCore(core =>
        {
            core.SetDefaultApplicationEntity<OpenIddictGroundworkApplication>()
                .SetDefaultAuthorizationEntity<OpenIddictGroundworkAuthorization>()
                .SetDefaultScopeEntity<OpenIddictGroundworkScope>()
                .SetDefaultTokenEntity<OpenIddictGroundworkToken>();

            core.ReplaceApplicationStore<OpenIddictGroundworkApplication, GroundworkOpenIddictApplicationStore>(ServiceLifetime.Scoped)
                .ReplaceAuthorizationStore<OpenIddictGroundworkAuthorization, GroundworkOpenIddictAuthorizationStore>(ServiceLifetime.Scoped)
                .ReplaceScopeStore<OpenIddictGroundworkScope, GroundworkOpenIddictScopeStore>(ServiceLifetime.Scoped)
                .ReplaceTokenStore<OpenIddictGroundworkToken, GroundworkOpenIddictTokenStore>(ServiceLifetime.Scoped);
        });

        return services;
    }
}
