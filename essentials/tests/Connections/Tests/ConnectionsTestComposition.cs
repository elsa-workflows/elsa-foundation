using Elsa.Connections.Extensions;
using Elsa.Connections.Workflows.Extensions;
using Elsa.Secrets.Extensions;
using Elsa.Secrets.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Connections.Tests;

internal static class ConnectionsTestComposition
{
    public const string EncryptionKey = "connections-tests-encryption-key";

    public static ServiceProvider CreateProvider(
        IConfiguration? configuration = null,
        bool workflows = false,
        Action<IServiceCollection>? configure = null)
    {
        var config = configuration ?? new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Elsa:Secrets:EncryptionKey"] = EncryptionKey
            })
            .Build();

        if (configuration is not null && string.IsNullOrEmpty(configuration["Elsa:Secrets:EncryptionKey"]))
        {
            config = new ConfigurationBuilder()
                .AddConfiguration(configuration)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Elsa:Secrets:EncryptionKey"] = EncryptionKey
                })
                .Build();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSecrets(config);
        services.Configure<SecretsOptions>(options => options.EncryptionKey ??= EncryptionKey);
        Elsa.Connections.Extensions.ConnectionsServiceCollectionExtensions.AddConnections(services, config);
        if (workflows)
            services.AddConnectionsWorkflows();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
