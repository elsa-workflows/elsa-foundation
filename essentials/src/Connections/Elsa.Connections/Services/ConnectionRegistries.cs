using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Services;

internal sealed class ConnectionTypeRegistry : IConnectionTypeRegistry
{
    private readonly IReadOnlyList<IConnectionTypeProvider> _providers;

    public ConnectionTypeRegistry(IEnumerable<IConnectionTypeProvider> providers)
    {
        var list = providers.ToArray();
        var duplicates = list
            .GroupBy(provider => provider.Descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
            throw new InvalidOperationException($"Duplicate connection type names: {string.Join(", ", duplicates)}.");
        _providers = list;
    }

    public IConnectionTypeProvider? Find(string name) =>
        _providers.FirstOrDefault(provider => string.Equals(provider.Descriptor.Name, name, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<IConnectionTypeProvider> List() => _providers;
}

internal sealed class AuthenticationSchemeRegistry : IAuthenticationSchemeRegistry
{
    private readonly IReadOnlyList<IAuthenticationSchemeProvider> _providers;

    public AuthenticationSchemeRegistry(IEnumerable<IAuthenticationSchemeProvider> providers)
    {
        var list = providers.ToArray();
        var duplicates = list
            .GroupBy(provider => provider.Descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
            throw new InvalidOperationException($"Duplicate authentication scheme names: {string.Join(", ", duplicates)}.");
        _providers = list;
    }

    public IAuthenticationSchemeProvider? Find(string name) =>
        _providers.FirstOrDefault(provider => string.Equals(provider.Descriptor.Name, name, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<IAuthenticationSchemeProvider> List() => _providers;
}

internal sealed class ConnectionCredentialSourceRegistry : IConnectionCredentialSourceRegistry
{
    private readonly IReadOnlyDictionary<string, IConnectionCredentialSource> _sources;

    public ConnectionCredentialSourceRegistry(IEnumerable<IConnectionCredentialSource> sources)
    {
        var list = sources.ToArray();
        var duplicates = list
            .GroupBy(source => source.Kind, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
            throw new InvalidOperationException($"Duplicate connection credential source kinds: {string.Join(", ", duplicates)}.");
        _sources = list.ToDictionary(source => source.Kind, StringComparer.OrdinalIgnoreCase);
    }

    public IConnectionCredentialSource Get(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (_sources.TryGetValue(kind, out var source))
            return source;

        throw new InvalidOperationException($"No connection credential source is registered for kind '{kind}'.");
    }
}

internal sealed class AccessTokenSourceRegistry : IAccessTokenSourceRegistry
{
    private readonly IReadOnlyList<IAccessTokenSource> _sources;

    public AccessTokenSourceRegistry(IEnumerable<IAccessTokenSource> sources)
    {
        var list = sources.ToArray();
        var duplicates = list
            .GroupBy(source => source.Kind, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
            throw new InvalidOperationException($"Duplicate access token source kinds: {string.Join(", ", duplicates)}.");
        _sources = list;
    }

    public IAccessTokenSource? Find(string kind) =>
        _sources.FirstOrDefault(source => string.Equals(source.Kind, kind, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<IAccessTokenSource> List() => _sources;
}
