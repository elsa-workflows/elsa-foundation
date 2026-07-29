using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;

namespace Elsa.Persistence.Groundwork.Sqlite.Tests;

public sealed class AccessBoundGroundworkStoreCacheTests
{
    [Fact]
    public void Equal_access_bindings_share_one_materialization()
    {
        var materializations = 0;
        var cache = new AccessBoundGroundworkStoreCache<object>(
            4,
            _ =>
            {
                Interlocked.Increment(ref materializations);
                return new object();
            });
        var firstAccess = DocumentStoreAccess.Scoped(new StorageScope("tenant-a"));
        var equivalentAccess = DocumentStoreAccess.Scoped(new StorageScope("tenant-a"));

        var first = cache.GetOrCreate(firstAccess);
        var second = cache.GetOrCreate(equivalentAccess);

        Assert.Same(first, second);
        Assert.Equal(1, materializations);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Capacity_bounds_retained_access_bindings()
    {
        var materializations = 0;
        var cache = new AccessBoundGroundworkStoreCache<object>(
            2,
            _ =>
            {
                Interlocked.Increment(ref materializations);
                return new object();
            });
        var firstAccess = DocumentStoreAccess.Scoped(new StorageScope("tenant-a"));

        var first = cache.GetOrCreate(firstAccess);
        cache.GetOrCreate(DocumentStoreAccess.Scoped(new StorageScope("tenant-b")));
        cache.GetOrCreate(DocumentStoreAccess.Scoped(new StorageScope("tenant-c")));
        var rematerialized = cache.GetOrCreate(firstAccess);

        Assert.NotSame(first, rematerialized);
        Assert.Equal(4, materializations);
        Assert.InRange(cache.Count, 1, 2);
    }

    [Fact]
    public void Disabled_cache_materializes_every_access()
    {
        var materializations = 0;
        var cache = new AccessBoundGroundworkStoreCache<object>(
            0,
            _ =>
            {
                Interlocked.Increment(ref materializations);
                return new object();
            });

        var first = cache.GetOrCreate(DocumentStoreAccess.Global);
        var second = cache.GetOrCreate(DocumentStoreAccess.Global);

        Assert.NotSame(first, second);
        Assert.Equal(2, materializations);
        Assert.Equal(0, cache.Count);
    }
}
