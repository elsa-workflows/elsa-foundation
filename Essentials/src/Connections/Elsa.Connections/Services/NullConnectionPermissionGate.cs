using Elsa.Connections.Core.Contracts;

namespace Elsa.Connections.Services;

internal sealed class NullConnectionPermissionGate : IConnectionPermissionGate
{
    public ValueTask EnsureManageAsync(string tenantId, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask EnsureUseAsync(string tenantId, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask EnsureTestAsync(string tenantId, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask EnsureBindSecretAsync(string tenantId, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
