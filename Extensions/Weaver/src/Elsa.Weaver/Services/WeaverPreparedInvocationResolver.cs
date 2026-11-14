using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Services;

/// <summary>One live admission observation, held only across this dispatch and its result validation.</summary>
internal sealed record WeaverToolAdmission(IWeaverTool Tool, WeaverToolDescriptor Descriptor);

/// <summary>Rechecks a frozen preparation without executing a module or custom proposal executor.</summary>
internal sealed class WeaverPreparedInvocationResolver(
    IWeaverToolRegistry registry,
    IWeaverSessionService sessions,
    IWeaverToolAuthorizer authorizer,
    IWeaverPolicyEvaluator policyEvaluator,
    WeaverEffectsAdmission effects,
    TimeProvider clock)
{
    internal async Task<(WeaverToolAdmission? Admission, WeaverError? Error)> ResolveAsync(WeaverActionProposal proposal, CancellationToken cancellationToken)
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
        if (await effects.CheckInvocationAsync(prepared, cancellationToken: cancellationToken) is { } frozenVeto)
            return (null, frozenVeto);
        IWeaverTool? tool;
        try { tool = await registry.FindAsync(prepared.ToolName, cancellationToken); }
        catch (WeaverContributionException)
        {
            // Catalog validation precedes module dispatch. Losing a declared disclosure owner
            // is a concrete refusal, not an uncertain effect reported by the executor catch.
            return (null, new("weaver.proposal.executor_unavailable",
                $"The prepared tool '{prepared.ToolName}' or disclosure source '{prepared.DisclosureSourceId}' is unavailable with its reviewed contract.", 409));
        }
        if (tool is null)
            return (null, new("weaver.proposal.executor_unavailable", "The prepared tool is no longer installed with its reviewed contract.", 409));
        // Observe once before authority can await; a dynamic Descriptor getter is not later authority.
        var observed = tool.Descriptor;
        var descriptor = observed with
        {
            RequiredPermissions = Array.AsReadOnly(observed.RequiredPermissions.ToArray()),
            WeaverScopes = Array.AsReadOnly(observed.WeaverScopes.ToArray())
        };
        if (descriptor.Name != prepared.ToolName || descriptor.ModuleId != prepared.ModuleId || descriptor.ContractVersion != prepared.ToolContractVersion
            || descriptor.DisclosureSourceId != prepared.DisclosureSourceId || !Enum.IsDefined(descriptor.Risk)
            || descriptor.ExecutionLocation != prepared.ExecutionLocation || descriptor.Mutability != WeaverToolMutability.Mutating)
            return (null, new("weaver.proposal.executor_unavailable", "The prepared tool is no longer installed with its reviewed contract.", 409));
        if (prepared.ExecutionLocation == WeaverExecutionLocation.ClientWorkingState
            && (prepared.Client is null || !proposal.RequiresApproval || proposal.ApprovedAt is null || tool is not IWeaverClientTool))
            return (null, new("weaver.proposal.client_unavailable", "The reviewed originating client execution contract is no longer available.", 409));
        if (descriptor.Risk > prepared.Risk)
            return (null, new("weaver.proposal.contract_changed", "The operation's current risk requires a new review.", 409));
        if (session.Policy.DeniedCapabilityIds.Contains(descriptor.Name, StringComparer.OrdinalIgnoreCase))
            return (null, new("weaver.tool.denied", "Current host policy disables this tool.", 403));
        if (await effects.CheckInvocationAsync(prepared, descriptor, cancellationToken) is { } contractVeto)
            return (null, contractVeto);
        var admission = await WeaverToolAccess.AuthorizeAsync(tool, descriptor, authorizer, session.ActorId, session.TenantId, prepared.Target, cancellationToken);
        if (WeaverToolAccess.OwnerUnavailable(admission, descriptor.ModuleCodes is not null) is { } unavailable)
            return (null, unavailable);
        if (!admission.Allowed)
            return (null, new("weaver.proposal.forbidden", "Current server authority denies the prepared target.", 403));
        if (await effects.CheckInvocationAsync(prepared, descriptor, cancellationToken) is { } currentVeto)
            return (null, currentVeto);
        if (prepared.ExpiresAt <= clock.GetUtcNow())
            return (null, new("weaver.proposal.expired", "The prepared operation has expired.", 409));
        return (new(tool, descriptor), null);
    }

}
