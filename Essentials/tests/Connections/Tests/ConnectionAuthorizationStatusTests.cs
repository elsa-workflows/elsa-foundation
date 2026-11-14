using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Connections.Tests;

public sealed class ConnectionAuthorizationStatusTests
{
    private const string Tenant = "tenant-a";

    [Fact]
    public void ConnectionUnavailableException_is_a_non_retryable_runtime_fault()
    {
        var exception = new ConnectionUnavailableException(
            "mail",
            ConnectionFaultCodes.Unavailable,
            "Connection 'mail' needs authorization.");
        IRuntimeFaultClassification classification = exception;

        Assert.False(exception.IsRetryable);
        Assert.False(classification.IsRetryable);
        Assert.Equal(ConnectionFaultCodes.Unavailable, exception.Code);
        Assert.Equal(ConnectionFaultCodes.Unavailable, classification.FailureCode);
    }

    [Fact]
    public async Task Interactive_oauth2_is_NeedsAuthorization_until_a_grant_exists()
    {
        await using var provider = CreateProvider();
        var manager = provider.GetRequiredService<IConnectionManager>();
        var resolver = provider.GetRequiredService<IConnectionResolver>();

        var created = await manager.CreateAsync(Tenant, InteractiveRequest("needs-grant", HttpOAuth2GrantTypes.AuthorizationCode));
        Assert.Equal(ConnectionStatuses.NeedsAuthorization, created.Status);

        var unavailable = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => resolver.ResolveAsync(new ConnectionResolveRequest
        {
            TenantId = Tenant,
            Name = created.Name
        }).AsTask());
        Assert.Equal(ConnectionFaultCodes.Unavailable, unavailable.Code);
        Assert.Contains(ConnectionStatuses.NeedsAuthorization, unavailable.Message, StringComparison.Ordinal);
        Assert.False(((IRuntimeFaultClassification)unavailable).IsRetryable);
        Assert.Equal(ConnectionFaultCodes.Unavailable, ((IRuntimeFaultClassification)unavailable).FailureCode);

        await using var forAuthorization = await resolver.ResolveAsync(new ConnectionResolveRequest
        {
            TenantId = Tenant,
            Name = created.Name,
            AllowNeedsAuthorization = true
        });
        Assert.Equal(ConnectionStatuses.NeedsAuthorization, forAuthorization.Status);
    }

    [Fact]
    public async Task Client_credentials_oauth2_is_created_Ready()
    {
        await using var provider = CreateProvider();
        var created = await provider.GetRequiredService<IConnectionManager>()
            .CreateAsync(Tenant, InteractiveRequest("machine-client", HttpOAuth2GrantTypes.ClientCredentials));
        Assert.Equal(ConnectionStatuses.Ready, created.Status);

        await using var resolved = await provider.GetRequiredService<IConnectionResolver>()
            .ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = created.Name });
        Assert.Equal(ConnectionStatuses.Ready, resolved.Status);
    }

    [Fact]
    public async Task Bearer_connections_are_still_created_Ready()
    {
        await using var provider = ConnectionsTestComposition.CreateProvider();
        var created = await provider.GetRequiredService<IConnectionManager>().CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "plain-bearer",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" }
        });
        Assert.Equal(ConnectionStatuses.Ready, created.Status);
    }

    private static ServiceProvider CreateProvider() =>
        ConnectionsTestComposition.CreateProvider(configure: services =>
            services.AddSingleton<IAuthenticationSchemeProvider, InteractiveOAuth2Scheme>());

    private static CreateConnectionRequest InteractiveRequest(string name, string grantType) => new()
    {
        Name = name,
        AuthScheme = HttpAuthSchemes.OAuth2,
        Settings = new Dictionary<string, string>
        {
            [HttpConnectionSettingKeys.AllowedHosts] = "api.example",
            [HttpOAuth2SettingKeys.GrantType] = grantType
        }
    };

    private sealed class InteractiveOAuth2Scheme : IAuthenticationSchemeProvider
    {
        public AuthSchemeDescriptor Descriptor { get; } = new(
            HttpAuthSchemes.OAuth2,
            "OAuth 2.0",
            [new AuthSchemeField(HttpOAuth2SettingKeys.GrantType, "Grant type", Sensitive: false, "text")],
            HasInteractiveAuthorization: true,
            HasRefreshableState: true,
            HasRevocation: false,
            RequiresWritableStore: false);
    }
}
