using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Stores;

public sealed class EncryptedSecretStore(ISecretValueProtector protector) : SecretStoreBase
{
    private const string ProtectedValueKey = "protectedValue";

    protected override string TestFailureCode => "unavailable";
    protected override string TestFailureMessage => "Secret value could not be decrypted.";

    public override SecretStoreDescriptor Descriptor { get; } = new(
        SecretStoreNames.Encrypted,
        "Encrypted",
        "Stores protected secret values in the configured Elsa secret persistence provider.",
        SecretStoreCapabilities.Read | SecretStoreCapabilities.Write | SecretStoreCapabilities.Delete | SecretStoreCapabilities.Test | SecretStoreCapabilities.Versioned,
        false);

    public override ValueTask<SecretPayload> WriteAsync(SecretWriteContext context, CancellationToken cancellationToken = default)
    {
        if (context.Payload.Value is null)
            throw new InvalidOperationException("Encrypted secrets require a value.");

        var metadata = new Dictionary<string, string>(context.Payload.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            [ProtectedValueKey] = protector.Protect(context.Payload.Value)
        };

        return new(new SecretPayload { Metadata = metadata });
    }

    public override ValueTask<SecretPayload?> ReadAsync(SecretReadContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Version.Payload.Metadata.TryGetValue(ProtectedValueKey, out var protectedValue))
            return new((SecretPayload?)null);

        return new(new SecretPayload
        {
            Value = protector.Unprotect(protectedValue),
            Metadata = new Dictionary<string, string>(context.Version.Payload.Metadata, StringComparer.OrdinalIgnoreCase)
        });
    }
}
