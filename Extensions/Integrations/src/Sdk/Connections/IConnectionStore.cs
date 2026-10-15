namespace Elsa.Integrations.Sdk.Connections;

/// <summary>STUB: tenant-scoped connection catalog. The connections platform (#801) replaces this.</summary>
internal interface IConnectionStore
{
    ValueTask<ConnectionRecord?> GetAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    ValueTask UpsertAsync(ConnectionRecord connection, CancellationToken cancellationToken = default);
}
