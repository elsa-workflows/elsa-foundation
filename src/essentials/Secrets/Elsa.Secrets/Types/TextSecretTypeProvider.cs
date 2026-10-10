using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Stores;

namespace Elsa.Secrets.Types;

public sealed class TextSecretTypeProvider : ISecretTypeProvider
{
    public SecretTypeDescriptor Descriptor { get; } = new(
        SecretTypeNames.Text,
        "Text",
        "Plain text secret value.",
        "text",
        [SecretStoreNames.Encrypted, SecretStoreNames.Configuration]);

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
