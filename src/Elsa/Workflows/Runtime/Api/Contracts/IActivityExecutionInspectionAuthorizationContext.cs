using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Contracts;

/// <summary>Request-scope inspection authorization; structure and captured-value grants are intentionally separate.</summary>
public interface IActivityExecutionInspectionAuthorizationContext
{
    string TenantScope { get; }
    string AuthorizationProfile { get; }
    bool CanInspectStructure(WorkflowExecutionState workflowExecution);
    bool CanInspectSensitiveValues(WorkflowExecutionState workflowExecution);
}

/// <summary>Explicit test/development adapter. Production API composition uses a fail-closed request adapter.</summary>
public sealed class AllowAllActivityExecutionInspectionAuthorizationContext : IActivityExecutionInspectionAuthorizationContext
{
    public string TenantScope => "all-tenants";
    public string AuthorizationProfile => "structure+values";
    public bool CanInspectStructure(WorkflowExecutionState workflowExecution) => true;
    public bool CanInspectSensitiveValues(WorkflowExecutionState workflowExecution) => true;
}
