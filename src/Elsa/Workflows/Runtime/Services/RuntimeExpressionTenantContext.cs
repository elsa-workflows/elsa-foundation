using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Lazily resolves and caches the authoritative tenant for one workflow expression context.
/// </summary>
public sealed class RuntimeExpressionTenantContext : IExpressionTenantContext
{
    private readonly Lazy<Task<string>> _tenantId;

    public RuntimeExpressionTenantContext(
        IServiceProvider serviceProvider,
        string workflowExecutionId,
        CancellationToken lifetimeCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _tenantId = new(
            () => LoadTenantIdAsync(serviceProvider, workflowExecutionId, lifetimeCancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async ValueTask<string> GetTenantIdAsync(CancellationToken cancellationToken = default) =>
        await _tenantId.Value.WaitAsync(cancellationToken);

    private static async Task<string> LoadTenantIdAsync(
        IServiceProvider serviceProvider,
        string workflowExecutionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workflowExecutionId))
            throw MissingTenant("The workflow execution identity is unavailable.");

        if (serviceProvider.GetService(typeof(IWorkflowExecutionStateStore)) is not IWorkflowExecutionStateStore stateStore)
            throw MissingTenant("The workflow execution state store is unavailable.");

        var state = await stateStore.FindAsync(workflowExecutionId, cancellationToken);
        if (state is null || !string.Equals(state.WorkflowExecutionId, workflowExecutionId, StringComparison.Ordinal))
            throw MissingTenant("The workflow execution state is unavailable.");
        if (string.IsNullOrWhiteSpace(state.TenantId))
            throw MissingTenant("The workflow execution has no tenant identity.");

        return state.TenantId;
    }

    private static InvalidOperationException MissingTenant(string detail) =>
        new($"Secret expression evaluation requires an authoritative tenant from workflow execution state. {detail}");
}
