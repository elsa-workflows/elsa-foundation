using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Microsoft.Extensions.Configuration;

namespace Elsa.Secrets.Stores;

public sealed class ConfigurationSecretStore(IConfiguration configuration) : SecretStoreBase
{
    public const string ConfigurationKeyMetadataName = "configurationKey";

    protected override string TestFailureCode => "not-found";
    protected override string TestFailureMessage => "Configured secret value could not be found.";

    public override SecretStoreDescriptor Descriptor { get; } = new(
        SecretStoreNames.Configuration,
        "Configuration",
        "Resolves secret values from host configuration.",
        SecretStoreCapabilities.Read | SecretStoreCapabilities.Test,
        true);

    public override ValueTask<SecretPayload> WriteAsync(SecretWriteContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Payload.Metadata.TryGetValue(ConfigurationKeyMetadataName, out var configurationKey) || string.IsNullOrWhiteSpace(configurationKey))
            throw new InvalidOperationException("Configuration secrets require a configuration key.");

        return new(new SecretPayload
        {
            Metadata = new Dictionary<string, string>(context.Payload.Metadata, StringComparer.OrdinalIgnoreCase)
        });
    }

    public override ValueTask<SecretPayload?> ReadAsync(SecretReadContext context, CancellationToken cancellationToken = default)
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
}
