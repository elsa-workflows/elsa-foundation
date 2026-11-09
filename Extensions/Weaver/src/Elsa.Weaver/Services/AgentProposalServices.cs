using System.Collections.Concurrent;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Core.Services;
using Microsoft.Extensions.Logging;

namespace Elsa.Weaver.Services;

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
    /// <summary>The recorded version of each proposal record this process read or wrote (<see cref="AgentProposalRecord.Version"/>).</summary>
    public ConcurrentDictionary<string, long> Versions { get; } = new(StringComparer.Ordinal);
    /// <summary>
    /// Dispatched operations whose outcome this process could not record, by proposal id (value: session id). Each is brought
    /// in line with its record, and settled Uncertain when the record still says Executing, before anything acts on it again.
    /// </summary>
    public ConcurrentDictionary<string, string> Unsettled { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Forgets everything this process loaded for a session it no longer owns, so nothing read before the loss is acted on;
    /// the next access hydrates the session's recorded state again. Call it under <see cref="Mutation"/>.
    /// </summary>
    public void Forget(string sessionId)
    {
        var proposals = Proposals.Values.Where(x => x.SessionId == sessionId).ToArray();
        var ids = proposals.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var calls = CallProposals.Where(x => ids.Contains(x.Value)).Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var proposal in proposals)
        {
            Proposals.TryRemove(proposal.Id, out _);
            Executions.TryRemove(proposal.Id, out _);
            Results.TryRemove(proposal.Id, out _);
            Versions.TryRemove(proposal.Id, out _);
            Unsettled.TryRemove(proposal.Id, out _);
            if (proposal.PreparedInvocation is { } prepared) Acknowledgements.TryRemove(prepared.InvocationId, out _);
        }
        foreach (var (invocationId, _) in InvocationProposals.Where(x => ids.Contains(x.Value)).ToArray()) InvocationProposals.TryRemove(invocationId, out _);
        foreach (var call in calls)
        {
            CallProposals.TryRemove(call, out _);
            CallInputs.TryRemove(call, out _);
        }
        foreach (var (turn, _) in TurnMutations.Where(x => calls.Contains(x.Value)).ToArray()) TurnMutations.TryRemove(turn, out _);
    }
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
    private readonly InMemoryAgentSessionService? _owner = sessions as InMemoryAgentSessionService;

    InMemoryAgentSessionService? IAgentSessionPolicyParticipant.PolicyOwner => _owner;

    public async Task<AgentActionProposal> AddAsync(AgentActionProposal proposal, CancellationToken cancellationToken = default)
        => (await InsertAsync(proposal, null, cancellationToken)).Proposal!;

    /// <summary>Whether this service records its proposals in <paramref name="proposalStore"/>.</summary>
    internal bool Uses(InMemoryAgentProposalStore proposalStore) => ReferenceEquals(store, proposalStore);

    /// <summary>
    /// Inserts a proposal and, for a prepared tool call, its call, invocation, turn-mutation and call-input keys as one
    /// recorded insert: either every key and the proposal are recorded, or nothing is. A turn that already owns another
    /// call's mutation is refused before anything is recorded; a duplicate call returns the original proposal.
    /// </summary>
    internal async Task<AgentProposalInsertion> InsertAsync(AgentActionProposal proposal, AgentPreparedCallKeys? call, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A human decision is recorded only by ApproveAsync. A review-required insertion that already carries
        // an approval would otherwise satisfy current review policy without any decision.
        if (proposal.RequiresApproval && (proposal.ApprovedBy is not null || proposal.ApprovedAt is not null
            || proposal.Status is not (AgentActionProposalStatus.Draft or AgentActionProposalStatus.AwaitingApproval or AgentActionProposalStatus.Edited)))
            throw new AgentContributionException("A review-required operation must be inserted awaiting review; only an approval decision records approval.");
        // The proposal claim exists before the store can expose the proposal, so a preparation claim can
        // hand off to it without an idle gap. Direct callers are covered the same way. A proposal without a
        // prepared invocation is never claimed: ExecuteAsync refuses it before dispatch, so its outcome cannot
        // depend on policy, and a claim with no deadline and no reachable settling transition would hold the
        // session busy forever.
        var claim = proposal.PreparedInvocation is { } claimed && _owner is not null
            ? await _owner.AcquireProposalAsync(proposal.SessionId, claimed.ExpiresAt, cancellationToken) : null;
        var inserted = false;
        try
        {
            var insertion = await CommitAsync<AgentProposalInsertion>(proposal.SessionId, fence =>
            {
                lock (store.Mutation)
                {
                    if (call is { } owned && store.TurnMutations.TryGetValue(owned.TurnKey, out var turnCall) && turnCall != owned.CallKey)
                        return new(AgentProposalInsertion.TurnMutationRefused);
                    if (claim is not null && proposal.PolicyRevision is { } stamped && stamped != claim.State.Revision)
                        throw new AgentSessionPolicyChangedException(proposal.SessionId, stamped, claim.State.Revision);
                    if (proposal.PreparedInvocation is not { } prepared)
                    {
                        if (store.Proposals.ContainsKey(proposal.Id))
                            throw new AgentContributionException("A proposal identity is already registered.");
                        var record = Record(proposal, 0);
                        return new(new(proposal, true), Write(fence, proposal.SessionId, record), () =>
                        {
                            lock (store.Mutation)
                            {
                                store.Proposals.TryAdd(proposal.Id, proposal);
                                store.Versions[proposal.Id] = record.Version + 1;
                            }
                        });
                    }
                    var callKey = AgentInvocationIntegrity.Digest(new { prepared.SessionId, prepared.TurnId, prepared.ToolCallId });
                    if (store.CallProposals.TryGetValue(callKey, out var existingId))
                    {
                        var existing = store.Proposals[existingId];
                        if (existing.PreparedInvocation?.ApprovalDigest != prepared.ApprovalDigest)
                            throw new AgentContributionException("A duplicate tool call supplied conflicting prepared arguments or target.");
                        return new(new(existing, false));
                    }
                    if (store.InvocationProposals.ContainsKey(prepared.InvocationId))
                        throw new AgentContributionException("A prepared invocation identity is already owned by another proposal.");
                    if (store.Proposals.ContainsKey(proposal.Id))
                        throw new AgentContributionException("A proposal identity is already registered.");
                    List<AgentSessionUniqueKey> keys =
                    [
                        new(AgentSessionKeyKinds.Call, callKey, proposal.Id),
                        new(AgentSessionKeyKinds.Invocation, prepared.InvocationId, proposal.Id)
                    ];
                    if (call is { } registered)
                    {
                        keys.Add(new(AgentSessionKeyKinds.TurnMutation, registered.TurnKey, registered.CallKey));
                        keys.Add(new(AgentSessionKeyKinds.CallInput, registered.CallKey, registered.InputDigest));
                    }
                    var inserted = Record(proposal, 0);
                    return new(new(proposal, true), Write(fence, proposal.SessionId, inserted, keys), () =>
                    {
                        lock (store.Mutation)
                        {
                            store.Proposals.TryAdd(proposal.Id, proposal);
                            store.Versions[proposal.Id] = inserted.Version + 1;
                            store.InvocationProposals[prepared.InvocationId] = proposal.Id;
                            store.CallProposals[callKey] = proposal.Id;
                            if (call is { } applied)
                            {
                                store.TurnMutations.TryAdd(applied.TurnKey, applied.CallKey);
                                store.CallInputs.TryAdd(applied.CallKey, applied.InputDigest);
                            }
                        }
                    });
                }
            });
            if (!insertion.Inserted) return insertion;
            inserted = true;
            if (claim is not null)
            {
                if (_owner!.BindProposal(claim, proposal.Id))
                {
                    if (Settled(proposal)) _owner.ReleaseProposal(proposal.SessionId, proposal.Id);
                }
                else
                {
                    // The row the provisional claim was acquired on was replaced before the insertion committed (an eviction
                    // and a new hydration). The proposal is recorded, so its claim is rebuilt on the current row from the
                    // recorded facts, as hydration would; without it a later execute would find no claim and expire it early.
                    _owner.Release(claim);
                    if (!Settled(proposal))
                        await _owner.EnsureProposalClaimAsync(proposal.SessionId, proposal.Id, proposal.PreparedInvocation!.ExpiresAt, CancellationToken.None);
                }
            }
        }
        finally
        {
            // A duplicate or refused insertion releases only its own provisional claim.
            if (claim is not null && !inserted) _owner!.Release(claim);
        }
        await AuditAsync(proposal, AgentAuditEventKind.ProposalCreated, null, "A prepared operation was proposed.", cancellationToken);
        return new(proposal, true);
    }

    public async Task<AgentActionProposal?> FindAsync(string proposalId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ReconcileGatedAsync(proposalId, cancellationToken);
        return await LookupAsync(proposalId, cancellationToken);
    }

    /// <summary>
    /// Under the proposal gate: brings an operation whose outcome this process could not record in line with its record. The
    /// recorded proposal, version and outcome replace what this process holds; a record still Executing is settled Uncertain
    /// once (zero new effect, attributed to recovery). Returns the recorded outcome when the record already holds one past
    /// Executing (the outcome write committed although it reported a failure); null otherwise, and for any other proposal.
    /// </summary>
    private async Task<AgentResult<AgentProposalExecutionResult>?> ReconcileAsync(string proposalId)
    {
        if (_owner is null || !store.Unsettled.TryGetValue(proposalId, out var sessionId)) return null;
        if (await _owner.LoadProposalRecordAsync(sessionId, proposalId) is not { } recorded)
        {
            store.Unsettled.TryRemove(proposalId, out _);
            return null;
        }
        var proposal = recorded.Proposal;
        lock (store.Mutation)
        {
            store.Proposals[proposalId] = proposal;
            store.Versions[proposalId] = recorded.Version;
            if (recorded.Execution is { } execution) store.Executions[proposalId] = execution; else store.Executions.TryRemove(proposalId, out _);
            if (recorded.Result is { } result) store.Results[proposalId] = result; else store.Results.TryRemove(proposalId, out _);
            if (proposal.PreparedInvocation is { } invocation)
            {
                if (recorded.AcknowledgementDigest is { } digest) store.Acknowledgements[invocation.InvocationId] = digest;
                else store.Acknowledgements.TryRemove(invocation.InvocationId, out _);
            }
        }
        if (proposal.Status == AgentActionProposalStatus.Executing && proposal.PreparedInvocation is { } prepared)
        {
            await StoreOutcomeAsync(proposal, recorded.Version, AgentResult<AgentProposalExecutionResult>.Success(DefaultAgentActionProposalExecutor.Uncertain(proposal,
                "The operation was dispatched, but its outcome could not be recorded. Its effect is uncertain and will not be replayed.")));
            await AuditAsync(store.Proposals[proposalId], AgentAuditEventKind.ProposalFailed, null,
                "The outcome of the dispatched operation could not be recorded; its effect is uncertain.", CancellationToken.None,
                new Dictionary<string, string>
                {
                    ["dispatch"] = prepared.ExecutionLocation == AgentExecutionLocation.ClientWorkingState ? "client" : "server", ["effect"] = "possible",
                    [AgentProposalRecovery.SettledBy] = AgentProposalRecovery.Recovery
                });
        }
        else if (Settled(proposal)) _owner.ReleaseProposal(sessionId, proposalId);
        store.Unsettled.TryRemove(proposalId, out _);
        return proposal.Status != AgentActionProposalStatus.Executing ? recorded.Execution : null;
    }

    /// <summary>The single audit record of an execution's recorded outcome.</summary>
    private Task AuditOutcomeAsync(string proposalId, AgentResult<AgentProposalExecutionResult> result, string? actorId,
        IReadOnlyDictionary<string, string>? metadata = null)
        => AuditAsync(store.Proposals[proposalId], result.Value?.Executed == true ? AgentAuditEventKind.ProposalExecuted
                : result.Value?.Status == "awaiting-client" ? AgentAuditEventKind.ToolInvoked : AgentAuditEventKind.ProposalFailed,
            actorId, result.Value?.Executed == true ? "The owning module supplied an observed effect receipt." : "Execution remains pending, refused or uncertain.",
            CancellationToken.None, metadata);

    private static readonly IReadOnlyDictionary<string, string> RecoveredOutcome = new Dictionary<string, string>
        { [AgentProposalRecovery.SettledBy] = AgentProposalRecovery.Recovery };

    /// <summary>
    /// Audits an outcome a reconcile found already recorded. An audit that cannot be emitted leaves the operation unsettled,
    /// so its next access reconciles it again and emits the owed record then (attributed to recovery); it never fails the caller.
    /// </summary>
    private async Task AuditOwedOutcomeAsync(string proposalId, AgentResult<AgentProposalExecutionResult> outcome, string? actorId,
        IReadOnlyDictionary<string, string>? metadata)
    {
        try { await AuditOutcomeAsync(proposalId, outcome, actorId, metadata); }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            if (store.Proposals.TryGetValue(proposalId, out var proposal)) store.Unsettled[proposalId] = proposal.SessionId;
            logger?.LogError("The audit of a recorded outcome could not be emitted yet ({FailureType}); it is retried on the operation's next access.", failure.GetType().Name);
        }
    }

    /// <summary>
    /// Under the proposal gate: <see cref="ReconcileAsync"/>, then the audit of an outcome the record already held, with no
    /// actor and <c>settledBy=recovery</c>: a real effect is audited once even when its own write failed to answer.
    /// </summary>
    private async Task ReconcileAndAuditAsync(string proposalId)
    {
        if (await ReconcileAsync(proposalId) is { } recorded)
            await AuditOwedOutcomeAsync(proposalId, recorded, null, RecoveredOutcome);
    }

    /// <summary>
    /// Retries every operation of the session whose outcome could not be recorded. A retry that fails again leaves it
    /// unsettled (its claim keeps the session busy) for a later access; it never fails the caller.
    /// </summary>
    internal async Task ReconcileSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        foreach (var proposalId in store.Unsettled.Where(x => x.Value == sessionId).Select(x => x.Key).ToArray())
        {
            try { await ReconcileGatedAsync(proposalId, cancellationToken); }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                logger?.LogError("An unsettled operation of agent session {SessionId} could not be settled yet ({FailureType}).", sessionId, failure.GetType().Name);
            }
        }
    }

    /// <summary><see cref="ReconcileAsync"/> for a caller that does not hold the proposal gate.</summary>
    private async Task ReconcileGatedAsync(string proposalId, CancellationToken cancellationToken)
    {
        if (!store.Unsettled.ContainsKey(proposalId)) return;
        var gate = Gate(proposalId);
        await gate.WaitAsync(cancellationToken);
        try { await ReconcileAndAuditAsync(proposalId); }
        finally { gate.Release(); }
    }

    /// <summary>The owning session from the process-local store, or else from the record store; nothing is hydrated.</summary>
    public async Task<string?> FindSessionIdAsync(string proposalId, CancellationToken cancellationToken = default)
    {
        if (store.Proposals.TryGetValue(proposalId, out var proposal)) return proposal.SessionId;
        return _owner is null ? (await LookupAsync(proposalId, cancellationToken))?.SessionId
            : await _owner.FindProposalSessionAsync(proposalId, cancellationToken);
    }

    /// <summary>
    /// The proposal from the process-local store. After a restart or eviction, the session that records it is
    /// hydrated first (which loads and settles its proposals); hydration takes no proposal gate.
    /// </summary>
    private async Task<AgentActionProposal?> LookupAsync(string proposalId, CancellationToken cancellationToken)
    {
        if (store.Proposals.TryGetValue(proposalId, out var proposal)) return proposal;
        if (_owner is null || !await _owner.HydrateProposalAsync(proposalId, cancellationToken)) return null;
        return store.Proposals.GetValueOrDefault(proposalId);
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
            var version = access.Version;
            if (!RevisionMatches(proposal, expectedRevision))
                return AgentResult<AgentActionProposal>.Failure("weaver.proposal.revision_conflict", "Approval does not match the prepared revision.", 409);
            // A pre-dispatch expiry is the accepted lifecycle outcome; repeated decisions observe it unchanged.
            if (proposal.Status == AgentActionProposalStatus.Expired)
                return Expired<AgentActionProposal>();
            if (PastPreDispatchDeadline(proposal))
            {
                await ExpireBeforeDispatchAsync(proposal, version, actorId);
                return Expired<AgentActionProposal>();
            }
            // Only a recorded review decision is idempotent. An automatic approval is not one, so approving it
            // cannot report or relabel it as a human decision.
            if (proposal.RequiresApproval && proposal.ApprovedBy == actorId
                && proposal.Status is AgentActionProposalStatus.Approved or AgentActionProposalStatus.AwaitingClient or AgentActionProposalStatus.Executed)
                return AgentResult<AgentActionProposal>.Success(proposal);
            if (proposal.Status is not AgentActionProposalStatus.AwaitingApproval and not AgentActionProposalStatus.Edited)
                return AgentResult<AgentActionProposal>.Failure("weaver.proposal.invalid_state", "This operation is not awaiting review.", 409);
            var now = clock.GetUtcNow();
            var approved = proposal with { Status = AgentActionProposalStatus.Approved, ApprovedBy = actorId, ApprovedAt = now, UpdatedAt = now };
            await TransitionAsync(approved, version);
            await AuditAsync(approved, AgentAuditEventKind.ProposalApproved, actorId, "The exact prepared operation was approved.", cancellationToken);
            return AgentResult<AgentActionProposal>.Success(approved);
        }
        catch (AgentProposalConflictException) { return Conflict<AgentActionProposal>(); }
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
            var version = access.Version;
            if (proposal.Status == AgentActionProposalStatus.Denied)
                return AgentResult<AgentActionProposal>.Success(proposal);
            if (proposal.Status == AgentActionProposalStatus.AwaitingClient)
            {
                var uncertain = AgentResult<AgentProposalExecutionResult>.Success(DefaultAgentActionProposalExecutor.Uncertain(proposal,
                    "The client request was already dispatched. Denial cannot establish whether a local effect occurred; the request will not be replayed."));
                await StoreOutcomeAsync(proposal, version, uncertain);
                await AuditAsync(store.Proposals[proposalId], AgentAuditEventKind.ToolInvoked, actorId,
                    "Denial arrived after client dispatch; its effect remains uncertain.", CancellationToken.None);
                return AgentResult<AgentActionProposal>.Success(store.Proposals[proposalId]);
            }
            if (IsTerminal(proposal.Status))
                return AgentResult<AgentActionProposal>.Failure("weaver.proposal.closed", "The operation is already closed.", 409);
            var denied = proposal with { Status = AgentActionProposalStatus.Denied, UpdatedAt = clock.GetUtcNow() };
            await TransitionAsync(denied, version, proposal.PreparedInvocation is { } prepared
                ? DefaultAgentActionProposalExecutor.LifecycleResult(prepared, "denied", "The reviewed operation was denied before dispatch.") : null);
            await AuditAsync(denied, AgentAuditEventKind.ProposalDenied, actorId, "The operation was denied before dispatch.", cancellationToken);
            return AgentResult<AgentActionProposal>.Success(denied);
        }
        catch (AgentProposalConflictException) { return Conflict<AgentActionProposal>(); }
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
            var version = access.Version;
            if (!RevisionMatches(proposal, expectedRevision))
                return AgentResult<AgentProposalExecutionResult>.Failure("weaver.proposal.revision_conflict", "Execution does not match the prepared revision.", 409);
            if (store.Executions.TryGetValue(proposalId, out var retained))
            {
                if (retained.Value?.Status == "awaiting-client")
                {
                    // Returning this envelope can cause the originating view to apply it for the
                    // first time. Recheck current authority without rerunning a custom executor.
                    var current = await preparedResolver.ResolveAsync(proposal, cancellationToken);
                    if (current.Error is not null)
                        return await SettlePendingClientAsync(proposal, version, actorId, current.Error.Code == "weaver.proposal.expired",
                            "The pending client operation no longer has its reviewed contract, authority or lifetime. Its effect is uncertain and the request will not be reissued.");
                }
                return retained;
            }
            if (proposal.Status == AgentActionProposalStatus.Expired)
                return Expired<AgentProposalExecutionResult>();
            if (IsTerminal(proposal.Status))
                return AgentResult<AgentProposalExecutionResult>.Failure("weaver.proposal.closed", "The operation is already closed.", 409);
            if (PastPreDispatchDeadline(proposal))
            {
                await ExpireBeforeDispatchAsync(proposal, version, actorId);
                return Expired<AgentProposalExecutionResult>();
            }
            if (proposal.RequiresApproval && proposal.Status != AgentActionProposalStatus.Approved)
                return AgentResult<AgentProposalExecutionResult>.Failure("weaver.proposal.approval_required", "The exact operation requires approval before execution.", 409);
            if (proposal.PreparedInvocation is null)
                return AgentResult<AgentProposalExecutionResult>.Failure("weaver.proposal.executor_unavailable", "This proposal has no concrete prepared executor.", 409);
            // Story 2.3: the approval basis must still be the session's current policy revision. A stale basis is a
            // recorded zero-effect refusal; the executor and resolver then recheck everything else before dispatch.
            if (_owner is not null && proposal.PolicyRevision is { } stamped
                && await _owner.CurrentRevisionAsync(proposal.SessionId, CancellationToken.None) is { } currentRevision && currentRevision != stamped)
            {
                var stale = await StoreOutcomeAsync(proposal, version, AgentResult<AgentProposalExecutionResult>.Failure("weaver.proposal.policy_stale",
                    "The session policy changed after this operation was approved. It was not dispatched; prepare it again under the current policy.", 409),
                    refusal: "The approval basis is no longer the session's current policy. The operation was not dispatched; no effect occurred.");
                await AuditAsync(store.Proposals[proposalId], AgentAuditEventKind.ProposalFailed, actorId,
                    "The approval basis is no longer the session's current policy; no effect occurred.", CancellationToken.None, PreDispatchExpiry);
                return stale;
            }
            // Executing is recorded before the executor runs, so a restart can never mistake a dispatch for "not started".
            // The owner checks the claim, records Executing and marks the claim dispatched as one step: an expired
            // pre-dispatch claim is no longer busy, so policy may have moved, and it can never authorize dispatch; a write
            // that fails leaves the claim undispatched.
            var executing = proposal with { Status = AgentActionProposalStatus.Executing, UpdatedAt = clock.GetUtcNow() };
            long dispatched;
            if (_owner is null) dispatched = await TransitionAsync(executing, version);
            else if (await _owner.DispatchProposalAsync(proposal.SessionId, proposalId, fence => PlanTransition(executing, version, fence), CancellationToken.None))
                dispatched = version + 1;
            else
            {
                await ExpireBeforeDispatchAsync(proposal, version, actorId);
                return Expired<AgentProposalExecutionResult>();
            }
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
            finally { _owner?.EndExecution(proposal.SessionId, proposalId); }
            try { result = await StoreOutcomeAsync(executing, dispatched, result); }
            catch (AgentProposalConflictException)
            {
                // Executing is recorded and the executor ran, so an effect may have happened. The operation changed
                // meanwhile (for example, another process settled it), so this outcome is not recorded here; it is
                // uncertain and never replayed, not a conflict to decide again.
                return AgentResult<AgentProposalExecutionResult>.Success(DefaultAgentActionProposalExecutor.Uncertain(executing,
                    "The operation was dispatched, but its outcome could not be recorded because the operation changed meanwhile. Its effect is uncertain and will not be replayed; read its current state."));
            }
            catch (Exception failure) when (_owner is not null && failure is not AgentSessionOwnershipException)
            {
                // The executor ran, but the store could not record its outcome. Only this operation is brought in line with
                // its record (settled Uncertain while the record still says Executing); the session, its running turn and
                // any other operation in flight are left alone. If that fails too, the next access to it retries. It is
                // never replayed.
                logger?.LogError("The outcome of prepared operation {InvocationId} could not be recorded ({FailureType}); it is settled from its record.",
                    proposal.PreparedInvocation.InvocationId, failure.GetType().Name);
                store.Unsettled[proposalId] = proposal.SessionId;
                try
                {
                    // A write that committed and then reported a failure (for example a timeout after commit) left the real
                    // outcome on the record: it is returned and audited as if the write had answered.
                    if (await ReconcileAsync(proposalId) is { } recordedOutcome)
                    {
                        // The recorded outcome is the answer even if its audit cannot be emitted now; that audit is then owed.
                        await AuditOwedOutcomeAsync(proposalId, recordedOutcome, actorId, null);
                        return recordedOutcome;
                    }
                }
                catch (Exception retry) when (retry is not OperationCanceledException)
                {
                    logger?.LogError("Prepared operation {InvocationId} could not be settled yet ({FailureType}); its next access retries.",
                        proposal.PreparedInvocation.InvocationId, retry.GetType().Name);
                }
                return AgentResult<AgentProposalExecutionResult>.Success(DefaultAgentActionProposalExecutor.Uncertain(executing,
                    "The operation was dispatched, but its outcome could not be recorded. Its effect is uncertain and will not be replayed; read its current state."));
            }
            await AuditOutcomeAsync(proposalId, result, actorId);
            return result;
        }
        catch (AgentProposalConflictException) { return Conflict<AgentProposalExecutionResult>(); }
        finally { gate.Release(); }
    }

    public async Task<AgentResult<AgentProposalExecutionResult>> AcknowledgeAsync(string sessionId, string invocationId, string actorId,
        AgentClientAcknowledgement acknowledgement, CancellationToken cancellationToken = default)
    {
        if (!store.InvocationProposals.TryGetValue(invocationId, out var proposalId)
            && (_owner is null || !await _owner.HydrateSessionAsync(sessionId, cancellationToken) || !store.InvocationProposals.TryGetValue(invocationId, out proposalId)))
            return AgentResult<AgentProposalExecutionResult>.Failure("weaver.invocation.not_found", "The retained invocation was not found; it cannot be replayed.", 404);
        var gate = Gate(proposalId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var access = await AccessAsync(proposalId, actorId, cancellationToken);
            if (access.Error is { } error) return new(null, error);
            var proposal = access.Proposal!;
            var version = access.Version;
            if (proposal.PreparedInvocation is not { ExecutionLocation: AgentExecutionLocation.ClientWorkingState, Client: not null } prepared
                || acknowledgement.Client is null)
                return AgentResult<AgentProposalExecutionResult>.Failure("weaver.invocation.client_unavailable", "The retained invocation is not a bound client operation.", 409);
            if (!Enum.IsDefined(acknowledgement.Outcome))
                return AgentResult<AgentProposalExecutionResult>.Failure("weaver.invocation.outcome_invalid", "The acknowledgement outcome is unsupported.", 400);
            if (proposal.SessionId != sessionId || prepared.InvocationId != invocationId || prepared.Client != acknowledgement.Client
                || prepared.ApprovalDigest != acknowledgement.ApprovalDigest)
                return AgentResult<AgentProposalExecutionResult>.Failure("weaver.invocation.binding_mismatch", "The acknowledgement does not match the retained session and approved client operation.", 409);
            var digest = AgentInvocationIntegrity.Digest(acknowledgement);
            if (store.Acknowledgements.TryGetValue(invocationId, out var accepted))
                return accepted == digest ? store.Executions[proposalId]
                    : AgentResult<AgentProposalExecutionResult>.Failure("weaver.invocation.acknowledgement_conflict", "A conflicting acknowledgement cannot replace the retained result.", 409);
            if (proposal.Status is not AgentActionProposalStatus.AwaitingClient and not AgentActionProposalStatus.Uncertain)
                return AgentResult<AgentProposalExecutionResult>.Failure("weaver.invocation.not_pending", "The client invocation is not pending completion.", 409);
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
            // An unvalidated application report cannot poison the slot for a later valid receipt.
            // Completion validation never executes the effect, so reconsidering such a report is safe.
            // The accepted acknowledgement is recorded in the same write as the outcome it produced.
            completion = await StoreOutcomeAsync(proposal, version, completion, stored => completionValidated && stored.Succeeded && (stored.Value?.Executed == true
                || acknowledgement.Outcome == AgentClientOutcome.Refused && stored.Value?.Status == "refused"
                || acknowledgement.Outcome == AgentClientOutcome.Uncertain && stored.Value?.Status == "uncertain") ? digest : null);
            await AuditAsync(store.Proposals[proposalId], completion.Value?.Executed == true ? AgentAuditEventKind.ProposalExecuted : AgentAuditEventKind.ProposalFailed,
                actorId, completion.Value?.Executed == true ? "The originating client supplied an admitted local effect receipt." : "The reported client outcome is refused or uncertain.", CancellationToken.None);
            return completion;
        }
        catch (AgentProposalConflictException) { return Conflict<AgentProposalExecutionResult>(); }
        finally { gate.Release(); }
    }

    /// <summary>
    /// The session's proposals after hydrating it, oldest first. A dispatched client request past its deadline is settled
    /// Uncertain first, as reading its result does, so the list never shows it still awaiting the client.
    /// </summary>
    public async Task<AgentResult<IReadOnlyList<AgentActionProposal>>> ListAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_owner is not null && !await _owner.HydrateSessionAsync(sessionId, cancellationToken))
            return AgentResult<IReadOnlyList<AgentActionProposal>>.Failure("weaver.session.not_found", $"Agent session '{sessionId}' was not found.", 404);
        await ReconcileSessionAsync(sessionId, cancellationToken);
        var now = clock.GetUtcNow();
        foreach (var overdue in store.Proposals.Values.Where(x => x.SessionId == sessionId && x.Status == AgentActionProposalStatus.AwaitingClient
                     && x.PreparedInvocation?.ExpiresAt <= now).Select(x => x.Id).ToArray())
            await FindResultAsync(overdue, cancellationToken);
        IReadOnlyList<AgentActionProposal> listed = store.Proposals.Values.Where(x => x.SessionId == sessionId)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
        return AgentResult<IReadOnlyList<AgentActionProposal>>.Success(listed);
    }

    /// <summary>
    /// Under the proposal gate: records the owner's statement on a client-scope Uncertain proposal in one awaited write
    /// and releases that operation's claim; any paused turn awaiting it keeps its own Pause claim. A dispatched client
    /// request past its deadline is first settled Uncertain, as reading its result does. An invocation of another session
    /// is not found, before any ownership check, so its existence is never disclosed. The same statement again is
    /// idempotent, also after a later receipt reconciled the operation.
    /// </summary>
    public async Task<AgentResult<AgentActionProposal>> ResolveClientInvocationAsync(string sessionId, string invocationId, string actorId,
        AgentOwnerResolutionClaim claim, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(claim))
            return AgentResult<AgentActionProposal>.Failure("weaver.invocation.resolution_invalid", "Send applied, notApplied or unknown.", 400);
        if (!store.InvocationProposals.TryGetValue(invocationId, out var proposalId)
            && (_owner is null || !await _owner.HydrateSessionAsync(sessionId, cancellationToken) || !store.InvocationProposals.TryGetValue(invocationId, out proposalId)))
            return AgentResult<AgentActionProposal>.Failure("weaver.invocation.not_found", "The retained invocation was not found.", 404);
        var gate = Gate(proposalId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!store.Proposals.TryGetValue(proposalId, out var routed) || routed.SessionId != sessionId)
                return AgentResult<AgentActionProposal>.Failure("weaver.invocation.not_found", "The retained invocation was not found.", 404);
            var access = await AccessAsync(proposalId, actorId, cancellationToken);
            if (access.Error is { } error) return new(null, error);
            var proposal = access.Proposal!;
            var version = access.Version;
            if (proposal.SessionId != sessionId)
                return AgentResult<AgentActionProposal>.Failure("weaver.invocation.not_found", "The retained invocation was not found.", 404);
            if (proposal.Status == AgentActionProposalStatus.AwaitingClient && proposal.PreparedInvocation!.ExpiresAt <= clock.GetUtcNow())
            {
                // The deadline passed after client dispatch: a local effect is possible, so this is never Expired.
                await SettlePendingClientAsync(proposal, version, null, true,
                    "The client request expired without an admitted receipt. Its effect is uncertain and will not be replayed.");
                (proposal, version) = Observe(proposalId)!.Value;
            }
            if (proposal.OwnerResolution is { } recorded)
                return recorded.Claim == claim ? AgentResult<AgentActionProposal>.Success(proposal)
                    : AgentResult<AgentActionProposal>.Failure("weaver.invocation.resolution_conflict", "A different statement is already recorded for this operation.", 409);
            if (proposal.Status != AgentActionProposalStatus.Uncertain
                || proposal.PreparedInvocation?.ExecutionLocation != AgentExecutionLocation.ClientWorkingState)
                return AgentResult<AgentActionProposal>.Failure("weaver.invocation.not_uncertain", "Only an uncertain client operation can be resolved by its owner.", 409);
            var now = clock.GetUtcNow();
            var resolved = proposal with { OwnerResolution = new(claim, actorId, now), UpdatedAt = now };
            await TransitionAsync(resolved, version);
            await AuditAsync(resolved, AgentAuditEventKind.ProposalResolved, actorId,
                "The owner stated the outcome of an uncertain client operation; the statement is not a receipt.", CancellationToken.None,
                new Dictionary<string, string> { ["claim"] = ClaimName(claim) });
            return AgentResult<AgentActionProposal>.Success(resolved);
        }
        catch (AgentProposalConflictException) { return Conflict<AgentActionProposal>(); }
        finally { gate.Release(); }
    }

    public async Task<AgentToolResult?> FindResultAsync(string proposalId, CancellationToken cancellationToken = default)
    {
        await ReconcileGatedAsync(proposalId, cancellationToken);
        if (store.Results.TryGetValue(proposalId, out var available))
            return available;
        if (await LookupAsync(proposalId, cancellationToken) is not { Status: AgentActionProposalStatus.AwaitingClient })
            return store.Results.GetValueOrDefault(proposalId);
        var gate = Gate(proposalId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (store.Results.TryGetValue(proposalId, out var result))
                return result;
            if (Observe(proposalId) is var (proposal, version) && proposal.Status == AgentActionProposalStatus.AwaitingClient
                && proposal.PreparedInvocation!.ExpiresAt <= clock.GetUtcNow())
            {
                // The deadline passed after client dispatch: a local effect is possible, so this is never Expired.
                await SettlePendingClientAsync(proposal, version, null, true,
                    "The client request expired without an admitted receipt. Its effect is uncertain and will not be replayed.");
                return store.Results.GetValueOrDefault(proposalId);
            }
            return null;
        }
        // Another process settled it first; what this process now holds is the recorded state.
        catch (AgentProposalConflictException) { return store.Results.GetValueOrDefault(proposalId); }
        finally { gate.Release(); }
    }

    /// <summary>
    /// The proposal its caller may decide on, with the record version it was read at. Every transition the caller then
    /// records names that version, so a decision made on a state that changed meanwhile (for example, another process
    /// executed the proposal and this process hydrated its newer record) is refused instead of overwriting it.
    /// </summary>
    private async Task<(AgentActionProposal? Proposal, long Version, AgentError? Error)> AccessAsync(string proposalId, string actorId, CancellationToken cancellationToken)
    {
        // An operation whose outcome this process could not record is brought in line with its record before any decision.
        await ReconcileAndAuditAsync(proposalId);
        if (await LookupAsync(proposalId, cancellationToken) is not { } found)
            return (null, 0, new("weaver.proposal.not_found", "The proposal was not found.", 404));
        var session = await sessions.FindAsync(found.SessionId, cancellationToken);
        // Finding the session can hydrate it and load a newer recorded state than the snapshot read before; only the
        // current state may be decided on.
        if (Observe(proposalId) is not var (proposal, version))
            return (null, 0, new("weaver.proposal.not_found", "The proposal was not found.", 404));
        if (session is null || session.ActorId != actorId || proposal.PreparedInvocation is { } prepared &&
            (prepared.ActorId != session.ActorId || prepared.TenantId != session.TenantId || prepared.SessionId != session.Id))
            return (null, 0, new("weaver.proposal.forbidden", "The proposal does not belong to the current session actor.", 403));
        return (proposal, version, null);
    }

    /// <summary>The proposal and the record version it holds, read together; null when this process holds no such proposal.</summary>
    private (AgentActionProposal Proposal, long Version)? Observe(string proposalId)
    {
        lock (store.Mutation)
            return store.Proposals.TryGetValue(proposalId, out var proposal) ? (proposal, store.Versions.GetValueOrDefault(proposalId)) : null;
    }

    /// <summary>
    /// Records an execution outcome as one aggregate write: the retained outcome, its result or lifecycle envelope, the
    /// accepted acknowledgement (when <paramref name="acknowledgement"/> names one) and the proposal status. The claim is
    /// released afterwards when the outcome no longer depends on policy. A refusal without its own result records
    /// <paramref name="refusal"/> (or the executor's refusal) as its zero-effect lifecycle envelope.
    /// </summary>
    private async Task<AgentResult<AgentProposalExecutionResult>> StoreOutcomeAsync(AgentActionProposal proposal, long version, AgentResult<AgentProposalExecutionResult> outcome,
        Func<AgentResult<AgentProposalExecutionResult>, string?>? acknowledgement = null, string? refusal = null)
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
        // Only the executor's own engine-owned signal is a pre-dispatch cancellation; a module returning the same
        // public code after dispatch is an ordinary refusal (Failed, refused lifecycle, claim released).
        var cancelled = DefaultAgentActionProposalExecutor.IsCancelledBeforeDispatch(outcome);
        var status = cancelled ? AgentActionProposalStatus.Cancelled
            : !outcome.Succeeded ? AgentActionProposalStatus.Failed
            : value?.Executed == true ? AgentActionProposalStatus.Executed
            : value?.Status == "awaiting-client" ? AgentActionProposalStatus.AwaitingClient
            : value?.Status == "uncertain" ? AgentActionProposalStatus.Uncertain
            : AgentActionProposalStatus.Failed;
        var result = value?.ToolResult
            // Module disclosure owners admit only denied/refused/uncertain lifecycle envelopes; a cancellation
            // before dispatch is a zero-effect refusal of the original invocation.
            ?? (!outcome.Succeeded && proposal.PreparedInvocation is { } prepared
                ? DefaultAgentActionProposalExecutor.LifecycleResult(prepared, "refused", cancelled
                    ? "The operation was cancelled before dispatch to its owning module. No effect occurred."
                    : refusal ?? "The owning executor refused application before reporting an effect.")
                : null);
        await TransitionAsync(proposal with { Status = status, UpdatedAt = clock.GetUtcNow(), Result = value?.ToolResult?.Payload },
            version, result, outcome, acknowledgement?.Invoke(outcome));
        return outcome;
    }

    /// <summary>
    /// Records one proposal transition (status, and optionally its result, outcome and accepted acknowledgement) as one
    /// awaited write, applies it, then releases the proposal's claim when the outcome no longer depends on policy.
    /// <paramref name="version"/> is the record version the transition was decided on: when the recorded version moved
    /// meanwhile the store refuses the write, nothing is applied, and <see cref="AgentProposalConflictException"/> is
    /// thrown. Returns the version the transition recorded.
    /// </summary>
    private async Task<long> TransitionAsync(AgentActionProposal proposal, long version, AgentToolResult? result = null,
        AgentResult<AgentProposalExecutionResult>? execution = null, string? acknowledgement = null)
    {
        await CommitAsync(proposal.SessionId, fence => PlanTransition(proposal, version, fence, result, execution, acknowledgement));
        if (_owner is not null && Settled(proposal))
            _owner.ReleaseProposal(proposal.SessionId, proposal.Id);
        return version + 1;
    }

    /// <summary>One proposal transition's record write and the in-memory change applied after it succeeded.</summary>
    private AgentSessionTransition<bool> PlanTransition(AgentActionProposal proposal, long version, AgentSessionFence? fence, AgentToolResult? result = null,
        AgentResult<AgentProposalExecutionResult>? execution = null, string? acknowledgement = null)
    {
        var invocationId = proposal.PreparedInvocation?.InvocationId;
        var record = Record(proposal, version, execution, result, acknowledgement);
        return new(true, Write(fence, proposal.SessionId, record), () =>
        {
            lock (store.Mutation)
            {
                store.Versions[proposal.Id] = record.Version + 1;
                if (execution is not null) store.Executions[proposal.Id] = execution;
                if (result is not null) store.Results[proposal.Id] = result;
                if (acknowledgement is not null && invocationId is not null) store.Acknowledgements[invocationId] = acknowledgement;
                store.Proposals[proposal.Id] = proposal;
            }
        });
    }

    /// <summary>
    /// Runs one proposal transition through the owner: session write gate, awaited record write, then apply. Without
    /// the default owner the plan and its change run as one step under the proposal store's (re-entrant) monitor, in
    /// memory only. A planned transition always completes.
    /// </summary>
    private Task<T> CommitAsync<T>(string sessionId, Func<AgentSessionFence?, AgentSessionTransition<T>> plan)
    {
        if (_owner is not null) return _owner.CommitAsync(sessionId, plan, CancellationToken.None);
        // Without the owner's session gate, the plan and its change are one step under the proposal store's monitor.
        lock (store.Mutation)
        {
            var planned = plan(null);
            planned.Apply?.Invoke();
            return Task.FromResult(planned.Result);
        }
    }

    private static AgentSessionRecordWrite? Write(AgentSessionFence? fence, string sessionId, AgentProposalRecord record, IReadOnlyList<AgentSessionUniqueKey>? keys = null)
        => fence is null ? null : new(sessionId, fence) { Proposals = [record], Keys = keys ?? [] };

    /// <summary>
    /// The proposal's complete record after a transition decided on <paramref name="version"/> (0 for a new proposal);
    /// members the transition does not change keep their retained values.
    /// </summary>
    private AgentProposalRecord Record(AgentActionProposal proposal, long version, AgentResult<AgentProposalExecutionResult>? execution = null,
        AgentToolResult? result = null, string? acknowledgement = null) => new(proposal)
    {
        Execution = execution ?? store.Executions.GetValueOrDefault(proposal.Id),
        Result = result ?? store.Results.GetValueOrDefault(proposal.Id),
        AcknowledgementDigest = acknowledgement ?? (proposal.PreparedInvocation is { } prepared ? store.Acknowledgements.GetValueOrDefault(prepared.InvocationId) : null),
        Version = version
    };

    /// <summary>
    /// Executed (validated receipt), Denied, Expired, Failed (refused), Cancelled and server-scope Uncertain settle.
    /// AwaitingClient retains its claim; client-scope Uncertain retains it until the originating continuation consumes it
    /// or its owner resolves it.
    /// </summary>
    internal static bool Settled(AgentActionProposal proposal) => proposal.Status switch
    {
        AgentActionProposalStatus.Executed or AgentActionProposalStatus.Denied or AgentActionProposalStatus.Expired
            or AgentActionProposalStatus.Failed or AgentActionProposalStatus.Cancelled => true,
        AgentActionProposalStatus.Uncertain => proposal.PreparedInvocation?.ExecutionLocation != AgentExecutionLocation.ClientWorkingState
            || proposal.OwnerResolution is not null,
        _ => false
    };

    private bool PastPreDispatchDeadline(AgentActionProposal proposal)
        => proposal.PreparedInvocation is { } prepared && prepared.ExpiresAt <= clock.GetUtcNow()
            && proposal.Status is AgentActionProposalStatus.AwaitingApproval or AgentActionProposalStatus.Edited or AgentActionProposalStatus.Approved;

    private static AgentResult<T> Expired<T>() => AgentResult<T>.Failure("weaver.proposal.expired", "The prepared operation has expired.", 409);

    /// <summary>The refusal of a decision made on a proposal state that changed meanwhile; nothing was recorded.</summary>
    private static AgentResult<T> Conflict<T>() => AgentResult<T>.Failure(AgentProposalConflictException.Code,
        "The operation changed while this decision was being made; nothing was recorded. Read its current state and decide again.", 409);

    /// <summary>
    /// Under the proposal gate: records the one pre-dispatch expiry (zero effect), releases the proposal claim
    /// and emits its single correlated audit event. Later attempts observe the retained Expired status.
    /// </summary>
    private async Task ExpireBeforeDispatchAsync(AgentActionProposal proposal, long version, string? actorId)
    {
        var expired = proposal with { Status = AgentActionProposalStatus.Expired, UpdatedAt = clock.GetUtcNow() };
        await TransitionAsync(expired, version);
        await AuditAsync(expired, AgentAuditEventKind.ProposalFailed, actorId, "The prepared operation expired before dispatch; no effect occurred.",
            CancellationToken.None, PreDispatchExpiry);
    }

    /// <summary>
    /// Under the proposal gate: a dispatched client request whose lifetime or contract ended without an admitted
    /// receipt becomes Uncertain (possible effect). It is never Expired and is never reissued.
    /// </summary>
    private async Task<AgentResult<AgentProposalExecutionResult>> SettlePendingClientAsync(AgentActionProposal proposal, long version, string? actorId, bool deadline, string summary)
    {
        var outcome = await StoreOutcomeAsync(proposal, version, AgentResult<AgentProposalExecutionResult>.Success(DefaultAgentActionProposalExecutor.Uncertain(proposal, summary)));
        await AuditAsync(store.Proposals[proposal.Id], AgentAuditEventKind.ProposalFailed, actorId, deadline
                ? "The pending client request reached its deadline without an admitted receipt; its effect is uncertain."
                : "The pending client request lost its reviewed contract or authority; its effect is uncertain.",
            CancellationToken.None, deadline ? PendingClientDeadline : PendingClientWithdrawn);
        return outcome;
    }

    private static readonly IReadOnlyDictionary<string, string> PreDispatchExpiry = new Dictionary<string, string> { ["dispatch"] = "none", ["effect"] = "none" };
    private static readonly IReadOnlyDictionary<string, string> PendingClientDeadline = new Dictionary<string, string>
        { ["dispatch"] = "client", ["effect"] = "possible", ["deadline"] = "pending-client" };
    private static readonly IReadOnlyDictionary<string, string> PendingClientWithdrawn = new Dictionary<string, string> { ["dispatch"] = "client", ["effect"] = "possible" };

    /// <summary>The owner's claim as the resolution route names it.</summary>
    private static string ClaimName(AgentOwnerResolutionClaim claim) => claim switch
    {
        AgentOwnerResolutionClaim.Applied => "applied",
        AgentOwnerResolutionClaim.NotApplied => "notApplied",
        _ => "unknown"
    };

    private SemaphoreSlim Gate(string proposalId) => store.Gates.GetOrAdd(proposalId, _ => new(1, 1));
    private static bool RevisionMatches(AgentActionProposal proposal, string? expected) => string.Equals(proposal.PreparedInvocation?.Target?.Revision ?? proposal.BaseRevision, expected, StringComparison.Ordinal);
    private static bool IsTerminal(AgentActionProposalStatus status) => status is AgentActionProposalStatus.Executed or AgentActionProposalStatus.Failed
        or AgentActionProposalStatus.Denied or AgentActionProposalStatus.Cancelled or AgentActionProposalStatus.Expired or AgentActionProposalStatus.Uncertain;
    private Task AuditAsync(AgentActionProposal proposal, AgentAuditEventKind kind, string? actorId, string summary, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? outcome = null)
        => auditSink.EmitAsync(Event(proposal, kind, actorId, summary, clock.GetUtcNow(), outcome), cancellationToken);

    /// <summary>Every proposal record correlates the session, proposal, prepared invocation, turn and tool call.</summary>
    internal static AgentAuditEvent Event(AgentActionProposal proposal, AgentAuditEventKind kind, string? actorId, string summary, DateTimeOffset now,
        IReadOnlyDictionary<string, string>? outcome = null)
    {
        var metadata = new Dictionary<string, string>
        {
            ["proposalId"] = proposal.Id, ["invocationId"] = proposal.PreparedInvocation?.InvocationId ?? string.Empty,
            ["status"] = proposal.Status.ToString()
        };
        if (proposal.PreparedInvocation is { } prepared)
        {
            metadata["turnId"] = prepared.TurnId;
            metadata["toolCallId"] = prepared.ToolCallId;
        }
        foreach (var (key, value) in outcome ?? new Dictionary<string, string>())
            metadata[key] = value;
        return new(AgentProviderPrimitives.NewId(), kind, proposal.SessionId, actorId, summary, now, metadata);
    }

    internal static IReadOnlyDictionary<string, string> PreDispatchExpiryOutcome => PreDispatchExpiry;
    internal static IReadOnlyDictionary<string, string> PendingClientDeadlineOutcome => PendingClientDeadline;
}

/// <summary>
/// A proposal transition named a record version that is no longer recorded: another process changed the proposal after
/// this decision read it. The store refused the write and nothing was applied.
/// </summary>
internal sealed class AgentProposalConflictException(string sessionId)
    : InvalidOperationException($"A proposal of agent session '{sessionId}' changed after the decision read it; nothing was recorded.")
{
    public const string Code = "weaver.proposal.conflict";
}

/// <summary>The duplicate-detection keys of one prepared tool call, recorded together with its proposal.</summary>
internal sealed record AgentPreparedCallKeys(string TurnKey, string CallKey, string InputDigest);

/// <summary>The outcome of a proposal insertion: the inserted or original proposal, or a refused turn mutation.</summary>
internal sealed record AgentProposalInsertion(AgentActionProposal? Proposal, bool Inserted)
{
    /// <summary>The turn already owns another call's mutation; nothing was recorded.</summary>
    public static AgentProposalInsertion TurnMutationRefused { get; } = new(null, false);
}

/// <summary>
/// Zero-effect lifecycle settlement of recorded proposals found when their session is hydrated (research R4). It
/// never executes, dispatches or replays anything, and is attributed to recovery, not to a user.
/// </summary>
internal static class AgentProposalRecovery
{
    /// <summary>The audit metadata key that attributes a lifecycle outcome; recovery outcomes carry <see cref="Recovery"/>.</summary>
    internal const string SettledBy = "settledBy";
    internal const string Recovery = "recovery";

    internal sealed record Settlement(AgentProposalRecord Record, string Summary, IReadOnlyDictionary<string, string> Outcome);

    /// <summary>
    /// A pending or approved operation past its deadline becomes Expired once, with zero effect. An operation that was
    /// Executing becomes Uncertain, and a dispatched client request past its deadline becomes Uncertain, never Expired.
    /// Anything else is left as recorded.
    /// </summary>
    internal static Settlement? Settle(AgentProposalRecord record, DateTimeOffset now)
    {
        var proposal = record.Proposal;
        if (proposal.PreparedInvocation is not { } prepared) return null;
        if (proposal.Status is AgentActionProposalStatus.AwaitingApproval or AgentActionProposalStatus.Edited or AgentActionProposalStatus.Approved
            && prepared.ExpiresAt <= now)
            return new(record with { Proposal = proposal with { Status = AgentActionProposalStatus.Expired, UpdatedAt = now } },
                "The prepared operation expired before dispatch; no effect occurred.", Attributed(InMemoryAgentProposalService.PreDispatchExpiryOutcome));
        if (proposal.Status == AgentActionProposalStatus.Executing)
            return Uncertain(record, prepared, now, "The process stopped after dispatch, before an outcome was recorded. Its effect is uncertain and will not be replayed.",
                "The operation was dispatched when the process stopped; its effect is uncertain.", new Dictionary<string, string>
                {
                    ["dispatch"] = prepared.ExecutionLocation == AgentExecutionLocation.ClientWorkingState ? "client" : "server", ["effect"] = "possible"
                });
        if (proposal.Status == AgentActionProposalStatus.AwaitingClient && prepared.ExpiresAt <= now)
            return Uncertain(record, prepared, now, "The client request expired without an admitted receipt. Its effect is uncertain and will not be replayed.",
                "The pending client request reached its deadline without an admitted receipt; its effect is uncertain.",
                InMemoryAgentProposalService.PendingClientDeadlineOutcome);
        return null;
    }

    /// <summary>The settlement's single correlated audit record, with no actor.</summary>
    internal static AgentAuditEvent Audit(Settlement settlement, DateTimeOffset now)
        => InMemoryAgentProposalService.Event(settlement.Record.Proposal, AgentAuditEventKind.ProposalFailed, null, settlement.Summary, now, settlement.Outcome);

    private static Settlement Uncertain(AgentProposalRecord record, AgentPreparedToolInvocation prepared, DateTimeOffset now, string result, string audit,
        IReadOnlyDictionary<string, string> outcome)
    {
        var value = DefaultAgentActionProposalExecutor.Uncertain(record.Proposal, result);
        var stamped = AgentResultProvenance.Stamp(value.ToolResult!, prepared);
        return new(record with
        {
            Proposal = record.Proposal with { Status = AgentActionProposalStatus.Uncertain, UpdatedAt = now, Result = stamped.Payload },
            Execution = AgentResult<AgentProposalExecutionResult>.Success(value with { ToolResult = stamped }),
            Result = stamped
        }, audit, Attributed(outcome));
    }

    private static IReadOnlyDictionary<string, string> Attributed(IReadOnlyDictionary<string, string> outcome)
        => new Dictionary<string, string>(outcome) { [SettledBy] = Recovery };
}
