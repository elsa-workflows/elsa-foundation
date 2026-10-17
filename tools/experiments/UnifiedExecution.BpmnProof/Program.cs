using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Bpmn.Model;
using Bpmn.Model.State;
using Bpmn.Semantics;

namespace UnifiedExecution.BpmnProof;

internal sealed record SequenceAdapterComparison(
    bool Match,
    string ChildContinuation,
    string ParentContinuation,
    IReadOnlyList<HostTraceEvent> DirectChildTrace,
    IReadOnlyList<HostTraceEvent> EmbeddedChildTrace,
    IReadOnlyList<SequenceEnvelopeRecord> ParentTransitions);

internal sealed record SourceIntegrityEvidence(
    string Repository,
    string Commit,
    string Tree,
    bool WorkingTreeClean,
    string PackagesLockSha256,
    string ProofProjectSha256);

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "restart-stage")
                return RestartStage(args[1], args[2]);
            if (args.Length > 0 && args[0] == "sequence-stage")
                return SequenceStage(args[1], args[2]);

            var command = args.FirstOrDefault() ?? "verify";
            var artifactsPath = ReadOption(args, "--artifacts")
                ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../artifacts"));
            Directory.CreateDirectory(artifactsPath);

            return command switch
            {
                "verify" => await VerifyAsync(artifactsPath),
                "help" or "--help" or "-h" => PrintHelp(),
                _ => throw new ArgumentException($"Unknown command '{command}'. Use 'verify' or 'help'.")
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"BPMN proof failed: {exception}");
            return 1;
        }
    }

    private static async Task<int> VerifyAsync(string artifactsPath)
    {
        var sourceIntegrity = CaptureSourceIntegrity();
        Require(sourceIntegrity.Commit == "be9c6eaa963b846104d5aebf38d337bafab08710" && sourceIntegrity.WorkingTreeClean,
            "The operational witness requires the clean, exact BPMN source pin. Run bootstrap.sh to restore it.");
        WriteJson(Path.Combine(artifactsPath, "source-integrity.json"), sourceIntegrity);

        var restartPath = Path.Combine(artifactsPath, "restart-fork-join.db");
        DeleteDatabase(restartPath);
        var restart = await VerifyFreshProcessRestartAsync(restartPath);

        var timer = VerifyTimerHumanRaces(artifactsPath);
        var incidents = VerifyWorkerIncidents(artifactsPath);
        var alteration = VerifyValidatedAlteration(artifactsPath);
        var definitionPin = VerifyDefinitionPin(artifactsPath);
        var ownership = VerifyHandleOwnership(artifactsPath);
        var comparison = await VerifySequenceAdapterAsync(artifactsPath, restartPath);
        WriteJson(Path.Combine(artifactsPath, "direct-vs-sequence-adapter.json"), comparison);

        var report = new
        {
            result = "passed",
            upstreamPin = sourceIntegrity.Commit,
            sourceIntegrity,
            database = "SQLite; state, work ledger and command outbox checkpointed together",
            freshProcesses = restart,
            timerHumanLateResult = timer,
            workerIncidents = incidents,
            validatedAlteration = alteration,
            definitionPin,
            handleOwnership = ownership,
            directVsSequenceAdapter = comparison,
            limitations = new[]
            {
                "The SQLite host models work as local ledger rows; no external worker or broker is connected.",
                "The Sequence-style adapter is a small experimental host-side composition proof, not an Elsa integration.",
                "The experiment proves process-restart recovery, not distributed execution or cross-system exactly-once effects."
            }
        };
        WriteJson(Path.Combine(artifactsPath, "operational-witness.json"), report);
        Console.WriteLine("BPMN operational witness passed.");
        Console.WriteLine($"Source pin: {report.upstreamPin}");
        Console.WriteLine("Evidence: fresh-process fork/join, timer/human races, incidents, modeled-message dispatch, and rejection controls.");
        Console.WriteLine($"Detailed artifacts: {Path.GetFullPath(artifactsPath)}");
        return 0;
    }

    private static async Task<object> VerifyFreshProcessRestartAsync(string databasePath)
    {
        await RunChildStageAsync("initialize", databasePath);
        await RunChildStageAsync("resume-left", databasePath);
        await RunChildStageAsync("resume-right", databasePath);

        using var store = new ProofStore(databasePath);
        var scope = store.GetScope("fork-instance");
        Require(scope.ContinuationKind == "complete", "The fresh-process fork/join run did not complete.");
        Require(store.PendingOutboxCount("fork-instance") == 0, "The command outbox was not fully drained.");
        var joined = store.GetTrace("fork-instance").Any(item => item.Kind == "start-work" && item.BindingRef == "right");
        Require(joined, "The right branch was not restored and dispatched after process restart.");

        var result = new
        {
            scope = "fork-instance",
            processes = 3,
            initialCommandsLeftOwedAtExit = 2,
            restartBoundaries = 2,
            joinedAfterRestore = true,
            continuation = scope.ContinuationKind,
            trace = store.GetTrace("fork-instance")
        };
        WriteJson(Path.ChangeExtension(databasePath, ".json"), result);
        return result;
    }

    private static int RestartStage(string stage, string databasePath)
    {
        var definition = ProofProcesses.ForkJoin();
        switch (stage)
        {
            case "initialize":
                using (var host = DurableBpmnHost.Create(databasePath, "fork-instance", definition))
                {
                    host.Start(dispatchOwedCommands: false);
                    Require(host.State.ActiveWork.Count == 2, "Fork evaluation should persist both active work records.");
                    Require(host.PendingCommandCount == 2, "The start-work commands should remain owed before process exit.");
                    Console.WriteLine("checkpointed state and 2 owed commands; exiting process");
                }
                return 0;

            case "resume-left":
                using (var host = DurableBpmnHost.Restore(databasePath, "fork-instance", definition))
                {
                    Require(host.PendingCommandCount == 0, "Restore did not replay the owed command batch.");
                    Require(host.LiveWork.Count == 2, "Restore did not rebuild both live work handles.");
                    var left = host.LiveWork.Single(work => work.BindingRef == "left");
                    host.CompleteWork(left.Handle);
                    Require(host.State.Tokens.Any(token => token.AtElementId == "Join" && token.Status == BpmnTokenStatus.WaitingAtJoin),
                        "The first branch should persist at the fork/join barrier.");
                    Console.WriteLine("restored owed commands, completed left and checkpointed the waiting join; exiting process");
                }
                return 0;

            case "resume-right":
                using (var host = DurableBpmnHost.Restore(databasePath, "fork-instance", definition))
                {
                    Require(host.State.Tokens.Any(token => token.AtElementId == "Join" && token.Status == BpmnTokenStatus.WaitingAtJoin),
                        "The join barrier was not recovered from persisted state.");
                    var right = host.LiveWork.Single(work => work.BindingRef == "right");
                    host.CompleteWork(right.Handle);
                    Require(host.ContinuationKind == "complete", "The second branch should release the join and finish.");
                    Console.WriteLine("restored the waiting join, completed right and finished; exiting process");
                }
                return 0;

            default:
                throw new ArgumentException($"Unknown restart stage '{stage}'.");
        }
    }

    private static async Task RunChildStageAsync(string stage, string databasePath)
    {
        var assembly = Assembly.GetEntryAssembly()?.Location
            ?? throw new InvalidOperationException("Could not locate the proof assembly for fresh-process restart.");
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("restart-stage");
        start.ArgumentList.Add(stage);
        start.ArgumentList.Add(databasePath);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the fresh-process proof stage.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Restart child '{stage}' exited {process.ExitCode}: {stderr}\n{stdout}");
        Console.WriteLine($"child[{stage}]: {stdout.Trim()}");
    }

    private static object VerifyTimerHumanRaces(string artifactsPath)
    {
        var humanFirstPath = Path.Combine(artifactsPath, "human-first.db");
        DeleteDatabase(humanFirstPath);
        object humanFirst;
        using (var host = DurableBpmnHost.Create(humanFirstPath, "human-first", ProofProcesses.HumanWithDeadline()))
        {
            host.Start();
            var review = host.LiveWork.Single(work => work.BindingRef == "review");
            host.CompleteWork(review.Handle);
            Require(host.ContinuationKind == "complete", "Human completion should win before the deadline.");
            Require(host.WorkItems.Single(work => work.BindingRef == "timeout").Status == "cancelled",
                "Human completion should retire the timer by a semantic cancel command.");
            humanFirst = new { winner = "human", continuation = host.ContinuationKind, trace = host.Trace };
        }

        var timerFirstPath = Path.Combine(artifactsPath, "timer-first.db");
        DeleteDatabase(timerFirstPath);
        object timerFirst;
        using (var host = DurableBpmnHost.Create(timerFirstPath, "timer-first", ProofProcesses.HumanWithDeadline()))
        {
            host.Start();
            var review = host.LiveWork.Single(work => work.BindingRef == "review");
            host.AdvanceBy(TimeSpan.FromHours(1));
            Require(host.WorkItems.Single(work => work.Handle == review.Handle).Status == "cancelled",
                "Timer completion should cancel the live human-task work item.");
            Require(host.LiveWork.Any(work => work.BindingRef == "cleanup"), "The timeout path should remain live for late-result absorption.");

            host.CompleteWork(review.Handle);
            Require(host.State.Diagnostics.Last().Kind == BpmnDiagnosticKind.Canceled,
                "The interrupted worker's first late result should be absorbed and diagnosed.");
            var duplicateStayedLoud = false;
            try
            {
                host.CompleteWork(review.Handle);
            }
            catch (BpmnExecutionException)
            {
                duplicateStayedLoud = true;
            }
            Require(duplicateStayedLoud, "A duplicate late result should not be absorbed a second time.");

            var cleanup = host.LiveWork.Single(work => work.BindingRef == "cleanup");
            host.CompleteWork(cleanup.Handle);
            Require(host.ContinuationKind == "complete", "The timeout path should complete after cleanup.");
            timerFirst = new
            {
                winner = "timer",
                lateResultAbsorbedOnce = true,
                duplicateLateResultRejected = true,
                continuation = host.ContinuationKind,
                trace = host.Trace,
                diagnostics = ProofProcesses.NormalizeDiagnostics(host.State)
            };
        }

        var report = new { humanFirst, timerFirst };
        WriteJson(Path.Combine(artifactsPath, "timer-human-races.json"), report);
        return report;
    }

    private static object VerifyWorkerIncidents(string artifactsPath)
    {
        var caughtPath = Path.Combine(artifactsPath, "worker-caught.db");
        DeleteDatabase(caughtPath);
        object caught;
        using (var host = DurableBpmnHost.Create(caughtPath, "worker-caught", ProofProcesses.Risky(catchError: true)))
        {
            host.Start();
            var worker = host.LiveWork.Single(work => work.BindingRef == "worker");
            var disposition = host.FaultWork(worker.Handle, "upstream service refused the request");
            Require(disposition is BpmnErrorDisposition.Caught, "The error boundary should catch the modeled worker fault.");
            Require(host.Incidents.Count == 0, "A caught BPMN error must not become an operator incident in this host policy.");
            host.CompleteWork(host.LiveWork.Single(work => work.BindingRef == "recover").Handle);
            Require(host.ContinuationKind == "complete", "Recovery path should complete.");
            caught = new { disposition = disposition.GetType().Name, incidentCount = host.Incidents.Count, trace = host.Trace };
        }

        var propagatedPath = Path.Combine(artifactsPath, "worker-propagated.db");
        DeleteDatabase(propagatedPath);
        object propagated;
        using (var host = DurableBpmnHost.Create(propagatedPath, "worker-propagated", ProofProcesses.Risky(catchError: false)))
        {
            host.Start();
            var worker = host.LiveWork.Single(work => work.BindingRef == "worker");
            var disposition = host.FaultWork(worker.Handle, "upstream service refused the request");
            Require(disposition is BpmnErrorDisposition.Propagated, "Uncaught worker fault should be propagated by BPMN semantics.");
            Require(host.Incidents.Count == 1, "This host policy should record one incident for a propagated worker failure.");
            Require(host.ContinuationKind == "fault", "Uncaught worker fault should fault the process scope.");
            propagated = new { disposition = disposition.GetType().Name, incidentCount = host.Incidents.Count, incidents = host.Incidents, trace = host.Trace };
        }

        var report = new { caught, propagated };
        WriteJson(Path.Combine(artifactsPath, "worker-incidents.json"), report);
        return report;
    }

    private static object VerifyValidatedAlteration(string artifactsPath)
    {
        var path = Path.Combine(artifactsPath, "validated-alteration.db");
        DeleteDatabase(path);
        using var host = DurableBpmnHost.Create(path, "alteration-proof", ProofProcesses.HumanWithMessageInterrupt());
        host.Start();
        var stateBeforeUnsupportedMove = JsonSerializer.Serialize(host.State, JsonOptions);
        var moveRejected = false;
        try
        {
            host.RejectUnsupportedAlteration("move-token");
        }
        catch (UnsupportedAlterationException)
        {
            moveRejected = true;
        }
        Require(moveRejected, "Arbitrary token movement must be rejected as unsupported.");
        Require(stateBeforeUnsupportedMove == JsonSerializer.Serialize(host.State, JsonOptions),
            "Rejected alteration must not mutate interpreter state.");

        var review = host.LiveWork.Single(work => work.BindingRef == "review");
        var listener = host.LiveWork.Single(work => work.BindingRef == "cancel-review-listener");
        var stateBeforeMismatch = JsonSerializer.Serialize(host.State, JsonOptions);
        var mismatchRejected = false;
        try
        {
            host.DeliverModeledMessage(listener.Handle, "different-message");
        }
        catch (InvalidOperationException)
        {
            mismatchRejected = true;
        }
        Require(mismatchRejected, "A message that does not match the modeled listener must be rejected.");
        Require(stateBeforeMismatch == JsonSerializer.Serialize(host.State, JsonOptions),
            "A rejected message must not mutate interpreter state.");

        host.DeliverModeledMessage(listener.Handle, ProofProcesses.InterruptionMessageName);
        Require(host.WorkItems.Single(work => work.Handle == review.Handle).Status == "cancelled",
            "The modeled interrupting message should cancel the human task through the interpreter's command.");
        Require(host.Trace.Any(item => item.Kind == "cancel-work-subtree" && item.Handle == review.Handle),
            "The modeled interruption must be observable as a returned CancelWorkSubtree command.");
        var stateBeforeStaleDelivery = JsonSerializer.Serialize(host.State, JsonOptions);
        var staleRejected = false;
        try
        {
            host.DeliverModeledMessage(listener.Handle, ProofProcesses.InterruptionMessageName);
        }
        catch (InvalidOperationException)
        {
            staleRejected = true;
        }
        Require(staleRejected && stateBeforeStaleDelivery == JsonSerializer.Serialize(host.State, JsonOptions),
            "A stale listener handle must be rejected without mutating interpreter state.");

        var report = new
        {
            supportedOperation = "deliver-modeled-message",
            result = "accepted-only-for-live-matching-listener",
            emittedCommand = "CancelWorkSubtree",
            mismatchedMessage = "rejected-without-state-mutation",
            staleListener = "rejected-without-state-mutation",
            unsupportedOperation = "move-token",
            unsupportedResult = "rejected-without-state-mutation",
            trace = host.Trace
        };
        WriteJson(Path.Combine(artifactsPath, "validated-alteration.json"), report);
        return report;
    }

    private static object VerifyDefinitionPin(string artifactsPath)
    {
        var path = Path.Combine(artifactsPath, "definition-pin.db");
        DeleteDatabase(path);
        using (DurableBpmnHost.Create(path, "definition-pin", ProofProcesses.ForkJoin())) { }

        var rejected = false;
        try
        {
            using var changed = DurableBpmnHost.Restore(path, "definition-pin", ProofProcesses.ForkJoinChangedGraph());
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("fingerprint", StringComparison.Ordinal))
        {
            rejected = true;
        }
        Require(rejected, "Restore must reject a changed BPMN graph even when the process id is unchanged.");
        var report = new
        {
            sameProcessId = ProofProcesses.ForkJoin().ProcessId,
            changedGraph = "Right -> RightV2",
            result = "restore-rejected-by-definition-fingerprint"
        };
        WriteJson(Path.Combine(artifactsPath, "definition-pin.json"), report);
        return report;
    }

    private static object VerifyHandleOwnership(string artifactsPath)
    {
        var path = Path.Combine(artifactsPath, "handle-ownership.db");
        DeleteDatabase(path);
        var definition = ProofProcesses.HumanWithMessageInterrupt();
        using var first = DurableBpmnHost.Create(path, "ownership-first", definition);
        using var second = DurableBpmnHost.Create(path, "ownership-second", definition);
        first.Start();
        second.Start();
        var foreignReview = first.LiveWork.Single(work => work.BindingRef == "review");
        var foreignListener = first.LiveWork.Single(work => work.BindingRef == "cancel-review-listener");
        var before = JsonSerializer.Serialize(second.State, JsonOptions);

        var completeRejected = Reject(() => second.CompleteWork(foreignReview.Handle));
        var faultRejected = Reject(() => second.FaultWork(foreignReview.Handle, "foreign callback"));
        var messageRejected = Reject(() => second.DeliverModeledMessage(foreignListener.Handle, ProofProcesses.InterruptionMessageName));
        Require(completeRejected && faultRejected && messageRejected,
            "Complete, fault and message delivery must reject handles owned by another scope.");
        Require(before == JsonSerializer.Serialize(second.State, JsonOptions),
            "Rejected foreign handles must not mutate the receiving scope's interpreter state.");

        var report = new
        {
            foreignComplete = "rejected",
            foreignFault = "rejected",
            foreignMessage = "rejected",
            receivingScopeUnchanged = true
        };
        WriteJson(Path.Combine(artifactsPath, "handle-ownership.json"), report);
        return report;

        static bool Reject(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    private static async Task<SequenceAdapterComparison> VerifySequenceAdapterAsync(string artifactsPath, string directDatabasePath)
    {
        var embeddedDatabasePath = Path.Combine(artifactsPath, "sequence-adapter.db");
        DeleteDatabase(embeddedDatabasePath);
        foreach (var stage in new[] { "initialize", "resume-left", "resume-right", "advance-tail", "complete-tail" })
            await RunSequenceStageAsync(stage, embeddedDatabasePath);

        using var directStore = new ProofStore(directDatabasePath);
        using var embeddedStore = new ProofStore(embeddedDatabasePath);
        using var sequenceStore = new SequenceEnvelopeStore(embeddedDatabasePath);
        var directTrace = directStore.GetTrace("fork-instance");
        var embeddedTrace = embeddedStore.GetTrace("sequence-proof:child");
        var normalizedDirect = NormalizeTrace(directTrace, "fork-instance");
        var normalizedEmbedded = NormalizeTrace(embeddedTrace, "sequence-proof:child");
        var parent = sequenceStore.Get("sequence-proof");
        var transitions = sequenceStore.GetTrace("sequence-proof");
        var match = normalizedDirect.SequenceEqual(normalizedEmbedded);
        Require(match, "Direct and persisted Sequence-adapter BPMN child traces diverged.");
        Require(parent.Status == "complete", "The Sequence-style parent did not resume and complete its next child.");
        Require(transitions.Select(item => item.Status).SequenceEqual(
                new[] { "waiting-child", "child-outcome-recorded", "next-child-pending", "waiting-tail", "complete" }),
            "The parent did not persist the expected wait -> child outcome -> next continuation transitions.");

        var result = new SequenceAdapterComparison(match, "complete", parent.Status, normalizedDirect, normalizedEmbedded, transitions);
        WriteJson(Path.Combine(artifactsPath, "direct-vs-sequence-adapter.json"), result);
        return result;
    }

    private static int SequenceStage(string stage, string databasePath)
    {
        const string sequenceId = "sequence-proof";
        const string childScopeId = "sequence-proof:child";
        switch (stage)
        {
            case "initialize":
                using (var sequence = new SequenceEnvelopeStore(databasePath))
                    sequence.Create(sequenceId, childScopeId);
                using (var child = DurableBpmnHost.Create(databasePath, childScopeId, ProofProcesses.ForkJoin()))
                {
                    child.Start(dispatchOwedCommands: false);
                    Require(child.PendingCommandCount == 2, "Sequence child must persist its fork work before process exit.");
                }
                Console.WriteLine("parent persisted waiting-child; child checkpointed fork and owed commands");
                return 0;

            case "resume-left":
                using (var child = DurableBpmnHost.Restore(databasePath, childScopeId, ProofProcesses.ForkJoin()))
                {
                    var left = child.LiveWork.Single(work => work.BindingRef == "left");
                    child.CompleteWork(left.Handle);
                    Require(child.State.Tokens.Any(token => token.AtElementId == "Join" && token.Status == BpmnTokenStatus.WaitingAtJoin),
                        "The embedded fork's first child should persist at its join.");
                }
                Console.WriteLine("restarted sequence child and checkpointed join wait");
                return 0;

            case "resume-right":
                using (var child = DurableBpmnHost.Restore(databasePath, childScopeId, ProofProcesses.ForkJoin()))
                {
                    var right = child.LiveWork.Single(work => work.BindingRef == "right");
                    child.CompleteWork(right.Handle);
                    Require(child.ContinuationKind == "complete", "Embedded fork/join child did not complete.");
                }
                using (var sequence = new SequenceEnvelopeStore(databasePath))
                    sequence.RecordChildOutcome(sequenceId, "complete");
                Console.WriteLine("restarted child, completed join, and persisted child outcome in parent");
                return 0;

            case "advance-tail":
                SequenceEnvelopeRecord run;
                using (var sequence = new SequenceEnvelopeStore(databasePath))
                    run = sequence.PrepareTail(sequenceId);
                using (var store = new ProofStore(databasePath))
                {
                    if (store.HasScope(run.CurrentChildScopeId))
                    {
                        using var tail = DurableBpmnHost.Restore(databasePath, run.CurrentChildScopeId, ProofProcesses.SequenceTail());
                        if (!tail.Trace.Any(item => item.Kind == "process-started"))
                            tail.Start();
                    }
                    else
                    {
                        using var tail = DurableBpmnHost.Create(databasePath, run.CurrentChildScopeId, ProofProcesses.SequenceTail());
                        tail.Start();
                    }
                }
                using (var sequence = new SequenceEnvelopeStore(databasePath))
                    sequence.MarkWaitingTail(sequenceId);
                Console.WriteLine("parent resumed next sequence child and checkpointed waiting-tail");
                return 0;

            case "complete-tail":
                string tailScope;
                using (var sequence = new SequenceEnvelopeStore(databasePath))
                    tailScope = sequence.Get(sequenceId).CurrentChildScopeId;
                using (var tail = DurableBpmnHost.Restore(databasePath, tailScope, ProofProcesses.SequenceTail()))
                {
                    tail.CompleteWork(tail.LiveWork.Single().Handle);
                    Require(tail.ContinuationKind == "complete", "The next sequence child did not complete.");
                }
                using (var sequence = new SequenceEnvelopeStore(databasePath))
                    sequence.MarkComplete(sequenceId, "complete");
                Console.WriteLine("restarted next child and completed sequence parent");
                return 0;

            default:
                throw new ArgumentException($"Unknown sequence stage '{stage}'.");
        }
    }

    private static async Task RunSequenceStageAsync(string stage, string databasePath)
    {
        var assembly = Assembly.GetEntryAssembly()?.Location
            ?? throw new InvalidOperationException("Could not locate the proof assembly for sequence restart.");
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("sequence-stage");
        start.ArgumentList.Add(stage);
        start.ArgumentList.Add(databasePath);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the sequence restart proof stage.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Sequence child '{stage}' exited {process.ExitCode}: {stderr}\n{stdout}");
        Console.WriteLine($"sequence[{stage}]: {stdout.Trim()}");
    }

    private static IReadOnlyList<HostTraceEvent> NormalizeTrace(IEnumerable<HostTraceEvent> trace, string scopeId) =>
        trace.Select(item => item with
        {
            Handle = item.Handle?.Replace($"{scopeId}:work-", "work-", StringComparison.Ordinal)
        }).ToArray();

    private static SourceIntegrityEvidence CaptureSourceIntegrity()
    {
        var projectDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../"));
        var sourceDirectory = Path.Combine(projectDirectory, ".source", "bpmn");
        var commit = RunGit(sourceDirectory, "rev-parse", "HEAD");
        var tree = RunGit(sourceDirectory, "rev-parse", "HEAD^{tree}");
        var status = RunGit(sourceDirectory, "status", "--porcelain", "--untracked-files=normal");
        var clean = string.IsNullOrEmpty(status);
        return new SourceIntegrityEvidence(
            "valence-works/bpmn",
            commit,
            tree,
            clean,
            Sha256(Path.Combine(projectDirectory, "packages.lock.json")),
            Sha256(Path.Combine(projectDirectory, "UnifiedExecution.BpmnProof.csproj")));
    }

    private static string RunGit(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not inspect the pinned BPMN checkout.");
        var output = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
        return output;
    }

    private static string Sha256(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string? ReadOption(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void DeleteDatabase(string path)
    {
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            var candidate = path + suffix;
            if (File.Exists(candidate))
                File.Delete(candidate);
        }
    }

    private static void WriteJson(string path, object value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static int PrintHelp()
    {
        Console.WriteLine("UnifiedExecution.BpmnProof [verify] [--artifacts <directory>]");
        Console.WriteLine("  verify  Run durable restart, timer/human, late result, incident, alteration, ownership and sequence-adapter assertions.");
        return 0;
    }
}
