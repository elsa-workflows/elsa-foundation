using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Persistence.Groundwork.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Secrets.Persistence.Groundwork.DependencyInjection;

public static class GroundworkSecretsStoreRegistration
{
    public static IServiceCollection AddGroundworkSecretsStore(this IServiceCollection services)
    {
        services.RemoveAll<ISecretRepository>();
        services.AddSingleton<ISecretRepository, GroundworkSecretRepository>();
        return services;
    }
}
