using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Secrets.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Integrations.Connectors.OAuth2.Tests;

public sealed class TokenRefreshAndStoreTests
{
    [Fact]
    public async Task Refresh_on_401_uses_compare_and_swap()
    {
        var preview = new MockOAuthServer();
        await using var harness = await OAuth2TestSupport.StartAsync(
            OAuth2TestSupport.AuthorizationCodeScheme(preview),
            configureServer: mock => mock.UnauthorizedOnce = true);

        await OAuth2TestSupport.AuthorizeAsync(harness, harness.Server);
        var before = (await harness.Secrets.FindAsync(OAuth2TestSupport.TenantId, OAuth2SecretKeys.Grant(OAuth2TestSupport.ConnectionName)))?.CurrentVersion ?? 0;

        var completion = await OAuth2TestSupport.SendAsync(harness, harness.Server.ResourceEndpoint);

        Assert.Equal(200, completion.Result.StatusCode);
        var after = (await harness.Secrets.FindAsync(OAuth2TestSupport.TenantId, OAuth2SecretKeys.Grant(OAuth2TestSupport.ConnectionName)))?.CurrentVersion ?? 0;
        Assert.True(after > before, "A 401 refresh must write a new grant version.");
    }

    [Fact]
    public async Task Refresh_on_expiry_writes_a_new_grant_version()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var preview = new MockOAuthServer();
        await using var harness = await OAuth2TestSupport.StartAsync(
            OAuth2TestSupport.ClientCredentialsScheme(preview),
            configureServer: mock => mock.AccessTokenLifetimeSeconds = 90,
            timeProvider: clock);

        var first = await OAuth2TestSupport.SendAsync(harness, harness.Server.ResourceEndpoint);
        Assert.Equal(200, first.Result.StatusCode);
        var before = (await harness.Secrets.FindAsync(OAuth2TestSupport.TenantId, OAuth2SecretKeys.Grant(OAuth2TestSupport.ConnectionName)))?.CurrentVersion ?? 0;

        clock.Advance(TimeSpan.FromMinutes(2));
        var second = await OAuth2TestSupport.SendAsync(harness, harness.Server.ResourceEndpoint);
        Assert.Equal(200, second.Result.StatusCode);
        var after = (await harness.Secrets.FindAsync(OAuth2TestSupport.TenantId, OAuth2SecretKeys.Grant(OAuth2TestSupport.ConnectionName)))?.CurrentVersion ?? 0;
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
        var store = new SecretStoreDescriptor(
            SecretStoreNames.Configuration,
            "Configuration",
            "Read-only configuration store",
            SecretStoreCapabilities.None,
            IsReadOnly: true);
        var error = Assert.Throws<InvalidOperationException>(() => scheme.EnsureStoreCanHoldGrants(store));
        Assert.Contains("RequiresWritableStore", error.Message, StringComparison.Ordinal);
        Assert.Contains(SecretStoreNames.Configuration, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lost_compare_and_swap_keeps_the_winning_grant()
    {
        var preview = new MockOAuthServer();
        await using var harness = await OAuth2TestSupport.StartAsync(OAuth2TestSupport.ClientCredentialsScheme(preview));

        await OAuth2TestSupport.SendAsync(harness, harness.Server.ResourceEndpoint);
        var name = OAuth2SecretKeys.Grant(OAuth2TestSupport.ConnectionName);
        var current = await harness.Secrets.FindAsync(OAuth2TestSupport.TenantId, name);
        Assert.NotNull(current);

        var grants = harness.Services.GetRequiredService<IConnectionGrantWriter>();
        var lost = await grants.TryCompareAndSwapAsync(new GrantWriteRequest
        {
            TenantId = OAuth2TestSupport.TenantId,
            ConnectionName = OAuth2TestSupport.ConnectionName,
            GrantSecretName = name,
            ExpectedSecretVersion = 0,
            AccessToken = "loser",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        });
        Assert.Equal(GrantWriteStatus.Conflict, lost.Status);

        var resolved = await harness.SecretValues.ResolveAsync(OAuth2TestSupport.TenantId, new SecretReference(name));
        Assert.DoesNotContain("loser", resolved.Value, StringComparison.Ordinal);
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _utc = start;
        public void Advance(TimeSpan delta) => _utc += delta;
        public override DateTimeOffset GetUtcNow() => _utc;
    }
}
