using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nuplane.Feeds.Credentials;

namespace Elsa.Secrets.Nuplane.Extensions;

public static class SecretsNuplaneServiceCollectionExtensions
{
    /// <summary>
    /// Claims the <c>elsa</c> secret-reference provider segment for the Secrets module, so a feed declaring
    /// <c>Credentials: secrets://elsa/&lt;name&gt;</c> has that value read through
    /// <c>ISecretValueResolver</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call this on the container that composes <c>AddNuplane</c>, because that is the container Nuplane
    /// resolves its providers from. The container also has to be one where <c>ISecretValueResolver</c>
    /// resolves — <c>AddSecrets</c>, or the <c>Secrets</c> shell feature — which is what
    /// <see cref="Features.SecretsNuplaneFeature"/> guarantees by depending on it. A container that never
    /// calls this has no <c>elsa</c> provider, and Nuplane refuses a feed referencing one by name, without
    /// contacting it.
    /// </para>
    /// <para>
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>
    /// rather than <c>AddSingleton</c>: Nuplane refuses the whole composition when two providers claim one
    /// segment, so calling this twice — a host that composes it and a shell feature that composes it again —
    /// must add one provider, not two.
    /// </para>
    /// </remarks>
    /// <param name="services">The container Nuplane is composed in.</param>
    /// <param name="tenantId">
    /// The tenant feed credentials are read under. A reconcile carries no request and no principal, so
    /// there is no tenant to inherit; defaults to <see cref="SecretsFeedCredentialProvider.DefaultTenantId"/>
    /// when null or blank.
    /// </param>
    public static IServiceCollection AddSecretsFeedCredentials(this IServiceCollection services, string? tenantId = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretReferenceProvider, SecretsFeedCredentialProvider>(
            provider => new(provider.GetRequiredService<IServiceScopeFactory>(), tenantId ?? SecretsFeedCredentialProvider.DefaultTenantId)));
        return services;
    }
}
