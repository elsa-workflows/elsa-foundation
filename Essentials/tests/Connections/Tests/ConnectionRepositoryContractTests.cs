using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Xunit;

namespace Elsa.Connections.Tests;

/// <summary>
/// The <see cref="IConnectionRepository.SaveAsync"/> compare-and-swap contract that the connection manager
/// depends on. Every repository derives a test class from this one. Expected versions are read before each
/// save because a durable repository may write the new version back onto the document it was given.
/// </summary>
public abstract class ConnectionRepositoryContractTests(IConnectionRepository repository)
{
    private const string Tenant = "tenant-a";
    // A durable repository may share one database across tests, so each test gets its own name.
    private readonly string Name = $"contract-{Guid.NewGuid():N}";

    [Fact]
    public async Task SaveAsync_StoresTheDocumentAndIncrementsItsVersion()
    {
        var loaded = await AddAndLoadAsync();
        var expectedVersion = loaded.State.Version + 1;
        loaded.Definition.DisplayName = "Saved";

        await repository.SaveAsync(loaded);

        var stored = await repository.FindAsync(Tenant, Name);
        Assert.Equal(expectedVersion, stored!.State.Version);
        Assert.Equal("Saved", stored.Definition.DisplayName);
    }

    [Fact]
    public async Task SaveAsync_RejectsAStaleDocumentAndWritesNothing()
    {
        var first = await AddAndLoadAsync();
        var stale = await repository.FindAsync(Tenant, Name);
        first.Definition.DisplayName = "Winner";
        await repository.SaveAsync(first);
        var winnerVersion = (await repository.FindAsync(Tenant, Name))!.State.Version;
        stale!.Definition.DisplayName = "Loser";

        await Assert.ThrowsAsync<ConnectionConflictException>(() => repository.SaveAsync(stale).AsTask());

        var stored = await repository.FindAsync(Tenant, Name);
        Assert.Equal(winnerVersion, stored!.State.Version);
        Assert.Equal("Winner", stored.Definition.DisplayName);
    }

    [Fact]
    public async Task SaveAsync_AfterDeleteThrowsConflictAndDoesNotRecreateTheDocument()
    {
        var loaded = await AddAndLoadAsync();
        Assert.True(await repository.DeleteAsync(Tenant, Name));

        await Assert.ThrowsAsync<ConnectionConflictException>(() => repository.SaveAsync(loaded).AsTask());

        Assert.Null(await repository.FindAsync(Tenant, Name));
    }

    private async Task<ConnectionDocument> AddAndLoadAsync()
    {
        Assert.True(await repository.TryAddAsync(new ConnectionDocument
        {
            Definition = new ConnectionDefinition
            {
                TenantId = Tenant,
                Name = Name,
                DisplayName = Name,
                TypeName = ConnectionTypeNames.Http,
                AuthScheme = HttpAuthSchemes.Bearer
            },
            State = new ConnectionState { CreatedAt = DateTimeOffset.UnixEpoch }
        }));

        return (await repository.FindAsync(Tenant, Name))!;
    }
}
