namespace Elsa.Etl.Core.Runtime;

/// <summary>Delivers a terminal ETL result to its native parent.</summary>
/// <remarks>
/// Return a disposition only when delivery responsibility has been consumed or completion is proven.
/// Return <see langword="null"/> while the durable delivery remains owed.
/// </remarks>
public interface IEtlResultDelivery
{
    ValueTask<EtlDeliveryDisposition?> DeliverAsync(EtlDeliveryLease delivery, CancellationToken cancellationToken = default);
}
