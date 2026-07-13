namespace Elsa.Expressions.Core.Contracts;

/// <summary>
/// Resolves the authoritative tenant for one expression-evaluation context.
/// </summary>
/// <remarks>
/// Implementations must be scoped to the concrete execution or materialization context. They must not derive
/// tenant identity from ambient HTTP state or a process-wide default, because expression evaluation may run
/// concurrently for multiple tenants on the same worker.
/// </remarks>
public interface IExpressionTenantContext
{
    ValueTask<string> GetTenantIdAsync(CancellationToken cancellationToken = default);
}
