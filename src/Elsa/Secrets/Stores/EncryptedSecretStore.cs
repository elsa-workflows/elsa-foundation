using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Stores;

public sealed class EncryptedSecretStore(ISecretValueProtector protector) : ISecretStore
{
    private const string ProtectedValueKey = "protectedValue";

    public SecretStoreDescriptor Descriptor { get; } = new(
        SecretStoreNames.Encrypted,
        "Encrypted",
        "Stores protected secret values in the configured Elsa secret persistence provider.",
        SecretStoreCapabilities.Read | SecretStoreCapabilities.Write | SecretStoreCapabilities.Delete | SecretStoreCapabilities.Test | SecretStoreCapabilities.Versioned,
        false);

    public ValueTask<SecretPayload> WriteAsync(SecretWriteContext context, CancellationToken cancellationToken = default)
    {
        if (context.Payload.Value is null)
            throw new InvalidOperationException("Encrypted secrets require a value.");

        var metadata = new Dictionary<string, string>(context.Payload.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            [ProtectedValueKey] = protector.Protect(context.Payload.Value)
        };

        return new(new SecretPayload { Metadata = metadata });
    }

    public ValueTask<SecretPayload?> ReadAsync(SecretReadContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Version.Payload.Metadata.TryGetValue(ProtectedValueKey, out var protectedValue))
            return new((SecretPayload?)null);

        return new(new SecretPayload
        {
            Value = protector.Unprotect(protectedValue),
            Metadata = new Dictionary<string, string>(context.Version.Payload.Metadata, StringComparer.OrdinalIgnoreCase)
        });
    }

    public ValueTask DeleteAsync(SecretDeleteContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public async ValueTask<SecretTestResult> TestAsync(SecretTestContext context, CancellationToken cancellationToken = default)
    {
        var payload = await ReadAsync(new SecretReadContext(context.Secret, context.Version), cancellationToken);
        return payload?.Value is null
            ? SecretTestResult.Failure("unavailable", "Secret value could not be decrypted.")
            : SecretTestResult.Success();
    }
}
