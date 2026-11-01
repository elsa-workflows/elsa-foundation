using System.Collections.Concurrent;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Microsoft.Extensions.Logging;

namespace Elsa.Agent.Services;

/// <summary>Process-lifetime data only. Scoped execution services and browser callbacks are never captured.</summary>
internal sealed class InMemoryAgentProposalStore
{
    public object Mutation { get; } = new();
    public ConcurrentDictionary<string, AgentActionProposal> Proposals { get; } = new(StringComparer.Ordinal);
    public ConcurrentDictionary<string, string> InvocationProposals { get; } = new(StringComparer.Ordinal);
    public ConcurrentDictionary<string, string> CallProposals { get; } = new(StringComparer.Ordinal);
    public ConcurrentDictionary<string, string> CallInputs { get; } = new(StringComparer.Ordinal);
    public ConcurrentDictionary<string, string> TurnMutations { get; } = new(StringComparer.Ordinal);
    public ConcurrentDictionary<string, AgentResult<AgentProposalExecutionResult>> Executions { get; } = new(StringComparer.Ordinal);
    public ConcurrentDictionary<string, AgentToolResult> Results { get; } = new(StringComparer.Ordinal);
    public ConcurrentDictionary<string, string> Acknowledgements { get; } = new(StringComparer.Ordinal);
    public ConcurrentDictionary<string, SemaphoreSlim> Gates { get; } = new(StringComparer.Ordinal);
}

internal sealed class InMemoryAgentProposalService(
    InMemoryAgentProposalStore store,
    IAgentActionProposalExecutor executor,
    AgentPreparedInvocationResolver preparedResolver,
    IAgentSessionService sessions,
    IAgentAuditSink auditSink,
    TimeProvider clock,
    ILogger<InMemoryAgentProposalService>? logger = null) : IAgentProposalService, IAgentSessionPolicyParticipant
{
    private readonly IAgentSessionPolicyOwner? _owner = sessions as IAgentSessionPolicyOwner;

    IAgentSessionPolicyOwner? IAgentSessionPolicyParticipant.PolicyOwner => _owner;

    public async Task<AgentActionProposal> AddAsync(AgentActionProposal proposal, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A human decision is recorded only by ApproveAsync. A review-required insertion that already carries
        // an approval would otherwise satisfy current review policy without any decision.
        if (proposal.RequiresApproval && (proposal.ApprovedBy is not null || proposal.ApprovedAt is not null
            || proposal.Status is not (AgentActionProposalStatus.Draft or AgentActionProposalStatus.AwaitingApproval or AgentActionProposalStatus.Edited)))
            throw new AgentContributionException("A review-required operation must be inserted awaiting review; only an approval decision records approval.");
        // The proposal claim exists before the store can expose the proposal, so a preparation claim can
        // hand off to it without an idle gap. Direct callers are covered the same way.
        var claim = _owner?.AcquireProposal(proposal.SessionId, proposal.PreparedInvocation?.ExpiresAt);
        var inserted = false;
        try
        {
            if (claim is not null && proposal.PolicyRevision is { } stamped && stamped != claim.State.Revision)
                throw new AgentSessionPolicyChangedException(proposal.SessionId, stamped, claim.State.Revision);
            lock (store.Mutation)
            {
                if (proposal.PreparedInvocation is { } prepared)
                {
                    var call = AgentInvocationIntegrity.Digest(new { prepared.SessionId, prepared.TurnId, prepared.ToolCallId });
                    if (store.CallProposals.TryGetValue(call, out var existingId))
                    {
                        var existing = store.Proposals[existingId];
                        if (existing.PreparedInvocation?.ApprovalDigest != prepared.ApprovalDigest)
                            throw new AgentContributionException("A duplicate tool call supplied conflicting prepared arguments or target.");
                        return existing;
                    }
                    if (store.InvocationProposals.ContainsKey(prepared.InvocationId))
                        throw new AgentContributionException("A prepared invocation identity is already owned by another proposal.");
                    if (!store.Proposals.TryAdd(proposal.Id, proposal))
                        throw new AgentContributionException("A proposal identity is already registered.");
                    store.InvocationProposals[prepared.InvocationId] = proposal.Id;
                    store.CallProposals[call] = proposal.Id;
                }
                else if (!store.Proposals.TryAdd(proposal.Id, proposal))
                    throw new AgentContributionException("A proposal identity is already registered.");
                inserted = true;
            }
            if (claim is not null)
            {
                inserted = _owner!.BindProposal(claim, proposal.Id);
                if (inserted && Settled(proposal)) _owner.ReleaseProposal(proposal.SessionId, proposal.Id);
            }
        }
        finally
        {
            // A duplicate or refused insertion releases only its own provisional claim.
            if (claim is not null && !inserted) _owner!.Release(claim);
        }
        await AuditAsync(proposal, AgentAuditEventKind.ProposalCreated, null, "A prepared operation was proposed.", cancellationToken);
        return proposal;
    }

    public Task<AgentActionProposal?> FindAsync(string proposalId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        store.Proposals.TryGetValue(proposalId, out var proposal);
        return Task.FromResult(proposal);
    }

    public async Task<AgentResult<AgentActionProposal>> ApproveAsync(string proposalId, string actorId, string? expectedRevision = null,
        string? comment = null, CancellationToken cancellationToken = default)
    {
        var gate = Gate(proposalId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var access = await AccessAsync(proposalId, actorId, cancellationToken);
            if (access.Error is { } error) return new(null, error);
            var proposal = access.Proposal!;
            if (!RevisionMatches(proposal, expectedRevision))
                return AgentResult<AgentActionProposal>.Failure("agent.proposal.revision_conflict", "Approval does not match the prepared revision.", 409);
            // Only a recorded review decision is idempotent. An automatic approval is not one, so approving it
            // cannot report or relabel it as a human decision.
            if (proposal.RequiresApproval && proposal.ApprovedBy == actorId
                && proposal.Status is AgentActionProposalStatus.Approved or AgentActionProposalStatus.AwaitingClient or AgentActionProposalStatus.Executed)
                return AgentResult<AgentActionProposal>.Success(proposal);
            if (proposal.Status is not AgentActionProposalStatus.AwaitingApproval and not AgentActionProposalStatus.Edited)
                return AgentResult<AgentActionProposal>.Failure("agent.proposal.invalid_state", "This operation is not awaiting review.", 409);
            if (proposal.PreparedInvocation is { } prepared && prepared.ExpiresAt <= clock.GetUtcNow())
            {
                Transition(proposal with { Status = AgentActionProposalStatus.Expired, UpdatedAt = clock.GetUtcNow() });
                return AgentResult<AgentActionProposal>.Failure("agent.proposal.expired", "The prepared operation has expired.", 409);
            }
            var now = clock.GetUtcNow();
            var approved = proposal with { Status = AgentActionProposalStatus.Approved, ApprovedBy = actorId, ApprovedAt = now, UpdatedAt = now };
            store.Proposals[proposalId] = approved;
            await AuditAsync(approved, AgentAuditEventKind.ProposalApproved, actorId, "The exact prepared operation was approved.", cancellationToken);
            return AgentResult<AgentActionProposal>.Success(approved);
        }
        finally { gate.Release(); }
    }

    public async Task<AgentResult<AgentActionProposal>> DenyAsync(string proposalId, string actorId, string? reason, CancellationToken cancellationToken = default)
    {
        var gate = Gate(proposalId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var access = await AccessAsync(proposalId, actorId, cancellationToken);
            if (access.Error is { } error) return new(null, error);
            var proposal = access.Proposal!;
            if (proposal.Status == AgentActionProposalStatus.Denied)
                return AgentResult<AgentActionProposal>.Success(proposal);
            if (proposal.Status == AgentActionProposalStatus.AwaitingClient)
            {
                var uncertain = AgentResult<AgentProposalExecutionResult>.Success(DefaultAgentActionProposalExecutor.Uncertain(proposal,
                    "The client request was already dispatched. Denial cannot establish whether a local effect occurred; the request will not be replayed."));
                StoreOutcome(proposal, uncertain);
                await AuditAsync(store.Proposals[proposalId], AgentAuditEventKind.ToolInvoked, actorId,
                    "Denial arrived after client dispatch; its effect remains uncertain.", CancellationToken.None);
                return AgentResult<AgentActionProposal>.Success(store.Proposals[proposalId]);
            }
            if (IsTerminal(proposal.Status))
                return AgentResult<AgentActionProposal>.Failure("agent.proposal.closed", "The operation is already closed.", 409);
            var denied = proposal with { Status = AgentActionProposalStatus.Denied, UpdatedAt = clock.GetUtcNow() };
            if (proposal.PreparedInvocation is { } prepared)
                store.Results[proposalId] = DefaultAgentActionProposalExecutor.LifecycleResult(prepared, "denied", "The reviewed operation was denied before dispatch.");
            Transition(denied);
            await AuditAsync(denied, AgentAuditEventKind.ProposalDenied, actorId, "The operation was denied before dispatch.", cancellationToken);
            return AgentResult<AgentActionProposal>.Success(denied);
        }
        finally { gate.Release(); }
    }

    public async Task<AgentResult<AgentProposalExecutionResult>> ExecuteAsync(string proposalId, string actorId, string? expectedRevision = null,
        CancellationToken cancellationToken = default)
    {
        var gate = Gate(proposalId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var access = await AccessAsync(proposalId, actorId, cancellationToken);
            if (access.Error is { } error) return new(null, error);
            var proposal = access.Proposal!;
            if (!RevisionMatches(proposal, expectedRevision))
                return AgentResult<AgentProposalExecutionResult>.Failure("agent.proposal.revision_conflict", "Execution does not match the prepared revision.", 409);
            if (store.Executions.TryGetValue(proposalId, out var retained))
            {
                if (retained.Value?.Status == "awaiting-client")
                {
                    // Returning this envelope can cause the originating view to apply it for the
                    // first time. Recheck current authority without rerunning a custom executor.
                    var current = await preparedResolver.ResolveAsync(proposal, cancellationToken);
                    if (current.Error is not null)
                        return StoreOutcome(proposal, AgentResult<AgentProposalExecutionResult>.Success(DefaultAgentActionProposalExecutor.Uncertain(proposal,
                            "The pending client operation no longer has its reviewed contract, authority or lifetime. Its effect is uncertain and the request will not be reissued.")));
                }
                return retained;
            }
            if (IsTerminal(proposal.Status))
                return AgentResult<AgentProposalExecutionResult>.Failure("agent.proposal.closed", "The operation is already closed.", 409);
            if (proposal.RequiresApproval && proposal.Status != AgentActionProposalStatus.Approved)
                return AgentResult<AgentProposalExecutionResult>.Failure("agent.proposal.approval_required", "The exact operation requires approval before execution.", 409);
            if (proposal.PreparedInvocation is null)
                return AgentResult<AgentProposalExecutionResult>.Failure("agent.proposal.executor_unavailable", "This proposal has no concrete prepared executor.", 409);
            // The owner marks the claim dispatched before Executing. An expired pre-dispatch claim is no
            // longer busy, so policy may have moved; it can never authorize dispatch.
            if (proposal.PreparedInvocation.ExpiresAt <= clock.GetUtcNow()
                || _owner?.MarkProposalDispatched(proposal.SessionId, proposalId) == false)
            {
                Transition(proposal with { Status = AgentActionProposalStatus.Expired, UpdatedAt = clock.GetUtcNow() });
                return AgentResult<AgentProposalExecutionResult>.Failure("agent.proposal.expired", "The prepared operation has expired.", 409);
            }
            var executing = proposal with { Status = AgentActionProposalStatus.Executing, UpdatedAt = clock.GetUtcNow() };
            store.Proposals[proposalId] = executing;
            AgentResult<AgentProposalExecutionResult> result;
            try
            {
                result = await executor.ExecuteAsync(executing, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                result = AgentResult<AgentProposalExecutionResult>.Success(DefaultAgentActionProposalExecutor.Uncertain(executing,
                    "Execution was interrupted after dispatch. Its effect is uncertain and will not be replayed."));
            }
            catch (Exception ex)
            {
                logger?.LogError("Prepared operation {InvocationId} failed with {FailureType}; its outcome is uncertain.", proposal.PreparedInvocation.InvocationId, ex.GetType().Name);
                result = AgentResult<AgentProposalExecutionResult>.Success(DefaultAgentActionProposalExecutor.Uncertain(executing,
                    "The installed executor failed after dispatch. Its effect is uncertain and will not be replayed."));
            }
            result = StoreOutcome(executing, result);
            await AuditAsync(store.Proposals[proposalId], result.Value?.Executed == true ? AgentAuditEventKind.ProposalExecuted
                    : result.Value?.Status == "awaiting-client" ? AgentAuditEventKind.ToolInvoked : AgentAuditEventKind.ProposalFailed,
                actorId, result.Value?.Executed == true ? "The owning module supplied an observed effect receipt." : "Execution remains pending, refused or uncertain.", CancellationToken.None);
            return result;
        }
        finally { gate.Release(); }
    }

    public async Task<AgentResult<AgentProposalExecutionResult>> AcknowledgeAsync(string sessionId, string invocationId, string actorId,
        AgentClientAcknowledgement acknowledgement, CancellationToken cancellationToken = default)
    {
        if (!store.InvocationProposals.TryGetValue(invocationId, out var proposalId))
            return AgentResult<AgentProposalExecutionResult>.Failure("agent.invocation.not_found", "The retained invocation was not found; it cannot be replayed.", 404);
        var gate = Gate(proposalId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var access = await AccessAsync(proposalId, actorId, cancellationToken);
            if (access.Error is { } error) return new(null, error);
            var proposal = access.Proposal!;
            if (proposal.PreparedInvocation is not { ExecutionLocation: AgentExecutionLocation.ClientWorkingState, Client: not null } prepared
                || acknowledgement.Client is null)
                return AgentResult<AgentProposalExecutionResult>.Failure("agent.invocation.client_unavailable", "The retained invocation is not a bound client operation.", 409);
            if (!Enum.IsDefined(acknowledgement.Outcome))
                return AgentResult<AgentProposalExecutionResult>.Failure("agent.invocation.outcome_invalid", "The acknowledgement outcome is unsupported.", 400);
            if (proposal.SessionId != sessionId || prepared.InvocationId != invocationId || prepared.Client != acknowledgement.Client
                || prepared.ApprovalDigest != acknowledgement.ApprovalDigest)
                return AgentResult<AgentProposalExecutionResult>.Failure("agent.invocation.binding_mismatch", "The acknowledgement does not match the retained session and approved client operation.", 409);
            var digest = AgentInvocationIntegrity.Digest(acknowledgement);
            if (store.Acknowledgements.TryGetValue(invocationId, out var accepted))
                return accepted == digest ? store.Executions[proposalId]
                    : AgentResult<AgentProposalExecutionResult>.Failure("agent.invocation.acknowledgement_conflict", "A conflicting acknowledgement cannot replace the retained result.", 409);
            if (proposal.Status is not AgentActionProposalStatus.AwaitingClient and not AgentActionProposalStatus.Uncertain)
                return AgentResult<AgentProposalExecutionResult>.Failure("agent.invocation.not_pending", "The client invocation is not pending completion.", 409);
            AgentResult<AgentProposalExecutionResult> completion;
            var completionValidated = false;
            try
            {
                completion = await executor.CompleteClientAsync(proposal, acknowledgement, cancellationToken);
                completionValidated = completion.Succeeded && completion.Value is not null;
            }
            catch (OperationCanceledException)
            {
                completion = AgentResult<AgentProposalExecutionResult>.Success(DefaultAgentActionProposalExecutor.Uncertain(proposal,
                    "Client completion validation was interrupted. Its reported effect remains uncertain and will not be replayed."));
            }
            catch (Exception ex)
            {
                logger?.LogError("Client completion {InvocationId} failed with {FailureType}.", invocationId, ex.GetType().Name);
                completion = AgentResult<AgentProposalExecutionResult>.Success(DefaultAgentActionProposalExecutor.Uncertain(proposal,
                    "Client completion could not be validated. Its reported effect remains uncertain and will not be replayed."));
            }
            if (!completion.Succeeded || completion.Value is null)
                completion = AgentResult<AgentProposalExecutionResult>.Success(DefaultAgentActionProposalExecutor.Uncertain(proposal,
                    "Client completion validation is unavailable. Its reported effect remains uncertain and will not be replayed."));
            completion = StoreOutcome(proposal, completion);
            // An unvalidated application report cannot poison the slot for a later valid receipt.
            // Completion validation never executes the effect, so reconsidering such a report is safe.
            if (completionValidated && completion.Succeeded && (completion.Value?.Executed == true
                || acknowledgement.Outcome == AgentClientOutcome.Refused && completion.Value?.Status == "refused"
                || acknowledgement.Outcome == AgentClientOutcome.Uncertain && completion.Value?.Status == "uncertain"))
                store.Acknowledgements[invocationId] = digest;
            await AuditAsync(store.Proposals[proposalId], completion.Value?.Executed == true ? AgentAuditEventKind.ProposalExecuted : AgentAuditEventKind.ProposalFailed,
                actorId, completion.Value?.Executed == true ? "The originating client supplied an admitted local effect receipt." : "The reported client outcome is refused or uncertain.", CancellationToken.None);
            return completion;
        }
        finally { gate.Release(); }
    }

    public async Task<AgentToolResult?> FindResultAsync(string proposalId, CancellationToken cancellationToken = default)
    {
        if (store.Results.TryGetValue(proposalId, out var available))
            return available;
        if (!store.Proposals.TryGetValue(proposalId, out var current) || current.Status != AgentActionProposalStatus.AwaitingClient)
            return null;
        var gate = Gate(proposalId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (store.Results.TryGetValue(proposalId, out var result))
                return result;
            if (store.Proposals.TryGetValue(proposalId, out var proposal) && proposal.Status == AgentActionProposalStatus.AwaitingClient
                && proposal.PreparedInvocation!.ExpiresAt <= clock.GetUtcNow())
            {
                var uncertain = AgentResult<AgentProposalExecutionResult>.Success(DefaultAgentActionProposalExecutor.Uncertain(proposal,
                    "The client request expired without an admitted receipt. Its effect is uncertain and will not be replayed."));
                StoreOutcome(proposal, uncertain);
                return uncertain.Value!.ToolResult;
            }
            return null;
        }
        finally { gate.Release(); }
    }

    private async Task<(AgentActionProposal? Proposal, AgentError? Error)> AccessAsync(string proposalId, string actorId, CancellationToken cancellationToken)
    {
        if (!store.Proposals.TryGetValue(proposalId, out var proposal))
            return (null, new("agent.proposal.not_found", "The proposal was not found.", 404));
        var session = await sessions.FindAsync(proposal.SessionId, cancellationToken);
        if (session is null || session.ActorId != actorId || proposal.PreparedInvocation is { } prepared &&
            (prepared.ActorId != session.ActorId || prepared.TenantId != session.TenantId || prepared.SessionId != session.Id))
            return (null, new("agent.proposal.forbidden", "The proposal does not belong to the current session actor.", 403));
        return (proposal, null);
    }

    private AgentResult<AgentProposalExecutionResult> StoreOutcome(AgentActionProposal proposal, AgentResult<AgentProposalExecutionResult> outcome)
    {
        if (outcome.Value?.Executed == true && (proposal.PreparedInvocation is not { } preparedInvocation
            || outcome.Value.ToolResult is not { } observed
            || !AgentInvocationIntegrity.ValidReceipt(preparedInvocation, observed,
                preparedInvocation.ExecutionLocation == AgentExecutionLocation.ClientWorkingState ? AgentEffectScope.ClientWorkingState : AgentEffectScope.ServerPersisted)))
            outcome = AgentResult<AgentProposalExecutionResult>.Success(DefaultAgentActionProposalExecutor.Uncertain(proposal,
                "The executor reported success without a valid owning-resource receipt. Its effect is uncertain and will not be replayed."));
        if (proposal.PreparedInvocation is { } original && outcome.Value?.ToolResult is { } observedResult)
            outcome = outcome with { Value = outcome.Value with { ToolResult = AgentResultProvenance.Stamp(observedResult, original) } };
        var value = outcome.Value;
        var status = !outcome.Succeeded ? AgentActionProposalStatus.Failed
            : value?.Executed == true ? AgentActionProposalStatus.Executed
            : value?.Status == "awaiting-client" ? AgentActionProposalStatus.AwaitingClient
            : value?.Status == "uncertain" ? AgentActionProposalStatus.Uncertain
            : AgentActionProposalStatus.Failed;
        store.Executions[proposal.Id] = outcome;
        if (value?.ToolResult is { } result)
            store.Results[proposal.Id] = result;
        else if (!outcome.Succeeded && proposal.PreparedInvocation is { } prepared)
            store.Results[proposal.Id] = DefaultAgentActionProposalExecutor.LifecycleResult(prepared, "refused", "The owning executor refused application before reporting an effect.");
        Transition(proposal with { Status = status, UpdatedAt = clock.GetUtcNow(), Result = value?.ToolResult?.Payload });
        return outcome;
    }

    /// <summary>Stores the proposal state, then releases its claim when the outcome no longer depends on policy.</summary>
    private void Transition(AgentActionProposal proposal)
    {
        store.Proposals[proposal.Id] = proposal;
        if (_owner is not null && Settled(proposal))
            _owner.ReleaseProposal(proposal.SessionId, proposal.Id);
    }

    /// <summary>
    /// Executed (validated receipt), Denied, Expired, Failed (refused), Cancelled and server-scope Uncertain settle.
    /// AwaitingClient retains its claim; client-scope Uncertain retains it until the originating continuation consumes it.
    /// </summary>
    private static bool Settled(AgentActionProposal proposal) => proposal.Status switch
    {
        AgentActionProposalStatus.Executed or AgentActionProposalStatus.Denied or AgentActionProposalStatus.Expired
            or AgentActionProposalStatus.Failed or AgentActionProposalStatus.Cancelled => true,
        AgentActionProposalStatus.Uncertain => proposal.PreparedInvocation?.ExecutionLocation != AgentExecutionLocation.ClientWorkingState,
        _ => false
    };

    private SemaphoreSlim Gate(string proposalId) => store.Gates.GetOrAdd(proposalId, _ => new(1, 1));
    private static bool RevisionMatches(AgentActionProposal proposal, string? expected) => string.Equals(proposal.PreparedInvocation?.Target?.Revision ?? proposal.BaseRevision, expected, StringComparison.Ordinal);
    private static bool IsTerminal(AgentActionProposalStatus status) => status is AgentActionProposalStatus.Executed or AgentActionProposalStatus.Failed
        or AgentActionProposalStatus.Denied or AgentActionProposalStatus.Cancelled or AgentActionProposalStatus.Expired or AgentActionProposalStatus.Uncertain;
    private Task AuditAsync(AgentActionProposal proposal, AgentAuditEventKind kind, string? actorId, string summary, CancellationToken cancellationToken)
        => auditSink.EmitAsync(new(AgentProviderPrimitives.NewId(), kind, proposal.SessionId, actorId, summary, clock.GetUtcNow(),
            new Dictionary<string, string> { ["proposalId"] = proposal.Id, ["invocationId"] = proposal.PreparedInvocation?.InvocationId ?? string.Empty,
                ["status"] = proposal.Status.ToString() }), cancellationToken);
}
