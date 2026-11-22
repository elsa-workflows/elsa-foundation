using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeHistoryStoreTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Equivalent_immutable_access_values_remain_valid_without_requiring_object_identity(bool occurrences)
    {
        await using var db = await Database.Create(); await db.Append(new Fact("equivalent-access", "activity"));
        await using var context = db.Open(); var access = new EquivalentReadAccess();
        var store = new EfRuntimeHistoryStore(context, access, Codec, Chain);
        Assert.NotSame(access.Current, access.Current); Assert.Equal(access.Current, access.Current);
        Assert.Equal(1, (await store.FindHeadAsync("run"))!.LastSequence);
        if (occurrences) Assert.Single((await store.ReadOccurrencesAsync(new("run", 1))).Items);
        else Assert.Equal("equivalent-access", Assert.Single((await store.ReadPageAsync(new("run", 1))).Items).HistoryEventId);
    }

    private sealed class EquivalentReadAccess : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("scope")); }
}
