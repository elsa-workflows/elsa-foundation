using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Core.Contracts;

public interface ISecretTypeRegistry
{
    IReadOnlyCollection<SecretTypeDescriptor> List();
    /// <summary>Returns the registered type provider with this name.</summary>
    /// <exception cref="Exceptions.SecretStoreConfigurationException">No type with this name is registered.</exception>
    ISecretTypeProvider Get(string name);
    bool TryGet(string name, out ISecretTypeProvider? provider);
}
