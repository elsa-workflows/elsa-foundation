namespace Elsa.Api.AspNetCore;

/// <summary>
/// The shared endpoint security convention's permission catalog.
/// </summary>
/// <remarks>
/// These names live with the shared endpoint security convention so custom hosts and identity
/// providers can grant them without referencing an individual domain API implementation. They were
/// previously housed in the first-party FastEndpoints project, which made a convention that is
/// authoring-model-neutral look FastEndpoints-specific; retiring that project moved them here,
/// beside the endpoint ownership, authoring, and security-disposition metadata they compose with.
/// </remarks>
public static class PermissionNames
{
    public const string All = "*";

    // Supported management-client APIs use action-scoped permissions.
    public const string WorkflowDesignRead = "workflow-design.read";
    public const string WorkflowDesignManage = "workflow-design.manage";
    public const string ActivityDesignRead = "activity-design.read";
    public const string ActivityDesignManage = "activity-design.manage";
    public const string ExpressionsRead = "expressions.read";
    public const string WorkflowPublishingRead = "workflow-publishing.read";
    public const string WorkflowPublishingManage = "workflow-publishing.manage";
    public const string WorkflowRuntimeRead = "workflow-runtime.read";
    public const string WorkflowRuntimeExecute = "workflow-runtime.execute";
    public const string WorkflowRuntimeManage = "workflow-runtime.manage";
    public const string ApiCapabilitiesRead = "api-capabilities.read";
    public const string Elsa3ImportRead = "elsa3-import.read";
    public const string Elsa3ImportManage = "elsa3-import.manage";
    public const string BpmnInterchangeRead = "bpmn-interchange.read";
    public const string BpmnInterchangeManage = "bpmn-interchange.manage";
}
