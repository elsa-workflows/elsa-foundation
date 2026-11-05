using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Etl.Tests;

/// <summary>A migrated ETL runtime database on one engine, for the runtime-store contracts.</summary>
public interface IEtlRuntimeTestDatabase : IAsyncDisposable
{
    /// <summary>A new context over the database, with optional interceptors for staging interleavings.</summary>
    internal EtlRuntimeDbContext CreateContext(params IInterceptor[] interceptors);

    /// <summary>A store over <paramref name="context"/> with the suite's leases and limits.</summary>
    internal EfEtlRuntimeStore CreateStore(EtlRuntimeDbContext context, TimeProvider? clock = null,
        TimeSpan? preparationLease = null, TimeSpan? partitionLease = null) =>
        EtlRuntimeTestStores.Create(context, clock, preparationLease, partitionLease);
}

/// <summary>The runtime store the contracts use: the suite's leases and limits over a given context.</summary>
internal static class EtlRuntimeTestStores
{
    public static EfEtlRuntimeStore Create(EtlRuntimeDbContext context, TimeProvider? clock = null,
        TimeSpan? preparationLease = null, TimeSpan? partitionLease = null) => new(context, clock ?? TimeProvider.System,
        new EtlRuntimeStoreOptions(preparationLease ?? TimeSpan.FromSeconds(30), partitionLease ?? TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30), MaximumPartitionsPerRun: 128, MaximumPageSize: 100));
}
