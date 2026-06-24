using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Secrets.Persistence.Groundwork.Stores;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class GroundworkSecretRepositoryTests
{
    [Fact]
    public async Task RoundTrips_And_Lists_Secrets_By_Collection_Index()
    {
        var repository = new GroundworkSecretRepository(new InMemoryDocumentStore(SecretsStorageManifest.Create()));
        var secret = Secret("payments.api", "v1");

        Assert.True(await repository.TryAddAsync(secret));
        Assert.False(await repository.TryAddAsync(secret));

        var found = await repository.FindAsync("payments.api");
        var all = await repository.ListAsync();

        Assert.NotNull(found);
        Assert.Equal("payments.api", found!.Name);
        Assert.Equal("v1", found.LatestActiveVersion!.Payload.Value);
        Assert.Equal("payments.api", Assert.Single(all).Name);
    }

    [Fact]
    public async Task List_Excludes_Deleted_Secrets()
    {
        var repository = new GroundworkSecretRepository(new InMemoryDocumentStore(SecretsStorageManifest.Create()));
        var secret = Secret("payments.api", "v1");

        await repository.SaveAsync(secret);
        secret.Status = SecretStatus.Deleted;
        await repository.SaveAsync(secret);

        Assert.Empty(await repository.ListAsync());
        Assert.Equal(SecretStatus.Deleted, (await repository.FindAsync("payments.api"))!.Status);
    }

    private static Secret Secret(string name, string value) => new()
    {
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
