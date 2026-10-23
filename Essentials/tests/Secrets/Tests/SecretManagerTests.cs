using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Exceptions;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Extensions;
using Elsa.Secrets.Tests.Support;
using Elsa.Secrets.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretManagerTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private readonly ServiceProvider _provider;
    private readonly ISecretManager _manager;
    private readonly ISecretValueResolver _resolver;
    private readonly ISecretRepository _repository;

    public SecretManagerTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Elsa:Secrets:EncryptionKey"] = "tests-only-secret-key",
                ["External:ApiKey"] = "from-configuration"
            })
            .Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration).AddSecrets(configuration);
        _provider = services.BuildServiceProvider();
        _manager = _provider.GetRequiredService<ISecretManager>();
        _resolver = _provider.GetRequiredService<ISecretValueResolver>();
        _repository = _provider.GetRequiredService<ISecretRepository>();
    }

    [Fact]
    public async Task Create_Normalizes_Name_And_Does_Not_Reveal_Value()
    {
        var created = await CreateEncryptedAsync(" Payments.Api ");

        Assert.Equal("payments.api", created.Name);
        Assert.Equal(" Payments.Api ".Trim(), created.DisplayName);
        Assert.Null(created.ExpiresAt);

        var listed = Assert.Single((await _manager.ListAsync(TenantId, new SecretQuery())).Items);
        Assert.Equal("payments.api", listed.Name);
        Assert.DoesNotContain("secret-value", listed.Description ?? "");
    }

    [Fact]
    public async Task Resolve_Returns_Value_From_Encrypted_Store()
    {
        await CreateEncryptedAsync("payments.api", value: "secret-value");

        var resolved = await _resolver.ResolveAsync(TenantId, new SecretReference("payments.api", SecretTypeNames.Text));

        Assert.True(resolved.Succeeded);
        Assert.Equal("secret-value", resolved.Value);
        Assert.Equal("payments.api", resolved.Metadata!.Name);
    }

    [Fact]
    public async Task Rotate_Retires_Previous_Version_And_Resolves_New_Value()
    {
        await CreateEncryptedAsync("payments.api", value: "old-value");

        var rotated = await _manager.RotateAsync(TenantId, "payments.api", new RotateSecretRequest { Value = "new-value" });
        var resolved = await _resolver.ResolveAsync(TenantId, new SecretReference("payments.api"));

        Assert.Equal(2, rotated.CurrentVersion);
        Assert.Equal("new-value", resolved.Value);
    }

    [Fact]
    public async Task Revoke_Prevents_Runtime_Resolution()
    {
        await CreateEncryptedAsync("payments.api");

        var revoked = await _manager.RevokeAsync(TenantId, "payments.api");
        var resolved = await _resolver.ResolveAsync(TenantId, new SecretReference("payments.api"));

        Assert.NotNull(revoked);
        Assert.Equal(SecretStatus.Revoked, (await _manager.FindAsync(TenantId, "payments.api"))!.Status);
        Assert.Equal(SecretStatus.Revoked, Assert.Single((await _manager.ListAsync(TenantId, new SecretQuery { Status = SecretStatus.Revoked })).Items).Status);
        Assert.False(resolved.Succeeded);
        Assert.Equal(SecretResolutionFailureCode.Revoked, resolved.FailureCode);
    }

    [Fact]
    public async Task Delete_Removes_Secret_From_List()
    {
        await CreateEncryptedAsync("payments.api");

        Assert.True(await _manager.DeleteAsync(TenantId, "payments.api"));
        Assert.Empty((await _manager.ListAsync(TenantId, new SecretQuery())).Items);
        Assert.Empty((await _manager.ListAsync(TenantId, new SecretQuery { Status = SecretStatus.Deleted })).Items);
        Assert.Null(await _manager.FindAsync(TenantId, "payments.api"));
        Assert.False(await _manager.DeleteAsync(TenantId, "payments.api"));
        Assert.Equal("not-found", (await _manager.TestAsync(TenantId, "payments.api")).Code);

        var resolved = await _resolver.ResolveAsync(TenantId, new SecretReference("payments.api"));

        Assert.Equal(SecretResolutionFailureCode.NotFound, resolved.FailureCode);
    }

    [Fact]
    public async Task Update_Deleted_Tombstone_Behaves_As_Not_Found()
    {
        await CreateEncryptedAsync("payments.api");
        await _manager.DeleteAsync(TenantId, "payments.api");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _manager.UpdateAsync(TenantId, "payments.api", new UpdateSecretMetadataRequest { DisplayName = "Updated" }));

        var tombstone = (await _repository.FindAsync(TenantId, "payments.api"))!;

        Assert.Equal(SecretStatus.Deleted, tombstone.Status);
        Assert.NotEqual("Updated", tombstone.DisplayName);
        Assert.Null(await _manager.FindAsync(TenantId, "payments.api"));
    }

    [Fact]
    public async Task Rotate_Deleted_Tombstone_Behaves_As_Not_Found()
    {
        await CreateEncryptedAsync("payments.api");
        await _manager.DeleteAsync(TenantId, "payments.api");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _manager.RotateAsync(TenantId, "payments.api", new RotateSecretRequest { Value = "new-value" }));

        var tombstone = (await _repository.FindAsync(TenantId, "payments.api"))!;

        Assert.Equal(SecretStatus.Deleted, tombstone.Status);
        Assert.Single(tombstone.Versions);
        Assert.Null(await _manager.FindAsync(TenantId, "payments.api"));
    }

    [Fact]
    public async Task Create_After_Delete_Starts_An_Independent_Secret_And_Keeps_The_Deleted_One_As_History()
    {
        var original = await CreateEncryptedAsync("payments.api", value: "first-value");
        await _manager.RotateAsync(TenantId, "payments.api", new RotateSecretRequest { Value = "second-value" });
        Assert.True(await _manager.DeleteAsync(TenantId, "payments.api"));

        var recreated = await CreateEncryptedAsync("payments.api", value: "third-value");

        Assert.NotEqual(original.Id, recreated.Id);
        Assert.Equal(1, recreated.CurrentVersion);
        Assert.Equal(recreated.Id, (await _manager.FindAsync(TenantId, "payments.api"))!.Id);
        Assert.Equal("third-value", (await _resolver.ResolveAsync(TenantId, new SecretReference("payments.api"))).Value);
        // Nothing of the deleted secret shows through: the new one rotates on from its own first version.
        Assert.Equal(2, (await _manager.RotateAsync(TenantId, "payments.api", new RotateSecretRequest { Value = "fourth-value" })).CurrentVersion);
        Assert.Single((await _manager.ListAsync(TenantId, new SecretQuery())).Items);

        var deleted = Assert.Single((await _repository.ListPageAsync(
            TenantId,
            new SecretRepositoryListRequest(status: SecretStatus.Deleted))).Items);
        Assert.Equal(original.Id, deleted.Id);
        Assert.Equal([1, 2], deleted.Versions.Select(version => version.Version));

        Assert.True(await _manager.DeleteAsync(TenantId, "payments.api"));
        Assert.Null(await _manager.FindAsync(TenantId, "payments.api"));
        Assert.Equal(2, (await _repository.ListPageAsync(
            TenantId,
            new SecretRepositoryListRequest(status: SecretStatus.Deleted))).TotalCount);
    }

    /// <summary>
    /// A store keys a deleted secret by its tenant id, name and id, so the manager refuses a name that leaves that key no
    /// room within <see cref="SecretNameConstraints.MaximumKeyLength"/>, whatever the store, and says how long a name the
    /// tenant can have. One at the limit is created and deleted.
    /// </summary>
    [Fact]
    public async Task Create_Refuses_A_Name_Whose_Deleted_Secret_Would_Not_Fit_The_Key()
    {
        var tenantId = SecretKeyLimit.TenantId;
        var limit = SecretKeyLimit.NameLength;
        var atLimit = await CreateEncryptedAsync(new string('a', limit), tenantId: tenantId);
        SecretKeyLimit.AssertFillsKey(atLimit.Name, atLimit.Id);
        Assert.True(await _manager.DeleteAsync(tenantId, atLimit.Name));

        var refused = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await CreateEncryptedAsync(new string('b', limit + 1), tenantId: tenantId));

        Assert.Contains($"Secret name cannot exceed {limit} characters in this tenant", refused.Message, StringComparison.Ordinal);
        Assert.Null(await _repository.FindAsync(tenantId, new string('b', limit + 1)));
    }

    [Fact]
    public async Task Create_Is_Still_Refused_While_A_Revoked_Secret_Holds_The_Name()
    {
        var original = await CreateEncryptedAsync("payments.api", value: "first-value");
        await _manager.RevokeAsync(TenantId, "payments.api");

        var refused = await Assert.ThrowsAsync<SecretNameInUseException>(async () =>
            await CreateEncryptedAsync("payments.api", value: "replacement-value"));

        Assert.Equal("A secret with the same name already exists.", refused.Message);
        Assert.Equal("payments.api", refused.SecretName);
        var holder = (await _manager.FindAsync(TenantId, "payments.api"))!;
        Assert.Equal(original.Id, holder.Id);
        Assert.Equal(SecretStatus.Revoked, holder.Status);
    }

    [Fact]
    public async Task Configuration_Store_Resolves_Host_Configuration_Value()
    {
        await _manager.CreateAsync(TenantId, new CreateSecretRequest
        {
            Name = "external.api-key",
            StoreName = SecretStoreNames.Configuration,
            TypeName = SecretTypeNames.Text,
            ConfigurationKey = "External:ApiKey"
        });

        var resolved = await _resolver.ResolveAsync(TenantId, new SecretReference("external.api-key"));

        Assert.True(resolved.Succeeded);
        Assert.Equal("from-configuration", resolved.Value);
    }

    [Fact]
    public async Task Resolve_Deleted_Tombstone_Does_Not_Disclose_Type_Or_Scope()
    {
        await _manager.CreateAsync(TenantId, new CreateSecretRequest
        {
            Name = "payments.api",
            TypeName = SecretTypeNames.Text,
            StoreName = SecretStoreNames.Encrypted,
            Scope = "payments",
            Value = "secret-value"
        });

        await _manager.DeleteAsync(TenantId, "payments.api");

        var resolved = await _resolver.ResolveAsync(TenantId, new SecretReference("payments.api", SecretTypeNames.RsaKey, "other"));

        Assert.False(resolved.Succeeded);
        Assert.Equal(SecretResolutionFailureCode.NotFound, resolved.FailureCode);
    }

    [Fact]
    public async Task Resolve_Secret_With_No_Active_Version_Returns_Inactive()
    {
        await _repository.SaveAsync(new Secret
        {
            TenantId = TenantId,
            Name = "empty.secret",
            DisplayName = "empty.secret",
            TypeName = SecretTypeNames.Text,
            StoreName = SecretStoreNames.Encrypted
        });

        var resolved = await _resolver.ResolveAsync(TenantId, new SecretReference("empty.secret"));

        Assert.False(resolved.Succeeded);
        Assert.Equal(SecretResolutionFailureCode.Inactive, resolved.FailureCode);
    }

    [Fact]
    public async Task Resolve_Expired_Latest_Active_Version_Does_Not_Fallback()
    {
        await _repository.SaveAsync(new Secret
        {
            TenantId = TenantId,
            Name = "expiring.secret",
            DisplayName = "expiring.secret",
            TypeName = SecretTypeNames.Text,
            StoreName = SecretStoreNames.Encrypted,
            Versions =
            [
                new SecretVersion
                {
                    Version = 1,
                    Payload = SecretPayload.FromValue("old-value")
                },
                new SecretVersion
                {
                    Version = 2,
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                    Payload = SecretPayload.FromValue("expired-new-value")
                }
            ]
        });

        var resolved = await _resolver.ResolveAsync(TenantId, new SecretReference("expiring.secret"));

        Assert.False(resolved.Succeeded);
        Assert.Equal(SecretResolutionFailureCode.Expired, resolved.FailureCode);
    }

    [Fact]
    public async Task Every_lifecycle_operation_remains_tenant_isolated()
    {
        await CreateEncryptedAsync("shared.name", "alpha-value", "tenant-alpha");
        await CreateEncryptedAsync("shared.name", "beta-value", "tenant-beta");

        await _manager.UpdateAsync("tenant-alpha", "shared.name", new UpdateSecretMetadataRequest { DisplayName = "Alpha" });
        await _manager.RotateAsync("tenant-alpha", "shared.name", new RotateSecretRequest { Value = "alpha-rotated" });
        await _manager.RevokeAsync("tenant-beta", "shared.name");

        Assert.Equal("Alpha", (await _manager.FindAsync("tenant-alpha", "shared.name"))!.DisplayName);
        Assert.Equal(SecretStatus.Revoked, (await _manager.FindAsync("tenant-beta", "shared.name"))!.Status);
        Assert.Equal("alpha-rotated", (await _resolver.ResolveAsync("tenant-alpha", new SecretReference("shared.name"))).Value);
        Assert.Equal(SecretResolutionFailureCode.Revoked, (await _resolver.ResolveAsync("tenant-beta", new SecretReference("shared.name"))).FailureCode);
        Assert.True((await _manager.TestAsync("tenant-alpha", "shared.name")).Succeeded);
        Assert.Equal("inactive", (await _manager.TestAsync("tenant-beta", "shared.name")).Code);
        Assert.Single((await _manager.ListAsync("tenant-alpha", new SecretQuery())).Items);
        Assert.Single((await _manager.ListAsync("tenant-beta", new SecretQuery())).Items);

        Assert.True(await _manager.DeleteAsync("tenant-alpha", "shared.name"));
        Assert.Null(await _manager.FindAsync("tenant-alpha", "shared.name"));
        Assert.Equal(SecretStatus.Revoked, (await _manager.FindAsync("tenant-beta", "shared.name"))!.Status);
    }

    [Theory]
    [InlineData("missing-store", SecretTypeNames.Text, "Secret store not found.")]
    [InlineData(SecretStoreNames.Encrypted, "missing-type", "Secret type not found.")]
    [InlineData(SecretStoreNames.Encrypted, ConfigurationOnlyType.Name, "Secret type is not compatible with the selected store.")]
    public async Task Create_Reports_A_Store_Or_Type_It_Cannot_Use_As_A_Configuration_Fault(string storeName, string typeName, string message)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Elsa:Secrets:EncryptionKey"] = "tests-only-secret-key" })
            .Build();
        await using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSecrets(configuration)
            .AddSingleton<ISecretTypeProvider, ConfigurationOnlyType>()
            .BuildServiceProvider();
        var manager = provider.GetRequiredService<ISecretManager>();

        var refused = await Assert.ThrowsAsync<SecretStoreConfigurationException>(async () => await manager.CreateAsync(TenantId, new CreateSecretRequest
        {
            Name = "payments.api",
            TypeName = typeName,
            StoreName = storeName,
            Value = "secret-value"
        }));

        // The message is unchanged from the plain InvalidOperationException this used to be.
        Assert.Equal(message, refused.Message);
        Assert.IsAssignableFrom<InvalidOperationException>(refused);
        Assert.Null(await manager.FindAsync(TenantId, "payments.api"));
    }

    private sealed class ConfigurationOnlyType() : DelegatingSecretTypeProvider(new(
        Name, "Configuration-only text", "A type no encrypted store can hold.", "text", [SecretStoreNames.Configuration]))
    {
        public const string Name = "configuration-only-text";
    }

    private ValueTask<SecretMetadata> CreateEncryptedAsync(
        string name,
        string value = "secret-value",
        string tenantId = TenantId) =>
        _manager.CreateAsync(tenantId, new CreateSecretRequest
        {
            Name = name,
            TypeName = SecretTypeNames.Text,
            StoreName = SecretStoreNames.Encrypted,
            Value = value
        });

    public void Dispose() => _provider.Dispose();
}

public sealed class SecretManagerDeterministicClockTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private static readonly DateTimeOffset CreatedNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RotatedNow = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _timeProvider = new(CreatedNow);
    private readonly ServiceProvider _provider;
    private readonly ISecretManager _manager;
    private readonly ISecretRepository _repository;

    public SecretManagerDeterministicClockTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Elsa:Secrets:EncryptionKey"] = "tests-only-secret-key" })
            .Build();

        // Registered before AddSecrets so the TryAddSingleton<TimeProvider> in AddSecrets is a no-op and this fake wins.
        var services = new ServiceCollection()
            .AddSingleton<TimeProvider>(_timeProvider)
            .AddSecrets(configuration);
        _provider = services.BuildServiceProvider();
        _manager = _provider.GetRequiredService<ISecretManager>();
        _repository = _provider.GetRequiredService<ISecretRepository>();
    }

    [Fact]
    public async Task CreateAsync_Sets_CreatedAt_From_TimeProvider_Not_UnixEpoch()
    {
        var created = await _manager.CreateAsync(TenantId, new CreateSecretRequest
        {
            Name = "payments.api",
            TypeName = SecretTypeNames.Text,
            StoreName = SecretStoreNames.Encrypted,
            Value = "secret-value"
        });

        Assert.Equal(CreatedNow, created.CreatedAt);
        Assert.NotEqual(DateTimeOffset.UnixEpoch, created.CreatedAt);

        var stored = (await _repository.FindAsync(TenantId, "payments.api"))!;
        Assert.Equal(CreatedNow, stored.CreatedAt);
        var version = Assert.Single(stored.Versions);
        Assert.Equal(CreatedNow, version.CreatedAt);
        Assert.NotEqual(DateTimeOffset.UnixEpoch, version.CreatedAt);
    }

    [Fact]
    public async Task RotateAsync_Sets_New_Version_CreatedAt_From_TimeProvider_Not_UnixEpoch()
    {
        await _manager.CreateAsync(TenantId, new CreateSecretRequest
        {
            Name = "payments.api",
            TypeName = SecretTypeNames.Text,
            StoreName = SecretStoreNames.Encrypted,
            Value = "old-value"
        });

        _timeProvider.SetUtcNow(RotatedNow);
        await _manager.RotateAsync(TenantId, "payments.api", new RotateSecretRequest { Value = "new-value" });

        var stored = (await _repository.FindAsync(TenantId, "payments.api"))!;
        var newVersion = Assert.Single(stored.Versions, x => x.Version == 2);
        Assert.Equal(RotatedNow, newVersion.CreatedAt);
        Assert.NotEqual(DateTimeOffset.UnixEpoch, newVersion.CreatedAt);

        var originalVersion = Assert.Single(stored.Versions, x => x.Version == 1);
        Assert.Equal(CreatedNow, originalVersion.CreatedAt);
    }

    [Fact]
    public async Task ListAsync_ActiveOnlyCapturesTheInjectedClockOnce()
    {
        await _manager.CreateAsync(TenantId, new CreateSecretRequest
        {
            Name = "payments.api",
            TypeName = SecretTypeNames.Text,
            StoreName = SecretStoreNames.Encrypted,
            ExpiresAt = CreatedNow.AddHours(1),
            Value = "value"
        });

        Assert.Single((await _manager.ListAsync(TenantId, new SecretQuery { ActiveOnly = true })).Items);

        _timeProvider.SetUtcNow(CreatedNow.AddHours(1));

        Assert.Empty((await _manager.ListAsync(TenantId, new SecretQuery { ActiveOnly = true })).Items);
    }

    public void Dispose() => _provider.Dispose();
}
