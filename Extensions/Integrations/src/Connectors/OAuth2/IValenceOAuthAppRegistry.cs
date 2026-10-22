namespace Elsa.Integrations.Connectors.OAuth2;

public sealed record ValenceOAuthApp(string AppId, string ClientId, string? ClientSecret);

public interface IValenceOAuthAppRegistry
{
    ValueTask<ValenceOAuthApp?> GetAsync(string appId, CancellationToken cancellationToken = default);
}

internal sealed class InMemoryValenceOAuthAppRegistry : IValenceOAuthAppRegistry
{
    private readonly Dictionary<string, ValenceOAuthApp> _apps = new(StringComparer.Ordinal);

    public void Register(ValenceOAuthApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        _apps[app.AppId] = app;
    }

    public ValueTask<ValenceOAuthApp?> GetAsync(string appId, CancellationToken cancellationToken = default)
    {
        _apps.TryGetValue(appId, out var app);
        return ValueTask.FromResult(app);
    }
}
