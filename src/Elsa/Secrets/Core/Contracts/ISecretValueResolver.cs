using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Core.Contracts;

/// <summary>
/// The runtime value-resolution boundary: resolves a <see cref="SecretReference"/> into a value at
/// point of use. Deliberately separate from <see cref="ISecretManager"/> (the CRUD/lifecycle facade)
/// so hosts can override resolution and lifecycle independently (the W18 store-vs-resolver split).
/// </summary>
public interface ISecretValueResolver
{
    ValueTask<ResolvedSecret> ResolveAsync(SecretReference reference, CancellationToken cancellationToken = default);
}
