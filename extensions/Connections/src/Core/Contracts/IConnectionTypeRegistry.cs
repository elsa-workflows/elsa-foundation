using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Core.Contracts;

public interface IConnectionTypeRegistry
{
    IConnectionTypeProvider? Find(string name);

    IReadOnlyList<IConnectionTypeProvider> List();
}

public interface IAuthenticationSchemeRegistry
{
    IAuthenticationSchemeProvider? Find(string name);

    IReadOnlyList<IAuthenticationSchemeProvider> List();
}

public interface IConnectionCredentialSourceRegistry
{
    IConnectionCredentialSource Get(string kind);
}
