using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Core.Contracts;

/// <summary>Lists the host's immutable snapshot of contributed secret scope suggestions.</summary>
public interface ISecretScopeRegistry
{
    IReadOnlyCollection<SecretScopeDescriptor> List();
}
