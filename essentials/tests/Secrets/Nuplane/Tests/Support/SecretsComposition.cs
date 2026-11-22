using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Secrets.Nuplane.Tests.Support;

/// <summary>
/// The Secrets read path as a shell composes it — <c>AddSecrets</c>, its in-memory repository and its
/// encrypted store — plus the two operations every test here performs against it.
/// </summary>
internal static class SecretsComposition
{
    /// <summary>The tenant a feed credential is read under when nothing configures one.</summary>
    internal const string HostTenantId = SecretsFeedCredentialProvider.DefaultTenantId;

    private const string EncryptionKey = "secrets-nuplane-tests-only-key";

    /// <summary>
    /// Composes what the <c>Secrets</c> shell feature composes. Deliberately the real registration rather
    /// than a fake resolver: the provider's contract is "whatever the module says", and a fake would let a
    /// lifecycle refusal the module actually reports pass as a value.
    /// </summary>
    internal static IServiceCollection AddSecretsReadPath(this IServiceCollection services)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Elsa:Secrets:EncryptionKey"] = EncryptionKey })
            .Build();
        return services.AddSingleton<IConfiguration>(configuration).AddSecrets(configuration);
    }

    /// <summary>Stores one active text secret, the way an operator creating a feed token does.</summary>
    internal static async Task StoreAsync(this IServiceProvider services, string name, string value, string tenantId = HostTenantId)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISecretManager>().CreateAsync(tenantId, new()
        {
            Name = name,
            DisplayName = name,
            TypeName = SecretTypeNames.Text,
            StoreName = SecretStoreNames.Encrypted,
            Value = value
        });
    }

    internal static async Task RevokeAsync(this IServiceProvider services, string name, string tenantId = HostTenantId)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISecretManager>().RevokeAsync(tenantId, name);
    }
}
