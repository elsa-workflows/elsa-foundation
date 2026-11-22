using Elsa.Weaver.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Exceptions;

namespace Elsa.Weaver.Workflows.Services;

internal static class WorkflowResourceAuthority
{
    /// <summary>The fixed, cause-free message of the unavailable outcome.</summary>
    internal const string UnavailableMessage = "Workflow definition authorization could not be completed.";

    internal static async ValueTask<WeaverPolicyDecision> AuthorizeAsync(IEnumerable<IWorkflowDefinitionAuthorizer>? authorizers,
        string actorId, string tenantId, WeaverResourceTarget? target, bool manage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(tenantId)
            || target is not { ModuleId: WorkflowWeaverSnapshot.ModuleId, ResourceType: WorkflowWeaverSnapshot.ResourceType, ResourceId: { Length: > 0 } definitionId })
            return Denied("weaver.workflow.resource_required", "A concrete owned workflow definition is required for this tool.");
        var current = authorizers?.ToArray() ?? [];
        // A composition fault (zero or several authorizers), not the owner-could-not-decide outcome below: it keeps
        // its own code and stays an ordinary denial.
        if (current.Length != 1)
            return Denied("weaver.workflow.authority_unavailable", "Exactly one owning workflow definition authorizer is required.");
        bool allowed;
        try
        {
            allowed = manage ? await current[0].CanManageAsync(definitionId, cancellationToken)
                : await current[0].CanReadAsync(definitionId, cancellationToken);
        }
        catch (WorkflowDefinitionAuthorizationException)
        {
            // The owner could not decide. Fail closed as unavailable, distinct from denied; neither the
            // inner cause nor the exception's message reaches the decision, so nothing private is disclosed.
            return Unavailable();
        }
        return allowed ? new(true, []) : Denied("weaver.workflow.permission_denied", "The owning workflow definition authority withholds this resource.");
    }

    /// <summary>Whether a decision is the owner-unavailable outcome rather than a denial.</summary>
    internal static bool IsUnavailable(WeaverPolicyDecision decision) =>
        !decision.Allowed && decision.Violations.Any(violation => violation.OwnerUnavailable
            && violation.Code == WorkflowDefinitionAuthorizationException.UnavailableCode);

    // The same stable code the Design API answers with, so Studio sees one code for one meaning. Marked as the owner
    // being unavailable, so the tool invoker reports it as unavailable (503) rather than as forbidden.
    private static WeaverPolicyDecision Unavailable() => new(false,
        [new(WorkflowDefinitionAuthorizationException.UnavailableCode, UnavailableMessage) { OwnerUnavailable = true }]);

    private static WeaverPolicyDecision Denied(string code, string message) => new(false, [new(code, message)]);
}
