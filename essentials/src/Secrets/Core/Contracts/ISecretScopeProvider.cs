using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Core.Contracts;

/// <summary>Contributes static, advisory scope suggestions for secret metadata.</summary>
public interface ISecretScopeProvider
{
    IEnumerable<SecretScopeDescriptor> GetScopes();
}
