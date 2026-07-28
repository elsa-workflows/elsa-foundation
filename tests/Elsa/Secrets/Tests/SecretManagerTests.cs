using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    private readonly FixedTimeProvider _timeProvider = new(CreatedNow);
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

        _timeProvider.Set(RotatedNow);
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

        _timeProvider.Set(CreatedNow.AddHours(1));

        Assert.Empty((await _manager.ListAsync(TenantId, new SecretQuery { ActiveOnly = true })).Items);
    }

    public void Dispose() => _provider.Dispose();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public void Set(DateTimeOffset value) => _now = value;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
