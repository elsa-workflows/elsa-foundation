using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;

namespace Elsa.Agent.Services;

internal sealed class DefaultAgentToolInvoker(
    IAgentToolRegistry registry,
    IAgentProposalService proposals,
    IAgentSessionService sessions,
    IAgentToolAuthorizer authorizer,
    IAgentPolicyEvaluator policyEvaluator,
    IAgentAuditSink auditSink,
    InMemoryAgentProposalStore proposalStore,
    TimeProvider clock) : IAgentToolInvoker
{
    public async Task<AgentToolDispatch> DispatchAsync(AgentToolInvocation invocation, AgentPolicy policy, CancellationToken cancellationToken = default)
    {
        var tool = await registry.FindAsync(invocation.ToolName, cancellationToken);
        if (tool is null)
            return await DeniedAsync("agent.tool.not_found", "The tool is not installed.", 404);
        var session = await sessions.FindAsync(invocation.SessionId, cancellationToken);
        if (session is null || session.ActorId != invocation.ActorId || session.TenantId != invocation.TenantId)
            return await DeniedAsync("agent.tool.forbidden", "The invocation does not belong to the current session actor and tenant.", 403);
        if (session.Status != AgentSessionStatus.Active || session.ExpiresAt is { } expires && expires <= clock.GetUtcNow())
            return await DeniedAsync("agent.session.closed", "The session is no longer active.", 409);
        if (string.IsNullOrWhiteSpace(invocation.TurnId) || string.IsNullOrWhiteSpace(invocation.ToolCallId))
            return await DeniedAsync("agent.tool.correlation_missing", "Tool execution requires server-owned turn and call correlation.", 400);

        var descriptor = tool.Descriptor;
        if (!Enum.IsDefined(descriptor.Mutability) || !Enum.IsDefined(descriptor.ExecutionLocation) || !Enum.IsDefined(descriptor.Risk))
            return await DeniedAsync("agent.tool.contract_invalid", "The installed tool declares an unsupported execution contract.", 409);
        if (descriptor.ExecutionLocation == AgentExecutionLocation.ClientWorkingState && tool is not IAgentClientTool)
            return await DeniedAsync("agent.tool.client_unavailable", "The installed client tool has no completion contract.", 409);
        if (descriptor.Mutability == AgentToolMutability.Mutating && !invocation.AllowMutations)
            return await DeniedAsync("agent.tool.continuation_read_only", "A receipt continuation cannot authorize another mutation.", 403);
        var availability = await policyEvaluator.EvaluateAvailabilityAsync(session.Policy, cancellationToken);
        if (!availability.Allowed || !policy.Enabled)
            return await DeniedAsync("agent.disabled", "Current host policy disables this operation.", 403);
        if (session.Policy.DeniedCapabilityIds.Contains(descriptor.Name, StringComparer.OrdinalIgnoreCase)
            || policy.DeniedCapabilityIds.Contains(descriptor.Name, StringComparer.OrdinalIgnoreCase))
            return await DeniedAsync("agent.tool.denied", "Current host policy disables this tool.", 403);
        var scopes = invocation.Context.SelectMany(x => x.AgentScopes).ToHashSet(StringComparer.Ordinal);
        if (descriptor.AgentScopes.Count > 0 && !descriptor.AgentScopes.Any(scopes.Contains))
            return await DeniedAsync("agent.tool.scope_denied", "The admitted module context does not support this tool scope.", 403);

        string callKey;
        string inputDigest;
        try
        {
            callKey = AgentInvocationIntegrity.Digest(new { invocation.SessionId, invocation.TurnId, invocation.ToolCallId });
            inputDigest = AgentInvocationIntegrity.Digest(new { invocation.ToolName, descriptor.ContractVersion, invocation.Arguments, invocation.Context, invocation.Client });
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException or ArgumentException)
        {
            return await DeniedAsync("agent.tool.arguments_invalid", "Tool inputs must be transportable JSON.", 400);
        }
        var callGate = descriptor.Mutability == AgentToolMutability.Mutating
            ? proposalStore.Gates.GetOrAdd($"call:{callKey}", _ => new(1, 1)) : null;
        if (callGate is not null)
            await callGate.WaitAsync(cancellationToken);
        try
        {
            if (proposalStore.CallProposals.TryGetValue(callKey, out var existingId) && proposalStore.CallInputs.TryGetValue(callKey, out var originalInput))
            {
                if (originalInput != inputDigest)
                    return await DeniedAsync("agent.tool.duplicate_conflict", "A repeated tool call cannot change its original arguments or context.", 409);
                var existing = await proposals.FindAsync(existingId, cancellationToken);
                if (existing?.PreparedInvocation is not { } original)
                    return await DeniedAsync("agent.tool.duplicate_unavailable", "The original prepared call is unavailable; it cannot be replayed.", 409);
                if (!(await authorizer.AuthorizeAsync(descriptor, session.ActorId, session.TenantId, original.Target, cancellationToken)).Allowed)
                    return await DeniedAsync("agent.tool.forbidden", "Current server authority does not permit access to this prepared operation.", 403);
                if (await proposals.FindResultAsync(existingId, cancellationToken) is { } retained)
                    return AgentToolDispatch.Executed(retained);
                return AgentToolDispatch.Proposed(existing);
            }

            AgentResult<AgentToolPreparation> preparation;
            try
            {
                preparation = await tool.PrepareAsync(invocation, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                return await DeniedAsync("agent.tool.preparation_failed", "The installed module could not prepare this operation.", 400);
            }
            if (!preparation.Succeeded || preparation.Value is null)
                return await DeniedAsync(preparation.Error?.Code ?? "agent.tool.preparation_failed", preparation.Error?.Message ?? "The module refused this operation.", preparation.Error?.StatusCode ?? 400);

            var module = preparation.Value;
            var isMutation = descriptor.Mutability == AgentToolMutability.Mutating;
            if (!Enum.IsDefined(module.Risk))
                return await DeniedAsync("agent.tool.preparation_invalid", "The module returned an unsupported risk classification.", 400);
            if (!isMutation && (module.RequiresApproval || descriptor.ExecutionLocation == AgentExecutionLocation.ClientWorkingState))
                return await DeniedAsync("agent.tool.reviewed_read_unavailable", "The tool requires review or a local read handler that is unavailable.", 409);
            if (module.Target is { } target && (target.ModuleId != descriptor.ModuleId || string.IsNullOrWhiteSpace(target.ResourceType))
                || isMutation && module.Target is null)
                return await DeniedAsync("agent.tool.target_invalid", "The prepared target is not owned by the registered tool.", 400);
            if (descriptor.ExecutionLocation == AgentExecutionLocation.ClientWorkingState)
            {
                var binding = module.Client;
                var matchingSnapshot = binding is not null && invocation.Context.Any(x => x.ModuleId == descriptor.ModuleId
                    && x.Origin == AgentContextOrigin.ClientSnapshot && x.Client == binding
                    && AgentInvocationIntegrity.SameTarget(x.Target, module.Target));
                if (binding is null || string.IsNullOrWhiteSpace(binding.ClientId) || string.IsNullOrWhiteSpace(binding.GenerationId)
                    || invocation.Client is not null && binding != invocation.Client || !matchingSnapshot)
                    return await DeniedAsync("agent.tool.client_binding_missing", "The view that created this proposal is no longer available for the exact target.", 409);
            }
            var admission = await authorizer.AuthorizeAsync(descriptor, session.ActorId, session.TenantId, module.Target, cancellationToken);
            if (!admission.Allowed)
                return await DeniedAsync("agent.tool.forbidden", "Current server authority does not permit the prepared target.", 403);

            AgentPreparedToolInvocation prepared;
            try
            {
                if (module.Arguments.ValueKind != System.Text.Json.JsonValueKind.Object)
                    throw new AgentContributionException("Prepared tool arguments must be a JSON object.");
                var requiresApproval = isMutation && (module.RequiresApproval || session.Policy.RequiresApprovalForMutations
                    || policy.RequiresApprovalForMutations || descriptor.ExecutionLocation == AgentExecutionLocation.ClientWorkingState);
                prepared = new(AgentProviderPrimitives.NewId(), session.Id, invocation.TurnId, invocation.ToolCallId,
                    descriptor.Name, descriptor.ContractVersion, descriptor.ModuleId, session.ActorId, session.TenantId,
                    module.Target, AgentInvocationIntegrity.Freeze(module.Arguments), string.Empty,
                    module.Risk > descriptor.Risk ? module.Risk : descriptor.Risk, AgentInvocationIntegrity.Freeze(module.Preview),
                    descriptor.ExecutionLocation, requiresApproval, module.Client, clock.GetUtcNow().AddMinutes(5));
                prepared = prepared with { ApprovalDigest = AgentInvocationIntegrity.PreparedDigest(prepared) };
            }
            catch (Exception ex) when (ex is AgentContributionException or System.Text.Json.JsonException or NotSupportedException)
            {
                return await DeniedAsync("agent.tool.preparation_invalid", "The module returned an invalid preparation contract.", 400);
            }

            if (!isMutation)
            {
                try
                {
                    var result = await tool.ExecuteAsync(prepared, cancellationToken);
                    if (!result.Succeeded || result.Value is null)
                        return await DeniedAsync(result.Error?.Code ?? "agent.tool.failed", result.Error?.Message ?? "The module refused the read.", result.Error?.StatusCode ?? 400);
                    if (result.Value.Receipt is not null || result.Value.ToolCallId != prepared.ToolCallId || result.Value.ToolName != prepared.ToolName)
                        return await DeniedAsync("agent.tool.result_invalid", "The module returned a mismatched read result.", 500);
                    var read = result.Value with { Payload = result.Value.Payload is null ? null : AgentInvocationIntegrity.Freeze(result.Value.Payload) };
                    await AuditAsync(invocation, "executed", cancellationToken);
                    return AgentToolDispatch.Executed(read);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception)
                {
                    return await DeniedAsync("agent.tool.invocation_failed", "The installed module failed while reading the resource.", 500);
                }
            }

            var now = clock.GetUtcNow();
            var proposal = new AgentActionProposal(AgentProviderPrimitives.NewId(), session.Id, null, descriptor.Name,
                "tool-invocation", descriptor.DisplayName, descriptor.Description, prepared.Risk, prepared.Target?.Revision,
                [], [], [], null, descriptor.RequiredPermissions.ToArray(), prepared.Target?.ResourceType, prepared.Target?.ResourceId,
                prepared.RequiresApproval, prepared.RequiresApproval ? AgentActionProposalStatus.AwaitingApproval : AgentActionProposalStatus.Approved,
                prepared.RequiresApproval ? null : session.ActorId, prepared.RequiresApproval ? null : now, now, now)
            { PreparedInvocation = prepared, ResultRendererId = descriptor.ResultRendererId };
            var turnKey = AgentInvocationIntegrity.Digest(new { invocation.SessionId, invocation.TurnId });
            if (proposalStore.TurnMutations.GetOrAdd(turnKey, callKey) != callKey)
                return await DeniedAsync("agent.tool.turn_mutation_limit", "Complete the current change before requesting another change.", 409);
            proposal = await proposals.AddAsync(proposal, cancellationToken);
            proposalStore.CallInputs.TryAdd(callKey, inputDigest);
            if (proposal.RequiresApproval && proposal.Status is AgentActionProposalStatus.AwaitingApproval or AgentActionProposalStatus.Edited)
            {
                await AuditAsync(invocation, "proposal-created", cancellationToken);
                return AgentToolDispatch.Proposed(proposal);
            }
            var execution = await proposals.ExecuteAsync(proposal.Id, session.ActorId, prepared.Target?.Revision, cancellationToken);
            if (execution.Succeeded && execution.Value?.ToolResult is { } applied)
            {
                await AuditAsync(invocation, applied.Succeeded ? "executed" : "uncertain", cancellationToken);
                return AgentToolDispatch.Executed(applied);
            }
            if (execution.Succeeded && execution.Value?.ClientInvocation is not null)
                return AgentToolDispatch.Proposed((await proposals.FindAsync(proposal.Id, cancellationToken))!);
            return await DeniedAsync(execution.Error?.Code ?? "agent.tool.execution_incomplete", execution.Error?.Message ?? execution.Value?.Summary ?? "The operation has no applied receipt.", execution.Error?.StatusCode ?? 409);
        }
        finally { callGate?.Release(); }

        async Task<AgentToolDispatch> DeniedAsync(string code, string message, int status)
        {
            await AuditAsync(invocation, "denied", cancellationToken);
            return AgentToolDispatch.Denied(new(code, message, status));
        }
    }

    private Task AuditAsync(AgentToolInvocation invocation, string outcome, CancellationToken cancellationToken) => auditSink.EmitAsync(new(
        AgentProviderPrimitives.NewId(), AgentAuditEventKind.ToolInvoked, invocation.SessionId, invocation.ActorId,
        "A registered tool invocation was evaluated.", clock.GetUtcNow(),
        new Dictionary<string, string> { ["toolCallId"] = invocation.ToolCallId, ["toolName"] = invocation.ToolName, ["outcome"] = outcome }), cancellationToken);
}

/// <summary>Hosts supply current identity integration explicitly; installation alone grants no authority.</summary>
internal sealed class UnavailableAgentToolAuthorizer : IAgentToolAuthorizer
{
    public Task<AgentPolicyDecision> AuthorizeAsync(AgentToolDescriptor tool, string actorId, string tenantId, AgentResourceTarget? target, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentPolicyDecision(false, [new("agent.authority.unavailable", "No current server identity authorizer is configured.")]));
}
