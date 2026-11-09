using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Core.Contracts;

/// <summary>
/// How a connection field's material is obtained. Enumerable by <see cref="Kind"/>; hosts replace or
/// add kinds (secret store, named credential, managed identity) without changing core.
/// </summary>
public interface IConnectionCredentialSource
{
    string Kind { get; }

    ValueTask<SensitiveValue> ResolveAsync(ConnectionCredentialRequest request, CancellationToken cancellationToken = default);
}
