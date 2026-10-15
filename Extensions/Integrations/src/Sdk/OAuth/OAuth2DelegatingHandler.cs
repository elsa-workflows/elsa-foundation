using System.Net;
using System.Net.Http.Headers;
using Elsa.Integrations.Sdk.Connections;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Integrations.Sdk.OAuth;

/// <summary>
/// Added to the named <c>Elsa.Activities.Http</c> client. Injects a short-lived token resolved
/// through <c>ISecretValueResolver</c> and refreshes on 401 or expiry with compare-and-swap.
/// </summary>
internal sealed class OAuth2DelegatingHandler(
    IActiveConnectionAccessor connections,
    OAuth2TokenService tokens,
    IRuntimeSecretMask? mask,
    string? activityExecutionId) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var connection = connections.Current;
        EnsureAllowedHost(request, connection);

        if (request.Options.TryGetValue(OAuth2RequestFlags.SkipTokenInjection, out var skip) && skip)
            return await base.SendAsync(request, cancellationToken);

        await AttachTokenAsync(request, connection, forceRefresh: false, cancellationToken);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        response.Dispose();
        await AttachTokenAsync(request, connection, forceRefresh: true, cancellationToken);
        return await base.SendAsync(request, cancellationToken);
    }

    private async Task AttachTokenAsync(
        HttpRequestMessage request,
        ConnectionRecord connection,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var grant = forceRefresh
            ? await tokens.RefreshAsync(connection, await tokens.ReadGrantAsync(connection, mask, activityExecutionId, cancellationToken), mask, activityExecutionId, cancellationToken)
            : await tokens.GetValidGrantAsync(connection, mask, activityExecutionId, cancellationToken);

        request.Headers.Remove("Authorization");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            string.IsNullOrWhiteSpace(grant.TokenType) ? "Bearer" : grant.TokenType,
            grant.AccessToken);
    }

    private static void EnsureAllowedHost(HttpRequestMessage request, ConnectionRecord connection)
    {
        var host = request.RequestUri?.IdnHost;
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("An OAuth2 request needs an absolute URL.");

        if (request.Options.TryGetValue(OAuth2RequestFlags.SkipTokenInjection, out var skip) && skip)
            return;

        var allowlist = ConnectionContext.BuildAllowlist(connection);
        if (allowlist.Count == 0)
            return;
        if (!allowlist.Any(allowed => HostMatches(host, allowed)))
            throw new InvalidOperationException($"Host '{host}' is not on the connection egress allowlist.");
    }

    private static bool HostMatches(string host, string allowed)
    {
        if (string.Equals(allowed, "*", StringComparison.Ordinal))
            return true;
        if (allowed.StartsWith("*.", StringComparison.Ordinal))
            return host.EndsWith(allowed[1..], StringComparison.OrdinalIgnoreCase);
        return string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase);
    }
}
