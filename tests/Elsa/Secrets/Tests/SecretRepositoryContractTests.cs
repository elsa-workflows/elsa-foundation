using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Secrets.Persistence.Groundwork.Stores;
using Elsa.Secrets.Services;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretRepositoryContractTests
{
    public static TheoryData<string, Func<ISecretRepository>> RepositoryFactories { get; } = new()
    {
        { "In-memory", () => new InMemorySecretRepository() },
        { "Groundwork", () => new GroundworkSecretRepository(new InMemoryDocumentStore(SecretsStorageManifest.Create())) }
    };

    [Theory]
    [MemberData(nameof(RepositoryFactories))]
    public async Task Find_Returns_Deleted_Tombstone(string _, Func<ISecretRepository> repositoryFactory)
    {
        var repository = repositoryFactory();
        var secret = DeletedSecret();

        await repository.SaveAsync(secret);

        Assert.Equal(SecretStatus.Deleted, (await repository.FindAsync(secret.Name))!.Status);
    }

    [Theory]
    [MemberData(nameof(RepositoryFactories))]
    public async Task List_Includes_Deleted_Tombstones(string _, Func<ISecretRepository> repositoryFactory)
    {
        var repository = repositoryFactory();
        var secret = DeletedSecret();

        await repository.SaveAsync(secret);

        Assert.Equal(SecretStatus.Deleted, Assert.Single(await repository.ListAsync()).Status);
    }

    [Theory]
    [MemberData(nameof(RepositoryFactories))]
    public async Task TryAdd_Reserves_Deleted_Name(string _, Func<ISecretRepository> repositoryFactory)
    {
        var repository = repositoryFactory();
        var secret = DeletedSecret();

        await repository.SaveAsync(secret);

        Assert.False(await repository.TryAddAsync(ActiveSecret(secret.Name)));
    }

    private static Secret DeletedSecret()
    {
        var secret = ActiveSecret("payments.api");
        secret.Status = SecretStatus.Deleted;
        return secret;
    }

    private static Secret ActiveSecret(string name) => new()
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
                Payload = SecretPayload.FromValue("v1")
            }
        ]
    };
}
