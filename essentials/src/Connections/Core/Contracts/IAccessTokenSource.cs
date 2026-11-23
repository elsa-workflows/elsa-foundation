using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Core.Contracts;

/// <summary>
/// BCL-typed named-credential contract. Kinds are pluggable: configuration, a host override, or a
/// cloud-vendor package. No Azure or other vendor type appears here.
/// </summary>
public interface IAccessTokenSource
{
    string Kind { get; }

    ValueTask<AccessToken> GetTokenAsync(AccessTokenRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Looks up a named credential by the kind declared under <c>Elsa:Credentials:&lt;name&gt;</c>.</summary>
public interface IAccessTokenSourceRegistry
{
    IAccessTokenSource? Find(string kind);

    IReadOnlyList<IAccessTokenSource> List();
}
