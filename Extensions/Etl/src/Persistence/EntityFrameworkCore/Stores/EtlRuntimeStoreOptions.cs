namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

internal sealed record EtlRuntimeStoreOptions(
    TimeSpan PreparationLease,
    TimeSpan PartitionLease,
    TimeSpan DeliveryLease,
    int MaximumPartitionsPerRun,
    int MaximumPageSize);
