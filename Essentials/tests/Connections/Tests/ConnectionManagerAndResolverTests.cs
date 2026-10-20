using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Services;
using Elsa.Primitives.Persistence;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Services;
using Elsa.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Connections.Tests;

public sealed class ConnectionManagerAndResolverTests : IAsyncDisposable
{
    private const string Tenant = "tenant-a";

    // Shared arrange for the tests that script secret-store faults and race the connection save.
    private readonly SaveInterceptor _interceptor = new();
    private readonly ScriptedSecretManager _secretHooks = new();
    private readonly RecordingLogger<DefaultConnectionManager> _logger = new();
    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;
    private readonly IConnectionManager _manager;
    private readonly ISecretManager _secrets;
    private readonly IConnectionResolver _resolver;

    public ConnectionManagerAndResolverTests()
    {
        _provider = CreateInstrumentedProvider(_interceptor, _secretHooks, _logger);
        _scope = _provider.CreateAsyncScope();
        _manager = _scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        _secrets = _scope.ServiceProvider.GetRequiredService<ISecretManager>();
        _resolver = _scope.ServiceProvider.GetRequiredService<IConnectionResolver>();
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task Create_Resolve_AndRejectWrongTypeOrOwner()
    {

        await _secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "api-token",
            Value = "tok-1",
            TypeName = SecretTypeNames.Text
        });

        var created = await _manager.CreateAsync(Tenant, new CreateConnectionRequest
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

        await using var resolved = await _resolver.ResolveAsync(new ConnectionResolveRequest
        {
            TenantId = Tenant,
            Name = "acme.api",
            RequiredType = ConnectionTypeNames.Http
        });
        Assert.Equal("bearer", resolved.AuthScheme, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("tok-1", resolved.ToString(), StringComparison.Ordinal);
        var token = await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token);
        Assert.Equal("tok-1", token.Reveal());

        var wrongType = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => _resolver.ResolveAsync(new ConnectionResolveRequest
        {
            TenantId = Tenant,
            Name = "acme.api",
            RequiredType = "smtp"
        }).AsTask());
        Assert.Equal(ConnectionFaultCodes.Unavailable, wrongType.Code);

        var owner = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => _resolver.ResolveAsync(new ConnectionResolveRequest
        {
            TenantId = Tenant,
            Name = "acme.api",
            Owner = "user"
        }).AsTask());
        Assert.Equal(ConnectionFaultCodes.OwnerNotSupported, owner.Code);

        await Assert.ThrowsAsync<ArgumentException>(() => _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "other",
            Owner = "user"
        }).AsTask());
    }

    [Fact]
    public async Task Create_ExistingSecretMustBeActiveTextAndBelongToTheCurrentTenant()
    {

        await _secrets.CreateAsync("tenant-b", new CreateSecretRequest { Name = "other-tenant-token", Value = "secret" });
        await _secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "rsa-token",
            TypeName = SecretTypeNames.RsaKey,
            Value = "key material"
        });
        await _secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "revoked-token",
            Value = "secret"
        });
        await _secrets.RevokeAsync(Tenant, "revoked-token");
        await _secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "expired-token",
            Value = "secret",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });

        foreach (var secretName in new[] { "missing-token", "other-tenant-token", "rsa-token", "revoked-token", "expired-token" })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => _manager.CreateAsync(Tenant, new CreateConnectionRequest
            {
                Name = $"reject-{secretName}",
                AuthScheme = HttpAuthSchemes.Bearer,
                Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
                SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
                {
                    [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = secretName }
                }
            }).AsTask());

            Assert.Null(await _manager.FindAsync(Tenant, $"reject-{secretName}"));
        }
    }

    [Fact]
    public async Task Create_RejectsAmbiguousUnknownAndEmptyCredentialSources()
    {
        await _secrets.CreateAsync(Tenant, new CreateSecretRequest { Name = "existing-token", Value = "secret" });

        var invalidSources = new[]
        {
            new ConnectionSecretFieldInput { SourceKind = ConnectionCredentialSourceKinds.Secret },
            new ConnectionSecretFieldInput { ExistingSecretName = "existing-token", Value = "inline" },
            new ConnectionSecretFieldInput { ExistingSecretName = "existing-token", NamedCredentialName = "payments" },
            new ConnectionSecretFieldInput { SourceKind = ConnectionCredentialSourceKinds.NamedCredential, ExistingSecretName = "existing-token" },
            new ConnectionSecretFieldInput { SourceKind = "unregistered", Value = "inline" },
            new ConnectionSecretFieldInput { NamedCredentialName = "payments" }
        };

        for (var index = 0; index < invalidSources.Length; index++)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => _manager.CreateAsync(Tenant, new CreateConnectionRequest
            {
                Name = $"invalid-source-{index}",
                AuthScheme = HttpAuthSchemes.Bearer,
                Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
                SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
                {
                    [HttpConnectionSecretFields.Token] = invalidSources[index]
                }
            }).AsTask());
        }

        var empty = await _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "empty-source-noop",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new()
            }
        });
        Assert.Empty(empty.SecretFieldNames);
    }

    [Fact]
    public async Task Update_RejectsUnavailableExistingSecretAndKeepsThePreviousCredential()
    {

        await _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "update-existing-secret",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "previous-token" }
            }
        });

        await Assert.ThrowsAsync<ArgumentException>(() => _manager.UpdateAsync(Tenant, "update-existing-secret", new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = "missing-token" }
            }
        }).AsTask());

        await using var resolved = await _resolver.ResolveAsync(new ConnectionResolveRequest
        {
            TenantId = Tenant,
            Name = "update-existing-secret"
        });
        Assert.Equal("previous-token", (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal());
    }

    [Fact]
    public async Task CreateAndUpdate_RejectNullCredentialFieldInputs()
    {

        await Assert.ThrowsAsync<ArgumentException>(() => _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "null-secret-fields",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = null!
        }).AsTask());

        await Assert.ThrowsAsync<ArgumentException>(() => _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "null-secret-field-value",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput> { ["token"] = null! }
        }).AsTask());

        await _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "null-update-field-value",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "previous-token" }
            }
        });

        await Assert.ThrowsAsync<ArgumentException>(() => _manager.UpdateAsync(Tenant, "null-update-field-value", new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput> { ["token"] = null! }
        }).AsTask());
    }

    [Fact]
    public async Task ExistingSecretNamedLikeOwnedSecret_IsNotDeletedWithConnection()
    {
        const string secretName = "connection:prefix-owned:token";

        await _secrets.CreateAsync(Tenant, new CreateSecretRequest { Name = secretName, Value = "external-value" });
        await _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "prefix-owned",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = secretName }
            }
        });

        Assert.True(await _manager.DeleteAsync(Tenant, "prefix-owned"));
        Assert.NotNull(await _secrets.FindAsync(Tenant, secretName));
    }

    [Fact]
    public async Task Rebind_DoesNotDeleteAnOwnedSecretRetainedByAnotherField()
    {
        const string ownedSecretName = "connection:retained-owned:token";

        await _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "retained-owned",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "owned-value" }
            }
        });
        await _secrets.CreateAsync(Tenant, new CreateSecretRequest { Name = "replacement-token", Value = "replacement-value" });
        await _manager.UpdateAsync(Tenant, "retained-owned", new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                ["extra"] = new() { ExistingSecretName = ownedSecretName }
            }
        });

        await _manager.UpdateAsync(Tenant, "retained-owned", new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = "replacement-token" }
            }
        });

        Assert.NotNull(await _secrets.FindAsync(Tenant, ownedSecretName));
        await using var resolved = await _resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = "retained-owned" });
        Assert.Equal("replacement-value", (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal());
    }

    [Fact]
    public async Task Update_RotatesSecretWhenTheStoredFieldProvesItIsOwned()
    {

        await _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "owned-rotation",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "old-value" }
            }
        });
        await _manager.UpdateAsync(Tenant, "owned-rotation", new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "new-value" }
            }
        });

        await using var resolved = await _resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = "owned-rotation" });
        Assert.Equal("new-value", (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal());
    }

    [Fact]
    public async Task Delete_DoesNotDeleteAnOwnedSecretReferencedByAnotherConnection()
    {
        const string ownedSecretName = "connection:reference-owner:token";

        await _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "reference-owner",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "shared-owned-value" }
            }
        });
        await _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "reference-consumer",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = ownedSecretName }
            }
        });

        await _secrets.CreateAsync(Tenant, new CreateSecretRequest { Name = "owner-replacement-token", Value = "replacement-value" });
        await _manager.UpdateAsync(Tenant, "reference-owner", new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = "owner-replacement-token" }
            }
        });
        Assert.NotNull(await _secrets.FindAsync(Tenant, ownedSecretName));

        Assert.True(await _manager.DeleteAsync(Tenant, "reference-owner"));
        Assert.NotNull(await _secrets.FindAsync(Tenant, ownedSecretName));
        await using var resolved = await _resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = "reference-consumer" });
        Assert.Equal("shared-owned-value", (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal());
    }

    [Fact]
    public async Task InlineSecretWrites_DoNotRotateExternalSecretsAtGeneratedNames()
    {

        await _secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "connection:collision-create:token",
            Value = "external-create-value"
        });
        await Assert.ThrowsAsync<ArgumentException>(() => _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "collision-create",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "attempted-overwrite" }
            }
        }).AsTask());

        await _secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "connection:collision-update:token",
            Value = "external-update-value"
        });
        await _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "collision-update",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" }
        });
        await Assert.ThrowsAsync<ArgumentException>(() => _manager.UpdateAsync(Tenant, "collision-update", new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "attempted-overwrite" }
            }
        }).AsTask());

        Assert.Equal(1, (await _secrets.FindAsync(Tenant, "connection:collision-create:token"))!.CurrentVersion);
        Assert.Equal(1, (await _secrets.FindAsync(Tenant, "connection:collision-update:token"))!.CurrentVersion);

        await _secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "connection:collision-preflight-create:extra",
            Value = "external-preflight-value"
        });
        await Assert.ThrowsAsync<ArgumentException>(() => _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "collision-preflight-create",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                ["token"] = new() { Value = "must-not-be-written" },
                ["extra"] = new() { Value = "attempted-overwrite" }
            }
        }).AsTask());
        Assert.Null(await _secrets.FindAsync(Tenant, "connection:collision-preflight-create:token"));

        await _secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "connection:collision-preflight-update:extra",
            Value = "external-preflight-value"
        });
        await _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "collision-preflight-update",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" }
        });
        await Assert.ThrowsAsync<ArgumentException>(() => _manager.UpdateAsync(Tenant, "collision-preflight-update", new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                ["token"] = new() { Value = "must-not-be-written" },
                ["extra"] = new() { Value = "attempted-overwrite" }
            }
        }).AsTask());
        Assert.Null(await _secrets.FindAsync(Tenant, "connection:collision-preflight-update:token"));
    }

    [Fact]
    public async Task MissingAndRevoked_AreUnavailable()
    {

        var missing = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => _resolver.ResolveAsync(new ConnectionResolveRequest
        {
            TenantId = Tenant,
            Name = "nope"
        }).AsTask());
        Assert.False(missing.IsRetryable);

        await _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "soon-gone",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" }
        });
        await _manager.RevokeAsync(Tenant, "soon-gone");
        var revoked = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => _resolver.ResolveAsync(new ConnectionResolveRequest
        {
            TenantId = Tenant,
            Name = "soon-gone"
        }).AsTask());
        Assert.Contains("Revoked", revoked.Message, StringComparison.Ordinal);

        var test = await _manager.TestAsync(Tenant, "soon-gone");
        Assert.False(test.Succeeded);
        Assert.Contains("Revoked", test.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicateCreate_DoesNotOverwriteTheExistingCredential()
    {

        await _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "dup",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "value-a" }
            }
        });

        await Assert.ThrowsAsync<ConnectionConflictException>(() => _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "dup",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "other.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "value-b" }
            }
        }).AsTask());

        await using var resolved = await _resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = "dup" });
        Assert.Equal("value-a", (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal());
        Assert.Equal("api.example", resolved.Settings[HttpConnectionSettingKeys.AllowedHosts]);
    }

    [Fact]
    public async Task Update_KeepsPreviousSecretFieldsWhenTheInputCarriesNoNewValue()
    {

        await _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "keep-fields",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "value-keep" }
            }
        });

        await _manager.UpdateAsync(Tenant, "keep-fields", new UpdateConnectionRequest
        {
            DisplayName = "Kept",
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new()
            }
        });

        await using var resolved = await _resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = "keep-fields" });
        Assert.Equal("value-keep", (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal());
        var metadata = await _manager.FindAsync(Tenant, "keep-fields");
        Assert.Equal("Kept", metadata!.DisplayName);
    }

    [Fact]
    public async Task HttpCreate_RequiresBaseUrlOrAllowedHosts()
    {

        await Assert.ThrowsAsync<ArgumentException>(() => _manager.CreateAsync(Tenant, new CreateConnectionRequest
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

        await _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "from-base",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.BaseUrl] = "https://api.example/v1" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "tok" }
            }
        });

        await using var resolved = await _resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = "from-base" });
        Assert.True(resolved.Hosts.Allows(new Uri("https://api.example/resource")));
        Assert.False(resolved.Hosts.Allows(new Uri("https://evil.example/resource")));
    }

    [Fact]
    public async Task NamedCredential_ResolvesFromConfiguration()
    {

        await _manager.CreateAsync(Tenant, new CreateConnectionRequest
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

        await using var resolved = await _resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = "payments" });
        Assert.Equal("cfg-token", (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal());
    }

    [Fact]
    public async Task GrantWriter_CompareAndSwap_ConflictsOnStaleVersion()
    {
        var writer = _scope.ServiceProvider.GetRequiredService<IConnectionGrantWriter>();

        var created = await _secrets.CreateAsync(Tenant, new CreateSecretRequest
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
        var writer = _scope.ServiceProvider.GetRequiredService<IConnectionGrantWriter>();

        var created = await _secrets.CreateAsync(Tenant, new CreateSecretRequest
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
        var current = await _secrets.FindAsync(Tenant, "oauth-grant-parallel");
        Assert.Equal(expectedVersion + 1, current!.CurrentVersion);
    }

    [Fact]
    public void HttpType_SupportsApiKeyBasicBearer_AndOAuthCanPlugIn()
    {
        var types = _provider.GetRequiredService<IConnectionTypeRegistry>();
        var schemes = _provider.GetRequiredService<IAuthenticationSchemeRegistry>();
        var http = types.Find(ConnectionTypeNames.Http)!;
        Assert.Contains(HttpAuthSchemes.ApiKey, http.Descriptor.SupportedSchemes);
        Assert.Contains(HttpAuthSchemes.Basic, http.Descriptor.SupportedSchemes);
        Assert.Contains(HttpAuthSchemes.Bearer, http.Descriptor.SupportedSchemes);
        Assert.NotNull(schemes.Find(HttpAuthSchemes.Bearer));
        Assert.Null(schemes.Find("oauth2"));
        Assert.Contains(_provider.GetServices<IAuthenticationSchemeProvider>(), _ => true);
        Assert.Contains(_provider.GetServices<IHttpRequestAuthenticator>(), _ => true);
    }

    [Fact]
    public async Task Create_CleansUpOwnedSecretsWhenALaterFieldFails()
    {
        var creates = 0;
        var outage = new InvalidOperationException("Simulated secret create failure.");
        _secretHooks.OnCreate = _ => ++creates >= 2 ? outage : null;

        var failure = await Assert.ThrowsAsync<ConnectionStoreException>(() => _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "partial",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                ["token"] = new() { Value = "first" },
                ["extra"] = new() { Value = "second" }
            }
        }).AsTask());

        Assert.Same(outage, failure.InnerException);
        Assert.Null(await _manager.FindAsync(Tenant, "partial"));
        Assert.Null(await _secrets.FindAsync(Tenant, "connection:partial:token"));
        Assert.Null(await _secrets.FindAsync(Tenant, "connection:partial:extra"));
    }

    [Fact]
    public async Task Rebind_DeletesThePreviousOwnedSecret()
    {

        await _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "rebind",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "owned-value" }
            }
        });
        await _secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "shared-token",
            Value = "shared-value",
            TypeName = SecretTypeNames.Text
        });

        await _manager.UpdateAsync(Tenant, "rebind", new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = "shared-token" }
            }
        });

        Assert.Null(await _secrets.FindAsync(Tenant, "connection:rebind:token"));
        await using var resolved = await _resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = "rebind" });
        Assert.Equal("shared-value", (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal());
    }

    [Fact]
    public async Task Update_SelectingTheSameOwnedSecretRetainsProvenanceAndConnectionDeleteCleansIt()
    {
        var repository = _scope.ServiceProvider.GetRequiredService<IConnectionRepository>();
        const string connectionName = "owned-rebind";
        const string secretName = "connection:owned-rebind:token";

        await _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = connectionName,
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "owned-value" }
            }
        });

        await _manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = $"  {secretName.ToUpperInvariant()}  " }
            }
        });

        var stored = await repository.FindAsync(Tenant, connectionName);
        var token = stored!.Definition.SecretFields[HttpConnectionSecretFields.Token];
        Assert.True(token.IsOwnedSecret);
        Assert.Equal(secretName, token.SecretName?.Trim(), StringComparer.OrdinalIgnoreCase);

        Assert.True(await _manager.DeleteAsync(Tenant, connectionName));
        Assert.Null(await _secrets.FindAsync(Tenant, secretName));
    }

    [Fact]
    public async Task Create_PreservesRawWhitespaceInNonblankInlineSecretValues()
    {
        const string connectionName = "raw-credential";
        const string inputValue = "  raw value with surrounding spaces  ";

        await _manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = connectionName,
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = inputValue }
            }
        });

        await using var resolved = await _resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = connectionName });
        Assert.Equal(inputValue, (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal());
    }

    [Fact]
    public async Task Update_ConflictDuringCredentialRebindKeepsPersistedOwnedSecretActive()
    {
        await CreateConnectionAsync("rebind-conflict", TokenValue("previous-value"));
        await CreateSecretAsync("replacement-token", "replacement-value");

        _interceptor.BeforeOuterSave = ConcurrentDisplayNameUpdate("rebind-conflict");
        await Assert.ThrowsAsync<ConnectionConflictException>(() => _manager.UpdateAsync(Tenant, "rebind-conflict", new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = "replacement-token" }
            }
        }).AsTask());

        var oldSecret = await _secrets.FindAsync(Tenant, "connection:rebind-conflict:token");
        Assert.NotNull(oldSecret);
        Assert.Equal(SecretStatus.Active, oldSecret.Status);
        Assert.Equal("previous-value", await RevealAsync("rebind-conflict"));
    }

    [Fact]
    public async Task Update_LosingTheSaveLeavesTheOwnedSecretValueUnchanged()
    {
        const string connectionName = "rotation-conflict";
        const string consumerName = "rotation-conflict-consumer";
        await CreateConnectionAsync(connectionName, TokenValue("previous-value"));
        await CreateConnectionAsync(consumerName, new Dictionary<string, ConnectionSecretFieldInput>
        {
            [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = "connection:rotation-conflict:token" }
        });

        _interceptor.BeforeOuterSave = ConcurrentDisplayNameUpdate(connectionName);
        await Assert.ThrowsAsync<ConnectionConflictException>(() => _manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = TokenValue("losing-value")
        }).AsTask());

        Assert.Equal("previous-value", await RevealAsync(connectionName));
        Assert.Equal("previous-value", await RevealAsync(consumerName));
        var secret = await _secrets.FindAsync(Tenant, "connection:rotation-conflict:token");
        Assert.Equal(SecretStatus.Active, secret!.Status);
        Assert.Equal(1, secret.CurrentVersion);
    }

    [Fact]
    public async Task Update_SaveFailureOtherThanAConflictKeepsStoredValuesAndLeavesNewlyCreatedSecrets()
    {
        const string connectionName = "save-outage";
        await CreateConnectionAsync(connectionName, InlineFields((HttpConnectionSecretFields.Token, "previous-value"), ("secondary", "secondary-value")));
        await CreateSecretAsync("save-outage-replacement", "replacement-value");

        // The interceptor fails before the inner save, so nothing is stored and no secret is deleted. Fields moving to a
        // new inline secret have already created theirs, and it stays; only rotations of already-referenced secrets
        // wait for the save. (A store that commits and then fails is why a failed save never deletes: the outcome is unknown.)
        _interceptor.BeforeOuterSave = () => Task.FromException(new InvalidOperationException("Simulated store outage."));
        var failure = await Assert.ThrowsAsync<ConnectionStoreException>(() => _manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "unsaved-value" },
                ["secondary"] = new() { ExistingSecretName = "save-outage-replacement" },
                ["extra"] = new() { Value = "extra-value" }
            }
        }).AsTask());

        Assert.Equal("Simulated store outage.", failure.InnerException!.Message);
        Assert.Equal(connectionName, failure.ConnectionName);
        Assert.Equal("previous-value", await RevealAsync(connectionName));
        Assert.Equal(1, (await _secrets.FindAsync(Tenant, "connection:save-outage:token"))!.CurrentVersion);
        Assert.NotNull(await _secrets.FindAsync(Tenant, "connection:save-outage:secondary"));
        Assert.NotNull(await _secrets.FindAsync(Tenant, "connection:save-outage:extra"));
    }

    [Fact]
    public async Task Update_DoesNotOverwriteAValueRotatedAfterItsSave()
    {
        const string connectionName = "rotated-after-save";
        await CreateConnectionAsync(connectionName, TokenValue("previous-value"));

        _interceptor.AfterOuterSave = RotateConcurrently("connection:rotated-after-save:token");
        await Assert.ThrowsAsync<ConnectionConflictException>(() => _manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = TokenValue("stale-value")
        }).AsTask());

        Assert.Equal("concurrent-value", await RevealAsync(connectionName));
    }

    [Fact]
    public async Task Update_CleansUpReplacedOwnedSecretsWhenARotationConflictsAfterItsSave()
    {
        const string connectionName = "conflict-cleanup";
        await CreateConnectionAsync(connectionName, InlineFields((HttpConnectionSecretFields.Token, "previous-value"), ("secondary", "secondary-value")));
        await CreateSecretAsync("conflict-cleanup-replacement", "replacement-value");

        _interceptor.AfterOuterSave = RotateConcurrently("connection:conflict-cleanup:token");
        await Assert.ThrowsAsync<ConnectionConflictException>(() => _manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "stale-value" },
                ["secondary"] = new() { ExistingSecretName = "conflict-cleanup-replacement" }
            }
        }).AsTask());

        // The save committed, so the secret it stopped referencing must not be left Active and unreferenced.
        Assert.Null(await _secrets.FindAsync(Tenant, "connection:conflict-cleanup:secondary"));
    }

    [Fact]
    public async Task Update_RotatesTheOtherFieldsWhenOneRotationConflicts()
    {
        const string connectionName = "partial-rotation";
        await CreateConnectionAsync(connectionName, InlineFields((HttpConnectionSecretFields.Token, "previous-value"), ("secondary", "secondary-previous")));

        _interceptor.AfterOuterSave = RotateConcurrently("connection:partial-rotation:token");
        var conflict = await Assert.ThrowsAsync<ConnectionConflictException>(() => _manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = InlineFields((HttpConnectionSecretFields.Token, "stale-value"), ("secondary", "secondary-new"))
        }).AsTask());

        Assert.Equal("concurrent-value", await RevealAsync(connectionName));
        await using var resolved = await _resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = connectionName });
        Assert.Equal("secondary-new", (await resolved.GetSensitiveAsync("secondary")).Reveal());
        Assert.Contains($"'{HttpConnectionSecretFields.Token}'", conflict.Message);
        Assert.DoesNotContain("'secondary'", conflict.Message);
    }

    [Fact]
    public async Task Update_NamesTheConflictedFieldsWhenAnotherFieldFaults()
    {
        const string connectionName = "fault-and-conflict";
        await CreateConnectionAsync(connectionName, InlineFields((HttpConnectionSecretFields.Token, "previous-value"), ("secondary", "secondary-previous")));

        _interceptor.AfterOuterSave = RotateConcurrently("connection:fault-and-conflict:token");
        var simulated = new IOException("Simulated secret store outage.");
        _secretHooks.OnRotate = name => name.EndsWith(":secondary", StringComparison.Ordinal) ? simulated : null;
        var failure = await Assert.ThrowsAsync<ConnectionCredentialWriteException>(() => _manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = InlineFields((HttpConnectionSecretFields.Token, "stale-value"), ("secondary", "secondary-new"))
        }).AsTask());

        // The fault outranks the conflict (no 409), yet both unwritten fields are named.
        Assert.Same(simulated, failure.InnerException);
        Assert.Equal(["secondary", HttpConnectionSecretFields.Token], failure.FieldNames.Order());
        Assert.Contains($"'{HttpConnectionSecretFields.Token}'", failure.Message);
        Assert.Contains("'secondary'", failure.Message);
    }

    [Fact]
    public async Task Update_KeepsEveryFaultWhenSeveralFieldsFail()
    {
        const string connectionName = "two-faults";
        await CreateConnectionAsync(connectionName, InlineFields((HttpConnectionSecretFields.Token, "previous-value"), ("secondary", "secondary-previous")));

        var tokenFault = new IOException("Simulated token outage.");
        var secondaryFault = new TimeoutException("Simulated secondary outage.");
        _secretHooks.OnRotate = name => name.EndsWith(":secondary", StringComparison.Ordinal) ? secondaryFault : tokenFault;
        var failure = await Assert.ThrowsAsync<ConnectionCredentialWriteException>(() => _manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = InlineFields((HttpConnectionSecretFields.Token, "new-value"), ("secondary", "secondary-new"))
        }).AsTask());

        // The later fault is not lost behind the first one: both travel as the inner exception and both are logged.
        var inner = Assert.IsType<AggregateException>(failure.InnerException);
        Assert.Equal([tokenFault, secondaryFault], inner.InnerExceptions);
        Assert.Contains(_logger.Entries, entry => entry.Exception == tokenFault);
        Assert.Contains(_logger.Entries, entry => entry.Exception == secondaryFault);
    }

    [Fact]
    public async Task Update_StaysAFaultWhenARotationThrowsWhileTheSecretIsUnchanged()
    {
        const string connectionName = "unchanged-fault";
        await CreateConnectionAsync(connectionName, TokenValue("previous-value"));

        // The same exception type Secrets uses for a vanished secret, but the secret is still there and unchanged.
        var simulated = new InvalidOperationException("Simulated rotation failure.");
        _secretHooks.OnRotate = _ => simulated;
        var failure = await Assert.ThrowsAsync<ConnectionCredentialWriteException>(() => _manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = TokenValue("new-value")
        }).AsTask());

        Assert.Same(simulated, failure.InnerException);
        Assert.Equal([HttpConnectionSecretFields.Token], failure.FieldNames);
    }

    [Fact]
    public async Task Update_ReportsTheOriginalFailureAndLogsWhenClassifyingItCannotReadTheSecret()
    {
        const string connectionName = "classify-outage";
        await CreateConnectionAsync(connectionName, TokenValue("previous-value"));

        var simulated = new InvalidOperationException("Simulated rotation failure.");
        var readFault = new IOException("Simulated read outage.");
        _secretHooks.OnRotate = _ => simulated;
        // Only the classification re-read, which follows the save, may fail.
        _interceptor.AfterOuterSave = () =>
        {
            _secretHooks.OnFind = _ => readFault;
            return Task.CompletedTask;
        };
        var failure = await Assert.ThrowsAsync<ConnectionCredentialWriteException>(() => _manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = TokenValue("new-value")
        }).AsTask());

        Assert.Same(simulated, failure.InnerException);
        Assert.Contains(_logger.Entries, entry => entry.Exception == readFault && entry.Message.Contains("connection:classify-outage:token"));
    }

    [Fact]
    public async Task Update_ReportsAConflictWhenASecretWithoutAnActiveVersionChangedMidRotation()
    {
        const string connectionName = "revoked-changed";
        await CreateConnectionAsync(connectionName, TokenValue("previous-value"));
        await _secrets.RevokeAsync(Tenant, "connection:revoked-changed:token");

        // No active version means no expected version, so Secrets reports a concurrent writer only as a plain
        // InvalidOperationException; the secret's changed metadata is what tells it from a fault.
        var rotateConcurrently = RotateConcurrently("connection:revoked-changed:token");
        _interceptor.AfterOuterSave = async () =>
        {
            await rotateConcurrently();
            _secretHooks.OnRotate = _ => new InvalidOperationException("Secret changed concurrently.");
        };
        var conflict = await Assert.ThrowsAsync<ConnectionConflictException>(() => _manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = TokenValue("stale-value")
        }).AsTask());

        Assert.Contains($"'{HttpConnectionSecretFields.Token}'", conflict.Message);
        Assert.Equal("concurrent-value", await RevealAsync(connectionName));
    }

    [Fact]
    public async Task Update_CleansUpEveryReplacedSecretAndSurfacesTheRotationFailureWhenCleanupThrowsToo()
    {
        const string connectionName = "cleanup-after-failure";
        await CreateConnectionAsync(connectionName, InlineFields(
            (HttpConnectionSecretFields.Token, "previous-value"), ("first", "first-value"), ("second", "second-value")));
        await CreateSecretAsync("cleanup-after-failure-a", "a");
        await CreateSecretAsync("cleanup-after-failure-b", "b");

        var rotationFault = new IOException("Simulated rotation outage.");
        var deleteFault = new TimeoutException("Simulated delete outage.");
        _secretHooks.OnRotate = _ => rotationFault;
        _secretHooks.OnDelete = name => name.EndsWith(":first", StringComparison.Ordinal) ? deleteFault : null;
        var failure = await Assert.ThrowsAsync<ConnectionCredentialWriteException>(() => _manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "new-value" },
                ["first"] = new() { ExistingSecretName = "cleanup-after-failure-a" },
                ["second"] = new() { ExistingSecretName = "cleanup-after-failure-b" }
            }
        }).AsTask());

        // The rotation failure is the one reported, not the cleanup failure.
        Assert.Same(rotationFault, failure.InnerException);
        Assert.Equal([HttpConnectionSecretFields.Token], failure.FieldNames);
        // The first field's delete threw, yet the second field's replaced secret was still deleted.
        Assert.Null(await _secrets.FindAsync(Tenant, "connection:cleanup-after-failure:second"));
        Assert.NotNull(await _secrets.FindAsync(Tenant, "connection:cleanup-after-failure:first"));
        // The swallowed cleanup failure leaves a trace.
        Assert.Contains(_logger.Entries, entry => entry.Exception == deleteFault && entry.Message.Contains("connection:cleanup-after-failure:first"));
    }

    [Fact]
    public async Task Update_SurfacesACleanupFailureAfterASuccessfulRotation()
    {
        const string connectionName = "cleanup-after-success";
        await CreateConnectionAsync(connectionName, InlineFields((HttpConnectionSecretFields.Token, "previous-value"), ("secondary", "secondary-value")));
        await CreateSecretAsync("cleanup-after-success-replacement", "replacement");

        var deleteFault = new TimeoutException("Simulated delete outage.");
        _secretHooks.OnDelete = _ => deleteFault;
        var failure = await Assert.ThrowsAsync<ConnectionCredentialWriteException>(() => _manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "rotated-value" },
                ["secondary"] = new() { ExistingSecretName = "cleanup-after-success-replacement" }
            }
        }).AsTask());

        Assert.Same(deleteFault, failure.InnerException);
        Assert.Equal(["secondary"], failure.FieldNames);
        Assert.Equal("rotated-value", await RevealAsync(connectionName));
        Assert.Contains(_logger.Entries, entry => entry.Exception == deleteFault);
    }

    [Fact]
    public async Task Update_ReportsAConflictWhenARotatedSecretWasDeletedAfterItsSave()
    {
        const string connectionName = "deleted-after-save";
        await CreateConnectionAsync(connectionName, TokenValue("previous-value"));

        _interceptor.AfterOuterSave = () => _secrets.DeleteAsync(Tenant, "connection:deleted-after-save:token").AsTask();
        var conflict = await Assert.ThrowsAsync<ConnectionConflictException>(() => _manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = TokenValue("stale-value")
        }).AsTask());

        Assert.Contains($"'{HttpConnectionSecretFields.Token}'", conflict.Message);
        Assert.Contains("point them at an existing secret", conflict.Message);
    }

    [Fact]
    public async Task Update_DoesNotTellACallerToRetryWhenTheConflictIsNotADeletion()
    {
        const string connectionName = "rotated-not-deleted";
        await CreateConnectionAsync(connectionName, TokenValue("previous-value"));

        _interceptor.AfterOuterSave = RotateConcurrently("connection:rotated-not-deleted:token");
        var conflict = await Assert.ThrowsAsync<ConnectionConflictException>(() => _manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = TokenValue("stale-value")
        }).AsTask());

        Assert.DoesNotContain("existing secret", conflict.Message);
    }

    [Fact]
    public async Task Update_WrapsAnOwnedSecretCreateFailureBeforeTheSave()
    {
        const string connectionName = "create-outage";
        await CreateConnectionAsync(connectionName);

        var outage = new InvalidOperationException("Simulated secret create failure.");
        _secretHooks.OnCreate = _ => outage;
        var failure = await Assert.ThrowsAsync<ConnectionStoreException>(() => _manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = TokenValue("new-value")
        }).AsTask());

        Assert.Same(outage, failure.InnerException);
        Assert.DoesNotContain("Simulated", failure.Message);
        Assert.Empty((await _manager.FindAsync(Tenant, connectionName))!.SecretFieldNames);
    }

    [Fact]
    public async Task Update_WrapsASecretStoreReadFailureBeforeTheSave()
    {
        const string connectionName = "read-outage";
        await CreateConnectionAsync(connectionName, TokenValue("previous-value"));

        _secretHooks.OnFind = _ => new IOException("Secret store unreachable.");
        var failure = await Assert.ThrowsAsync<ConnectionStoreException>(() => _manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = TokenValue("new-value")
        }).AsTask());

        Assert.IsType<IOException>(failure.InnerException);
    }

    [Fact]
    public async Task Update_ReportsAConflictWhenADeferredCreateFindsTheNameTaken()
    {
        const string connectionName = "create-taken";
        await CreateConnectionAsync(connectionName, TokenValue("previous-value"));

        // The manager reads the owned secret as absent (calls 1 and 2: write-target validation, then the merge), so it
        // defers a create; by the time that create runs, a concurrent writer has taken the name.
        var finds = 0;
        _secretHooks.HideFind = name => name == "connection:create-taken:token" && ++finds <= 2;
        var conflict = await Assert.ThrowsAsync<ConnectionConflictException>(() => _manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = TokenValue("stale-value")
        }).AsTask());

        Assert.Contains($"'{HttpConnectionSecretFields.Token}'", conflict.Message);
        Assert.Equal("previous-value", await RevealAsync(connectionName));
    }

    [Fact]
    public async Task Update_CancelledAfterItsSaveStillWritesTheNewValueAndCleansUp()
    {
        using var cancellation = new CancellationTokenSource();
        const string connectionName = "cancelled-after-save";
        await CreateConnectionAsync(connectionName, InlineFields((HttpConnectionSecretFields.Token, "previous-value"), ("secondary", "secondary-value")));
        await CreateSecretAsync("cancelled-after-save-replacement", "replacement-value");

        // The save has committed by now, so the update must finish rather than report a cancellation.
        _interceptor.AfterOuterSave = () => cancellation.CancelAsync();
        await _manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "committed-value" },
                ["secondary"] = new() { ExistingSecretName = "cancelled-after-save-replacement" }
            }
        }, cancellation.Token);

        Assert.Equal("committed-value", await RevealAsync(connectionName));
        Assert.Null(await _secrets.FindAsync(Tenant, "connection:cancelled-after-save:secondary"));
    }

    [Fact]
    public async Task Update_DoesNotOverwriteAConcurrentRevoke()
    {
        await CreateConnectionAsync("raced");

        _interceptor.BeforeOuterSave = () => _manager.RevokeAsync(Tenant, "raced").AsTask();
        await Assert.ThrowsAsync<ConnectionConflictException>(() => _manager.UpdateAsync(Tenant, "raced", new UpdateConnectionRequest
        {
            DisplayName = "Hijacked"
        }).AsTask());

        var current = await _manager.FindAsync(Tenant, "raced");
        Assert.Equal(ConnectionStatuses.Revoked, current!.Status);
        Assert.NotEqual("Hijacked", current.DisplayName);
    }

    [Fact]
    public async Task Test_DoesNotOverwriteAConcurrentRevoke()
    {
        await CreateConnectionAsync("raced-test");

        _interceptor.BeforeOuterSave = () => _manager.RevokeAsync(Tenant, "raced-test").AsTask();
        await Assert.ThrowsAsync<ConnectionConflictException>(() => _manager.TestAsync(Tenant, "raced-test").AsTask());

        var current = await _manager.FindAsync(Tenant, "raced-test");
        Assert.Equal(ConnectionStatuses.Revoked, current!.Status);
        Assert.Null(current.LastTestedAt);
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

    private static ServiceProvider CreateInstrumentedProvider(SaveInterceptor interceptor, ScriptedSecretManager secretHooks, ILogger<DefaultConnectionManager> logger) =>
        ConnectionsTestComposition.CreateProvider(NamedCredentialConfiguration(), configure: services =>
        {
            services.RemoveAll<IConnectionRepository>();
            services.AddSingleton<IConnectionRepository>(_ => new InterceptingConnectionRepository(new InMemoryConnectionRepository(), interceptor));
            services.RemoveAll<ISecretManager>();
            services.AddScoped<ISecretManager>(sp =>
            {
                secretHooks.Inner = ActivatorUtilities.CreateInstance<DefaultSecretManager>(sp);
                return secretHooks;
            });
            services.AddSingleton(logger);
        });

    private static IConfiguration NamedCredentialConfiguration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Elsa:Credentials:payments:Kind"] = "configuration",
        ["Elsa:Credentials:payments:Token"] = "cfg-token",
        ["Elsa:Credentials:payments:TokenType"] = "Bearer"
    }).Build();

    private static CreateConnectionRequest BearerConnection(string name, IDictionary<string, ConnectionSecretFieldInput> secretFields) => new()
    {
        Name = name,
        AuthScheme = HttpAuthSchemes.Bearer,
        Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
        SecretFields = secretFields
    };

    private static Dictionary<string, ConnectionSecretFieldInput> TokenValue(string value) =>
        InlineFields((HttpConnectionSecretFields.Token, value));

    private static Dictionary<string, ConnectionSecretFieldInput> InlineFields(params (string Field, string Value)[] fields) =>
        fields.ToDictionary(field => field.Field, field => new ConnectionSecretFieldInput { Value = field.Value });

    private ValueTask<ConnectionMetadata> CreateConnectionAsync(string name, IDictionary<string, ConnectionSecretFieldInput>? secretFields = null) =>
        _manager.CreateAsync(Tenant, BearerConnection(name, secretFields ?? new Dictionary<string, ConnectionSecretFieldInput>()));

    private ValueTask<SecretMetadata> CreateSecretAsync(string name, string value) =>
        _secrets.CreateAsync(Tenant, new CreateSecretRequest { Name = name, Value = value });

    private async Task<string> RevealAsync(string connectionName)
    {
        await using var resolved = await _resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = connectionName });
        return (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal();
    }

    private Func<Task> ConcurrentDisplayNameUpdate(string connectionName) =>
        () => _manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest { DisplayName = "Concurrent update" }).AsTask();

    private Func<Task> RotateConcurrently(string secretName) =>
        () => _secrets.RotateAsync(Tenant, secretName, new RotateSecretRequest { Value = "concurrent-value" }).AsTask();

    /// <summary>Wraps a real secret manager and lets a test fail or hide individual calls, to script faults the real one never raises on demand.</summary>
    private sealed class ScriptedSecretManager : ISecretManager
    {
        public ISecretManager Inner { get; set; } = null!;
        public Func<string, Exception?>? OnCreate { get; set; }
        public Func<string, Exception?>? OnRotate { get; set; }
        public Func<string, Exception?>? OnDelete { get; set; }
        public Func<string, bool>? HideFind { get; set; }
        public Func<string, Exception?>? OnFind { get; set; }

        public ValueTask<SecretMetadata> CreateAsync(string tenantId, CreateSecretRequest request, CancellationToken cancellationToken = default)
        {
            if (OnCreate?.Invoke(request.Name) is { } fault)
                throw fault;
            return Inner.CreateAsync(tenantId, request, cancellationToken);
        }

        public async ValueTask<SecretMetadata?> FindAsync(string tenantId, string name, CancellationToken cancellationToken = default)
        {
            if (OnFind?.Invoke(name) is { } fault)
                throw fault;
            return HideFind?.Invoke(name) == true ? null : await Inner.FindAsync(tenantId, name, cancellationToken);
        }

        public ValueTask<Page<SecretMetadata>> ListAsync(string tenantId, SecretQuery query, CancellationToken cancellationToken = default) =>
            Inner.ListAsync(tenantId, query, cancellationToken);

        public ValueTask<SecretMetadata> UpdateAsync(string tenantId, string name, UpdateSecretMetadataRequest request, CancellationToken cancellationToken = default) =>
            Inner.UpdateAsync(tenantId, name, request, cancellationToken);

        public ValueTask<SecretMetadata> RotateAsync(string tenantId, string name, RotateSecretRequest request, CancellationToken cancellationToken = default)
        {
            if (OnRotate?.Invoke(name) is { } fault)
                throw fault;
            return Inner.RotateAsync(tenantId, name, request, cancellationToken);
        }

        public ValueTask<SecretMetadata?> RevokeAsync(string tenantId, string name, CancellationToken cancellationToken = default) =>
            Inner.RevokeAsync(tenantId, name, cancellationToken);

        public ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default)
        {
            if (OnDelete?.Invoke(name) is { } fault)
                throw fault;
            return Inner.DeleteAsync(tenantId, name, cancellationToken);
        }

        public ValueTask<SecretTestResult> TestAsync(string tenantId, string name, CancellationToken cancellationToken = default) =>
            Inner.TestAsync(tenantId, name, cancellationToken);
    }

    private sealed class SaveInterceptor
    {
        public Func<Task>? BeforeOuterSave { get; set; }
        public Func<Task>? AfterOuterSave { get; set; }
    }

    private sealed class InterceptingConnectionRepository(IConnectionRepository inner, SaveInterceptor interceptor) : IConnectionRepository
    {
        private int _saveDepth;

        public ValueTask<ConnectionDocument?> FindAsync(string tenantId, string name, CancellationToken cancellationToken = default) =>
            inner.FindAsync(tenantId, name, cancellationToken);

        public ValueTask<bool> IsSecretReferencedAsync(
            string tenantId,
            string secretName,
            string? excludedConnectionName = null,
            CancellationToken cancellationToken = default) =>
            inner.IsSecretReferencedAsync(tenantId, secretName, excludedConnectionName, cancellationToken);

        public ValueTask<IReadOnlyList<ConnectionDocument>> ListAsync(string tenantId, ConnectionQuery? query = null, CancellationToken cancellationToken = default) =>
            inner.ListAsync(tenantId, query, cancellationToken);

        public ValueTask<bool> TryAddAsync(ConnectionDocument document, CancellationToken cancellationToken = default) =>
            inner.TryAddAsync(document, cancellationToken);

        public async ValueTask SaveAsync(ConnectionDocument document, CancellationToken cancellationToken = default)
        {
            var isOuterSave = Interlocked.Increment(ref _saveDepth) == 1;
            try
            {
                if (isOuterSave)
                    await RunOnceAsync(interceptor.BeforeOuterSave, () => interceptor.BeforeOuterSave = null);

                await inner.SaveAsync(document, cancellationToken);

                if (isOuterSave)
                    await RunOnceAsync(interceptor.AfterOuterSave, () => interceptor.AfterOuterSave = null);
            }
            finally
            {
                Interlocked.Decrement(ref _saveDepth);
            }
        }

        private static async Task RunOnceAsync(Func<Task>? hook, Action clear)
        {
            clear();
            if (hook is not null)
                await hook();
        }

        public ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(tenantId, name, cancellationToken);
    }

}
