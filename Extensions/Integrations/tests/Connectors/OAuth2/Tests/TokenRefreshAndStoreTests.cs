using Elsa.Integrations.Sdk.Harness;
using Elsa.Integrations.Sdk.OAuth;
using Elsa.Secrets.Core.Models;

namespace Elsa.Integrations.Connectors.OAuth2.Tests;

public sealed class TokenRefreshAndStoreTests
{
    [Fact]
    public async Task Refresh_on_401_uses_compare_and_swap()
    {
        var preview = new MockOAuthServer();
        var (harness, server) = await OAuth2TestSupport.StartAsync(
            OAuth2TestSupport.AuthorizationCodeScheme(preview),
            configureServer: mock => mock.UnauthorizedOnce = true);
        await using var _ = harness;

        await AuthorizeAsync(harness, server);
        var before = await harness.Secrets.GetVersionAsync(OAuth2TestSupport.TenantId, OAuth2SecretKeys.Grant(OAuth2TestSupport.ConnectionName));

        var completion = await OAuth2TestSupport.SendAsync(harness, server.ResourceEndpoint);

        Assert.Equal(200, completion.Result.StatusCode);
        var after = await harness.Secrets.GetVersionAsync(OAuth2TestSupport.TenantId, OAuth2SecretKeys.Grant(OAuth2TestSupport.ConnectionName));
        Assert.True(after > before, "A 401 refresh must write a new grant version.");
    }

    [Fact]
    public async Task Refresh_on_expiry_writes_a_new_grant_version()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var preview = new MockOAuthServer();
        var (harness, server) = await OAuth2TestSupport.StartAsync(
            OAuth2TestSupport.ClientCredentialsScheme(preview),
            configureServer: mock => mock.AccessTokenLifetimeSeconds = 90,
            configureHarness: options => options.TimeProvider = clock);
        await using var _ = harness;

        var first = await OAuth2TestSupport.SendAsync(harness, server.ResourceEndpoint);
        Assert.Equal(200, first.Result.StatusCode);
        var before = await harness.Secrets.GetVersionAsync(OAuth2TestSupport.TenantId, OAuth2SecretKeys.Grant(OAuth2TestSupport.ConnectionName));

        clock.Advance(TimeSpan.FromMinutes(2));
        var second = await OAuth2TestSupport.SendAsync(harness, server.ResourceEndpoint);
        Assert.Equal(200, second.Result.StatusCode);
        var after = await harness.Secrets.GetVersionAsync(OAuth2TestSupport.TenantId, OAuth2SecretKeys.Grant(OAuth2TestSupport.ConnectionName));
        Assert.True(after > before);
    }

    [Fact]
    public void Read_only_store_is_refused_for_grants()
    {
        var scheme = new OAuth2AuthenticationScheme
        {
            GrantType = OAuth2GrantTypes.ClientCredentials,
            TokenUrl = "https://oauth.test/token"
        };
        var error = Assert.Throws<InvalidOperationException>(() =>
            scheme.EnsureStoreCanHoldGrants(FakeSecretBackend.ReadOnlyConfigurationStore));
        Assert.Contains("RequiresWritableStore", error.Message, StringComparison.Ordinal);
        Assert.Contains(SecretStoreNames.Configuration, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lost_compare_and_swap_keeps_the_winning_grant()
    {
        var preview = new MockOAuthServer();
        var (harness, _) = await OAuth2TestSupport.StartAsync(OAuth2TestSupport.ClientCredentialsScheme(preview));
        await using var _ = harness;

        var name = OAuth2SecretKeys.Grant(OAuth2TestSupport.ConnectionName);
        Assert.True(await harness.Secrets.CompareAndSwapAsync(OAuth2TestSupport.TenantId, name, 0, """{"accessToken":"first","version":1}"""));
        Assert.False(await harness.Secrets.CompareAndSwapAsync(OAuth2TestSupport.TenantId, name, 0, """{"accessToken":"loser","version":1}"""));
        var resolved = await harness.Secrets.ResolveAsync(OAuth2TestSupport.TenantId, new Elsa.Secrets.Core.Models.SecretReference(name));
        Assert.Contains("first", resolved.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("loser", resolved.Value, StringComparison.Ordinal);
    }

    private static async Task AuthorizeAsync(ConnectorHarness harness, MockOAuthServer server)
    {
        var started = await harness.Tokens.BeginAuthorizationCodeAsync(OAuth2TestSupport.BoundConnection(harness), harness.Callback);
        var code = server.ApproveAuthorization(new Uri(started.AuthorizationUrl));
        var result = await harness.CallbackHandler.HandleAsync(OAuth2TestSupport.BoundConnection(harness), code, started.State);
        Assert.True(result.Ok, result.Error);
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _utc = start;
        public void Advance(TimeSpan delta) => _utc += delta;
        public override DateTimeOffset GetUtcNow() => _utc;
    }
}
