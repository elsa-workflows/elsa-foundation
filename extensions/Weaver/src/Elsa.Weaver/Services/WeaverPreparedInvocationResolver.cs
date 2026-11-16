using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Services;

/// <summary>Rechecks a frozen preparation without executing a module or custom proposal executor.</summary>
internal sealed class WeaverPreparedInvocationResolver(
    IWeaverToolRegistry registry,
    IWeaverSessionService sessions,
    IWeaverToolAuthorizer authorizer,
    IWeaverPolicyEvaluator policyEvaluator,
    TimeProvider clock)
{
    internal async Task<(IWeaverTool? Tool, WeaverError? Error)> ResolveAsync(WeaverActionProposal proposal, CancellationToken cancellationToken)
    {
        var prepared = proposal.PreparedInvocation;
        if (prepared is null)
            return (null, new("weaver.proposal.executor_unavailable", "This proposal has no concrete prepared execution contract.", 409));
        if (prepared.ExpiresAt <= clock.GetUtcNow())
            return (null, new("weaver.proposal.expired", "The prepared operation has expired.", 409));
        if (!Enum.IsDefined(prepared.Risk) || !Enum.IsDefined(prepared.ExecutionLocation))
            return (null, new("weaver.proposal.binding_invalid", "The stored execution contract is invalid.", 409));
        if (prepared.SessionId != proposal.SessionId || prepared.ModuleId != prepared.Target?.ModuleId
            || prepared.RequiresApproval != proposal.RequiresApproval
            || prepared.RequiresApproval && (proposal.ApprovedAt is null || proposal.ApprovedBy != prepared.ActorId)
            || !WeaverInvocationIntegrity.SameTarget(prepared.Target, new(prepared.ModuleId, proposal.ResourceType ?? string.Empty, proposal.ResourceId, proposal.BaseRevision))
            || WeaverInvocationIntegrity.PreparedDigest(prepared) != prepared.ApprovalDigest)
            return (null, new("weaver.proposal.binding_invalid", "The stored proposal does not match its immutable prepared operation.", 409));
        var session = await sessions.FindAsync(proposal.SessionId, cancellationToken);
        if (session is null || session.ActorId != prepared.ActorId || session.TenantId != prepared.TenantId
            || session.Status != WeaverSessionStatus.Active || session.ExpiresAt is { } expires && expires <= clock.GetUtcNow())
            return (null, new("weaver.proposal.forbidden", "The prepared session actor or tenant is no longer available.", 403));
        if (!(await policyEvaluator.EvaluateAvailabilityAsync(session.Policy, cancellationToken)).Allowed)
            return (null, new("weaver.disabled", "Current host policy disables this operation.", 403));
        // The digest-bound RequiresApproval records the approval's origin. An automatic approval only records
        // that the policy accepted at preparation allowed the operation without review; it is never a human
        // decision, so current policy that requires review refuses it before dispatch.
        if (session.Policy.RequiresApprovalForMutations
            && (!proposal.RequiresApproval || proposal.ApprovedAt is null || proposal.ApprovedBy != session.ActorId))
            return (null, new("weaver.proposal.approval_required", "Current session policy requires approval of the exact operation.", 409));
        IWeaverTool? tool;
        try { tool = await registry.FindAsync(prepared.ToolName, cancellationToken); }
        catch (WeaverContributionException)
        {
            // Catalog validation precedes module dispatch. Losing a declared disclosure owner
            // is a concrete refusal, not an uncertain effect reported by the executor catch.
            return (null, new("weaver.proposal.executor_unavailable",
                $"The prepared tool '{prepared.ToolName}' or disclosure source '{prepared.DisclosureSourceId}' is unavailable with its reviewed contract.", 409));
        }
        if (tool is null || tool.Descriptor.ModuleId != prepared.ModuleId || tool.Descriptor.ContractVersion != prepared.ToolContractVersion
            || tool.Descriptor.DisclosureSourceId != prepared.DisclosureSourceId
            || tool.Descriptor.ExecutionLocation != prepared.ExecutionLocation || tool.Descriptor.Mutability != WeaverToolMutability.Mutating)
            return (null, new("weaver.proposal.executor_unavailable", "The prepared tool is no longer installed with its reviewed contract.", 409));
        if (prepared.ExecutionLocation == WeaverExecutionLocation.ClientWorkingState
            && (prepared.Client is null || !proposal.RequiresApproval || proposal.ApprovedAt is null || tool is not IWeaverClientTool))
            return (null, new("weaver.proposal.client_unavailable", "The reviewed originating client execution contract is no longer available.", 409));
        if (tool.Descriptor.Risk > prepared.Risk)
            return (null, new("weaver.proposal.contract_changed", "The operation's current risk requires a new review.", 409));
        if (session.Policy.DeniedCapabilityIds.Contains(tool.Descriptor.Name, StringComparer.OrdinalIgnoreCase))
            return (null, new("weaver.tool.denied", "Current host policy disables this tool.", 403));
        var admission = await WeaverToolAccess.AuthorizeAsync(tool, authorizer, session.ActorId, session.TenantId, prepared.Target, cancellationToken);
        if (WeaverToolAccess.OwnerUnavailable(admission) is { } unavailable)
            return (null, unavailable);
        if (!admission.Allowed)
            return (null, new("weaver.proposal.forbidden", "Current server authority denies the prepared target.", 403));
        return (tool, null);
    }

}
