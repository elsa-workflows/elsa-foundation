namespace Elsa.Integrations.Sdk.Connections;

/// <summary>
/// Connection auth on the named <c>Elsa.Activities.Http</c> client. When no handle is in
/// <see cref="ConnectionExecutionScope"/>, the request is unchanged so <c>SendHttpRequest</c> keeps
/// working without a service connection.
/// </summary>
public sealed class ConnectionAuthDelegatingHandler : DelegatingHandler
{
    private readonly IReadOnlyDictionary<string, IConnectionAuthenticator> _authenticators;

    public ConnectionAuthDelegatingHandler(IEnumerable<IConnectionAuthenticator> authenticators)
    {
        ArgumentNullException.ThrowIfNull(authenticators);
        _authenticators = authenticators.ToDictionary(
            authenticator => authenticator.Scheme,
            authenticator => authenticator,
            StringComparer.Ordinal);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var handle = ConnectionExecutionScope.CurrentHandle;
        if (handle is not null)
        {
            ConnectionEgress.EnsureHostAllowed(request, handle);

            if (!_authenticators.TryGetValue(handle.AuthScheme, out var authenticator))
            {
                throw new ConnectionUnavailableException(
                    $"Connection '{handle.ConnectionName}' uses scheme '{handle.AuthScheme}', which has no authenticator.",
                    handle.ConnectionName);
            }

            await authenticator.ApplyAsync(request, handle, cancellationToken);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
