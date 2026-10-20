using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Services;
using Elsa.Primitives.Persistence;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    public async Task Create_ExistingSecretMustBeActiveTextAndBelongToTheCurrentTenant()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();

        await secrets.CreateAsync("tenant-b", new CreateSecretRequest { Name = "other-tenant-token", Value = "secret" });
        await secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "rsa-token",
            TypeName = SecretTypeNames.RsaKey,
            Value = "key material"
        });
        await secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "revoked-token",
            Value = "secret"
        });
        await secrets.RevokeAsync(Tenant, "revoked-token");
        await secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "expired-token",
            Value = "secret",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });

        foreach (var secretName in new[] { "missing-token", "other-tenant-token", "rsa-token", "revoked-token", "expired-token" })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => manager.CreateAsync(Tenant, new CreateConnectionRequest
            {
                Name = $"reject-{secretName}",
                AuthScheme = HttpAuthSchemes.Bearer,
                Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
                SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
                {
                    [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = secretName }
                }
            }).AsTask());

            Assert.Null(await manager.FindAsync(Tenant, $"reject-{secretName}"));
        }
    }

    [Fact]
    public async Task Create_RejectsAmbiguousUnknownAndEmptyCredentialSources()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();
        await secrets.CreateAsync(Tenant, new CreateSecretRequest { Name = "existing-token", Value = "secret" });

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
            await Assert.ThrowsAsync<ArgumentException>(() => manager.CreateAsync(Tenant, new CreateConnectionRequest
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

        var empty = await manager.CreateAsync(Tenant, new CreateConnectionRequest
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
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var resolver = scope.ServiceProvider.GetRequiredService<IConnectionResolver>();

        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "update-existing-secret",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "previous-token" }
            }
        });

        await Assert.ThrowsAsync<ArgumentException>(() => manager.UpdateAsync(Tenant, "update-existing-secret", new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = "missing-token" }
            }
        }).AsTask());

        await using var resolved = await resolver.ResolveAsync(new ConnectionResolveRequest
        {
            TenantId = Tenant,
            Name = "update-existing-secret"
        });
        Assert.Equal("previous-token", (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal());
    }

    [Fact]
    public async Task CreateAndUpdate_RejectNullCredentialFieldInputs()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();

        await Assert.ThrowsAsync<ArgumentException>(() => manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "null-secret-fields",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = null!
        }).AsTask());

        await Assert.ThrowsAsync<ArgumentException>(() => manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "null-secret-field-value",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput> { ["token"] = null! }
        }).AsTask());

        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "null-update-field-value",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "previous-token" }
            }
        });

        await Assert.ThrowsAsync<ArgumentException>(() => manager.UpdateAsync(Tenant, "null-update-field-value", new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput> { ["token"] = null! }
        }).AsTask());
    }

    [Fact]
    public async Task ExistingSecretNamedLikeOwnedSecret_IsNotDeletedWithConnection()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();
        const string secretName = "connection:prefix-owned:token";

        await secrets.CreateAsync(Tenant, new CreateSecretRequest { Name = secretName, Value = "external-value" });
        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "prefix-owned",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = secretName }
            }
        });

        Assert.True(await manager.DeleteAsync(Tenant, "prefix-owned"));
        Assert.NotNull(await secrets.FindAsync(Tenant, secretName));
    }

    [Fact]
    public async Task Rebind_DoesNotDeleteAnOwnedSecretRetainedByAnotherField()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();
        var resolver = scope.ServiceProvider.GetRequiredService<IConnectionResolver>();
        const string ownedSecretName = "connection:retained-owned:token";

        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "retained-owned",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "owned-value" }
            }
        });
        await secrets.CreateAsync(Tenant, new CreateSecretRequest { Name = "replacement-token", Value = "replacement-value" });
        await manager.UpdateAsync(Tenant, "retained-owned", new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                ["extra"] = new() { ExistingSecretName = ownedSecretName }
            }
        });

        await manager.UpdateAsync(Tenant, "retained-owned", new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = "replacement-token" }
            }
        });

        Assert.NotNull(await secrets.FindAsync(Tenant, ownedSecretName));
        await using var resolved = await resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = "retained-owned" });
        Assert.Equal("replacement-value", (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal());
    }

    [Fact]
    public async Task Update_RotatesSecretWhenTheStoredFieldProvesItIsOwned()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var resolver = scope.ServiceProvider.GetRequiredService<IConnectionResolver>();

        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "owned-rotation",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "old-value" }
            }
        });
        await manager.UpdateAsync(Tenant, "owned-rotation", new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "new-value" }
            }
        });

        await using var resolved = await resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = "owned-rotation" });
        Assert.Equal("new-value", (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal());
    }

    [Fact]
    public async Task Delete_DoesNotDeleteAnOwnedSecretReferencedByAnotherConnection()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();
        var resolver = scope.ServiceProvider.GetRequiredService<IConnectionResolver>();
        const string ownedSecretName = "connection:reference-owner:token";

        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "reference-owner",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "shared-owned-value" }
            }
        });
        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "reference-consumer",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = ownedSecretName }
            }
        });

        await secrets.CreateAsync(Tenant, new CreateSecretRequest { Name = "owner-replacement-token", Value = "replacement-value" });
        await manager.UpdateAsync(Tenant, "reference-owner", new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = "owner-replacement-token" }
            }
        });
        Assert.NotNull(await secrets.FindAsync(Tenant, ownedSecretName));

        Assert.True(await manager.DeleteAsync(Tenant, "reference-owner"));
        Assert.NotNull(await secrets.FindAsync(Tenant, ownedSecretName));
        await using var resolved = await resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = "reference-consumer" });
        Assert.Equal("shared-owned-value", (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal());
    }

    [Fact]
    public async Task InlineSecretWrites_DoNotRotateExternalSecretsAtGeneratedNames()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();

        await secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "connection:collision-create:token",
            Value = "external-create-value"
        });
        await Assert.ThrowsAsync<ArgumentException>(() => manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "collision-create",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "attempted-overwrite" }
            }
        }).AsTask());

        await secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "connection:collision-update:token",
            Value = "external-update-value"
        });
        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "collision-update",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" }
        });
        await Assert.ThrowsAsync<ArgumentException>(() => manager.UpdateAsync(Tenant, "collision-update", new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "attempted-overwrite" }
            }
        }).AsTask());

        Assert.Equal(1, (await secrets.FindAsync(Tenant, "connection:collision-create:token"))!.CurrentVersion);
        Assert.Equal(1, (await secrets.FindAsync(Tenant, "connection:collision-update:token"))!.CurrentVersion);

        await secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "connection:collision-preflight-create:extra",
            Value = "external-preflight-value"
        });
        await Assert.ThrowsAsync<ArgumentException>(() => manager.CreateAsync(Tenant, new CreateConnectionRequest
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
        Assert.Null(await secrets.FindAsync(Tenant, "connection:collision-preflight-create:token"));

        await secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "connection:collision-preflight-update:extra",
            Value = "external-preflight-value"
        });
        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "collision-preflight-update",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" }
        });
        await Assert.ThrowsAsync<ArgumentException>(() => manager.UpdateAsync(Tenant, "collision-preflight-update", new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                ["token"] = new() { Value = "must-not-be-written" },
                ["extra"] = new() { Value = "attempted-overwrite" }
            }
        }).AsTask());
        Assert.Null(await secrets.FindAsync(Tenant, "connection:collision-preflight-update:token"));
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
    public async Task Update_KeepsPreviousSecretFieldsWhenTheInputCarriesNoNewValue()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var resolver = scope.ServiceProvider.GetRequiredService<IConnectionResolver>();

        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "keep-fields",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "value-keep" }
            }
        });

        await manager.UpdateAsync(Tenant, "keep-fields", new UpdateConnectionRequest
        {
            DisplayName = "Kept",
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new()
            }
        });

        await using var resolved = await resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = "keep-fields" });
        Assert.Equal("value-keep", (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal());
        var metadata = await manager.FindAsync(Tenant, "keep-fields");
        Assert.Equal("Kept", metadata!.DisplayName);
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
    public async Task Create_CleansUpOwnedSecretsWhenALaterFieldFails()
    {
        await using var provider = ConnectionsTestComposition.CreateProvider(configure: services =>
        {
            services.RemoveAll<ISecretManager>();
            services.AddScoped<ISecretManager>(sp =>
                new FailOnNthSecretCreate(ActivatorUtilities.CreateInstance<DefaultSecretManager>(sp), failOn: 2));
        });
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.CreateAsync(Tenant, new CreateConnectionRequest
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

        Assert.Null(await manager.FindAsync(Tenant, "partial"));
        Assert.Null(await secrets.FindAsync(Tenant, "connection:partial:token"));
        Assert.Null(await secrets.FindAsync(Tenant, "connection:partial:extra"));
    }

    [Fact]
    public async Task Rebind_DeletesThePreviousOwnedSecret()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();
        var resolver = scope.ServiceProvider.GetRequiredService<IConnectionResolver>();

        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "rebind",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "owned-value" }
            }
        });
        await secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "shared-token",
            Value = "shared-value",
            TypeName = SecretTypeNames.Text
        });

        await manager.UpdateAsync(Tenant, "rebind", new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = "shared-token" }
            }
        });

        Assert.Null(await secrets.FindAsync(Tenant, "connection:rebind:token"));
        await using var resolved = await resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = "rebind" });
        Assert.Equal("shared-value", (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal());
    }

    [Fact]
    public async Task Update_SelectingTheSameOwnedSecretRetainsProvenanceAndConnectionDeleteCleansIt()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var repository = scope.ServiceProvider.GetRequiredService<IConnectionRepository>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();
        const string connectionName = "owned-rebind";
        const string secretName = "connection:owned-rebind:token";

        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = connectionName,
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "owned-value" }
            }
        });

        await manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
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

        Assert.True(await manager.DeleteAsync(Tenant, connectionName));
        Assert.Null(await secrets.FindAsync(Tenant, secretName));
    }

    [Fact]
    public async Task Create_PreservesRawWhitespaceInNonblankInlineSecretValues()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var resolver = scope.ServiceProvider.GetRequiredService<IConnectionResolver>();
        const string connectionName = "raw-credential";
        const string inputValue = "  raw value with surrounding spaces  ";

        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = connectionName,
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = inputValue }
            }
        });

        await using var resolved = await resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = connectionName });
        Assert.Equal(inputValue, (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal());
    }

    [Fact]
    public async Task Update_ConflictDuringCredentialRebindKeepsPersistedOwnedSecretActive()
    {
        var interceptor = new SaveInterceptor();
        await using var provider = CreateInterceptedProvider(interceptor);
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();
        var resolver = scope.ServiceProvider.GetRequiredService<IConnectionResolver>();
        const string connectionName = "rebind-conflict";
        const string ownedSecretName = "connection:rebind-conflict:token";

        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = connectionName,
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "previous-value" }
            }
        });
        await secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "replacement-token",
            Value = "replacement-value",
            TypeName = SecretTypeNames.Text
        });

        interceptor.BeforeOuterSave = () => manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            DisplayName = "Concurrent update"
        }).AsTask();

        await Assert.ThrowsAsync<ConnectionConflictException>(() => manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = "replacement-token" }
            }
        }).AsTask());

        var oldSecret = await secrets.FindAsync(Tenant, ownedSecretName);
        Assert.NotNull(oldSecret);
        Assert.Equal(SecretStatus.Active, oldSecret.Status);
        await using var resolved = await resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = connectionName });
        Assert.Equal("previous-value", (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal());
    }

    [Fact]
    public async Task Update_LosingTheSaveLeavesTheOwnedSecretValueUnchanged()
    {
        var interceptor = new SaveInterceptor();
        await using var provider = CreateInterceptedProvider(interceptor);
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();
        var resolver = scope.ServiceProvider.GetRequiredService<IConnectionResolver>();
        const string connectionName = "rotation-conflict";
        const string consumerName = "rotation-conflict-consumer";
        const string ownedSecretName = "connection:rotation-conflict:token";

        await manager.CreateAsync(Tenant, BearerConnection(connectionName, TokenValue("previous-value")));
        await manager.CreateAsync(Tenant, BearerConnection(consumerName, new Dictionary<string, ConnectionSecretFieldInput>
        {
            [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = ownedSecretName }
        }));

        interceptor.BeforeOuterSave = () => manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            DisplayName = "Concurrent update"
        }).AsTask();
        await Assert.ThrowsAsync<ConnectionConflictException>(() => manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = TokenValue("losing-value")
        }).AsTask());

        Assert.Equal("previous-value", await RevealAsync(resolver, connectionName));
        Assert.Equal("previous-value", await RevealAsync(resolver, consumerName));
        var secret = await secrets.FindAsync(Tenant, ownedSecretName);
        Assert.Equal(SecretStatus.Active, secret!.Status);
        Assert.Equal(1, secret.CurrentVersion);
    }

    [Fact]
    public async Task Update_SaveFailureOtherThanAConflictKeepsStoredValuesAndLeavesNewlyCreatedSecrets()
    {
        var interceptor = new SaveInterceptor();
        await using var provider = CreateInterceptedProvider(interceptor);
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();
        var resolver = scope.ServiceProvider.GetRequiredService<IConnectionResolver>();
        const string connectionName = "save-outage";

        await manager.CreateAsync(Tenant, BearerConnection(connectionName, new Dictionary<string, ConnectionSecretFieldInput>
        {
            [HttpConnectionSecretFields.Token] = new() { Value = "previous-value" },
            ["secondary"] = new() { Value = "secondary-value" }
        }));
        await secrets.CreateAsync(Tenant, new CreateSecretRequest { Name = "save-outage-replacement", Value = "replacement-value" });

        // The store may have committed before failing, so no secret is deleted. Fields moving to a new inline secret
        // have already created theirs, and it stays; only rotations of already-referenced secrets wait for the save.
        interceptor.BeforeOuterSave = () => Task.FromException(new InvalidOperationException("Simulated store outage."));
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "unsaved-value" },
                ["secondary"] = new() { ExistingSecretName = "save-outage-replacement" },
                ["extra"] = new() { Value = "extra-value" }
            }
        }).AsTask());

        Assert.Equal("Simulated store outage.", failure.Message);
        Assert.Equal("previous-value", await RevealAsync(resolver, connectionName));
        Assert.Equal(1, (await secrets.FindAsync(Tenant, "connection:save-outage:token"))!.CurrentVersion);
        Assert.NotNull(await secrets.FindAsync(Tenant, "connection:save-outage:secondary"));
        Assert.NotNull(await secrets.FindAsync(Tenant, "connection:save-outage:extra"));
    }

    [Fact]
    public async Task Update_DoesNotOverwriteAValueRotatedAfterItsSave()
    {
        var interceptor = new SaveInterceptor();
        await using var provider = CreateInterceptedProvider(interceptor);
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();
        var resolver = scope.ServiceProvider.GetRequiredService<IConnectionResolver>();
        const string connectionName = "rotated-after-save";

        await manager.CreateAsync(Tenant, BearerConnection(connectionName, TokenValue("previous-value")));

        interceptor.AfterOuterSave = () => secrets.RotateAsync(Tenant, "connection:rotated-after-save:token", new RotateSecretRequest
        {
            Value = "concurrent-value"
        }).AsTask();
        await Assert.ThrowsAsync<ConnectionConflictException>(() => manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = TokenValue("stale-value")
        }).AsTask());

        Assert.Equal("concurrent-value", await RevealAsync(resolver, connectionName));
    }

    [Fact]
    public async Task Update_CleansUpReplacedOwnedSecretsWhenARotationConflictsAfterItsSave()
    {
        var interceptor = new SaveInterceptor();
        await using var provider = CreateInterceptedProvider(interceptor);
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();
        const string connectionName = "conflict-cleanup";

        await manager.CreateAsync(Tenant, BearerConnection(connectionName, new Dictionary<string, ConnectionSecretFieldInput>
        {
            [HttpConnectionSecretFields.Token] = new() { Value = "previous-value" },
            ["secondary"] = new() { Value = "secondary-value" }
        }));
        await secrets.CreateAsync(Tenant, new CreateSecretRequest { Name = "conflict-cleanup-replacement", Value = "replacement-value" });

        interceptor.AfterOuterSave = () => secrets.RotateAsync(Tenant, "connection:conflict-cleanup:token", new RotateSecretRequest
        {
            Value = "concurrent-value"
        }).AsTask();
        await Assert.ThrowsAsync<ConnectionConflictException>(() => manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "stale-value" },
                ["secondary"] = new() { ExistingSecretName = "conflict-cleanup-replacement" }
            }
        }).AsTask());

        // The save committed, so the secret it stopped referencing must not be left Active and unreferenced.
        Assert.Null(await secrets.FindAsync(Tenant, "connection:conflict-cleanup:secondary"));
    }

    [Fact]
    public async Task Update_RotatesTheOtherFieldsWhenOneRotationConflicts()
    {
        var interceptor = new SaveInterceptor();
        await using var provider = CreateInterceptedProvider(interceptor);
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();
        var resolver = scope.ServiceProvider.GetRequiredService<IConnectionResolver>();
        const string connectionName = "partial-rotation";

        await manager.CreateAsync(Tenant, BearerConnection(connectionName, new Dictionary<string, ConnectionSecretFieldInput>
        {
            [HttpConnectionSecretFields.Token] = new() { Value = "previous-value" },
            ["secondary"] = new() { Value = "secondary-previous" }
        }));

        interceptor.AfterOuterSave = () => secrets.RotateAsync(Tenant, "connection:partial-rotation:token", new RotateSecretRequest
        {
            Value = "concurrent-value"
        }).AsTask();
        var conflict = await Assert.ThrowsAsync<ConnectionConflictException>(() => manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "stale-value" },
                ["secondary"] = new() { Value = "secondary-new" }
            }
        }).AsTask());

        Assert.Equal("concurrent-value", await RevealAsync(resolver, connectionName));
        await using var resolved = await resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = connectionName });
        Assert.Equal("secondary-new", (await resolved.GetSensitiveAsync("secondary")).Reveal());
        Assert.Contains($"'{HttpConnectionSecretFields.Token}'", conflict.Message);
        Assert.DoesNotContain("'secondary'", conflict.Message);
    }

    [Fact]
    public async Task Update_CancelledAfterItsSaveStillWritesTheNewValueAndCleansUp()
    {
        var interceptor = new SaveInterceptor();
        await using var provider = CreateInterceptedProvider(interceptor);
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();
        var resolver = scope.ServiceProvider.GetRequiredService<IConnectionResolver>();
        using var cancellation = new CancellationTokenSource();
        const string connectionName = "cancelled-after-save";

        await manager.CreateAsync(Tenant, BearerConnection(connectionName, new Dictionary<string, ConnectionSecretFieldInput>
        {
            [HttpConnectionSecretFields.Token] = new() { Value = "previous-value" },
            ["secondary"] = new() { Value = "secondary-value" }
        }));
        await secrets.CreateAsync(Tenant, new CreateSecretRequest { Name = "cancelled-after-save-replacement", Value = "replacement-value" });

        // The save has committed by now, so the update must finish rather than report a cancellation.
        interceptor.AfterOuterSave = () => cancellation.CancelAsync();
        await manager.UpdateAsync(Tenant, connectionName, new UpdateConnectionRequest
        {
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { Value = "committed-value" },
                ["secondary"] = new() { ExistingSecretName = "cancelled-after-save-replacement" }
            }
        }, cancellation.Token);

        Assert.Equal("committed-value", await RevealAsync(resolver, connectionName));
        Assert.Null(await secrets.FindAsync(Tenant, "connection:cancelled-after-save:secondary"));
    }

    [Fact]
    public async Task Update_DoesNotOverwriteAConcurrentRevoke()
    {
        var interceptor = new SaveInterceptor();
        await using var provider = CreateInterceptedProvider(interceptor);
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();

        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "raced",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" }
        });

        interceptor.BeforeOuterSave = () => manager.RevokeAsync(Tenant, "raced").AsTask();
        await Assert.ThrowsAsync<ConnectionConflictException>(() => manager.UpdateAsync(Tenant, "raced", new UpdateConnectionRequest
        {
            DisplayName = "Hijacked"
        }).AsTask());

        var current = await manager.FindAsync(Tenant, "raced");
        Assert.Equal(ConnectionStatuses.Revoked, current!.Status);
        Assert.NotEqual("Hijacked", current.DisplayName);
    }

    [Fact]
    public async Task Test_DoesNotOverwriteAConcurrentRevoke()
    {
        var interceptor = new SaveInterceptor();
        await using var provider = CreateInterceptedProvider(interceptor);
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();

        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "raced-test",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" }
        });

        interceptor.BeforeOuterSave = () => manager.RevokeAsync(Tenant, "raced-test").AsTask();
        await Assert.ThrowsAsync<ConnectionConflictException>(() => manager.TestAsync(Tenant, "raced-test").AsTask());

        var current = await manager.FindAsync(Tenant, "raced-test");
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

    private static ServiceProvider CreateProvider(IConfiguration? configuration = null) =>
        ConnectionsTestComposition.CreateProvider(configuration);

    private static ServiceProvider CreateInterceptedProvider(SaveInterceptor interceptor) =>
        ConnectionsTestComposition.CreateProvider(configure: services =>
        {
            services.RemoveAll<IConnectionRepository>();
            services.AddSingleton<IConnectionRepository>(_ => new InterceptingConnectionRepository(new InMemoryConnectionRepository(), interceptor));
        });

    private static CreateConnectionRequest BearerConnection(string name, IDictionary<string, ConnectionSecretFieldInput> secretFields) => new()
    {
        Name = name,
        AuthScheme = HttpAuthSchemes.Bearer,
        Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
        SecretFields = secretFields
    };

    private static Dictionary<string, ConnectionSecretFieldInput> TokenValue(string value) => new()
    {
        [HttpConnectionSecretFields.Token] = new() { Value = value }
    };

    private static async Task<string> RevealAsync(IConnectionResolver resolver, string connectionName)
    {
        await using var resolved = await resolver.ResolveAsync(new ConnectionResolveRequest { TenantId = Tenant, Name = connectionName });
        return (await resolved.GetSensitiveAsync(HttpConnectionSecretFields.Token)).Reveal();
    }

    private sealed class FailOnNthSecretCreate(ISecretManager inner, int failOn) : ISecretManager
    {
        private int _creates;

        public ValueTask<SecretMetadata> CreateAsync(string tenantId, CreateSecretRequest request, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _creates) >= failOn)
                throw new InvalidOperationException("Simulated secret create failure.");
            return inner.CreateAsync(tenantId, request, cancellationToken);
        }

        public ValueTask<SecretMetadata?> FindAsync(string tenantId, string name, CancellationToken cancellationToken = default) =>
            inner.FindAsync(tenantId, name, cancellationToken);

        public ValueTask<Page<SecretMetadata>> ListAsync(string tenantId, SecretQuery query, CancellationToken cancellationToken = default) =>
            inner.ListAsync(tenantId, query, cancellationToken);

        public ValueTask<SecretMetadata> UpdateAsync(string tenantId, string name, UpdateSecretMetadataRequest request, CancellationToken cancellationToken = default) =>
            inner.UpdateAsync(tenantId, name, request, cancellationToken);

        public ValueTask<SecretMetadata> RotateAsync(string tenantId, string name, RotateSecretRequest request, CancellationToken cancellationToken = default) =>
            inner.RotateAsync(tenantId, name, request, cancellationToken);

        public ValueTask<SecretMetadata?> RevokeAsync(string tenantId, string name, CancellationToken cancellationToken = default) =>
            inner.RevokeAsync(tenantId, name, cancellationToken);

        public ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(tenantId, name, cancellationToken);

        public ValueTask<SecretTestResult> TestAsync(string tenantId, string name, CancellationToken cancellationToken = default) =>
            inner.TestAsync(tenantId, name, cancellationToken);
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
