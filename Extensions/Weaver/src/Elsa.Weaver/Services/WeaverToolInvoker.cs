using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Core.Services;

namespace Elsa.Weaver.Services;

internal sealed class DefaultWeaverToolInvoker(
    IWeaverToolRegistry registry,
    IWeaverProposalService proposals,
    IWeaverSessionService sessions,
    IWeaverToolAuthorizer authorizer,
    IWeaverPolicyEvaluator policyEvaluator,
    IWeaverAuditSink auditSink,
    InMemoryWeaverProposalStore proposalStore,
    TimeProvider clock) : IWeaverToolInvoker, IWeaverSessionPolicyParticipant
{
    private readonly InMemoryWeaverSessionService? _owner = sessions as InMemoryWeaverSessionService;

    InMemoryWeaverSessionService? IWeaverSessionPolicyParticipant.PolicyOwner => WeaverSessionPolicyParticipation.SameOwner(_owner, proposals);

    public async Task<WeaverToolDispatch> DispatchAsync(WeaverToolInvocation invocation, WeaverPolicy policy, CancellationToken cancellationToken = default)
    {
        var tool = await registry.FindAsync(invocation.ToolName, cancellationToken);
        if (tool is null)
            return await DeniedAsync("weaver.tool.not_found", "The tool is not installed.", 404);
        var session = await sessions.FindAsync(invocation.SessionId, cancellationToken);
        if (session is null || session.ActorId != invocation.ActorId || session.TenantId != invocation.TenantId)
            return await DeniedAsync("weaver.tool.forbidden", "The invocation does not belong to the current session actor and tenant.", 403);
        if (session.Status != WeaverSessionStatus.Active || session.ExpiresAt is { } expires && expires <= clock.GetUtcNow())
            return await DeniedAsync("weaver.session.closed", "The session is no longer active.", 409);
        if (string.IsNullOrWhiteSpace(invocation.TurnId) || string.IsNullOrWhiteSpace(invocation.ToolCallId))
            return await DeniedAsync("weaver.tool.correlation_missing", "Tool execution requires server-owned turn and call correlation.", 400);

        var descriptor = tool.Descriptor;
        if (!Enum.IsDefined(descriptor.Mutability) || !Enum.IsDefined(descriptor.ExecutionLocation) || !Enum.IsDefined(descriptor.Risk))
            return await DeniedAsync("weaver.tool.contract_invalid", "The installed tool declares an unsupported execution contract.", 409);
        if (descriptor.ExecutionLocation == WeaverExecutionLocation.ClientWorkingState && tool is not IWeaverClientTool)
            return await DeniedAsync("weaver.tool.client_unavailable", "The installed client tool has no completion contract.", 409);
        if (descriptor.Mutability == WeaverToolMutability.Mutating && !invocation.AllowMutations)
            return await DeniedAsync("weaver.tool.continuation_read_only", "A receipt continuation cannot authorize another mutation.", 403);
        var availability = await policyEvaluator.EvaluateAvailabilityAsync(session.Policy, cancellationToken);
        if (!availability.Allowed || !policy.Enabled)
            return await DeniedAsync("weaver.disabled", "Current host policy disables this operation.", 403);
        if (session.Policy.DeniedCapabilityIds.Contains(descriptor.Name, StringComparer.OrdinalIgnoreCase)
            || policy.DeniedCapabilityIds.Contains(descriptor.Name, StringComparer.OrdinalIgnoreCase))
            return await DeniedAsync("weaver.tool.denied", "Current host policy disables this tool.", 403);
        var scopes = invocation.Context.SelectMany(x => x.AgentScopes).ToHashSet(StringComparer.Ordinal);
        if (descriptor.AgentScopes.Count > 0 && !descriptor.AgentScopes.Any(scopes.Contains))
            return await DeniedAsync("weaver.tool.scope_denied", "The admitted module context does not support this tool scope.", 403);

        // A mutation's approval basis is the claim's snapshot, read after acquisition and held until a
        // proposal claim covers the work. The caller-supplied policy can only tighten it.
        var preparationClaim = descriptor.Mutability == WeaverToolMutability.Mutating && _owner is not null
            ? await _owner.AcquireAsync(session.Id, WeaverSessionClaimKind.Preparation, cancellationToken) : null;
        var approvalBasis = preparationClaim?.State.Policy ?? session.Policy;
        try
        {
            string callKey;
            string inputDigest;
            try
            {
                callKey = WeaverInvocationIntegrity.Digest(new { invocation.SessionId, invocation.TurnId, invocation.ToolCallId });
                inputDigest = WeaverInvocationIntegrity.Digest(new { invocation.ToolName, descriptor.ContractVersion, invocation.Arguments, invocation.Context, invocation.Client });
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException or ArgumentException)
            {
                return await DeniedAsync("weaver.tool.arguments_invalid", "Tool inputs must be transportable JSON.", 400);
            }
            var callGate = descriptor.Mutability == WeaverToolMutability.Mutating
                ? proposalStore.Gates.GetOrAdd($"call:{callKey}", _ => new(1, 1)) : null;
            if (callGate is not null)
                await callGate.WaitAsync(cancellationToken);
            try
            {
                if (proposalStore.CallProposals.TryGetValue(callKey, out var existingId) && proposalStore.CallInputs.TryGetValue(callKey, out var originalInput))
                {
                    if (originalInput != inputDigest)
                        return await DeniedAsync("weaver.tool.duplicate_conflict", "A repeated tool call cannot change its original arguments or context.", 409);
                    var existing = await proposals.FindAsync(existingId, cancellationToken);
                    if (existing?.PreparedInvocation is not { } original)
                        return await DeniedAsync("weaver.tool.duplicate_unavailable", "The original prepared call is unavailable; it cannot be replayed.", 409);
                    var replayAdmission = await WeaverToolAccess.AuthorizeAsync(tool, authorizer, session.ActorId, session.TenantId, original.Target, cancellationToken);
                    if (WeaverToolAccess.OwnerUnavailable(replayAdmission) is { } replayUnavailable)
                        return await DeniedAsync(replayUnavailable.Code, replayUnavailable.Message, replayUnavailable.StatusCode);
                    if (!replayAdmission.Allowed)
                        return await DeniedAsync("weaver.tool.forbidden", "Current server authority does not permit access to this prepared operation.", 403);
                    if (await proposals.FindResultAsync(existingId, cancellationToken) is { } retained)
                        return WeaverToolDispatch.Executed(WeaverResultProvenance.Stamp(retained, original));
                    return WeaverToolDispatch.Proposed(existing);
                }

                WeaverResult<WeaverToolPreparation> preparation;
                try
                {
                    preparation = await tool.PrepareAsync(invocation, cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception)
                {
                    return await DeniedAsync("weaver.tool.preparation_failed", "The installed module could not prepare this operation.", 400);
                }
                if (!preparation.Succeeded || preparation.Value is null)
                    return await DeniedAsync(preparation.Error?.Code ?? "weaver.tool.preparation_failed", preparation.Error?.Message ?? "The module refused this operation.", preparation.Error?.StatusCode ?? 400);

                var module = preparation.Value;
                var isMutation = descriptor.Mutability == WeaverToolMutability.Mutating;
                if (!Enum.IsDefined(module.Risk))
                    return await DeniedAsync("weaver.tool.preparation_invalid", "The module returned an unsupported risk classification.", 400);
                if (!isMutation && (module.RequiresApproval || descriptor.ExecutionLocation == WeaverExecutionLocation.ClientWorkingState))
                    return await DeniedAsync("weaver.tool.reviewed_read_unavailable", "The tool requires review or a local read handler that is unavailable.", 409);
                if (module.Target is { } target && (target.ModuleId != descriptor.ModuleId || string.IsNullOrWhiteSpace(target.ResourceType))
                    || isMutation && module.Target is null)
                    return await DeniedAsync("weaver.tool.target_invalid", "The prepared target is not owned by the registered tool.", 400);
                if (descriptor.ExecutionLocation == WeaverExecutionLocation.ClientWorkingState)
                {
                    var binding = module.Client;
                    var matchingSnapshot = binding is not null && invocation.Context.Any(x => x.ModuleId == descriptor.ModuleId
                        && x.Origin == WeaverContextOrigin.ClientSnapshot && x.Client == binding
                        && WeaverInvocationIntegrity.SameTarget(x.Target, module.Target));
                    if (binding is null || string.IsNullOrWhiteSpace(binding.ClientId) || string.IsNullOrWhiteSpace(binding.GenerationId)
                        || invocation.Client is not null && binding != invocation.Client || !matchingSnapshot)
                        return await DeniedAsync("weaver.tool.client_binding_missing", "The view that created this proposal is no longer available for the exact target.", 409);
                }
                var admission = await WeaverToolAccess.AuthorizeAsync(tool, authorizer, session.ActorId, session.TenantId, module.Target, cancellationToken);
                if (WeaverToolAccess.OwnerUnavailable(admission) is { } unavailable)
                    return await DeniedAsync(unavailable.Code, unavailable.Message, unavailable.StatusCode);
                if (!admission.Allowed)
                    return await DeniedAsync("weaver.tool.forbidden", "Current server authority does not permit the prepared target.", 403);

                WeaverPreparedToolInvocation prepared;
                try
                {
                    if (module.Arguments.ValueKind != System.Text.Json.JsonValueKind.Object)
                        throw new WeaverContributionException("Prepared tool arguments must be a JSON object.");
                    var requiresApproval = isMutation && (module.RequiresApproval || approvalBasis.RequiresApprovalForMutations
                        || policy.RequiresApprovalForMutations || descriptor.ExecutionLocation == WeaverExecutionLocation.ClientWorkingState);
                    prepared = new(WeaverProviderPrimitives.NewId(), session.Id, invocation.TurnId, invocation.ToolCallId,
                        descriptor.Name, descriptor.ContractVersion, descriptor.ModuleId, session.ActorId, session.TenantId,
                        module.Target, WeaverInvocationIntegrity.Freeze(module.Arguments), string.Empty,
                        module.Risk > descriptor.Risk ? module.Risk : descriptor.Risk, WeaverInvocationIntegrity.Freeze(module.Preview),
                        descriptor.ExecutionLocation, requiresApproval, module.Client, clock.GetUtcNow().AddMinutes(5))
                    { DisclosureSourceId = descriptor.DisclosureSourceId };
                    prepared = prepared with { ApprovalDigest = WeaverInvocationIntegrity.PreparedDigest(prepared) };
                }
                catch (Exception ex) when (ex is WeaverContributionException or System.Text.Json.JsonException or NotSupportedException)
                {
                    return await DeniedAsync("weaver.tool.preparation_invalid", "The module returned an invalid preparation contract.", 400);
                }

                if (!isMutation)
                {
                    try
                    {
                        var result = await tool.ExecuteAsync(prepared, cancellationToken);
                        if (!result.Succeeded || result.Value is null)
                            return await DeniedAsync(result.Error?.Code ?? "weaver.tool.failed", result.Error?.Message ?? "The module refused the read.", result.Error?.StatusCode ?? 400);
                        if (result.Value.Receipt is not null || result.Value.ToolCallId != prepared.ToolCallId || result.Value.ToolName != prepared.ToolName)
                            return await DeniedAsync("weaver.tool.result_invalid", "The module returned a mismatched read result.", 500);
                        var read = WeaverResultProvenance.Stamp(result.Value with
                        {
                            Payload = result.Value.Payload is null ? null : WeaverInvocationIntegrity.Freeze(result.Value.Payload)
                        }, prepared);
                        await AuditAsync(invocation, "executed", cancellationToken);
                        return WeaverToolDispatch.Executed(read);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception)
                    {
                        return await DeniedAsync("weaver.tool.invocation_failed", "The installed module failed while reading the resource.", 500);
                    }
                }

                var now = clock.GetUtcNow();
                var proposal = new WeaverActionProposal(WeaverProviderPrimitives.NewId(), session.Id, null, descriptor.Name,
                    "tool-invocation", descriptor.DisplayName, descriptor.Description, prepared.Risk, prepared.Target?.Revision,
                    [], [], [], null, descriptor.RequiredPermissions.ToArray(), prepared.Target?.ResourceType, prepared.Target?.ResourceId,
                    prepared.RequiresApproval, prepared.RequiresApproval ? WeaverActionProposalStatus.AwaitingApproval : WeaverActionProposalStatus.Approved,
                    prepared.RequiresApproval ? null : session.ActorId, prepared.RequiresApproval ? null : now, now, now)
                { PreparedInvocation = prepared, ResultRendererId = descriptor.ResultRendererId, PolicyRevision = preparationClaim?.State.Revision };
                var turnKey = WeaverInvocationIntegrity.Digest(new { invocation.SessionId, invocation.TurnId });
                if (proposals is InMemoryWeaverProposalService defaults && defaults.Uses(proposalStore))
                {
                    // The turn mutation, call, invocation and call-input keys are recorded with the proposal as one
                    // insert, so a failure can never leave a call registered without its input.
                    var insertion = await defaults.InsertAsync(proposal, new(turnKey, callKey, inputDigest), cancellationToken);
                    if (insertion.Proposal is null)
                        return await DeniedAsync("weaver.tool.turn_mutation_limit", "Complete the current change before requesting another change.", 409);
                    proposal = insertion.Proposal;
                }
                else
                {
                    // A replaced proposal service owns its own insertion; only the process-local keys are kept here.
                    if (proposalStore.TurnMutations.GetOrAdd(turnKey, callKey) != callKey)
                        return await DeniedAsync("weaver.tool.turn_mutation_limit", "Complete the current change before requesting another change.", 409);
                    proposal = await proposals.AddAsync(proposal, cancellationToken);
                    proposalStore.CallInputs.TryAdd(callKey, inputDigest);
                }
                if (proposal.RequiresApproval && proposal.Status is WeaverActionProposalStatus.AwaitingApproval or WeaverActionProposalStatus.Edited)
                {
                    await AuditAsync(invocation, "proposal-created", cancellationToken);
                    return WeaverToolDispatch.Proposed(proposal);
                }
                var execution = await proposals.ExecuteAsync(proposal.Id, session.ActorId, prepared.Target?.Revision, cancellationToken);
                if (execution.Succeeded && execution.Value?.ToolResult is { } applied)
                {
                    await AuditAsync(invocation, applied.Succeeded ? "executed" : "uncertain", cancellationToken);
                    return WeaverToolDispatch.Executed(WeaverResultProvenance.Stamp(applied, prepared));
                }
                if (execution.Succeeded && execution.Value?.ClientInvocation is not null)
                    return WeaverToolDispatch.Proposed((await proposals.FindAsync(proposal.Id, cancellationToken))!);
                return await DeniedAsync(execution.Error?.Code ?? "weaver.tool.execution_incomplete", execution.Error?.Message ?? execution.Value?.Summary ?? "The operation has no applied receipt.", execution.Error?.StatusCode ?? 409);
            }
            finally { callGate?.Release(); }
        }
        finally
        {
            // Every exit: refusal, duplicate replay, cancellation and audit failure. A created proposal
            // already holds its own claim, so this release leaves no idle gap.
            if (preparationClaim is not null) _owner!.Release(preparationClaim);
        }

        async Task<WeaverToolDispatch> DeniedAsync(string code, string message, int status)
        {
            await AuditAsync(invocation, "denied", cancellationToken);
            return WeaverToolDispatch.Denied(new(code, message, status));
        }
    }

    private Task AuditAsync(WeaverToolInvocation invocation, string outcome, CancellationToken cancellationToken) => auditSink.EmitAsync(new(
        WeaverProviderPrimitives.NewId(), WeaverAuditEventKind.ToolInvoked, invocation.SessionId, invocation.ActorId,
        "A registered tool invocation was evaluated.", clock.GetUtcNow(),
        new Dictionary<string, string> { ["toolCallId"] = invocation.ToolCallId, ["toolName"] = invocation.ToolName, ["outcome"] = outcome }), cancellationToken);
}

/// <summary>Hosts supply current identity integration explicitly; installation alone grants no authority.</summary>
internal sealed class UnavailableWeaverToolAuthorizer : IWeaverToolAuthorizer
{
    public Task<WeaverPolicyDecision> AuthorizeAsync(WeaverToolDescriptor tool, string actorId, string tenantId, WeaverResourceTarget? target, CancellationToken cancellationToken = default)
        => Task.FromResult(new WeaverPolicyDecision(false, [new("weaver.authority.unavailable", "No current server identity authorizer is configured.")]));
}
