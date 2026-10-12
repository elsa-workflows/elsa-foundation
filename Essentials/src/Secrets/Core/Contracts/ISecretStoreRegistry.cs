namespace Elsa.Secrets.Core.Contracts;

public interface ISecretStoreRegistry
{
    IReadOnlyCollection<ISecretStore> List();
    ISecretStore Get(string name);
    bool TryGet(string name, out ISecretStore? store);
}
