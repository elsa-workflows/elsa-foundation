using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Inspection.Core;

namespace Elsa.Workflows.Runtime.Api.Contracts;

/// <summary>Explicit test/development adapter. Production API composition uses a fail-closed request adapter.</summary>
public sealed class AllowAllActivityExecutionInspectionAuthorizationContext : IActivityInspectionContextAsync
{
    public string TenantScope => "all-tenants";
    public string AuditSubject => "allow-all";
    public string RequestCorrelationId => string.Empty;

    public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult("structure+values");

    public ValueTask<bool> CanInspectStructureAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(true);

    public ValueTask<bool> CanInspectSensitiveValuesAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(true);

    public ValueTask<bool> CanResolveSensitiveValuePayloadsAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(true);
}
