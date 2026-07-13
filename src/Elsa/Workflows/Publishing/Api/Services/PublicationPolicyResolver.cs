using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>Resolves publication intent deterministically using request, workflow, then host precedence.</summary>
public sealed class PublicationPolicyResolver : IPublicationPolicyResolver
{
    public ResolvedPublicationAction Resolve(
        string workflowDefinitionId,
        string workflowDefinitionVersionId,
        PublicationRequestIntent? request,
        PublicationPolicy? workflowPolicy,
        PublicationPolicy hostPolicy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionVersionId);
        ArgumentNullException.ThrowIfNull(hostPolicy);

        if (workflowPolicy?.WorkflowDefinitionId is { } policyDefinitionId &&
            !StringComparer.Ordinal.Equals(policyDefinitionId, workflowDefinitionId))
            throw new PublicationPolicyResolutionException("workflow_policy_mismatch", "The workflow publication policy belongs to another workflow definition.");

        if (hostPolicy.WorkflowDefinitionId is not null)
            throw new PublicationPolicyResolutionException("invalid_host_policy", "The host publication policy cannot be scoped to a workflow definition.");

        if (request is not null)
            return ResolveRequest(workflowDefinitionId, workflowDefinitionVersionId, request, workflowPolicy ?? hostPolicy);

        var selectedPolicy = workflowPolicy ?? hostPolicy;
        if (selectedPolicy.DefaultAction == PublicationPolicyDefaultAction.RequireExplicitSlot)
            throw new PublicationPolicyResolutionException("explicit_slot_required", "This workflow requires an explicit publication slot.");

        var slotName = RequireSlotName(selectedPolicy.DefaultSlotName, "invalid_default_slot");
        return new ResolvedPublicationAction(
            workflowDefinitionId,
            workflowDefinitionVersionId,
            PublicationAction.Replace,
            slotName,
            workflowPolicy is null ? PublicationPolicySource.Host : PublicationPolicySource.Workflow,
            selectedPolicy.Revision);
    }

    private static ResolvedPublicationAction ResolveRequest(
        string workflowDefinitionId,
        string workflowDefinitionVersionId,
        PublicationRequestIntent request,
        PublicationPolicy fallbackPolicy)
    {
        var slotName = request.Action switch
        {
            PublicationAction.PublishSideBySide => RequireNamedSideBySideSlot(request.SlotName),
            PublicationAction.Replace => string.IsNullOrWhiteSpace(request.SlotName)
                ? RequireSlotName(fallbackPolicy.DefaultSlotName, "invalid_default_slot")
                : request.SlotName.Trim(),
            _ => throw new PublicationPolicyResolutionException("unsupported_action", $"Publication action '{request.Action}' is not supported.")
        };

        return new ResolvedPublicationAction(
            workflowDefinitionId,
            workflowDefinitionVersionId,
            request.Action,
            slotName,
            PublicationPolicySource.Request,
            PolicyRevision: null);
    }

    private static string RequireNamedSideBySideSlot(string? slotName)
    {
        var normalized = RequireSlotName(slotName, "named_slot_required");
        if (StringComparer.Ordinal.Equals(normalized, "default"))
            throw new PublicationPolicyResolutionException("named_slot_required", "Side-by-side publication requires a meaningful named slot other than 'default'.");
        return normalized;
    }

    private static string RequireSlotName(string? slotName, string code)
    {
        if (string.IsNullOrWhiteSpace(slotName))
            throw new PublicationPolicyResolutionException(code, "A publication slot name is required.");
        return slotName.Trim();
    }
}
