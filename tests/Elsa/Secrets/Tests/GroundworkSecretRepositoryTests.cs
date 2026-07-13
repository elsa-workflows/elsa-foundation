using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Secrets.Persistence.Groundwork.Stores;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class GroundworkSecretRepositoryTests
{
    private const string TenantId = "tenant-1";
    [Fact]
    public async Task RoundTrips_And_Lists_Secrets_By_Collection_Index()
    {
        var repository = new GroundworkSecretRepository(new InMemoryDocumentStore(SecretsStorageManifest.Create()));
        var secret = Secret("payments.api", "v1");

        Assert.True(await repository.TryAddAsync(secret));
        Assert.False(await repository.TryAddAsync(secret));

        var found = await repository.FindAsync(TenantId, "payments.api");
        var all = await repository.ListAsync(TenantId);

        Assert.NotNull(found);
        Assert.Equal("payments.api", found!.Name);
        Assert.Equal("v1", found.LatestActiveVersion!.Payload.Value);
        Assert.Equal("payments.api", Assert.Single(all).Name);
    }

    [Fact]
    public async Task List_Includes_Deleted_Tombstones()
    {
        var repository = new GroundworkSecretRepository(new InMemoryDocumentStore(SecretsStorageManifest.Create()));
        var secret = Secret("payments.api", "v1");

        await repository.SaveAsync(secret);
        secret.Status = SecretStatus.Deleted;
        await repository.SaveAsync(secret);

        Assert.Equal(SecretStatus.Deleted, Assert.Single(await repository.ListAsync(TenantId)).Status);
        Assert.Equal(SecretStatus.Deleted, (await repository.FindAsync(TenantId, "payments.api"))!.Status);
    }

    private static Secret Secret(string name, string value) => new()
    {
        TenantId = TenantId,
        Name = name,
        DisplayName = name,
        TypeName = SecretTypeNames.Text,
        StoreName = SecretStoreNames.Encrypted,
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
