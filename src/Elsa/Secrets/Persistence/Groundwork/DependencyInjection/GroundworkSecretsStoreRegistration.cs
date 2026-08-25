using Elsa.Persistence.Groundwork.Composition;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Persistence.Groundwork.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Secrets.Persistence.Groundwork.DependencyInjection;

public static class GroundworkSecretsStoreRegistration
{
    public static IServiceCollection AddGroundworkSecretsStore(
        this IServiceCollection services,
        string? targetName = null)
    {
        services.AddGroundworkStorageUnit(SecretsGroundworkStorageSchema.CreateUnit(), targetName);
        services.RemoveAll<ISecretRepository>();
        services.AddScoped<ISecretRepository>(provider => new GroundworkSecretRepository(
            provider.GetRequiredService<IGroundworkStorageSessionSource>(),
            targetName));
        return services;
    }
}
