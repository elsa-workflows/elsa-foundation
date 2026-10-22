namespace Elsa.Secrets.Core.Contracts;

public interface ISecretStoreRegistry
{
    IReadOnlyCollection<ISecretStore> List();
    /// <summary>Returns the registered store with this name.</summary>
    /// <exception cref="Exceptions.SecretStoreConfigurationException">No store with this name is registered.</exception>
    ISecretStore Get(string name);
    bool TryGet(string name, out ISecretStore? store);
}
