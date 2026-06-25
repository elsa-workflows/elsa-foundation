using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Microsoft.Extensions.Configuration;

namespace Elsa.Secrets.Stores;

public sealed class ConfigurationSecretStore(IConfiguration configuration) : ISecretStore
{
    public const string ConfigurationKeyMetadataName = "configurationKey";

    public SecretStoreDescriptor Descriptor { get; } = new(
        SecretStoreNames.Configuration,
        "Configuration",
        "Resolves secret values from host configuration.",
        SecretStoreCapabilities.Read | SecretStoreCapabilities.Test,
        true);

    public ValueTask<SecretPayload> WriteAsync(SecretWriteContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Payload.Metadata.TryGetValue(ConfigurationKeyMetadataName, out var configurationKey) || string.IsNullOrWhiteSpace(configurationKey))
            throw new InvalidOperationException("Configuration secrets require a configuration key.");

        return new(new SecretPayload
        {
            Metadata = new Dictionary<string, string>(context.Payload.Metadata, StringComparer.OrdinalIgnoreCase)
        });
    }

    public ValueTask<SecretPayload?> ReadAsync(SecretReadContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Version.Payload.Metadata.TryGetValue(ConfigurationKeyMetadataName, out var configurationKey) || string.IsNullOrWhiteSpace(configurationKey))
            return new((SecretPayload?)null);

        var value = configuration[configurationKey];

        return new(value is null
            ? null
            : new SecretPayload
            {
                Value = value,
                Metadata = new Dictionary<string, string>(context.Version.Payload.Metadata, StringComparer.OrdinalIgnoreCase)
            });
    }

    public ValueTask DeleteAsync(SecretDeleteContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public async ValueTask<SecretTestResult> TestAsync(SecretTestContext context, CancellationToken cancellationToken = default)
    {
        var payload = await ReadAsync(new SecretReadContext(context.Secret, context.Version), cancellationToken);
        return payload?.Value is null
            ? SecretTestResult.Failure("not-found", "Configured secret value could not be found.")
            : SecretTestResult.Success();
    }
}
