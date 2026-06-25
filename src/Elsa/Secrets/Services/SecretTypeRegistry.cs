using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Services;

public sealed class SecretTypeRegistry(IEnumerable<ISecretTypeProvider> providers) : ISecretTypeRegistry
{
    private readonly IReadOnlyDictionary<string, ISecretTypeProvider> _providers = providers.ToDictionary(x => x.Descriptor.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<SecretTypeDescriptor> List() => _providers.Values.Select(x => x.Descriptor).ToArray();

    public ISecretTypeProvider Get(string name)
    {
        if (TryGet(name, out var provider))
            return provider!;

        throw new InvalidOperationException("Secret type not found.");
    }

    public bool TryGet(string name, out ISecretTypeProvider? provider) => _providers.TryGetValue(name, out provider);
}
