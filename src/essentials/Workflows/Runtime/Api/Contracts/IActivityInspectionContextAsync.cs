using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Contracts;

/// <summary>
/// Request-scope inspection authorization; structure and captured-value grants are intentionally separate.
/// Runtime readers use this contract for all permission decisions.
/// </summary>
/// <remarks>
/// <see cref="AuditSubject"/> is the stable subject identifier attributable to the request, or an empty value when
/// the request cannot be safely attributed. Raw payload resolution must fail closed for an empty value.
/// </remarks>
[ReplacementContract]
public interface IActivityInspectionContextAsync
{
    string TenantScope { get; }
    string AuditSubject { get; }
    string RequestCorrelationId { get; }

    ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default);

    ValueTask<bool> CanInspectStructureAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default);

    ValueTask<bool> CanInspectSensitiveValuesAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default);

    ValueTask<bool> CanResolveSensitiveValuePayloadsAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default);
}

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
