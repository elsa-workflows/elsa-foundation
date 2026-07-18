using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Contracts;

/// <summary>Request-scope inspection authorization; structure and captured-value grants are intentionally separate.</summary>
public interface IActivityExecutionInspectionAuthorizationContext
{
    string TenantScope { get; }
    string AuthorizationProfile { get; }
    /// <summary>
    /// Gets the stable subject identifier attributable to the request, or an empty value when the request
    /// cannot be safely attributed. Raw payload resolution must fail closed for an empty value.
    /// </summary>
    string AuditSubject { get; }
    string RequestCorrelationId { get; }
    bool CanInspectStructure(WorkflowExecutionState workflowExecution);
    bool CanInspectSensitiveValues(WorkflowExecutionState workflowExecution);
    bool CanResolveSensitiveValuePayloads(WorkflowExecutionState workflowExecution);
}

/// <summary>Explicit test/development adapter. Production API composition uses a fail-closed request adapter.</summary>
public sealed class AllowAllActivityExecutionInspectionAuthorizationContext : IActivityExecutionInspectionAuthorizationContext
{
    public string TenantScope => "all-tenants";
    public string AuthorizationProfile => "structure+values";
    public string AuditSubject => "allow-all";
    public string RequestCorrelationId => string.Empty;
    public bool CanInspectStructure(WorkflowExecutionState workflowExecution) => true;
    public bool CanInspectSensitiveValues(WorkflowExecutionState workflowExecution) => true;
    public bool CanResolveSensitiveValuePayloads(WorkflowExecutionState workflowExecution) => true;
}
