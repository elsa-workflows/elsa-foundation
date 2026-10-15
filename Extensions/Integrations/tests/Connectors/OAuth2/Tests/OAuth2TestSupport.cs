using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Http.Options;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Connectors.OAuth2;
using Elsa.Integrations.Sdk.Connections;
using Elsa.Integrations.Sdk.Harness;
using Elsa.Integrations.Sdk.OAuth;
using Elsa.Secrets.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Integrations.Connectors.OAuth2.Tests;

internal static class OAuth2TestSupport
{
    public const string TenantId = "tenant-a";
    public const string ConnectionName = "http-oauth";
    public const string ActivityExecutionId = "actexec-oauth2";

    public static async Task<(ConnectorHarness Harness, MockOAuthServer Server)> StartAsync(
        OAuth2AuthenticationScheme scheme,
        string clientSource = OAuth2ClientSources.Valence,
        Action<MockOAuthServer>? configureServer = null,
        Action<ConnectorHarnessOptions>? configureHarness = null)
    {
        var server = new MockOAuthServer();
        configureServer?.Invoke(server);

        var callback = new OAuthCallbackOptions { PublicBaseUrl = "https://app.example" };
        var harness = ConnectorHarness.Create(options =>
        {
            options.TenantId = TenantId;
            options.PrimaryHandler = server;
            options.OwnsPrimaryHandler = true;
            options.Callback = callback;
            configureHarness?.Invoke(options);
        });

        if (clientSource == OAuth2ClientSources.Valence)
            harness.ValenceApps.Register(new ValenceOAuthApp(scheme.ValenceAppId ?? "elsa.oauth2", server.ExpectedClientId, server.ExpectedClientSecret));
        else
            await harness.Secrets.CompareAndSwapAsync(TenantId, OAuth2SecretKeys.ClientSecret(ConnectionName), 0, server.ExpectedClientSecret);

        var connection = new ConnectionRecord
        {
            TenantId = TenantId,
            Name = ConnectionName,
            Type = "http",
            AuthScheme = OAuth2AuthenticationScheme.Name,
            ClientSource = clientSource,
            Settings = scheme.ToConnectionSettings()
        };
        await harness.UseConnectionAsync(connection);
        return (harness, server);
    }

    public static OAuth2AuthenticationScheme ClientCredentialsScheme(MockOAuthServer server) => new()
    {
        GrantType = OAuth2GrantTypes.ClientCredentials,
        TokenUrl = server.TokenEndpoint.ToString(),
        ScopesUrl = server.ScopesEndpoint.ToString(),
        Scopes = "read",
        ClientSource = OAuth2ClientSources.Valence,
        ValenceAppId = "elsa.oauth2"
    };

    public static OAuth2AuthenticationScheme AuthorizationCodeScheme(MockOAuthServer server) => new()
    {
        GrantType = OAuth2GrantTypes.AuthorizationCode,
        AuthorizationUrl = server.AuthorizationEndpoint.ToString(),
        TokenUrl = server.TokenEndpoint.ToString(),
        ScopesUrl = server.ScopesEndpoint.ToString(),
        Scopes = "read write",
        ClientSource = OAuth2ClientSources.Valence,
        ValenceAppId = "elsa.oauth2"
    };

    public static async Task<IActivityCompletionTransition<SendHttpRequestResult>> SendAsync(ConnectorHarness harness, Uri url)
    {
        var factory = harness.Services.GetRequiredService<IHttpClientFactory>();
        var activity = new SendHttpRequest(factory, Options.Create(new HttpActivityOptions()))
        {
            Url = url,
            Method = "GET"
        };
        var context = new ActivityExecutionContext("wf-1", "inv-1", "att-1", "node-1", CancellationToken.None);
        var transition = await ((IActivity)activity).ExecuteAsync(context);
        return Assert.IsAssignableFrom<IActivityCompletionTransition<SendHttpRequestResult>>(transition);
    }

    public static ConnectionRecord BoundConnection(ConnectorHarness harness) =>
        harness.Auth.ActiveConnection ?? throw new InvalidOperationException("No connection.");
}
