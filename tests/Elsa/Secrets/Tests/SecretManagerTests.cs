using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretManagerTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ISecretManager _manager;
    private readonly ISecretResolver _resolver;
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
        _resolver = _provider.GetRequiredService<ISecretResolver>();
        _repository = _provider.GetRequiredService<ISecretRepository>();
    }

    [Fact]
    public async Task Create_Normalizes_Name_And_Does_Not_Reveal_Value()
    {
        var created = await CreateEncryptedAsync(" Payments.Api ");

        Assert.Equal("payments.api", created.Name);
        Assert.Equal(" Payments.Api ".Trim(), created.DisplayName);
        Assert.Null(created.ExpiresAt);

        var listed = Assert.Single((await _manager.ListAsync(new SecretQuery())).Items);
        Assert.Equal("payments.api", listed.Name);
        Assert.DoesNotContain("secret-value", listed.Description ?? "");
    }

    [Fact]
    public async Task Resolve_Returns_Value_From_Encrypted_Store()
    {
        await CreateEncryptedAsync("payments.api", value: "secret-value");

        var resolved = await _resolver.ResolveAsync(new SecretReference("payments.api", SecretTypeNames.Text));

        Assert.True(resolved.Succeeded);
        Assert.Equal("secret-value", resolved.Value);
        Assert.Equal("payments.api", resolved.Metadata!.Name);
    }

    [Fact]
    public async Task Rotate_Retires_Previous_Version_And_Resolves_New_Value()
    {
        await CreateEncryptedAsync("payments.api", value: "old-value");

        var rotated = await _manager.RotateAsync("payments.api", new RotateSecretRequest { Value = "new-value" });
        var resolved = await _resolver.ResolveAsync(new SecretReference("payments.api"));

        Assert.Equal(2, rotated.CurrentVersion);
        Assert.Equal("new-value", resolved.Value);
    }

    [Fact]
    public async Task Revoke_Prevents_Runtime_Resolution()
    {
        await CreateEncryptedAsync("payments.api");

        var revoked = await _manager.RevokeAsync("payments.api");
        var resolved = await _resolver.ResolveAsync(new SecretReference("payments.api"));

        Assert.NotNull(revoked);
        Assert.Equal(SecretStatus.Revoked, (await _manager.FindAsync("payments.api"))!.Status);
        Assert.Equal(SecretStatus.Revoked, Assert.Single((await _manager.ListAsync(new SecretQuery { Status = SecretStatus.Revoked })).Items).Status);
        Assert.False(resolved.Succeeded);
        Assert.Equal(SecretResolutionFailureCode.Revoked, resolved.FailureCode);
    }

    [Fact]
    public async Task Delete_Removes_Secret_From_List()
    {
        await CreateEncryptedAsync("payments.api");

        Assert.True(await _manager.DeleteAsync("payments.api"));
        Assert.Empty((await _manager.ListAsync(new SecretQuery())).Items);
        Assert.Empty((await _manager.ListAsync(new SecretQuery { Status = SecretStatus.Deleted })).Items);
        Assert.Null(await _manager.FindAsync("payments.api"));
        Assert.False(await _manager.DeleteAsync("payments.api"));
        Assert.Equal("not-found", (await _manager.TestAsync("payments.api")).Code);

        var resolved = await _resolver.ResolveAsync(new SecretReference("payments.api"));

        Assert.Equal(SecretResolutionFailureCode.NotFound, resolved.FailureCode);
    }

    [Fact]
    public async Task Configuration_Store_Resolves_Host_Configuration_Value()
    {
        await _manager.CreateAsync(new CreateSecretRequest
        {
            Name = "external.api-key",
            StoreName = SecretStoreNames.Configuration,
            TypeName = SecretTypeNames.Text,
            ConfigurationKey = "External:ApiKey"
        });

        var resolved = await _resolver.ResolveAsync(new SecretReference("external.api-key"));

        Assert.True(resolved.Succeeded);
        Assert.Equal("from-configuration", resolved.Value);
    }

    [Fact]
    public async Task Resolve_Deleted_Tombstone_Does_Not_Disclose_Type_Or_Scope()
    {
        await _manager.CreateAsync(new CreateSecretRequest
        {
            Name = "payments.api",
            TypeName = SecretTypeNames.Text,
            StoreName = SecretStoreNames.Encrypted,
            Scope = "payments",
            Value = "secret-value"
        });

        await _manager.DeleteAsync("payments.api");

        var resolved = await _resolver.ResolveAsync(new SecretReference("payments.api", SecretTypeNames.RsaKey, "other"));

        Assert.False(resolved.Succeeded);
        Assert.Equal(SecretResolutionFailureCode.NotFound, resolved.FailureCode);
    }

    [Fact]
    public async Task Resolve_Secret_With_No_Active_Version_Returns_Inactive()
    {
        await _repository.SaveAsync(new Secret
        {
            Name = "empty.secret",
            DisplayName = "empty.secret",
            TypeName = SecretTypeNames.Text,
            StoreName = SecretStoreNames.Encrypted
        });

        var resolved = await _resolver.ResolveAsync(new SecretReference("empty.secret"));

        Assert.False(resolved.Succeeded);
        Assert.Equal(SecretResolutionFailureCode.Inactive, resolved.FailureCode);
    }

    [Fact]
    public async Task Resolve_Expired_Latest_Active_Version_Does_Not_Fallback()
    {
        await _repository.SaveAsync(new Secret
        {
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

        var resolved = await _resolver.ResolveAsync(new SecretReference("expiring.secret"));

        Assert.False(resolved.Succeeded);
        Assert.Equal(SecretResolutionFailureCode.Expired, resolved.FailureCode);
    }

    private ValueTask<SecretMetadata> CreateEncryptedAsync(string name, string value = "secret-value") =>
        _manager.CreateAsync(new CreateSecretRequest
        {
            Name = name,
            TypeName = SecretTypeNames.Text,
            StoreName = SecretStoreNames.Encrypted,
            Value = value
        });

    public void Dispose() => _provider.Dispose();
}
