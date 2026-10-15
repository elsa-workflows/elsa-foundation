using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Connections.Tests;

public sealed class ConnectionManagerAndResolverTests
{
    private const string Tenant = "tenant-a";

    [Fact]
    public async Task Create_Resolve_AndRejectWrongTypeOrOwner()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var resolver = scope.ServiceProvider.GetRequiredService<IConnectionResolver>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();

        await secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "api-token",
            Value = "tok-1",
            TypeName = SecretTypeNames.Text
        });

        var created = await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "Acme.Api",
            TypeName = ConnectionTypeNames.Http,
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = "api-token" }
            }
        });

        Assert.Equal("acme.api", created.Name);
        Assert.Equal(ConnectionOwners.Tenant, created.Owner);
        Assert.Equal(ConnectionManagedBy.Studio, created.ManagedBy);
        Assert.DoesNotContain("tok-1", created.ToString(), StringComparison.Ordinal);

        await using var resolved = await resolver.ResolveAsync(new ConnectionResolveRequest
        {
            TenantId = Tenant,
            Name = "acme.api",
            RequiredType = ConnectionTypeNames.Http
        });
        Assert.Equal("bearer", resolved.AuthScheme, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("tok-1", resolved.ToString(), StringComparison.Ordinal);
        var token = await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token);
        Assert.Equal("tok-1", token.Reveal());

        var wrongType = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => resolver.ResolveAsync(new ConnectionResolveRequest
        {
            TenantId = Tenant,
            Name = "acme.api",
            RequiredType = "smtp"
        }).AsTask());
        Assert.Equal(ConnectionFaultCodes.Unavailable, wrongType.Code);

        var owner = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => resolver.ResolveAsync(new ConnectionResolveRequest
        {
            TenantId = Tenant,
            Name = "acme.api",
            Owner = "user"
        }).AsTask());
        Assert.Equal(ConnectionFaultCodes.OwnerNotSupported, owner.Code);

        await Assert.ThrowsAsync<ArgumentException>(() => manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "other",
            Owner = "user"
        }).AsTask());
    }

    [Fact]
    public async Task MissingAndRevoked_AreUnavailable()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var resolver = scope.ServiceProvider.GetRequiredService<IConnectionResolver>();

        var missing = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => resolver.ResolveAsync(new ConnectionResolveRequest
        {
            TenantId = Tenant,
            Name = "nope"
        }).AsTask());
        Assert.False(missing.IsRetryable);

        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "soon-gone",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" }
        });
        await manager.RevokeAsync(Tenant, "soon-gone");
        var revoked = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => resolver.ResolveAsync(new ConnectionResolveRequest
        {
            TenantId = Tenant,
            Name = "soon-gone"
        }).AsTask());
        Assert.Contains("Revoked", revoked.Message, StringComparison.Ordinal);

        var test = await manager.TestAsync(Tenant, "soon-gone");
        Assert.False(test.Succeeded);
        Assert.Contains("Revoked", test.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicateCreate_DoesNotOverwriteTheExistingCredential()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var resolver = scope.ServiceProvider.GetRequiredService<IConnectionResolver>();

        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "dup",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "value-a" }
            }
        });

        await Assert.ThrowsAsync<ConnectionConflictException>(() => manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "dup",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "other.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "value-b" }
            }
        }).AsTask());

        await using var resolved = await resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = "dup" });
        Assert.Equal("value-a", (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal());
        Assert.Equal("api.example", resolved.Settings[HttpConnectionSettingKeys.AllowedHosts]);
    }

    [Fact]
    public async Task HttpCreate_RequiresBaseUrlOrAllowedHosts()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();

        await Assert.ThrowsAsync<ArgumentException>(() => manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "no-hosts",
            AuthScheme = HttpAuthSchemes.Bearer,
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "tok" }
            }
        }).AsTask());
    }

    [Fact]
    public async Task Resolve_UsesBaseUrlHostWhenAllowedHostsIsOmitted()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var resolver = scope.ServiceProvider.GetRequiredService<IConnectionResolver>();

        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "from-base",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.BaseUrl] = "https://api.example/v1" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "tok" }
            }
        });

        await using var resolved = await resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = "from-base" });
        Assert.True(resolved.Hosts.Allows(new Uri("https://api.example/resource")));
        Assert.False(resolved.Hosts.Allows(new Uri("https://evil.example/resource")));
    }

    [Fact]
    public async Task NamedCredential_ResolvesFromConfiguration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elsa:Credentials:payments:Kind"] = "configuration",
            ["Elsa:Credentials:payments:Token"] = "cfg-token",
            ["Elsa:Credentials:payments:TokenType"] = "Bearer"
        }).Build();

        await using var provider = CreateProvider(configuration);
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var resolver = scope.ServiceProvider.GetRequiredService<IConnectionResolver>();

        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "payments",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "pay.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new()
                {
                    SourceKind = ConnectionCredentialSourceKinds.NamedCredential,
                    NamedCredentialName = "payments"
                }
            }
        });

        await using var resolved = await resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = "payments" });
        Assert.Equal("cfg-token", (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal());
    }

    [Fact]
    public async Task GrantWriter_CompareAndSwap_ConflictsOnStaleVersion()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();
        var writer = scope.ServiceProvider.GetRequiredService<IConnectionGrantWriter>();

        var created = await secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "oauth-grant",
            Value = "{}",
            TypeName = SecretTypeNames.Text
        });
        Assert.True(created.CurrentVersion.HasValue);
        var expectedVersion = created.CurrentVersion.Value;

        var first = await writer.TryCompareAndSwapAsync(new GrantWriteRequest
        {
            TenantId = Tenant,
            ConnectionName = "oauth",
            GrantSecretName = "oauth-grant",
            ExpectedSecretVersion = expectedVersion,
            AccessToken = "a1",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        });
        Assert.Equal(GrantWriteStatus.Written, first.Status);

        var stale = await writer.TryCompareAndSwapAsync(new GrantWriteRequest
        {
            TenantId = Tenant,
            ConnectionName = "oauth",
            GrantSecretName = "oauth-grant",
            ExpectedSecretVersion = expectedVersion,
            AccessToken = "a2",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        });
        Assert.Equal(GrantWriteStatus.Conflict, stale.Status);

        var missing = await writer.TryCompareAndSwapAsync(new GrantWriteRequest
        {
            TenantId = Tenant,
            ConnectionName = "oauth",
            GrantSecretName = "absent",
            ExpectedSecretVersion = 1,
            AccessToken = "a3",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        });
        Assert.Equal(GrantWriteStatus.Missing, missing.Status);
    }

    [Fact]
    public async Task GrantWriter_CompareAndSwap_OnlyOneWriterSucceedsForTheSameVersion()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();
        var writer = scope.ServiceProvider.GetRequiredService<IConnectionGrantWriter>();

        var created = await secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "oauth-grant-parallel",
            Value = "{}",
            TypeName = SecretTypeNames.Text
        });
        var expectedVersion = created.CurrentVersion!.Value;

        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(index => writer.TryCompareAndSwapAsync(new GrantWriteRequest
        {
            TenantId = Tenant,
            ConnectionName = "oauth",
            GrantSecretName = "oauth-grant-parallel",
            ExpectedSecretVersion = expectedVersion,
            AccessToken = $"token-{index}",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        }).AsTask()));

        Assert.Equal(1, results.Count(result => result.Status == GrantWriteStatus.Written));
        Assert.Equal(31, results.Count(result => result.Status == GrantWriteStatus.Conflict));
        var current = await secrets.FindAsync(Tenant, "oauth-grant-parallel");
        Assert.Equal(expectedVersion + 1, current!.CurrentVersion);
    }

    [Fact]
    public void HttpType_SupportsApiKeyBasicBearer_AndOAuthCanPlugIn()
    {
        using var provider = CreateProvider();
        var types = provider.GetRequiredService<IConnectionTypeRegistry>();
        var schemes = provider.GetRequiredService<IAuthenticationSchemeRegistry>();
        var http = types.Find(ConnectionTypeNames.Http)!;
        Assert.Contains(HttpAuthSchemes.ApiKey, http.Descriptor.SupportedSchemes);
        Assert.Contains(HttpAuthSchemes.Basic, http.Descriptor.SupportedSchemes);
        Assert.Contains(HttpAuthSchemes.Bearer, http.Descriptor.SupportedSchemes);
        Assert.NotNull(schemes.Find(HttpAuthSchemes.Bearer));
        Assert.Null(schemes.Find("oauth2"));
        Assert.Contains(provider.GetServices<IAuthenticationSchemeProvider>(), _ => true);
        Assert.Contains(provider.GetServices<IHttpRequestAuthenticator>(), _ => true);
    }

    [Fact]
    public void CoreContracts_HaveNoAzureOrValenceTypes()
    {
        foreach (var type in typeof(IConnectionResolver).Assembly.GetExportedTypes())
        {
            Assert.DoesNotContain("Azure", type.FullName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Valence", type.FullName, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static ServiceProvider CreateProvider(IConfiguration? configuration = null) =>
        ConnectionsTestComposition.CreateProvider(configuration);
}
