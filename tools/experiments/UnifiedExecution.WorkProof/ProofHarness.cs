using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Elsa.UnifiedExecution.WorkProof;

internal static class ProofHarness
{
    private static readonly List<ProofAssertion> Assertions = [];
    private static readonly List<ProcessEvidence> Processes = [];
    private static readonly List<MutationEvidence> Mutations = [];
    private static readonly object EvidenceLock = new();
    private static string _root = "";
    private static string _workDb = "";
    private static string _sinkDb = "";
    private static HmacWorkScopeAuthorizer _authorizer = null!;
    private static DurableWorkStore _store = null!;
    private static IndependentSink _sink = null!;
    private static WorkScope _scope = null!;

    public static async Task<int> RunAsync(string[] args)
    {
        string evidencePath = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../evidence.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
        _root = Path.Combine(Path.GetTempPath(), "elsa-unified-work-proof", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workDb = Path.Combine(_root, "coordinator.sqlite");
        _sinkDb = Path.Combine(_root, "destination.sqlite");
        _authorizer = new HmacWorkScopeAuthorizer(WorkerCommands.ProofKey);
        _scope = _authorizer.Issue("tenant-red", "shared-persistence", "workflow-host");
        _store = new DurableWorkStore(_workDb, _authorizer);
        _sink = new IndependentSink(_sinkDb, _authorizer);

        try
        {
            Processes.Add(new ProcessEvidence("proof-harness", Environment.ProcessId, 0, "started"));
            await ProveAtomicDispatchAndDiscoveryAsync();
            await ProveSingleInvocationClaimAsync();
            await ProveCoordinatorRevisionCasAsync();
            await ProveSequenceAndPinnedInputsAsync();
            await ProveOverlappingFlowchartAndMutantAsync();
            await ProveCancellationAndLateReceiptsAsync();
            await ProveContextAndHandlerPinningAsync();
            await ProvePartitionBatchesAndRecoveryAsync();
            await ProveAggregateAdmissionAsync();
            await ProveCommitReplayAndFencingAsync();

            var report = new ProofReport(
                "unified-execution-work-proof/v1",
                DateTimeOffset.UtcNow,
                ReadGitHead(),
                ReadSourceHashes(),
                typeof(SqliteConnection).Assembly.GetName().Version?.ToString() ?? "unknown",
                SqliteVersion(_workDb),
                Path.GetFileName(_root),
                Assertions.ToArray(),
                Processes.ToArray(),
                Mutations.ToArray(),
                CaptureAudit(),
                "SQLite WAL on one machine; child OS processes share a local coordinator DB and a separate destination DB.");
            await WriteEvidenceAsync(evidencePath, report);
            string archivePath = Path.ChangeExtension(evidencePath, ".zip");
            CreateEvidenceArchive(evidencePath, archivePath);
            Console.WriteLine($"PASS: {Assertions.Count} assertions; {Processes.Count} OS process identities; {Mutations.Count} negative controls.");
            Console.WriteLine($"Scratch databases: {_root}");
            Console.WriteLine($"Evidence: {evidencePath}");
            Console.WriteLine($"Sanitized archive: {archivePath}");
            return 0;
        }
        catch (Exception exception)
        {
            var report = new ProofReport("unified-execution-work-proof/v1", DateTimeOffset.UtcNow, ReadGitHead(), ReadSourceHashes(),
                typeof(SqliteConnection).Assembly.GetName().Version?.ToString() ?? "unknown", "unavailable", Path.GetFileName(_root),
                Assertions.ToArray(), Processes.ToArray(), Mutations.ToArray(), [],
                $"FAILED: {exception.GetType().Name}: {exception.Message}");
            await WriteEvidenceAsync(evidencePath, report);
            Console.Error.WriteLine($"FAIL: {exception.GetType().Name}: {exception.Message}");
            Console.Error.WriteLine($"Partial evidence: {evidencePath}");
            return 1;
        }
    }

    private static async Task ProveAtomicDispatchAndDiscoveryAsync()
    {
        string database = Path.Combine(_root, "dispatch.sqlite");
        string sinkDatabase = Path.Combine(_root, "dispatch-sink.sqlite");
        string scopeArg = WorkerCommands.EncodeScope(_scope);
        var before = WorkerCommands.Start("--before-dispatch-crash", database, scopeArg, "dispatch-before");
        ChildResult beforeResult = await WaitChild("coordinator-exit-before-dispatch", before);
        Assert(beforeResult.ExitCode == 72 && beforeResult.Output.Contains("EXIT_BEFORE_ATOMIC_DISPATCH"), "dispatch.before-atomic-transaction", $"exit={beforeResult.ExitCode}; no state or work commit was attempted");
        var store = new DurableWorkStore(database, _authorizer);
        bool absent = false;
        try { _ = store.ReadExecution(_scope, "dispatch-before"); }
        catch (UnauthorizedAccessException) { absent = true; }
        Assert(absent, "dispatch.pre-commit-crash-atomicity", "execution row and owed-work row are both absent after child exits before the atomic initial transaction");

        var committed = WorkerCommands.Start("--seed-dispatch-crash", database, scopeArg, "dispatch-after");
        ChildResult afterResult = await WaitChild("coordinator-exit-after-dispatch-before-notification", committed);
        Assert(afterResult.ExitCode == 73 && afterResult.Output.Contains("COMMIT_DONE_NO_NOTIFICATION_SENT"), "dispatch.commit-before-notification-crash", $"fresh coordinator process {afterResult.ProcessId} committed then exited before notification");
        WorkSnapshot owed = store.ReadWork(_scope, "dispatch-after").Single(x => x.InvocationId == "after-crash");
        Assert(owed.Status == "owed", "dispatch.owed-work-survives-coordinator-loss", "fresh process discovered durable owed work without the lost notification");
        var worker = StartWorker(database, sinkDatabase, _scope, "dispatch-after", "after-crash", "dispatch-recovery-worker", "-", "none");
        ChildResult recovered = await WaitChild("fresh-worker-discovers-dispatch", worker);
        Assert(recovered.ExitCode == 0 && store.ReadWork(_scope, "dispatch-after").Single().Status == "succeeded", "dispatch.fresh-worker-completes", $"worker process {recovered.ProcessId} completed after coordinator exit");
    }

    private static async Task ProveSequenceAndPinnedInputsAsync()
    {
        var input = new Dictionary<string, string> { ["value"] = "pinned-before-dispatch" };
        string snapshot = JsonSerializer.Serialize(input);
        var coordinator = new CompositionCoordinator(_store, _scope, "sequence-main", "sequence-owner", new SequencePolicy());
        coordinator.Start(snapshot);
        input["value"] = "mutated-after-dispatch";
        _store.CreateBarrier("sequence-predecessor-gate");
        RunningChild predecessor = StartWorker(_workDb, _sinkDb, _scope, "sequence-main", "sequence-0", "sequence-worker-0", "sequence-predecessor-gate", "none");
        await WaitForBarrier("sequence-predecessor-gate", 1);
        WorkSnapshot[] beforeRelease = _store.ReadWork(_scope, "sequence-main").ToArray();
        Assert(beforeRelease.All(x => x.InvocationId != "sequence-1"), "sequence.successor-not-dispatched-early", "predecessor is held at an OS-process barrier and successor has no durable dispatch row");
        Assert(beforeRelease.Single(x => x.InvocationId == "sequence-0").InputJson == snapshot, "sequence.input-snapshot-pinned", "worker input is serialized at dispatch and is unchanged after source object mutation");
        _store.ReleaseBarrier("sequence-predecessor-gate");
        ChildResult completed = await WaitChild("sequence-predecessor", predecessor);
        Assert(completed.ExitCode == 0, "sequence.predecessor-worker-result", $"worker process {completed.ProcessId} returned successfully");
        Assert(_store.ReadWork(_scope, "sequence-main").All(x => x.InvocationId != "sequence-1"), "sequence.result-awaits-component", "result receipt is durable but successor remains undispatched until SequencePolicy applies it");

        WorkSnapshot first = _store.ReadWork(_scope, "sequence-main").Single(x => x.InvocationId == "sequence-0");
        WorkLease firstLease = Lease(first, _scope, "sequence-main");
        WorkReceipt duplicate = _store.CompleteWork(firstLease, first.ResultJson!);
        Assert(duplicate.Duplicate, "results.identical-receipt-idempotent", "same worker fence and identical result returns the committed receipt");
        bool conflictRejected = Throws<InvalidOperationException>(() => _store.CompleteWork(firstLease, "{\"value\":\"conflicting\"}"));
        Assert(conflictRejected, "results.conflicting-receipt-rejected", "same invocation identity cannot replace its accepted result");

        Assert(coordinator.ApplyPendingResults() == 1, "sequence.component-accepts-predecessor", "SequencePolicy consumes one durable receipt");
        WorkSnapshot[] afterFirst = _store.ReadWork(_scope, "sequence-main").ToArray();
        Assert(afterFirst.Single(x => x.InvocationId == "sequence-1").Status == "owed", "sequence.successor-after-result", "second activity is dispatched only by the Sequence component after first acceptance");
        RunningChild successor = StartWorker(_workDb, _sinkDb, _scope, "sequence-main", "sequence-1", "sequence-worker-1", "-", "none");
        ChildResult successorResult = await WaitChild("sequence-successor", successor);
        Assert(successorResult.ExitCode == 0, "sequence.successor-executes", $"successor worker process {successorResult.ProcessId} completed");
        Assert(coordinator.ApplyPendingResults() == 1 && _store.ReadExecution(_scope, "sequence-main").Status == "Completed", "sequence.component-owns-completion", "SequencePolicy, not zero outstanding work, commits the terminal state after the second result");
        Assert(_sink.CountEffects(_scope, "sequence-main") == 2, "sink.sequence-effects-separate", "each activity effect was accepted once in the independent destination database");
    }

    private static async Task ProveCoordinatorRevisionCasAsync()
    {
        const string executionId = "same-revision-result-race";
        using var policy = new ConcurrentApplyPolicy();
        var coordinator = new CompositionCoordinator(_store, _scope, executionId, "cas-owner", policy);
        coordinator.Start("{}");
        RunningChild a = StartWorker(_workDb, _sinkDb, _scope, executionId, "race-a", "cas-worker-a", "-", "none");
        RunningChild b = StartWorker(_workDb, _sinkDb, _scope, executionId, "race-b", "cas-worker-b", "-", "none");
        ChildResult[] results = await Task.WhenAll(WaitChild("cas-worker-a", a), WaitChild("cas-worker-b", b));
        Assert(results.All(x => x.ExitCode == 0), "coordinator-revision.two-result-workers", "two separate workers durably completed independent invocations");

        // Compete distinct receipts at the same revision. Retrying the same commit ID
        // would correctly replay its receipt and would not exercise revision exclusion.
        PendingResult[] pendingResults = _store.ReadPendingResults(_scope, executionId).ToArray();
        Task<int> ApplyDistinct(PendingResult pending) => Task.Factory.StartNew(() =>
        {
            ExecutionSnapshot snapshot = _store.ReadExecution(_scope, executionId);
            var receipt = new WorkReceipt(pending.InvocationId, "Succeeded", pending.ResultJson, pending.Fingerprint);
            CompositionPlan plan = policy.ApplyResult(snapshot.StateJson, receipt);
            _store.CommitPlan(_scope, executionId, snapshot.Owner, snapshot.Fence, snapshot.Revision,
                $"result:{pending.InvocationId}", plan, sourceResultInvocation: pending.InvocationId);
            return 1;
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task<int> applyA = ApplyDistinct(pendingResults[0]);
        Task<int> applyB = ApplyDistinct(pendingResults[1]);
        try { await Task.WhenAll(applyA, applyB); }
        catch (InvalidOperationException) { }
        int rejected = new[] { applyA, applyB }.Count(x => x.IsFaulted && x.Exception?.GetBaseException() is InvalidOperationException);
        Assert(rejected == 1 && _store.ReadPendingResults(_scope, executionId).Count == 1,
            "coordinator-revision-cas-excludes-stale-plan", $"one distinct commit loses revision race; rejected={rejected}, taskA={Describe(applyA)}, taskB={Describe(applyB)}");
        coordinator.ApplyPendingResults();
        ConcurrentResultState state = JsonSupport.Deserialize<ConcurrentResultState>(_store.ReadExecution(_scope, executionId).StateJson);
        Assert(state.Accepted.Order(StringComparer.Ordinal).SequenceEqual(new[] { "race-a", "race-b" })
            && _store.ReadPendingResults(_scope, executionId).Count == 0,
            "coordinator-revision-cas-no-lost-update", "losing receipt remains pending and is reapplied against the committed state without losing either result");
    }

    private static async Task ProveSingleInvocationClaimAsync()
    {
        const string executionId = "same-invocation-claim";
        var plan = new CompositionPlan("{}", [new WorkDefinition("only-work", "snapshot-echo", "1", "{\"value\":\"single-owner\"}")], [], false);
        _store.CreateAndCommitInitialPlan(_scope, executionId, "single-claim-policy", "single-claim-owner", $"start:{executionId}", plan);
        bool changedPolicyRejected = Throws<InvalidOperationException>(() => _store.CreateAndCommitInitialPlan(_scope, executionId,
            "different-policy", "single-claim-owner", $"start:{executionId}", plan));
        Assert(changedPolicyRejected, "dispatch.initial-replay-pins-policy", "a retried start receipt cannot silently substitute a different composition policy");
        _store.CreateBarrier("single-claim-held");
        RunningChild first = StartWorker(_workDb, _sinkDb, _scope, executionId, "only-work", "claim-worker-one", "single-claim-held", "none");
        await WaitForBarrier("single-claim-held", 1);
        RunningChild competing = StartWorker(_workDb, _sinkDb, _scope, executionId, "only-work", "claim-worker-two", "-", "none");
        ChildResult declined = await WaitChild("same-invocation-competing-claim", competing);
        Assert(declined.ExitCode == 9 && _store.BarrierEntryCount("single-claim-held") == 1,
            "work.concurrent-same-invocation-single-claim", "second fresh process cannot claim an invocation already held by the first owner and fence");
        _store.ReleaseBarrier("single-claim-held");
        ChildResult completed = await WaitChild("same-invocation-claim-owner", first);
        Assert(completed.ExitCode == 0 && _store.ReadWork(_scope, executionId).Single().Status == "succeeded",
            "work.single-claim-owner-completes", "the one accepted claimant returns the durable result receipt");
    }

    private static async Task ProveOverlappingFlowchartAndMutantAsync()
    {
        string executionId = "flowchart-main";
        var coordinator = new CompositionCoordinator(_store, _scope, executionId, "flowchart-owner", new FlowchartPolicy());
        coordinator.Start("{}");
        _store.CreateBarrier("flowchart-a-gate");
        _store.CreateBarrier("flowchart-b-gate");
        RunningChild a = StartWorker(_workDb, _sinkDb, _scope, executionId, "branch-a", "flowchart-worker-a", "flowchart-a-gate", "none");
        RunningChild b = StartWorker(_workDb, _sinkDb, _scope, executionId, "branch-b", "flowchart-worker-b", "flowchart-b-gate", "none");
        await WaitForBarrier("flowchart-a-gate", 1);
        await WaitForBarrier("flowchart-b-gate", 1);
        int[] ids = _store.BarrierProcessIds("flowchart-a-gate").Concat(_store.BarrierProcessIds("flowchart-b-gate")).ToArray();
        Assert(ids.Length == 2 && ids.Distinct().Count() == 2 && !a.Process.HasExited && !b.Process.HasExited,
            "activity.real-overlap-two-fresh-processes", $"two distinct OS workers {string.Join(',', ids)} both entered and remained blocked before release");

        _store.ReleaseBarrier("flowchart-b-gate");
        ChildResult bResult = await WaitChild("flowchart-branch-b-first", b);
        Assert(bResult.ExitCode == 0 && coordinator.ApplyPendingResults() == 1, "flowchart.reverse-first-branch", "branch B receipt is applied while branch A remains blocked");
        Assert(!a.Process.HasExited && _store.ReadPendingContinuations(_scope, executionId).Count == 0, "flowchart.join-waits-for-all", "one branch result does not release the component join");
        _store.ReleaseBarrier("flowchart-a-gate");
        ChildResult aResult = await WaitChild("flowchart-branch-a-second", a);
        Assert(aResult.ExitCode == 0 && coordinator.ApplyPendingResults() == 1, "flowchart.reverse-second-branch", "branch A arrives second and is accepted");
        ExecutionSnapshot joinedPending = _store.ReadExecution(_scope, executionId);
        Assert(joinedPending.Status == "Running" && _store.ReadPendingContinuations(_scope, executionId).SequenceEqual(["flowchart-join"]),
            "flowchart.component-owns-held-join", "both workers are terminal, no work is owed, yet root stays nonterminal with one durable semantic continuation");
        Assert(_store.ReadWork(_scope, executionId).All(x => x.Status == "succeeded"), "flowchart.workers-terminal-before-join", "all activity jobs are terminal while composition still owns the continuation");
        WorkSnapshot branchB = _store.ReadWork(_scope, executionId).Single(x => x.InvocationId == "branch-b");
        WorkReceipt branchDuplicate = _store.CompleteWork(Lease(branchB, _scope, executionId), branchB.ResultJson!);
        Assert(branchDuplicate.Duplicate, "flowchart.duplicate-branch-idempotent", "duplicate late branch delivery does not create another join event");
        Assert(_store.ReadPendingContinuations(_scope, executionId).Count == 1, "flowchart.join-continuation-singleton", "join continuation remains exactly one after duplicate receipt replay");

        // Run this deliberately broken completion provider as a separate child process.
        string scopeArg = WorkerCommands.EncodeScope(_scope);
        ChildResult completionMutant = await WaitChild("negative-control-completion-owner", WorkerCommands.Start("--mutant", "completion", _workDb, scopeArg, executionId));
        RecordMutation("completion-owner", completionMutant, "MUTATION_INVARIANT_FAILED: completion-owner");
        Assert(_store.ReadExecution(_scope, executionId).Status == "Completed", "negative.completion-mutant-is-real", "separate mutant process demonstrated the job-count rule falsely completes while Flowchart join is held");

        // Restore the valid state via the semantic component only after recording the negative control.
        // In a clean execution this is the only path that consumes the join and marks terminal.
        _store.CreateBarrier("flowchart-main-validity-note");
        // Use a sibling clean flowchart as positive proof so the mutated execution is never treated as valid evidence.
        string cleanId = "flowchart-clean";
        var clean = new CompositionCoordinator(_store, _scope, cleanId, "flowchart-clean-owner", new FlowchartPolicy());
        clean.Start("{}");
        foreach (string invocation in new[] { "branch-a", "branch-b" })
        {
            RunningChild child = StartWorker(_workDb, _sinkDb, _scope, cleanId, invocation, $"clean-{invocation}", "-", "none");
            Assert((await WaitChild($"flowchart-clean-{invocation}", child)).ExitCode == 0, "flowchart.clean-worker", $"{invocation} worker completed");
            clean.ApplyPendingResults();
        }
        Assert(_store.ReadExecution(_scope, cleanId).Status == "Running" && clean.RunPendingContinuations() == 1 && _store.ReadExecution(_scope, cleanId).Status == "Completed",
            "flowchart.component-resumes-and-completes", "only FlowchartPolicy continuation changes the clean execution to Completed");
    }

    private static async Task ProveCancellationAndLateReceiptsAsync()
    {
        var cancelled = new CompositionCoordinator(_store, _scope, "sequence-cancel-first", "cancel-first-owner", new SequencePolicy());
        cancelled.Start("{\"value\":\"effect-may-outlive-cancel\"}");
        _store.CreateBarrier("cancel-before-result-gate");
        RunningChild running = StartWorker(_workDb, _sinkDb, _scope, "sequence-cancel-first", "sequence-0", "late-worker", "cancel-before-result-gate", "none");
        await WaitForBarrier("cancel-before-result-gate", 1);
        Assert(_store.CancelWork(_scope, "sequence-cancel-first", "sequence-0", "cancel-1"), "cancellation.cancel-first-accepted", "cancellation decision committed while worker was held before effect delivery");
        _store.ReleaseBarrier("cancel-before-result-gate");
        ChildResult late = await WaitChild("cancellation-late-result", running);
        Assert(late.ExitCode == 0 && _store.ReadWork(_scope, "sequence-cancel-first").Single().Status == "cancelled", "cancellation.late-result-does-not-revive", "late worker result was rejected after cancellation became terminal");
        Assert(cancelled.ApplyPendingResults() == 0 && _store.ReadWork(_scope, "sequence-cancel-first").All(x => x.InvocationId != "sequence-1"), "cancellation-no-successor", "cancellation-first does not schedule Sequence successor");
        Assert(_sink.CountEffects(_scope, "sequence-cancel-first") == 1, "cancellation-effect-not-rolled-back-claim", "effect delivered by the already-running worker remains in separate sink; cancellation does not claim rollback");

        var successFirst = new CompositionCoordinator(_store, _scope, "sequence-success-first", "success-first-owner", new SequencePolicy());
        successFirst.Start("{\"value\":\"success-wins\"}");
        RunningChild successful = StartWorker(_workDb, _sinkDb, _scope, "sequence-success-first", "sequence-0", "success-before-cancel-worker", "-", "none");
        Assert((await WaitChild("success-before-cancel", successful)).ExitCode == 0, "cancellation.success-before-cancel-worker", "worker result committed before cancellation request");
        Assert(!_store.CancelWork(_scope, "sequence-success-first", "sequence-0", "cancel-too-late"), "cancellation.success-terminal-wins", "a later cancellation cannot overwrite accepted success");
        Assert(successFirst.ApplyPendingResults() == 1 && _store.ReadWork(_scope, "sequence-success-first").Any(x => x.InvocationId == "sequence-1"), "cancellation.success-may-advance", "component applies the earlier accepted success");
    }

    private static async Task ProveContextAndHandlerPinningAsync()
    {
        string executionId = "red-only";
        var red = new CompositionCoordinator(_store, _scope, executionId, "red-owner", new SequencePolicy());
        red.Start("{\"value\":\"red\"}");
        WorkScope blueScope = _authorizer.Issue("tenant-blue", "shared-persistence", "workflow-host");
        var blue = new CompositionCoordinator(_store, blueScope, executionId, "blue-owner", new SequencePolicy());
        blue.Start("{\"value\":\"blue\"}");
        Assert(_store.ReadWork(_scope, executionId).Single().InputJson.Contains("red"), "context.tenant-red-isolated", "red tenant reads its own pinned invocation");
        Assert(_store.ReadWork(blueScope, executionId).Single().InputJson.Contains("blue"), "context.tenant-blue-same-id-isolated", "same execution and invocation identities coexist under the blue tenant key");
        bool crossScopeBlocked = Throws<UnauthorizedAccessException>(() => _store.ReadExecution(blueScope, "sequence-main"));
        Assert(crossScopeBlocked, "context.cross-tenant-read-blocked", "valid blue capability cannot resolve red execution using same persistence and authority labels");
        bool crossScopeReplayBlocked = Throws<UnauthorizedAccessException>(() => _store.CommitPlan(blueScope, "sequence-main", "red-owner", 1, 1,
            "start:sequence-main", new CompositionPlan("{}", [], [], false)));
        Assert(crossScopeReplayBlocked, "context.cross-tenant-commit-replay-blocked", "a blue tenant cannot retrieve or reuse red tenant commit identity before receipt replay lookup");
        WorkScope forgedAuthority = blueScope with { Authority = "forged-authority" };
        bool forgedBlocked = Throws<UnauthorizedAccessException>(() => _store.ReadExecution(forgedAuthority, executionId));
        Assert(forgedBlocked, "context.forged-authority-rejected", "capability signature binds tenant, persistence scope, and authority");
        bool unauthorizedEffectBlocked = Throws<UnauthorizedAccessException>(() => _sink.Write(forgedAuthority, "sequence-main", "forged-effect", "{}"));
        Assert(unauthorizedEffectBlocked, "context.unauthorized-effect-blocked", "separate sink verifies the supplied local capability before accepting an effect");

        ChildResult scopeMutant = await WaitChild("negative-control-scope-filter", WorkerCommands.Start("--mutant", "scope", _workDb,
            WorkerCommands.EncodeScope(blueScope), executionId, "sequence-0"));
        RecordMutation("scope-filter", scopeMutant, "MUTATION_INVARIANT_FAILED: scope-filter");

        string unsupportedId = "unsupported-handler-version";
        var unsupportedPlan = new CompositionPlan("{\"handler\":\"v2\"}",
            [new WorkDefinition("needs-v2", "snapshot-echo", "2", "{\"value\":\"unsupported\"}")], [], false);
        _store.CreateAndCommitInitialPlan(_scope, unsupportedId, "handler-pin", "handler-pin-owner", $"start:{unsupportedId}", unsupportedPlan);
        ChildResult unsupported = await WaitChild("incompatible-handler-version", StartWorker(_workDb, _sinkDb, _scope, unsupportedId, "needs-v2", "v1-only-worker", "-", "none"));
        WorkSnapshot unresolved = _store.ReadWork(_scope, unsupportedId).Single();
        Assert(unsupported.ExitCode == 9 && unresolved.Status == "owed" && unresolved.UnsupportedReason is not null,
            "context.incompatible-handler-stays-owed", "v1-only process cannot substitute for pinned v2 handler and unsupported work remains inspectable");
    }

    private static async Task ProvePartitionBatchesAndRecoveryAsync()
    {
        var source = new PartitionSourceStore(_workDb, _authorizer);
        string executionId = "etl-pipeline-main";
        string[] rows = Enumerable.Range(0, 9).Select(i => $"row-{i}").ToArray();
        source.CreatePartition(_scope, executionId, "A", batchSize: 3, maxBatchRecords: 3, rows);
        source.CreatePartition(_scope, executionId, "B", batchSize: 3, maxBatchRecords: 3, rows);
        var policy = new PartitionPipelinePolicy();
        var pipeline = new CompositionCoordinator(_store, _scope, executionId, "etl-pipeline-coordinator", policy);
        pipeline.Start(JsonSupport.Serialize(new PartitionPipelineInput([
            new PartitionBatchInput("A", -1, 3),
            new PartitionBatchInput("B", -1, 3)
        ])));
        Assert(_store.ReadExecution(_scope, executionId).PolicyId == policy.Id && _store.ReadWork(_scope, executionId).Count == 2,
            "partition.one-pipeline-two-work-items", "one pipeline execution coordinator owns both partition batch jobs");
        _store.CreateBarrier("etl-A-sink-gate");
        _store.CreateBarrier("etl-B-sink-gate");
        RunningChild workerA = StartWorker(_workDb, _sinkDb, _scope, executionId, "partition:A:after:-1", "etl-worker-A", "etl-A-sink-gate", "none");
        RunningChild workerB = StartWorker(_workDb, _sinkDb, _scope, executionId, "partition:B:after:-1", "etl-worker-B", "etl-B-sink-gate", "none");
        await WaitForBarrier("etl-A-sink-gate", 1);
        await WaitForBarrier("etl-B-sink-gate", 1);
        int inFlight = source.CurrentInFlight(_scope, executionId);
        Assert(inFlight == 6 && inFlight <= 2 * 3, "partition.aggregate-buffer-bound", "two fresh workers each hold at most one configured three-record batch; aggregate peak is six");
        _store.ReleaseBarrier("etl-B-sink-gate");
        ChildResult resultB = await WaitChild("etl-partition-B-progress", workerB);
        Assert(resultB.ExitCode == 0 && pipeline.ApplyPendingResults() == 1, "partition.independent-progress", "partition B checkpoints while partition A is still blocked at sink boundary");
        IReadOnlyDictionary<string, long> cursors = ReadPartitionCursors(_store.ReadExecution(_scope, executionId).StateJson);
        long cursorB = cursors["B"];
        long cursorA = cursors["A"];
        Assert(!workerA.Process.HasExited && cursorB == 2 && cursorA == -1, "partition.blocked-A-does-not-block-B", "B advances through the shared work protocol while A cursor remains at its original checkpoint");
        _store.ReleaseBarrier("etl-A-sink-gate");
        ChildResult resultA = await WaitChild("etl-partition-A-progress", workerA);
        Assert(resultA.ExitCode == 0 && pipeline.ApplyPendingResults() == 1, "partition.independent-A-resumes", "A can complete its batch after its own release");

        await DrainPartitionAsync(pipeline, executionId);
        Assert(_store.ReadExecution(_scope, executionId).Status == "Completed", "partition.semantic-completion", "the shared dataflow policy completes only after both empty terminal batch receipts");
        Assert(_store.CountWorkRows(_scope, executionId) == 8 && source.SourceCount(_scope, executionId, "A") == 9 && source.SourceCount(_scope, executionId, "B") == 9,
            "partition.batch-not-per-record-messages", "one execution processed eighteen records using eight batch invocations, with three records maximum per invocation");
        Assert(source.PeakFetched(_scope, executionId) <= 3, "partition.per-partition-batch-bound", "no source read exceeded its pinned batch size");
        Assert(_sink.CountEffects(_scope, executionId) == 18, "partition.all-record-effects", "both partitions delivered each source row once to the separate destination");

        await ProvePartitionCrashReplayAsync(source);
    }

    private static async Task ProvePartitionCrashReplayAsync(PartitionSourceStore source)
    {
        const string expiryOnlyExecutionId = "lease-expired-current-owner";
        var expiryOnlyPlan = new CompositionPlan("{}", [new WorkDefinition("expiry-only", "snapshot-echo", "1", "{}")], [], false);
        _store.CreateAndCommitInitialPlan(_scope, expiryOnlyExecutionId, "lease-expiry-policy", "lease-owner", $"start:{expiryOnlyExecutionId}", expiryOnlyPlan);
        WorkLease expiringLease = _store.TryClaimWork(_scope, expiryOnlyExecutionId, "expiry-only", "lease-worker", "snapshot-echo", "1")!;
        _store.ForceExpireWorkForProof(_scope, expiryOnlyExecutionId, "expiry-only");
        WorkReceipt expiryOnlyReceipt = _store.CompleteWork(expiringLease, "{}");
        Assert(expiryOnlyReceipt.Status == "Succeeded", "work-expiry-alone-does-not-revoke", "expiry alone makes a claim available for recovery; the current fence remains valid until a newer claim commits");

        const string executionId = "etl-crash-replay";
        source.CreatePartition(_scope, executionId, "R", batchSize: 2, maxBatchRecords: 2, ["replay-0", "replay-1", "replay-2", "replay-3", "replay-4"]);
        var coordinator = new CompositionCoordinator(_store, _scope, executionId, "etl-replay-owner", new PartitionPipelinePolicy());
        coordinator.Start(JsonSupport.Serialize(new PartitionPipelineInput([new PartitionBatchInput("R", -1, 2)])));
        const string invocation = "partition:R:after:-1";
        ChildResult crashed = await WaitChild("partition-crash-after-sink-before-result", StartWorker(_workDb, _sinkDb, _scope, executionId, invocation, "etl-attempt-1", "-", "crash-after-sink"));
        Assert(crashed.ExitCode == 73 && crashed.Output.Contains("FAILPOINT: destination effect persisted"), "partition.crash-after-separate-effect", "child exited after destination commit and before result receipt");
        Assert(_store.ReadExecution(_scope, executionId).Status == "Running" && _store.ReadWork(_scope, executionId).Single().Status == "claimed", "partition.checkpoint-not-advanced-on-lost-result", "durable cursor is unchanged and work remains recoverable after worker process death");
        Assert(_sink.CountEffects(_scope, executionId) == 2, "partition.first-effect-once", "the first attempt persisted two deterministic destination keys");
        WorkSnapshot crashedWork = _store.ReadWork(_scope, executionId).Single();
        WorkLease oldLease = Lease(crashedWork, _scope, executionId);
        _store.ForceExpireWorkForProof(_scope, executionId, invocation);
        _store.CreateBarrier("etl-retry-claimed");
        RunningChild retry = StartWorker(_workDb, _sinkDb, _scope, executionId, invocation, "etl-attempt-2", "etl-retry-claimed", "none");
        await WaitForBarrier("etl-retry-claimed", 1);
        WorkSnapshot reclaimedWork = _store.ReadWork(_scope, executionId).Single();
        Assert(reclaimedWork.Attempt == 2 && reclaimedWork.Fence > oldLease.Fence, "partition.expiry-reclaims-with-new-fence", "logical lease expiry permits a new claim that advances the work fence");
        bool oldWriterRejected = Throws<InvalidOperationException>(() => _store.CompleteWork(oldLease, JsonSupport.Serialize(new PartitionBatchResult("R", -1, 1, 2, false))));
        Assert(oldWriterRejected, "partition.expired-old-lease-rejected-after-reclaim", "expiry itself leaves the current lease valid, but the old writer is refused once a new attempt takes a higher fence");
        _store.ReleaseBarrier("etl-retry-claimed");
        ChildResult recovered = await WaitChild("partition-retry-same-batch", retry);
        Assert(recovered.ExitCode == 0 && _store.ReadWork(_scope, executionId).Single().Attempt == 2, "partition.recovered-new-fence", "fresh process claimed attempt two using same pinned cursor and a higher fence");
        Assert(_sink.CountEffects(_scope, executionId) == 2 && _sink.CountDeliveries(_scope, executionId) == 4, "partition.sink-idempotent-replay", "four delivery attempts produced two unique external effects after replay");
        Assert(coordinator.ApplyPendingResults() == 1 && ReadPartitionCursors(_store.ReadExecution(_scope, executionId).StateJson)["R"] == 1,
            "partition.checkpoint-follows-result", "the composition advances its durable cursor only after accepting the retried result");
        await DrainPartitionAsync(coordinator, executionId);
        Assert(_sink.CountEffects(_scope, executionId) == 5 && source.PeakFetched(_scope, executionId) <= 2,
            "partition.replay-no-loss-bounded", "all five rows complete with no lost effect and the two-row fetch bound held");
    }

    private static async Task ProveAggregateAdmissionAsync()
    {
        const string id = "aggregate-admission";
        var source = new PartitionSourceStore(_workDb, _authorizer);
        foreach (string partition in new[] { "A", "B", "C" })
            source.CreatePartition(_scope, id, partition, 3, 3, ["one", "two", "three"]);
        Assert(Throws<InvalidOperationException>(() => source.ConfigureBudget(_scope, id, 2)),
            "admission.reject-too-small-budget", "budget cannot be smaller than a configured pinned batch");
        source.ConfigureBudget(_scope, "budget-first-validation", 2);
        Assert(Throws<InvalidOperationException>(() => source.CreatePartition(_scope, "budget-first-validation", "A", 3, 3, [])),
            "admission.reject-too-large-batch", "partition cannot exceed an already configured budget even when empty");
        source.ConfigureBudget(_scope, id, 6);
        var coordinator = new CompositionCoordinator(_store, _scope, id, "admission-owner", new PartitionPipelinePolicy());
        coordinator.Start(JsonSupport.Serialize(new PartitionPipelineInput(
            new[] { "A", "B", "C" }.Select(p => new PartitionBatchInput(p, -1, 3)).ToArray())));
        _store.CreateBarrier("admission-A");
        _store.CreateBarrier("admission-B");
        RunningChild a = StartWorker(_workDb, _sinkDb, _scope, id, "partition:A:after:-1", "admission-a", "admission-A", "none");
        RunningChild b = StartWorker(_workDb, _sinkDb, _scope, id, "partition:B:after:-1", "admission-b", "admission-B", "none");
        await WaitForBarrier("admission-A", 1);
        await WaitForBarrier("admission-B", 1);
        ChildResult denied = await WaitChild("budget-refuses-third-worker", StartWorker(_workDb, _sinkDb, _scope, id, "partition:C:after:-1", "admission-c", "-", "none"));
        Assert(denied.ExitCode == 12 && source.CurrentInFlight(_scope, id) == 6 && _sink.CountEffects(_scope, id) == 0,
            "admission.third-worker-refused-before-fetch", "two held batches fill six-record budget; third OS worker receives backpressure without a source read or effect");
        // Actual process death, rather than lease expiry, establishes that A's buffer is gone.
        a.Process.Kill(entireProcessTree: true);
        ChildResult killed = await WaitChild("budget-worker-killed-before-end-batch", a);
        Assert(killed.ExitCode != 0 && source.CurrentInFlight(_scope, id) == 6,
            "admission.crash-keeps-reservation", "a killed worker leaves conservative durable accounting until the host confirms its exit");
        source.ReleaseAfterConfirmedWorkerExit(_scope, id, "partition:A:after:-1", 1);
        Assert(source.CurrentInFlight(_scope, id) == 3, "admission.confirmed-exit-releases-budget", "confirmed exit releases only A's attempt reservation");
        _store.ForceExpireWorkForProof(_scope, id, "partition:C:after:-1");
        ChildResult c = await WaitChild("budget-third-worker-retry", StartWorker(_workDb, _sinkDb, _scope, id, "partition:C:after:-1", "admission-c-retry", "-", "none"));
        Assert(c.ExitCode == 0 && source.CurrentInFlight(_scope, id) == 3 && _sink.CountEffects(_scope, id) == 3,
            "admission.capacity-reused-without-blocking-B", "C succeeds after released capacity while B remains held");
        _store.ReleaseBarrier("admission-B");
        ChildResult completedB = await WaitChild("budget-B-resumes", b);
        Assert(completedB.ExitCode == 0, "admission.B-completes", "held B completes normally");
        _store.ForceExpireWorkForProof(_scope, id, "partition:A:after:-1");
        ChildResult recoveredA = await WaitChild("budget-A-recovered", StartWorker(_workDb, _sinkDb, _scope, id, "partition:A:after:-1", "admission-a-retry", "-", "none"));
        Assert(recoveredA.ExitCode == 0, "admission.A-recovered", "fresh worker recovers killed A's pinned batch");
        coordinator.ApplyPendingResults();
        await DrainPartitionAsync(coordinator, id);
        Assert(_store.ReadExecution(_scope, id).Status == "Completed" && source.CurrentInFlight(_scope, id) == 0 && _sink.CountEffects(_scope, id) == 9,
            "admission.all-partitions-complete", "three partitions complete with nine distinct effects and no retained reservation");
    }

    private static async Task ProveCommitReplayAndFencingAsync()
    {
        const string executionId = "commit-replay-main";
        CompositionPlan start = new("{\"counter\":0}", [], [], false);
        _store.CreateAndCommitInitialPlan(_scope, executionId, "commit-replay-policy", "coordinator-old", $"start:{executionId}", start);
        CoordinatorSnapshot old = _store.ReadCoordinator(_scope, executionId);
        ExecutionSnapshot before = _store.ReadExecution(_scope, executionId);
        const string commitId = "lost-response-commit";
        var lost = WorkerCommands.Start("--lost-commit-reply", _workDb, WorkerCommands.EncodeScope(_scope), executionId,
            old.Owner, old.Fence.ToString(), before.Revision.ToString(), commitId);
        ChildResult lostResponse = await WaitChild("commit-process-exits-after-transaction", lost);
        Assert(lostResponse.ExitCode == 73 && lostResponse.Output.Contains("COMMIT_DONE_RESPONSE_NOT_DELIVERED"), "commit.reply-loss-is-real-process-exit", $"process {lostResponse.ProcessId} exited after commit marker/state/owed work transaction and before returning reply");
        int rowsAfterCommit = _store.CountWorkRows(_scope, executionId);
        long newFence = _store.AcquireExecution(_scope, executionId, "coordinator-new");
        ExecutionSnapshot afterOwnerChange = _store.ReadExecution(_scope, executionId);
        var original = new CompositionPlan("{\"counter\":1}", [new WorkDefinition("lost-response-work", "snapshot-echo", "1", "{\"value\":\"once\"}")], [], false);
        CommitReceipt replay = _store.CommitPlan(_scope, executionId, old.Owner, old.Fence, before.Revision, commitId, original);
        Assert(replay.Status == "Running" && _store.CountWorkRows(_scope, executionId) == rowsAfterCommit && _store.ReadExecution(_scope, executionId).Revision == afterOwnerChange.Revision,
            "commit.same-fingerprint-replay-after-owner-change", "same receipt is returned despite prior owner epoch and no state or dispatch is written twice");
        bool conflict = Throws<InvalidOperationException>(() => _store.CommitPlan(_scope, executionId, "coordinator-new", newFence,
            afterOwnerChange.Revision, commitId, new CompositionPlan("{\"counter\":2}", [], [], false)));
        Assert(conflict, "commit.conflicting-fingerprint-rejected", "same commit ID with different state/work content is refused after owner change");
        bool staleWriter = Throws<InvalidOperationException>(() => _store.CommitPlan(_scope, executionId, old.Owner, old.Fence,
            afterOwnerChange.Revision, "stale-new-commit", new CompositionPlan("{\"counter\":3}", [], [], false)));
        Assert(staleWriter, "commit.expired-old-owner-rejected", "a fresh write from superseded owner/fence is rejected; expiry alone is not revocation until a new fence is claimed");

        ChildResult receiptMutant = await WaitChild("negative-control-receipt-fingerprint", WorkerCommands.Start("--mutant", "receipt", _workDb,
            WorkerCommands.EncodeScope(_scope), executionId, commitId));
        RecordMutation("receipt-fingerprint", receiptMutant, "MUTATION_INVARIANT_FAILED: conflicting-receipt-accepted");
        ChildResult fenceMutant = await WaitChild("negative-control-fence-check", WorkerCommands.Start("--mutant", "fence", _workDb,
            WorkerCommands.EncodeScope(_scope), executionId, old.Owner, old.Fence.ToString()));
        RecordMutation("commit-fencing", fenceMutant, "MUTATION_INVARIANT_FAILED: stale-fence-write");
    }

    private static async Task DrainPartitionAsync(CompositionCoordinator coordinator, string executionId)
    {
        for (int turn = 0; turn < 12 && _store.ReadExecution(_scope, executionId).Status != "Completed"; turn++)
        {
            WorkSnapshot work = _store.ReadWork(_scope, executionId).First(x => x.Status == "owed");
            RunningChild child = StartWorker(_workDb, _sinkDb, _scope, executionId, work.InvocationId, $"drain-{executionId}-{turn}", "-", "none");
            ChildResult completed = await WaitChild($"partition-drain-{executionId}-{turn}", child);
            Assert(completed.ExitCode == 0, $"partition.drain-worker-{executionId}-{turn}", $"worker process {completed.ProcessId} completed");
            Assert(coordinator.ApplyPendingResults() == 1, $"partition.drain-receipt-{executionId}-{turn}", "component consumes exactly one batch result");
        }
        Assert(_store.ReadExecution(_scope, executionId).Status == "Completed", $"partition.drain-terminal-{executionId}", "partition policy reaches its explicit terminal plan");
    }

    private static RunningChild StartWorker(string workDb, string sinkDb, WorkScope scope, string executionId, string invocationId, string worker, string barrier, string failpoint)
        => WorkerCommands.Start("--worker", workDb, WorkerCommands.EncodeScope(scope), executionId, invocationId, worker, barrier, failpoint, sinkDb);

    private static async Task WaitForBarrier(string barrier, int expected)
    {
        var timer = Stopwatch.StartNew();
        while (_store.BarrierEntryCount(barrier) < expected)
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(25)) throw new TimeoutException($"Expected {expected} child entries at barrier '{barrier}'.");
            await Task.Delay(10);
        }
    }

    private static async Task<ChildResult> WaitChild(string role, RunningChild child)
    {
        ChildResult result = await WorkerCommands.WaitAsync(child);
        string marker = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).FirstOrDefault(x => x.StartsWith("COMMIT_") || x.StartsWith("FAILPOINT:") || x.StartsWith("MUTATION_INVARIANT_FAILED:") || x.StartsWith("EXIT_BEFORE_")) ?? $"exit={result.ExitCode}";
        lock (EvidenceLock) Processes.Add(new ProcessEvidence(role, result.ProcessId, result.ExitCode, marker));
        return result;
    }

    private static void RecordMutation(string name, ChildResult result, string expectedMarker)
    {
        bool detected = result.ExitCode == 41 && result.Output.Contains(expectedMarker, StringComparison.Ordinal);
        Mutations.Add(new MutationEvidence(name, result.ProcessId, result.ExitCode, expectedMarker, detected));
        Assert(detected, $"negative.{name}-mutation-killed", $"separate process exited {result.ExitCode} with marker '{expectedMarker}'");
    }

    private static WorkLease Lease(WorkSnapshot work, WorkScope scope, string executionId)
        => new(scope, executionId, work.InvocationId, work.Owner ?? throw new InvalidOperationException("Completed work has no owner."), work.Fence,
            work.Attempt, work.HandlerId, work.HandlerVersion, work.InputJson);

    private static IReadOnlyDictionary<string, long> ReadPartitionCursors(string stateJson)
        => JsonDocument.Parse(stateJson).RootElement.GetProperty("Partitions").EnumerateArray()
            .ToDictionary(x => x.GetProperty("PartitionId").GetString()!, x => x.GetProperty("Cursor").GetInt64(), StringComparer.Ordinal);

    private sealed record ConcurrentResultState(string[] Accepted);

    private static void Assert(bool passed, string id, string observed)
    {
        Assertions.Add(new ProofAssertion(id, passed, observed));
        if (!passed) throw new InvalidOperationException($"Assertion '{id}' failed: {observed}");
    }

    private static bool Throws<TException>(Action action) where TException : Exception
    {
        try { action(); return false; }
        catch (TException) { return true; }
    }

    private static string Describe(Task<int> task)
        => task.Status == TaskStatus.RanToCompletion ? $"completed:{task.Result}"
            : task.Exception?.GetBaseException().GetType().Name ?? task.Status.ToString();

    private static string ReadGitHead()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            process!.WaitForExit();
            return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd().Trim() : "unknown";
        }
        catch { return "unknown"; }
    }

    private static IReadOnlyDictionary<string, string> ReadSourceHashes()
    {
        string projectRoot = Directory.GetCurrentDirectory();
        string relativeRoot = Path.Combine("tools", "experiments", "UnifiedExecution.WorkProof");
        string[] sourceFiles = ["UnifiedExecution.WorkProof.csproj", "packages.lock.json", "Program.cs", "Contracts.cs", "DurableWorkStore.cs", "Composition.cs", "IndependentSink.cs", "PartitionSourceStore.cs", "SqliteScopeParameters.cs", "WorkerCommands.cs", "ProofHarness.cs", "README.md"];
        return sourceFiles.ToDictionary(name => name, name =>
        {
            string path = Path.Combine(projectRoot, relativeRoot, name);
            if (!File.Exists(path)) return "missing";
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        }, StringComparer.Ordinal);
    }

    private static IReadOnlyList<AuditEvidence> CaptureAudit()
    {
        string[] executionIds = ["aggregate-admission", "same-invocation-claim", "same-revision-result-race", "sequence-main", "flowchart-main", "flowchart-clean", "sequence-cancel-first", "sequence-success-first",
            "red-only", "unsupported-handler-version", "etl-pipeline-main", "etl-crash-replay", "lease-expired-current-owner", "commit-replay-main"];
        return executionIds.SelectMany(executionId => _store.ReadAudit(_scope, executionId)
            .Select(x => new AuditEvidence(executionId, x.Kind, x.Subject, x.CreatedUtc))).ToArray();
    }

    private static string SqliteVersion(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version()";
        return (string)command.ExecuteScalar()!;
    }

    private static async Task WriteEvidenceAsync(string path, ProofReport report)
    {
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void CreateEvidenceArchive(string evidencePath, string archivePath)
    {
        if (File.Exists(archivePath)) File.Delete(archivePath);
        using var archive = System.IO.Compression.ZipFile.Open(archivePath, System.IO.Compression.ZipArchiveMode.Create);
        archive.CreateEntryFromFile(evidencePath, "evidence.json", System.IO.Compression.CompressionLevel.Optimal);
    }
}

internal sealed record ProofAssertion(string Id, bool Passed, string Observed);
internal sealed record ProcessEvidence(string Role, int ProcessId, int ExitCode, string Marker);
internal sealed record MutationEvidence(string Name, int ProcessId, int ExitCode, string ExpectedFailureMarker, bool Detected);
internal sealed record AuditEvidence(string ExecutionId, string Kind, string Subject, string CreatedUtc);
internal sealed record ProofReport(string Format, DateTimeOffset CapturedUtc, string SourceCommit, IReadOnlyDictionary<string, string> SourceFiles,
    string SqlitePackageAssemblyVersion, string NativeSqliteVersion, string ScratchRoot, IReadOnlyList<ProofAssertion> Assertions,
    IReadOnlyList<ProcessEvidence> Processes, IReadOnlyList<MutationEvidence> NegativeControls, IReadOnlyList<AuditEvidence> DurableAudit, string Limits);

internal static class MutationCommands
{
    public static int Run(string[] args)
    {
        try
        {
            string key = Environment.GetEnvironmentVariable("ELSA_WORK_PROOF_KEY") ?? throw new InvalidOperationException("Proof scope key was not inherited by mutant process.");
            var authorizer = new HmacWorkScopeAuthorizer(key);
            string mutant = args[1];
            string workDb = args[2];
            WorkScope scope = WorkerCommands.DecodeScope(args[3]);
            var store = new DurableWorkStore(workDb, authorizer);
            string executionId = args[4];

            switch (mutant)
            {
                case "completion":
                    store.UnsafeCompleteIfNoWork(scope, executionId);
                    if (store.ReadExecution(scope, executionId).Status == "Completed")
                    {
                        Console.WriteLine("MUTATION_INVARIANT_FAILED: completion-owner");
                        return 41;
                    }
                    return 0;
                case "scope":
                    WorkSnapshot? leaked = store.UnsafeReadWorkIgnoringScope(executionId, args[5]);
                    if (leaked is not null && leaked.TenantId != scope.TenantId)
                    {
                        Console.WriteLine("MUTATION_INVARIANT_FAILED: scope-filter");
                        return 41;
                    }
                    return 0;
                case "fence":
                {
                    string oldOwner = args[5];
                    long oldFence = long.Parse(args[6]);
                    ExecutionSnapshot current = store.ReadExecution(scope, executionId);
                    var plan = new CompositionPlan("{\"mutant\":\"stale-owner-wrote\"}", [], [], false);
                    store.UnsafeCommitPlanIgnoringFence(scope, executionId, oldOwner, oldFence, current.Revision,
                        "mutant-stale-write", plan);
                    Console.WriteLine("MUTATION_INVARIANT_FAILED: stale-fence-write");
                    return 41;
                }
                case "receipt":
                {
                    string commitId = args[5];
                    var current = store.ReadCoordinator(scope, executionId);
                    ExecutionSnapshot execution = store.ReadExecution(scope, executionId);
                    var conflict = new CompositionPlan("{\"counter\":999}", [], [], false);
                    store.UnsafeCommitPlanIgnoringFingerprint(scope, executionId, current.Owner, current.Fence,
                        execution.Revision, commitId, conflict);
                    Console.WriteLine("MUTATION_INVARIANT_FAILED: conflicting-receipt-accepted");
                    return 41;
                }
                default:
                    throw new ArgumentException($"Unknown mutant '{mutant}'.");
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"MUTANT_ERROR: {exception.GetType().Name}: {exception.Message}");
            return 3;
        }
    }
}
