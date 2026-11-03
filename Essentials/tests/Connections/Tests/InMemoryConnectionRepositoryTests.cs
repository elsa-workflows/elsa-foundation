using Elsa.Connections.Services;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Xunit;

namespace Elsa.Connections.Tests;

public sealed class InMemoryConnectionRepositoryTests() : ConnectionRepositoryContractTests(new InMemoryConnectionRepository())
{
    [Theory]
    [InlineData("tenant-a", "Shared", "TENANT-A", "shared")]
    [InlineData("tenant-a", "Shared", "tenant-a ", "shared")]
    [InlineData("tenant-a\u001fbranch", "shared", "tenant-a", "branch\u001fshared")]
    public async Task Independent_tenant_and_name_pairs_keep_their_own_lifecycle(string firstTenant, string firstName, string secondTenant, string secondName)
    {
        var repository = new InMemoryConnectionRepository();
        Assert.True(await repository.TryAddAsync(Document(firstTenant, firstName)));
        Assert.False(await repository.TryAddAsync(Document(firstTenant, firstName.ToUpperInvariant())));
        Assert.True(await repository.TryAddAsync(Document(secondTenant, secondName)));

        var first = (await repository.FindAsync(firstTenant, firstName.ToUpperInvariant()))!;
        first.Definition.DisplayName = "Updated first";
        await repository.SaveAsync(first);
        Assert.Equal(secondTenant, (await repository.FindAsync(secondTenant, secondName))!.Definition.DisplayName);
        Assert.Equal(firstTenant, Assert.Single(await repository.ListAsync(firstTenant)).Definition.TenantId);
        Assert.Equal(secondTenant, Assert.Single(await repository.ListAsync(secondTenant)).Definition.TenantId);

        Assert.False(await repository.DeleteAsync(firstTenant, firstName, expectedVersion: 0));
        Assert.True(await repository.DeleteAsync(firstTenant, firstName, expectedVersion: 1));
        Assert.NotNull(await repository.FindAsync(secondTenant, secondName));
        Assert.True(await repository.DeleteAsync(secondTenant, secondName, expectedVersion: 0));
    }

    [Theory]
    [InlineData("tenant-a", "shared", "TENANT-A", "shared")]
    [InlineData("tenant-a\u001fbranch", "shared", "tenant-a", "branch\u001fshared")]
    public async Task An_unstored_tenant_and_name_pair_cannot_read_save_or_delete_another(string storedTenant, string storedName, string otherTenant, string otherName)
    {
        var repository = new InMemoryConnectionRepository();
        Assert.True(await repository.TryAddAsync(Document(storedTenant, storedName)));

        Assert.Null(await repository.FindAsync(otherTenant, otherName));
        await Assert.ThrowsAsync<ConnectionConflictException>(() => repository.SaveAsync(Document(otherTenant, otherName)).AsTask());
        Assert.False(await repository.DeleteAsync(otherTenant, otherName, expectedVersion: 0));
        Assert.False(await repository.DeleteAsync(otherTenant, otherName));
        Assert.NotNull(await repository.FindAsync(storedTenant, storedName));
    }

    private static ConnectionDocument Document(string tenant, string name) => new()
    {
        Definition = new ConnectionDefinition { TenantId = tenant, Name = name, DisplayName = tenant },
        State = new ConnectionState { Version = 0 }
    };
}
