namespace Elsa.Foundation.Identity.Abstractions.Authorization;

/// <summary>
/// The permission catalog custom hosts and identity providers grant against.
/// </summary>
/// <remarks>
/// These names exist so a host or an identity provider can grant a permission without referencing
/// the domain API that enforces it. They were previously housed in the first-party FastEndpoints
/// project, which made an authoring-model-neutral convention look FastEndpoints-specific; retiring
/// that project moved them here, beside <see cref="EndpointPermissionPolicy"/>, which composes them.
/// <para>
/// The action-scoped names below still couple this catalog to the Workflows, BPMN, and Elsa 3
/// domains, which is a layering compromise inherited from the FastEndpoints project rather than a
/// deliberate design. Wave 9 established the better pattern: an owner declares its own permissions
/// class, as <c>Elsa.Workflows.Runtime.Api.Authorization.WorkflowRuntimePermissions</c> does. Only
/// <see cref="All"/> is genuinely cross-domain. Devolving the remaining names to their owners is
/// tracked as follow-up work; it is a wide consumer-side rename and does not belong in a retirement
/// unit whose contract is that observable behavior does not move.
/// </para>
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
