using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Integrations.Sdk.Harness;

/// <summary>
/// Fake <see cref="ISecretValueResolver"/> for tests. Each resolve is counted so callers can prove
/// the SDK does not cache secret material (#2726 rule 5).
/// </summary>
public sealed class InMemorySecretValueResolver : ISecretValueResolver
{
    private readonly Dictionary<(string TenantId, string Name), string> _values = new();
    private int _resolveCount;

    public int ResolveCount => Volatile.Read(ref _resolveCount);

    public void Add(string tenantId, string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _values[(tenantId, name)] = value;
    }

    public ValueTask<ResolvedSecret> ResolveAsync(string tenantId, SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();

        Interlocked.Increment(ref _resolveCount);

        if (_values.TryGetValue((tenantId, reference.Name), out var value))
        {
            return ValueTask.FromResult(ResolvedSecret.Success(value, new SecretMetadata
            {
                TenantId = tenantId,
                Name = reference.Name,
                TypeName = reference.TypeName ?? SecretTypeNames.Text
            }));
        }

        return ValueTask.FromResult(ResolvedSecret.Failure(SecretResolutionFailureCode.NotFound, $"Secret '{reference.Name}' was not found."));
    }
}
