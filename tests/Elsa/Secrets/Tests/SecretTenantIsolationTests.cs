using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Secrets.Persistence.Groundwork.Stores;
using Elsa.Secrets.Services;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretTenantIsolationTests
{
    public static TheoryData<string, Func<ISecretRepository>> Repositories { get; } = new()
    {
        { "In-memory", () => new InMemorySecretRepository() },
        { "Groundwork", () => new GroundworkSecretRepository(new InMemoryDocumentStore(SecretsStorageManifest.Create())) }
    };

    [Theory]
    [MemberData(nameof(Repositories))]
    public async Task Same_name_is_isolated_by_authoritative_tenant(
        string _,
        Func<ISecretRepository> repositoryFactory)
    {
        var repository = repositoryFactory();
        var alpha = Secret("tenant-alpha", "payments.api", "alpha-value");
        var beta = Secret("tenant-beta", "payments.api", "beta-value");

        Assert.True(await repository.TryAddAsync(alpha));
        Assert.True(await repository.TryAddAsync(beta));

        Assert.Equal("alpha-value", (await repository.FindAsync("tenant-alpha", "payments.api"))!.LatestActiveVersion!.Payload.Value);
        Assert.Equal("beta-value", (await repository.FindAsync("tenant-beta", "payments.api"))!.LatestActiveVersion!.Payload.Value);
        Assert.Equal("tenant-alpha", Assert.Single(await repository.ListAsync("tenant-alpha")).TenantId);
        Assert.Equal("tenant-beta", Assert.Single(await repository.ListAsync("tenant-beta")).TenantId);
    }

    [Theory]
    [MemberData(nameof(Repositories))]
    public async Task Missing_tenant_is_rejected_instead_of_falling_back_to_shared_storage(
        string _,
        Func<ISecretRepository> repositoryFactory)
    {
        var repository = repositoryFactory();
        var unscoped = Secret("", "payments.api", "value");

        await Assert.ThrowsAsync<ArgumentException>(async () => await repository.TryAddAsync(unscoped));
        await Assert.ThrowsAsync<ArgumentException>(async () => await repository.FindAsync("", "payments.api"));
        await Assert.ThrowsAsync<ArgumentException>(async () => await repository.ListAsync(""));
    }

    private static Secret Secret(string tenantId, string name, string value) => new()
    {
        TenantId = tenantId,
        Name = name,
        DisplayName = name,
        Versions =
        [
            new SecretVersion
            {
                Version = 1,
                Payload = SecretPayload.FromValue(value)
            }
        ]
    };
}
