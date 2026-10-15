using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Services;

public sealed class ConnectionTypeRegistry(IEnumerable<IConnectionTypeProvider> providers) : IConnectionTypeRegistry
{
    private readonly IReadOnlyList<IConnectionTypeProvider> _providers = providers.ToArray();

    public IConnectionTypeProvider? Find(string name) =>
        _providers.FirstOrDefault(provider => string.Equals(provider.Descriptor.Name, name, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<IConnectionTypeProvider> List() => _providers;
}

public sealed class AuthenticationSchemeRegistry(IEnumerable<IAuthenticationSchemeProvider> providers) : IAuthenticationSchemeRegistry
{
    private readonly IReadOnlyList<IAuthenticationSchemeProvider> _providers = providers.ToArray();

    public IAuthenticationSchemeProvider? Find(string name) =>
        _providers.FirstOrDefault(provider => string.Equals(provider.Descriptor.Name, name, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<IAuthenticationSchemeProvider> List() => _providers;
}

public sealed class ConnectionCredentialSourceRegistry(IEnumerable<IConnectionCredentialSource> sources) : IConnectionCredentialSourceRegistry
{
    private readonly IReadOnlyDictionary<string, IConnectionCredentialSource> _sources = sources
        .ToDictionary(source => source.Kind, StringComparer.OrdinalIgnoreCase);

    public IConnectionCredentialSource Get(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (_sources.TryGetValue(kind, out var source))
            return source;

        throw new InvalidOperationException($"No connection credential source is registered for kind '{kind}'.");
    }
}

public sealed class AccessTokenSourceRegistry(IEnumerable<IAccessTokenSource> sources) : IAccessTokenSourceRegistry
{
    private readonly IReadOnlyList<IAccessTokenSource> _sources = sources.ToArray();

    public IAccessTokenSource? Find(string kind) =>
        _sources.FirstOrDefault(source => string.Equals(source.Kind, kind, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<IAccessTokenSource> List() => _sources;
}
