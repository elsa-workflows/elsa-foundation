using System.Collections.Concurrent;
using Elsa.Integrations.Sdk.OAuth;

namespace Elsa.Integrations.Sdk.Harness;

internal sealed class InMemoryValenceOAuthAppRegistry : IValenceOAuthAppRegistry
{
    private readonly ConcurrentDictionary<string, ValenceOAuthApp> _apps = new(StringComparer.OrdinalIgnoreCase);

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
