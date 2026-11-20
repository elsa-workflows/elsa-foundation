using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Core.Contracts;

public interface ISecretStore
{
    SecretStoreDescriptor Descriptor { get; }
    ValueTask<SecretPayload> WriteAsync(SecretWriteContext context, CancellationToken cancellationToken = default);
    ValueTask<SecretPayload?> ReadAsync(SecretReadContext context, CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(SecretDeleteContext context, CancellationToken cancellationToken = default);
    ValueTask<SecretTestResult> TestAsync(SecretTestContext context, CancellationToken cancellationToken = default);
}

public sealed record SecretWriteContext(Secret Secret, SecretVersion Version, SecretPayload Payload);

public sealed record SecretReadContext(Secret Secret, SecretVersion Version);

public sealed record SecretDeleteContext(Secret Secret);

public sealed record SecretTestContext(Secret Secret, SecretVersion Version);
