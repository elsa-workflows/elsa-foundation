using Elsa.Secrets.Core.Contracts;

namespace Elsa.Secrets.Services;

public sealed class SecretStoreRegistry(IEnumerable<ISecretStore> stores) : ISecretStoreRegistry
{
    private readonly IReadOnlyDictionary<string, ISecretStore> _stores = stores.ToDictionary(x => x.Descriptor.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ISecretStore> List() => _stores.Values.ToArray();

    public ISecretStore Get(string name)
    {
        if (TryGet(name, out var store))
            return store!;

        throw new InvalidOperationException("Secret store not found.");
    }

    public bool TryGet(string name, out ISecretStore? store) => _stores.TryGetValue(name, out store);
}
