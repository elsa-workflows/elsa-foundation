using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Core.Contracts;

public interface ISecretTypeRegistry
{
    IReadOnlyCollection<SecretTypeDescriptor> List();
    ISecretTypeProvider Get(string name);
    bool TryGet(string name, out ISecretTypeProvider? provider);
}
