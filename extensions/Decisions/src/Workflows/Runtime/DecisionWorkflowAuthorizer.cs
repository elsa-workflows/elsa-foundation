using Elsa.Decisions.Core;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Decisions.Workflows.Runtime;

/// <summary>Host replacement contract: authorizes the actual trusted execution subject against current grants.</summary>
[ReplacementContract]
public interface IDecisionWorkflowAuthorizer
{
    ValueTask EnsureAsync(string tenantId, WorkflowExecutionAuthoritySnapshot authority, string assetId, CancellationToken cancellationToken = default);
}

/// <summary>Uses the persisted user subject captured by the authenticated start boundary; other identity schemes replace the port.</summary>
internal sealed class DecisionWorkflowAuthorizer(ICurrentSubjectPermissionAuthorizer authorization) : IDecisionWorkflowAuthorizer
{
    public async ValueTask EnsureAsync(string tenantId, WorkflowExecutionAuthoritySnapshot authority, string assetId,
        CancellationToken cancellationToken = default)
    {
        var subject = authority.SystemIdentity;
        if (authority.Metadata.ContainsKey("elsa.identity.subject-kind") || authority.Metadata.ContainsKey("elsa.identity.subject-id"))
        {
            if (authority.Metadata.GetValueOrDefault("elsa.identity.subject-kind") != "persisted-user" ||
                authority.Metadata.GetValueOrDefault("elsa.identity.subject-id") is not { Length: > 0 } capturedSubject)
                throw new RuntimeSchedulerWorkUnavailableException("decisions.authority-unavailable", "The start boundary did not capture a supported decision execution subject.");
            subject = capturedSubject;
        }
        else if (subject is "runtime-api" or "workflow-designer-test-run" or "activity-designer-test-run" or "bpmn-runtime")
            throw new RuntimeSchedulerWorkUnavailableException("decisions.authority-unavailable", "This legacy service start has no captured decision execution subject.");
        // Direct model callers may already name a persisted user as SystemIdentity. The current
        // provider must verify it; service labels are not granted permissions by this fallback.
        var result = await authorization.AuthorizeAsync(new(tenantId, subject, DecisionPermissions.Evaluate, assetId), cancellationToken);
        if (result.Status == CurrentSubjectPermissionStatus.Unsupported)
            throw new RuntimeSchedulerWorkUnavailableException("decisions.authority-unavailable", "Current decision execution authority cannot be verified.");
        if (result.Status != CurrentSubjectPermissionStatus.Granted)
            throw new DecisionInvocationException("forbidden", "The execution subject cannot evaluate this decision.");
    }
}
