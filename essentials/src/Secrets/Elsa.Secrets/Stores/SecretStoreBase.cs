using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Stores;

/// <summary>
/// Provides the shared shape for <see cref="ISecretStore"/> implementations: a no-op delete and a
/// read-then-test template method whose failure code/message is supplied by the derived store.
/// </summary>
public abstract class SecretStoreBase : ISecretStore
{
    /// <summary>The failure code surfaced when <see cref="TestAsync"/> cannot resolve a value.</summary>
    protected abstract string TestFailureCode { get; }

    /// <summary>The failure message surfaced when <see cref="TestAsync"/> cannot resolve a value.</summary>
    protected abstract string TestFailureMessage { get; }

    public abstract SecretStoreDescriptor Descriptor { get; }

    public abstract ValueTask<SecretPayload> WriteAsync(SecretWriteContext context, CancellationToken cancellationToken = default);

    public abstract ValueTask<SecretPayload?> ReadAsync(SecretReadContext context, CancellationToken cancellationToken = default);

    public virtual ValueTask DeleteAsync(SecretDeleteContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public async ValueTask<SecretTestResult> TestAsync(SecretTestContext context, CancellationToken cancellationToken = default)
    {
        var payload = await ReadAsync(new SecretReadContext(context.Secret, context.Version), cancellationToken);
        return payload?.Value is null
            ? SecretTestResult.Failure(TestFailureCode, TestFailureMessage)
            : SecretTestResult.Success();
    }
}
