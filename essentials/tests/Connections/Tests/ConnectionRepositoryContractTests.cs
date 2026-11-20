using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Xunit;

namespace Elsa.Connections.Tests;

/// <summary>
/// The add-at-own-version rule of <see cref="IConnectionRepository.TryAddAsync"/>, the compare-and-swap contract of
/// <see cref="IConnectionRepository.SaveAsync"/> and the expected-version <c>DeleteAsync</c> that the connection manager
/// depends on. Every repository derives a test class from this one. Expected versions are read before each
/// save because a durable repository may write the new version back onto the document it was given.
/// </summary>
public abstract class ConnectionRepositoryContractTests(IConnectionRepository repository)
{
    private const string Tenant = "tenant-a";
    // A durable repository may share one database across tests, so each test gets its own name.
    private readonly string _name = $"contract-{Guid.NewGuid():N}";

    [Fact]
    public async Task SaveAsync_StoresTheDocumentAndIncrementsItsVersion()
    {
        var loaded = await AddAndLoadAsync();
        var expectedVersion = loaded.State.Version + 1;
        loaded.Definition.DisplayName = "Saved";

        await repository.SaveAsync(loaded);

        var stored = await repository.FindAsync(Tenant, _name);
        Assert.Equal(expectedVersion, stored!.State.Version);
        Assert.Equal("Saved", stored.Definition.DisplayName);
    }

    [Fact]
    public async Task SaveAsync_RejectsAStaleDocumentAndWritesNothing()
    {
        var first = await AddAndLoadAsync();
        var stale = await repository.FindAsync(Tenant, _name);
        first.Definition.DisplayName = "Winner";
        await repository.SaveAsync(first);
        var winnerVersion = (await repository.FindAsync(Tenant, _name))!.State.Version;
        stale!.Definition.DisplayName = "Loser";

        await Assert.ThrowsAsync<ConnectionConflictException>(() => repository.SaveAsync(stale).AsTask());

        var stored = await repository.FindAsync(Tenant, _name);
        Assert.Equal(winnerVersion, stored!.State.Version);
        Assert.Equal("Winner", stored.Definition.DisplayName);
    }

    [Fact]
    public async Task SaveAsync_AfterDeleteThrowsConflictAndDoesNotRecreateTheDocument()
    {
        var loaded = await AddAndLoadAsync();
        Assert.True(await repository.DeleteAsync(Tenant, _name));

        await Assert.ThrowsAsync<ConnectionConflictException>(() => repository.SaveAsync(loaded).AsTask());

        Assert.Null(await repository.FindAsync(Tenant, _name));
    }

    [Fact]
    public async Task DeleteAsync_AtTheExpectedVersionDeletesTheDocument()
    {
        var loaded = await AddAndLoadAsync();

        Assert.True(await repository.DeleteAsync(Tenant, _name, loaded.State.Version));

        Assert.Null(await repository.FindAsync(Tenant, _name));
    }

    [Theory]
    [InlineData("incarnation-a", "incarnation-b")]
    [InlineData("incarnation", "INCARNATION")]
    [InlineData("", "new-known-incarnation")]
    public async Task SaveAsync_CannotRestoreADeletedIncarnationWhenTheRevisionRepeats(string oldIdentity, string newIdentity)
    {
        var original = NewDocument(); original.Definition.IncarnationId = oldIdentity;
        original.Definition.SecretFields["token"] = new() { SecretName = "stale-secret" };
        Assert.True(await repository.TryAddAsync(original));
        var stale = (await repository.FindAsync(Tenant, _name))!;
        Assert.True(await repository.DeleteAsync(Tenant, _name));
        var replacement = NewDocument(); replacement.Definition.IncarnationId = newIdentity;
        replacement.Definition.SecretFields["token"] = new() { SecretName = "replacement-secret" };
        Assert.True(await repository.TryAddAsync(replacement));
        stale.Definition.DisplayName = "Stale overwrite";

        await Assert.ThrowsAsync<ConnectionConflictException>(() => repository.SaveAsync(stale).AsTask());

        var saved = (await repository.FindAsync(Tenant, _name))!;
        Assert.Equal(newIdentity, saved.Definition.IncarnationId);
        Assert.Equal(replacement.State.Version, saved.State.Version);
        Assert.Equal(replacement.Definition.DisplayName, saved.Definition.DisplayName);
        Assert.True(await repository.IsSecretReferencedAsync(Tenant, "replacement-secret"));
        Assert.False(await repository.IsSecretReferencedAsync(Tenant, "stale-secret"));
        saved.Definition.DisplayName = "Valid subsequent save";
        await repository.SaveAsync(saved);
        Assert.Equal("Valid subsequent save", (await repository.FindAsync(Tenant, _name))!.Definition.DisplayName);
    }

    [Fact]
    public async Task SaveAsync_CannotChangeAKnownCreationIdentityAtTheCurrentRevision()
    {
        var original = NewDocument(); original.Definition.IncarnationId = "original-incarnation";
        Assert.True(await repository.TryAddAsync(original));
        var changed = (await repository.FindAsync(Tenant, _name))!;
        changed.Definition.IncarnationId = "invented-incarnation";
        await Assert.ThrowsAsync<ConnectionConflictException>(() => repository.SaveAsync(changed).AsTask());
        Assert.Equal("original-incarnation", (await repository.FindAsync(Tenant, _name))!.Definition.IncarnationId);
    }

    [Fact]
    public async Task TryAddAsync_StoresTheDocumentAtItsOwnVersion()
    {
        var document = NewDocument();
        var addedVersion = document.State.Version;

        Assert.True(await repository.TryAddAsync(document));

        Assert.True(await repository.DeleteAsync(Tenant, _name, addedVersion));
        Assert.Null(await repository.FindAsync(Tenant, _name));
    }

    [Fact]
    public async Task DeleteAsync_AtAnOlderVersionKeepsTheDocumentSavedSince()
    {
        var loaded = await AddAndLoadAsync();
        var addedVersion = loaded.State.Version;
        loaded.Definition.DisplayName = "Saved since";
        await repository.SaveAsync(loaded);

        Assert.False(await repository.DeleteAsync(Tenant, _name, addedVersion));

        Assert.Equal("Saved since", (await repository.FindAsync(Tenant, _name))!.Definition.DisplayName);
    }

    [Fact]
    public async Task DeleteAsync_WithAnExpectedVersionReturnsFalseWhenThereIsNoDocument()
    {
        Assert.False(await repository.DeleteAsync(Tenant, _name, expectedVersion: 0));
    }

    private async Task<ConnectionDocument> AddAndLoadAsync()
    {
        Assert.True(await repository.TryAddAsync(NewDocument()));

        return (await repository.FindAsync(Tenant, _name))!;
    }

    private ConnectionDocument NewDocument() => new()
    {
        Definition = new ConnectionDefinition
        {
            TenantId = Tenant,
            Name = _name,
            DisplayName = _name,
            TypeName = ConnectionTypeNames.Http,
            AuthScheme = HttpAuthSchemes.Bearer
        },
        State = new ConnectionState { CreatedAt = DateTimeOffset.UnixEpoch }
    };
}
