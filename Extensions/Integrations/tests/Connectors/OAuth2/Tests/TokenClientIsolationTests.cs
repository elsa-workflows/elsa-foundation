using Elsa.Activities.Http.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Integrations.Connectors.OAuth2.Tests;

public sealed class TokenClientIsolationTests
{
    private const string ActivityMarkerHeader = "X-Activity-Http";
    private const string ActivityMarkerValue = "1";
    private const string PlantedAuthorization = "Bearer activity-pipeline-token";

    [Fact]
    public async Task Client_credentials_token_exchange_bypasses_activity_http_handlers()
    {
        var activity = new List<ObservedRequest>();
        var token = new List<ObservedRequest>();
        var preview = new MockOAuthServer();
        await using var harness = await OAuth2TestSupport.StartAsync(
            OAuth2TestSupport.ClientCredentialsScheme(preview),
            configureServices: Observe(activity, token));

        var tokens = harness.Services.GetRequiredService<OAuth2TokenService>();
        await using var connection = await OAuth2TestSupport.ResolveAsync(harness);
        await tokens.GetValidGrantAsync(connection);

        AssertIsolatedTokenExchange(harness, activity, token, OAuth2GrantTypes.ClientCredentials);
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal) { "grant_type", "client_id", "client_secret", "scope" },
            harness.Server.LastTokenForm.Keys.ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Authorization_code_token_exchange_bypasses_activity_http_handlers()
    {
        var activity = new List<ObservedRequest>();
        var token = new List<ObservedRequest>();
        var preview = new MockOAuthServer();
        await using var harness = await OAuth2TestSupport.StartAsync(
            OAuth2TestSupport.AuthorizationCodeScheme(preview),
            configureServices: Observe(activity, token));

        await OAuth2TestSupport.AuthorizeAsync(harness, harness.Server);

        AssertIsolatedTokenExchange(harness, activity, token, OAuth2GrantTypes.AuthorizationCode);
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "grant_type", "code", "client_id", "code_verifier", "redirect_uri", "client_secret"
            },
            harness.Server.LastTokenForm.Keys.ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Refresh_token_exchange_bypasses_activity_http_handlers()
    {
        var activity = new List<ObservedRequest>();
        var token = new List<ObservedRequest>();
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var preview = new MockOAuthServer();
        await using var harness = await OAuth2TestSupport.StartAsync(
            OAuth2TestSupport.AuthorizationCodeScheme(preview),
            configureServer: mock => mock.AccessTokenLifetimeSeconds = 90,
            timeProvider: clock,
            configureServices: Observe(activity, token));

        await OAuth2TestSupport.AuthorizeAsync(harness, harness.Server);
        activity.Clear();
        token.Clear();
        clock.Advance(TimeSpan.FromMinutes(2));

        var tokens = harness.Services.GetRequiredService<OAuth2TokenService>();
        await using var connection = await OAuth2TestSupport.ResolveAsync(harness);
        await tokens.GetValidGrantAsync(connection);

        AssertIsolatedTokenExchange(harness, activity, token, OAuth2GrantTypes.RefreshToken);
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal) { "grant_type", "refresh_token", "client_id", "client_secret" },
            harness.Server.LastTokenForm.Keys.ToHashSet(StringComparer.Ordinal));
    }

    private static Action<IServiceCollection> Observe(List<ObservedRequest> activity, List<ObservedRequest> token) =>
        services =>
        {
            services.AddHttpClient(HttpActivityConstants.HttpClientName)
                .AddHttpMessageHandler(() => new ObservingHandler(activity, request =>
                {
                    request.Headers.TryAddWithoutValidation(ActivityMarkerHeader, ActivityMarkerValue);
                    request.Headers.TryAddWithoutValidation("Authorization", PlantedAuthorization);
                }));
            services.AddHttpClient(OAuth2HttpClientNames.HttpClientName)
                .AddHttpMessageHandler(() => new ObservingHandler(token));
        };

    private static void AssertIsolatedTokenExchange(
        OAuth2Harness harness,
        List<ObservedRequest> activity,
        List<ObservedRequest> token,
        string grantType)
    {
        Assert.DoesNotContain(activity, request => request.IsTokenEndpoint);
        Assert.Contains(token, request => request.IsTokenEndpoint);
        Assert.Null(harness.Server.LastTokenAuthorizationHeader);
        Assert.DoesNotContain(ActivityMarkerHeader, harness.Server.LastTokenHeaderNames, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(grantType, harness.Server.LastTokenForm["grant_type"]);
        Assert.Equal(harness.Server.ExpectedClientId, harness.Server.LastTokenForm["client_id"]);
        Assert.Equal(harness.Server.ExpectedClientSecret, harness.Server.LastTokenForm["client_secret"]);
        Assert.All(token.Where(request => request.IsTokenEndpoint), request =>
        {
            Assert.Null(request.Authorization);
            Assert.False(request.Headers.ContainsKey(ActivityMarkerHeader));
        });
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _utc = start;
        public void Advance(TimeSpan delta) => _utc += delta;
        public override DateTimeOffset GetUtcNow() => _utc;
    }
}

internal sealed class ObservingHandler(List<ObservedRequest> sink, Action<HttpRequestMessage>? mutate = null)
    : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        mutate?.Invoke(request);
        sink.Add(ObservedRequest.Capture(request));
        return base.SendAsync(request, cancellationToken);
    }
}

internal sealed record ObservedRequest(
    Uri? Uri,
    string? Authorization,
    IReadOnlyDictionary<string, string[]> Headers)
{
    public bool IsTokenEndpoint =>
        Uri is not null && string.Equals(Uri.AbsolutePath.TrimEnd('/'), "/token", StringComparison.OrdinalIgnoreCase);

    public static ObservedRequest Capture(HttpRequestMessage request)
    {
        var headers = request.Headers.ToDictionary(
            header => header.Key,
            header => header.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
        return new ObservedRequest(request.RequestUri, request.Headers.Authorization?.ToString(), headers);
    }
}
