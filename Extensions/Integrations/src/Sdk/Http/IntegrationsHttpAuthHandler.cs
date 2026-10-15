namespace Elsa.Integrations.Sdk.Http;

/// <summary>
/// Applies <see cref="IntegrationsHttpAuthScope"/> to the named <c>Elsa.Activities.Http</c> client.
/// When no scope is active, the request is unchanged so <c>SendHttpRequest</c> keeps working.
/// </summary>
public sealed class IntegrationsHttpAuthHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        IntegrationsHttpAuthScope.CurrentApply?.Invoke(request);
        return base.SendAsync(request, cancellationToken);
    }
}
