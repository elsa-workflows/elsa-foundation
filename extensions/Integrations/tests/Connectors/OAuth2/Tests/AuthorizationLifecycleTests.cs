using System.Net;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Integrations.Connectors.OAuth2.Tests;

public sealed class AuthorizationLifecycleTests
{
    [Fact]
    public async Task New_authorization_code_connection_is_unavailable_until_a_grant_exists()
    {
        var preview = new MockOAuthServer();
        await using var harness = await OAuth2TestSupport.StartAsync(OAuth2TestSupport.AuthorizationCodeScheme(preview));

        var created = await ConnectionAsync(harness);
        Assert.Equal(ConnectionStatuses.NeedsAuthorization, created.State.Status);
        Assert.Null(created.State.GrantSecretName);

        var unavailable = await Assert.ThrowsAsync<ConnectionUnavailableException>(
            () => OAuth2TestSupport.SendAsync(harness, harness.Server.ResourceEndpoint));
        AssertConnectionUnavailable(unavailable);
        Assert.Equal(ConnectionStatuses.NeedsAuthorization, (await ConnectionAsync(harness)).State.Status);
    }

    [Fact]
    public async Task Callback_success_moves_the_connection_to_Ready_and_records_the_grant()
    {
        var preview = new MockOAuthServer();
        await using var harness = await OAuth2TestSupport.StartAsync(OAuth2TestSupport.AuthorizationCodeScheme(preview));

        await OAuth2TestSupport.AuthorizeAsync(harness, harness.Server);

        var ready = await ConnectionAsync(harness);
        Assert.Equal(ConnectionStatuses.Ready, ready.State.Status);
        Assert.Equal(OAuth2SecretKeys.Grant(OAuth2TestSupport.ConnectionName), ready.State.GrantSecretName);
        Assert.True(ready.State.GrantSecretVersion > 0);

        var completion = await OAuth2TestSupport.SendAsync(harness, harness.Server.ResourceEndpoint);
        Assert.Equal(200, completion.Result.StatusCode);
    }

    [Fact]
    public async Task Missing_grant_returns_NeedsAuthorization_and_ConnectionUnavailableException()
    {
        var preview = new MockOAuthServer();
        await using var harness = await OAuth2TestSupport.StartAsync(OAuth2TestSupport.AuthorizationCodeScheme(preview));
        await OAuth2TestSupport.AuthorizeAsync(harness, harness.Server);

        Assert.True(await harness.Secrets.DeleteAsync(
            OAuth2TestSupport.TenantId,
            OAuth2SecretKeys.Grant(OAuth2TestSupport.ConnectionName)));

        var unavailable = await Assert.ThrowsAsync<ConnectionUnavailableException>(
            () => OAuth2TestSupport.SendAsync(harness, harness.Server.ResourceEndpoint));
        AssertConnectionUnavailable(unavailable);
        Assert.Contains("needs authorization", unavailable.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ConnectionStatuses.NeedsAuthorization, (await ConnectionAsync(harness)).State.Status);
    }

    [Fact]
    public async Task Invalid_grant_refresh_returns_NeedsAuthorization_and_ConnectionUnavailableException()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var preview = new MockOAuthServer();
        await using var harness = await OAuth2TestSupport.StartAsync(
            OAuth2TestSupport.AuthorizationCodeScheme(preview),
            configureServer: mock => mock.AccessTokenLifetimeSeconds = 90,
            timeProvider: clock);
        await OAuth2TestSupport.AuthorizeAsync(harness, harness.Server);

        harness.Server.TokenErrorStatus = HttpStatusCode.BadRequest;
        harness.Server.TokenErrorBody = new Dictionary<string, object?> { ["error"] = "invalid_grant" };
        clock.Advance(TimeSpan.FromMinutes(2));

        var unavailable = await Assert.ThrowsAsync<ConnectionUnavailableException>(
            () => OAuth2TestSupport.SendAsync(harness, harness.Server.ResourceEndpoint));
        AssertConnectionUnavailable(unavailable);
        Assert.Contains("rejected its grant", unavailable.Message, StringComparison.Ordinal);
        Assert.Equal(ConnectionStatuses.NeedsAuthorization, (await ConnectionAsync(harness)).State.Status);
    }

    [Fact]
    public async Task Successful_reauthorization_returns_the_connection_to_Ready()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var preview = new MockOAuthServer();
        await using var harness = await OAuth2TestSupport.StartAsync(
            OAuth2TestSupport.AuthorizationCodeScheme(preview),
            configureServer: mock => mock.AccessTokenLifetimeSeconds = 90,
            timeProvider: clock);
        await OAuth2TestSupport.AuthorizeAsync(harness, harness.Server);

        harness.Server.TokenErrorStatus = HttpStatusCode.BadRequest;
        harness.Server.TokenErrorBody = new Dictionary<string, object?> { ["error"] = "invalid_grant" };
        clock.Advance(TimeSpan.FromMinutes(2));
        await Assert.ThrowsAsync<ConnectionUnavailableException>(
            () => OAuth2TestSupport.SendAsync(harness, harness.Server.ResourceEndpoint));
        Assert.Equal(ConnectionStatuses.NeedsAuthorization, (await ConnectionAsync(harness)).State.Status);

        harness.Server.TokenErrorBody = null;
        await OAuth2TestSupport.AuthorizeAsync(harness, harness.Server);

        var ready = await ConnectionAsync(harness);
        Assert.Equal(ConnectionStatuses.Ready, ready.State.Status);
        Assert.Equal(OAuth2SecretKeys.Grant(OAuth2TestSupport.ConnectionName), ready.State.GrantSecretName);
        var completion = await OAuth2TestSupport.SendAsync(harness, harness.Server.ResourceEndpoint);
        Assert.Equal(200, completion.Result.StatusCode);
    }

    [Fact]
    public async Task Transient_token_endpoint_failure_leaves_status_unchanged()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var preview = new MockOAuthServer();
        await using var harness = await OAuth2TestSupport.StartAsync(
            OAuth2TestSupport.AuthorizationCodeScheme(preview),
            configureServer: mock => mock.AccessTokenLifetimeSeconds = 90,
            timeProvider: clock);
        await OAuth2TestSupport.AuthorizeAsync(harness, harness.Server);
        var before = await ConnectionAsync(harness);

        harness.Server.TokenErrorStatus = HttpStatusCode.InternalServerError;
        harness.Server.TokenErrorBody = new Dictionary<string, object?> { ["error"] = "server_error" };
        clock.Advance(TimeSpan.FromMinutes(2));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => OAuth2TestSupport.SendAsync(harness, harness.Server.ResourceEndpoint));
        Assert.Equal("Token endpoint returned 500.", error.Message);
        Assert.True(OAuth2TokenError.IsTransientStatus(HttpStatusCode.InternalServerError));

        var after = await ConnectionAsync(harness);
        Assert.Equal(ConnectionStatuses.Ready, after.State.Status);
        Assert.Equal(before.State.GrantSecretName, after.State.GrantSecretName);
        Assert.Equal(before.State.GrantSecretVersion, after.State.GrantSecretVersion);
    }

    private static async Task<ConnectionDocument> ConnectionAsync(OAuth2Harness harness)
    {
        var document = await harness.Services.GetRequiredService<IConnectionRepository>()
            .FindAsync(OAuth2TestSupport.TenantId, OAuth2TestSupport.ConnectionName);
        Assert.NotNull(document);
        return document;
    }

    private static void AssertConnectionUnavailable(ConnectionUnavailableException exception)
    {
        IRuntimeFaultClassification classification = exception;
        Assert.Equal(ConnectionFaultCodes.Unavailable, exception.Code);
        Assert.False(exception.IsRetryable);
        Assert.False(classification.IsRetryable);
        Assert.Equal(ConnectionFaultCodes.Unavailable, classification.FailureCode);
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _utc = start;
        public void Advance(TimeSpan delta) => _utc += delta;
        public override DateTimeOffset GetUtcNow() => _utc;
    }
}
