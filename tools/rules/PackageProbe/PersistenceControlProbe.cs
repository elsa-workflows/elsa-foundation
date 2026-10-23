using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Microsoft.Extensions.DependencyInjection;

internal static partial class Program
{
    private static async Task<object> RunPersistenceControlAsync(string kind, string statePath)
    {
        Require(kind is "cancellation" or "fence" or "consumed", "Persistence control kind is unsupported.");
        var state = ReadProcessState(statePath);
        Require(state.Boundary == "accepted", "Persistence controls require a committed accepted-receipt boundary.");

        var connection = RequiredProcessConnection();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var host = BuildProcessHost(connection, control: null, suppressWorker: true);
        try
        {
            await StartProcessHostAsync(host, timeout.Token);
            await using var scope = host.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var store = services.GetRequiredService<IRuleRunStore>();
            var reader = services.GetRequiredService<IRuleRunReader>();
            var profile = await ReadPostgresProfileAsync(connection, timeout.Token);
            Require(profile.Engine == state.DatabaseEngine && profile.DatabaseName == state.DatabaseName,
                "Persistence control is not connected to the boundary's live PostgreSQL database.");
            Require(Environment.ProcessId != state.ProcessId,
                "Persistence control did not run in an independent process.");

            var accepted = await ReadAcceptedBoundaryAsync(reader, state, timeout.Token);
            var outcome = kind switch
            {
                "cancellation" => await VerifyCancellationControlAsync(store, reader, state, accepted, timeout.Token),
                "fence" => await VerifyFenceControlAsync(store, reader, state, accepted, timeout.Token),
                "consumed" => await VerifyConsumedControlAsync(store, reader, state, timeout.Token),
                _ => throw new ProbeAssertionException("Persistence control kind is unsupported.")
            };

            return new
            {
                command = "process-control",
                passed = true,
                kind,
                processId = Environment.ProcessId,
                originalProcessId = state.ProcessId,
                machineName = Environment.MachineName,
                databaseEngine = profile.Engine,
                databaseEngineVersion = profile.Version,
                databaseName = profile.DatabaseName,
                invocationFingerprint = state.InvocationFingerprint,
                boundaryReceiptId = state.ReceiptId,
                outcome
            };
        }
        finally
        {
            await StopProcessHostAsync(host, timeout.Token);
        }
    }

    private static async Task<RunSnapshot> ReadAcceptedBoundaryAsync(IRuleRunReader reader,
        ProcessBoundaryState state, CancellationToken cancellationToken)
    {
        var snapshot = await ReadRunSnapshotAsync(reader, state.Invocation.Identity, cancellationToken);
        Require(snapshot.View.Status == RuleRunStatus.Running && snapshot.View.ModelRevision == 2
                && snapshot.View.CommittedRounds == 0 && snapshot.View.PendingReceiptId == state.ReceiptId
                && snapshot.Facts.SequenceEqual(["seed"], StringComparer.Ordinal)
                && snapshot.Rounds.Length == 1 && snapshot.Rounds[0].Ordinal == 0
                && snapshot.Rounds[0].ReceiptId == state.ReceiptId
                && snapshot.Rounds[0].SemanticReceiptSha256 == state.SemanticReceiptSha256
                && snapshot.Rounds[0].ConsumedModelRevision is null
                && snapshot.Rounds[0].AcceptedAttemptId == state.AcceptedAttemptId
                && snapshot.Rounds[0].AcceptedWorkerId == state.AcceptedWorkerId
                && snapshot.Rounds[0].AcceptedLeaseFence == state.AcceptedLeaseFence,
            "Persistence control did not start from the original accepted, pending receipt snapshot.");
        return snapshot;
    }

    private static async Task<object> VerifyCancellationControlAsync(IRuleRunStore store, IRuleRunReader reader,
        ProcessBoundaryState state, RunSnapshot accepted, CancellationToken cancellationToken)
    {
        var changedInvocation = WithMaxRounds(state.Invocation, state.Invocation.MaxRounds - 1);
        var changed = await store.CancelAsync(new(changedInvocation), cancellationToken);
        var afterChanged = await ReadRunSnapshotAsync(reader, state.Invocation.Identity, cancellationToken);
        Require(changed.Disposition == RuleCancellationDisposition.Conflict
                && changed.ModelRevision is null && changed.TerminalId is null
                && SameSnapshot(accepted, afterChanged),
            "Control: changed-fingerprint cancellation did not remain Conflict without mutation.");

        var cancelled = await store.CancelAsync(new(state.Invocation), cancellationToken);
        Require(cancelled.Disposition == RuleCancellationDisposition.Cancelled
                && cancelled.ModelRevision == 3 && cancelled.TerminalId is not null,
            $"Exact cancellation returned {cancelled.Disposition} without the expected terminal confirmation.");
        var afterExact = await ReadRunSnapshotAsync(reader, state.Invocation.Identity, cancellationToken);
        Require(afterExact.View.Status == RuleRunStatus.Cancelled && afterExact.View.CommittedRounds == 0
                && afterExact.View.CommittedFactCount == 1 && afterExact.View.PendingReceiptId is null
                && afterExact.Terminal?.Kind == RuleTerminalKind.Cancelled
                && afterExact.Terminal?.TerminalId == cancelled.TerminalId
                && afterExact.Facts.SequenceEqual(["seed"], StringComparer.Ordinal)
                && afterExact.TerminalFacts.SequenceEqual(["seed"], StringComparer.Ordinal)
                && afterExact.Rounds.Length == 1 && afterExact.Rounds[0].Ordinal == 0
                && afterExact.Rounds[0].ReceiptId == state.ReceiptId
                && afterExact.Rounds[0].ConsumedModelRevision is null
                && afterExact.Rounds[0].AcceptedAttemptId == state.AcceptedAttemptId
                && afterExact.Rounds[0].AcceptedWorkerId == state.AcceptedWorkerId
                && afterExact.Rounds[0].AcceptedLeaseFence == state.AcceptedLeaseFence,
            "Exact cancellation changed the retained accepted receipt or committed facts.");

        var replay = await store.CancelAsync(new(state.Invocation), cancellationToken);
        Require(replay.Disposition == RuleCancellationDisposition.Replay
                && replay.ModelRevision == cancelled.ModelRevision && replay.TerminalId == cancelled.TerminalId
                && SameSnapshot(afterExact, await ReadRunSnapshotAsync(reader, state.Invocation.Identity, cancellationToken)),
            "Exact cancellation replay changed its durable terminal confirmation.");
        return new
        {
            changedCancellation = changed.Disposition.ToString(),
            exactCancellation = cancelled.Disposition.ToString(),
            exactReplay = replay.Disposition.ToString(),
            modelRevision = cancelled.ModelRevision,
            terminalId = cancelled.TerminalId,
            committedFacts = afterExact.Facts,
            retainedReceiptId = afterExact.Rounds[0].ReceiptId,
            retainedAcceptedAttemptId = afterExact.Rounds[0].AcceptedAttemptId
        };
    }

    private static async Task<object> VerifyFenceControlAsync(IRuleRunStore store, IRuleRunReader reader,
        ProcessBoundaryState state, RunSnapshot accepted, CancellationToken cancellationToken)
    {
        var request = ProcessControlClaimRequest(state.Invocation, state.TokenOwner);
        var firstClaim = await ClaimWorkAfterExpiryAsync(store, request, cancellationToken);
        Require(firstClaim.Lease.Owner == state.TokenOwner && firstClaim.Lease.Fence > state.TokenFence,
            "Same-owner reclaim did not advance the original work fence.");

        var pending = await reader.ReadWorkAsync(firstClaim, cancellationToken);
        Require(pending.Disposition == RuleWorkReadDisposition.ConsumeReceipt
                && pending.PendingReceipt is not null && pending.CurrentToken is not null
                && pending.PendingReceipt.ReceiptId == state.ReceiptId
                && pending.PendingReceipt.Receipt.SemanticReceiptSha256 == state.SemanticReceiptSha256
                && pending.CurrentToken.ExpectedModelRevision == 2
                && pending.CurrentToken.Lease.Fence == firstClaim.Lease.Fence,
            "Same-owner reclaim did not read the original pending receipt at model revision two.");
        var staleToken = pending.CurrentToken!;
        var beforeStaleConsume = await ReadRunSnapshotAsync(reader, state.Invocation.Identity, cancellationToken);
        Require(SameSnapshot(accepted, beforeStaleConsume),
            "Same-owner lease reclaim changed the pending run snapshot.");

        var successor = await ClaimWorkAfterExpiryAsync(store, request, cancellationToken);
        Require(successor.Lease.Owner == firstClaim.Lease.Owner && successor.Lease.Fence > staleToken.Lease.Fence
                && successor.ExpectedModelRevision == staleToken.ExpectedModelRevision,
            "Second same-owner reclaim did not advance only the work fence.");
        var stale = await store.ConsumeRoundAsync(staleToken,
            new(state.ReceiptId, state.SemanticReceiptSha256), cancellationToken);
        var afterStaleConsume = await ReadRunSnapshotAsync(reader, state.Invocation.Identity, cancellationToken);
        Require(stale.Disposition == RuleTransitionDisposition.LeaseLost
                && stale.TransitionId is null && stale.ReceiptId is null && stale.TerminalId is null
                && stale.NextRoundId is null && stale.ModelRevision is null
                && SameSnapshot(beforeStaleConsume, afterStaleConsume),
            "Control: stale same-owner token was not rejected by the work fence.");

        return new
        {
            oldFence = state.TokenFence,
            firstReclaimedFence = staleToken.Lease.Fence,
            successorFence = successor.Lease.Fence,
            staleConsume = stale.Disposition.ToString(),
            pendingReceiptId = state.ReceiptId,
            modelRevision = afterStaleConsume.View.ModelRevision
        };
    }

    private static async Task<object> VerifyConsumedControlAsync(IRuleRunStore store, IRuleRunReader reader,
        ProcessBoundaryState state, CancellationToken cancellationToken)
    {
        var request = ProcessControlClaimRequest(state.Invocation, "process-consumer-" + Guid.NewGuid().ToString("N"));
        var claim = await ClaimWorkAfterExpiryAsync(store, request, cancellationToken);
        var pending = await reader.ReadWorkAsync(claim, cancellationToken);
        Require(pending.Disposition == RuleWorkReadDisposition.ConsumeReceipt
                && pending.PendingReceipt is not null && pending.CurrentToken is not null
                && pending.PendingReceipt.ReceiptId == state.ReceiptId
                && pending.PendingReceipt.Receipt.SemanticReceiptSha256 == state.SemanticReceiptSha256
                && pending.CurrentToken.ExpectedModelRevision == 2,
            "Recovery claim did not read the original pending receipt at model revision two.");

        var receiptIdentity = new RuleReceiptIdentity(state.ReceiptId, state.SemanticReceiptSha256);
        var pendingToken = pending.CurrentToken!;
        var consumed = await store.ConsumeRoundAsync(pendingToken, receiptIdentity, cancellationToken);
        Require(consumed.Disposition == RuleTransitionDisposition.Consumed && consumed.ModelRevision == 3
                && consumed.ReceiptId == state.ReceiptId && consumed.TerminalId is null
                && !string.IsNullOrWhiteSpace(consumed.TransitionId)
                && !string.IsNullOrWhiteSpace(consumed.NextRoundId),
            "Control: exact consumption replay did not preserve its consumed revision and confirmation.");

        var afterConsume = await ReadRunSnapshotAsync(reader, state.Invocation.Identity, cancellationToken);
        var replay = await store.ConsumeRoundAsync(pendingToken, receiptIdentity, cancellationToken);
        var afterReplay = await ReadRunSnapshotAsync(reader, state.Invocation.Identity, cancellationToken);
        var firstRound = afterConsume.Rounds.SingleOrDefault(round => round.Ordinal == 0);
        Require(firstRound?.ConsumedModelRevision == 3
                && replay.Disposition == RuleTransitionDisposition.Replay
                && replay.TransitionId == consumed.TransitionId && replay.ReceiptId == consumed.ReceiptId
                && replay.TerminalId == consumed.TerminalId && replay.NextRoundId == consumed.NextRoundId
                && replay.ModelRevision == consumed.ModelRevision
                && SameSnapshot(afterConsume, afterReplay),
            "Control: exact consumption replay did not preserve its consumed revision and confirmation.");

        Require(afterConsume.View.Status == RuleRunStatus.Running && afterConsume.View.ModelRevision == 3
                && afterConsume.View.CommittedRounds == 1 && afterConsume.View.PendingReceiptId is null
                && afterConsume.Facts.SequenceEqual(["left", "right", "seed"], StringComparer.Ordinal)
                && afterConsume.Rounds.Length == 1 && afterConsume.Rounds[0].ReceiptId == state.ReceiptId,
            "First receipt consumption produced an unexpected run snapshot.");
        return new
        {
            firstConsumption = consumed.Disposition.ToString(),
            exactReplay = replay.Disposition.ToString(),
            transitionId = replay.TransitionId,
            receiptId = replay.ReceiptId,
            nextRoundId = replay.NextRoundId,
            modelRevision = replay.ModelRevision,
            consumedModelRevision = firstRound.ConsumedModelRevision,
            committedFacts = afterReplay.Facts,
            committedRounds = afterReplay.View.CommittedRounds
        };
    }

    private static RuleClaimRequest ProcessControlClaimRequest(RuleInvocationEnvelope invocation, string owner) =>
        new(owner, [invocation.ExpectedImplementation], [invocation.InvocationSchemaVersion],
            [invocation.ExpectedImplementation.ReceiptSchemaVersion], [invocation.ExpectedImplementation.StateSchemaVersion]);

    private static async Task<RuleWorkToken> ClaimWorkAfterExpiryAsync(IRuleRunStore store, RuleClaimRequest request,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            var claim = await store.ClaimWorkAsync(request, timeout.Token);
            if (claim.Disposition == RuleWorkClaimDisposition.Claimed && claim.Token is not null)
                return claim.Token;
            Require(claim.Disposition == RuleWorkClaimDisposition.NoEligibleWork,
                $"Work reclaim returned {claim.Disposition} instead of waiting for the prior lease to expire.");
            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
        }
    }
}
