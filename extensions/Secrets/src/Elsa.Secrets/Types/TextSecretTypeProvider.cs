using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Core.Options;
using Elsa.Secrets.Options;
using Elsa.Secrets.Stores;
using Microsoft.Extensions.Options;

namespace Elsa.Secrets.Types;

public sealed class TextSecretTypeProvider : ISecretTypeProvider
{
    public TextSecretTypeProvider()
        : this(Microsoft.Extensions.Options.Options.Create(new SecretsOptions()))
    {
    }

    public TextSecretTypeProvider(IOptions<SecretsOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SecretStoreNames.Encrypted,
            SecretStoreNames.Configuration,
            DefaultWritableSecretStore.ResolveName(options.Value.DefaultWritableStoreName)
        };
        Descriptor = new(
            SecretTypeNames.Text,
            "Text",
            "Plain text secret value.",
            "text",
            names);
    }

    public SecretTypeDescriptor Descriptor { get; }

    public ValueTask<SecretValidationResult> ValidateCreateAsync(CreateSecretRequest request, CancellationToken cancellationToken = default)
        => new(Validate(request.StoreName, request.Value, request.ConfigurationKey, request.Metadata));

    public ValueTask<SecretValidationResult> ValidateRotateAsync(RotateSecretRequest request, string storeName, CancellationToken cancellationToken = default)
        => new(Validate(storeName, request.Value, request.ConfigurationKey, request.Metadata));

    private static SecretValidationResult Validate(string storeName, string? value, string? configurationKey, IDictionary<string, string> metadata)
    {
        if (storeName.Equals(SecretStoreNames.Configuration, StringComparison.OrdinalIgnoreCase))
        {
            var key = configurationKey;
            if (string.IsNullOrWhiteSpace(key) && metadata.TryGetValue(ConfigurationSecretStore.ConfigurationKeyMetadataName, out var metadataKey))
                key = metadataKey;

            return string.IsNullOrWhiteSpace(key)
                ? SecretValidationResult.Failure("Configuration secrets require a configuration key.")
                : SecretValidationResult.Success();
        }

        return value is null
            ? SecretValidationResult.Failure("Encrypted secrets require a value.")
            : SecretValidationResult.Success();
    }
}
