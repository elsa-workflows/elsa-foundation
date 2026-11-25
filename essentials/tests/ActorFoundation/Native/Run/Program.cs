using System.Diagnostics;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Native.Migration.SqliteFixture;
using Kernel.EntityFramework;
using Kernel.Native;
using Kernel.Native.Run;
using Microsoft.EntityFrameworkCore;

// Usage:
//   dotnet ../../essentials/tests/ActorFoundation/Native/Run/bin/Release/net10.0/Kernel.Native.Run.dll smoke
//   dotnet ../../essentials/tests/ActorFoundation/Native/Run/bin/Release/net10.0/Kernel.Native.Run.dll cost sqlite
//   dotnet ../../essentials/tests/ActorFoundation/Native/Run/bin/Release/net10.0/Kernel.Native.Run.dll cost postgres "<connection string>"
//   dotnet ../../essentials/tests/ActorFoundation/Native/Run/bin/Release/net10.0/Kernel.Native.Run.dll memory [runs]

WriteLine.Out = null;
if (args is ["pg-workbench-core-preview", .. var workbenchPreview])
{
    var workbenchExecutable = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_WORKBENCH");
    if (string.IsNullOrWhiteSpace(workbenchExecutable) || !File.Exists(workbenchExecutable))
    {
        Console.Error.WriteLine("Set KERNEL_PREVIEW_WORKBENCH to the built Workbench executable.");
        return 2;
    }
    await Elsa.Workbench.Qualification.WorkbenchPostgresPreview.RunAsync(workbenchPreview, workbenchExecutable);
    return 0;
}
if (args is ["pg-workbench-preview-reads-seed", var originalRoot, var seedFile, .. var readsProfile] && readsProfile is ([] or ["timed"]))
    return await PostgresCorePreviewReadSeed.RunAsync(originalRoot, seedFile, timed: readsProfile.Length != 0);
if (args is ["profile-capabilities"]) return ProfileCapabilityChecks.Run();
if (args is ["pg-root-operators", "admit"])
    return await PostgresRootOperatorChecks.RunAsync(null);
if (args is ["pg-root-operators", var defaultOperatorPhase and ("read" or "resume"), var operatorA, var operatorB, var operatorC])
    return await PostgresRootOperatorChecks.RunAsync([operatorA, operatorB, operatorC], resume: defaultOperatorPhase == "resume");
if (args is ["pg-root-operators", var operatorWriter and ("owned" or "ef"), var operatorFence and ("in-batch" or "checked"), .. var operatorArgs])
{
    if (operatorArgs is ["admit"]) return await PostgresRootOperatorChecks.RunAsync(null, operatorWriter == "owned", operatorFence == "in-batch");
    if (operatorArgs is [var operatorPhase and ("read" or "resume"), var a, var b, var c])
        return await PostgresRootOperatorChecks.RunAsync([a, b, c], operatorWriter == "owned", operatorFence == "in-batch", operatorPhase == "resume");
}
if (args is ["sqlite-plan-projection", ..]) return await SqlitePlanFixtureCommand.RunAsync(args);
if (args is ["pg-http-owner-loss", var ownerFence and ("checked" or "in-batch"), var loss and ("before" or "before-late" or "after")])
    return await PostgresHttpOwnerLoss.RunAsync(ownerFence, loss);
if (args is ["pg-http-owner-process", var workerFence and ("checked" or "in-batch"), var pause and ("before" or "after" or "resume"), var ownerRef])
    return await PostgresHttpOwnerLoss.RunOwnerAsync(workerFence, pause, new(ownerRef));
if (args is ["pg-held-start"]) return await PostgresHeldStartChecks.RunAsync();
if (args is ["pg-held-reply-order", var orderCallers, var orderStarts] && int.TryParse(orderCallers, out var callerCount) &&
    int.TryParse(orderStarts, out var startCount))
    return await PostgresHeldStartChecks.RunOrderAsync(callerCount, startCount);
if (args is ["pg-http-preview", var httpFence and ("checked" or "in-batch"), var httpSchedule and ("timely" or "late" or "disconnect" or "slow-body" or "blocked" or "clock-delay")])
    return await PostgresHttpPreview.RunAsync(httpFence == "in-batch", httpSchedule);
if (args is ["pg-root-held-direct", var replyFence and ("checked" or "in-batch")])
    return await PostgresHeldDirectChecks.RunAsync(replyFence == "in-batch", afterDeadline: false);
if (args is ["pg-root-held-direct", var delayedReplyFence and ("checked" or "in-batch"), "after-deadline"])
    return await PostgresHeldDirectChecks.RunAsync(delayedReplyFence == "in-batch", afterDeadline: true);
if (args is [var chartMode and ("pg-cluster-statechart" or "pg-cluster-statechart-peer"), var chartSize] &&
    int.TryParse(chartSize, out var chartPartitions))
    return await PostgresClusterChecks.RunAsync(chartPartitions, chartMode == "pg-cluster-statechart-peer", statechart: true);
if (args is [var flowClusterMode and ("pg-cluster-flowchart" or "pg-cluster-flowchart-peer"), var flowSize] && int.TryParse(flowSize, out var flowPartitions))
    return await PostgresClusterChecks.RunAsync(flowPartitions, flowClusterMode == "pg-cluster-flowchart-peer", flowchart: true);
if (args is ["pg-preview-host-lifetime"]) return await PostgresPreviewLifetimeChecks.RunAsync();
if (args is ["pg-preview-host-watchdog"]) return await PostgresPreviewHostChecks.RunAsync();
if (args is [var clusterMode and ("pg-cluster-roots" or "pg-cluster-peer" or "pg-cluster-crash"), var size] && int.TryParse(size, out var clusterPartitions))
    return await PostgresClusterChecks.RunAsync(clusterPartitions, clusterMode == "pg-cluster-peer", clusterMode == "pg-cluster-crash");
if (args is ["pg-discover-roots", var discoveryPhase and ("birth" or "restart"), var discoveryScope, .. var modes] &&
    int.TryParse(discoveryScope, out var selectedPartition))
{
    if (modes is []) return await PostgresDiscoveryChecks.RunAsync(selectedPartition, discoveryPhase == "birth");
    if (modes is [var sql and ("owned" or "ef"), var check and ("in-batch" or "checked")])
        return await PostgresDiscoveryChecks.RunAsync(selectedPartition, discoveryPhase == "birth",
            sql == "owned" ? CommitSql.Owned : CommitSql.EntityFramework, check == "in-batch");
}
if (args is ["pg-multi-root-secondary", .. var clientArgs]) return await PostgresMultiRootClient.SecondaryAsync(clientArgs);
if (args is ["pg-multi-root", var writer and ("owned" or "ef"), var fence and ("in-batch" or "checked"), .. var phaseArgs])
{
    if (phaseArgs is ["birth"]) return await PostgresMultiRootChecks.RunAsync([], writer == "owned", fence == "in-batch");
    if (phaseArgs is ["restart", var a, var b, var outside]) return await PostgresMultiRootChecks.RunAsync([a, b, outside], writer == "owned", fence == "in-batch");
}
if (args is ["pg-root-job-preview", var rootJobFence and ("checked" or "in-batch"), "birth"])
    return await PostgresRootJobPreview.RunAsync(rootJobFence == "in-batch", null);
if (args is ["pg-root-job-preview", var rootJobRestartFence and ("checked" or "in-batch"), var rootJobPhase and ("advance" or "restart"),
    var jobActor, var jobNeighbor, var jobFreeApproval, var jobEpoch, var jobPosition, var jobBookmark, var neighborJobBookmark] &&
    long.TryParse(jobEpoch, out var priorJobEpoch) && priorJobEpoch > 0 && long.TryParse(jobPosition, out var priorJobPosition) && priorJobPosition > 0)
    return await PostgresRootJobPreview.RunAsync(rootJobRestartFence == "in-batch",
        new(jobActor, jobNeighbor, jobFreeApproval, priorJobEpoch, priorJobPosition, jobBookmark, neighborJobBookmark), finish: rootJobPhase == "restart");
if (args is ["pg-write-budget", var budgetWriter and ("owned" or "ef"), var budgetFence and ("checked" or "in-batch"), .. var budgetOptions] &&
    budgetOptions is [] or ["--require-budget"])
    return await PostgresWriteBudget.RunAsync(budgetWriter == "owned" ? CommitSql.Owned : CommitSql.EntityFramework,
        budgetFence == "in-batch", budgetOptions.Length != 0);
if (args is ["pg-root-value-check", var valueFence and ("checked" or "in-batch"), "birth"])
    return await PostgresRootValueChecks.RunAsync(valueFence == "in-batch", null);
if (args is ["pg-root-value-check", var valueRestartFence and ("checked" or "in-batch"), "restart", var valueActor, var valueEpoch] &&
    long.TryParse(valueEpoch, out var priorValueEpoch) && priorValueEpoch > 0)
    return await PostgresRootValueChecks.RunAsync(valueRestartFence == "in-batch", new(valueActor, priorValueEpoch));
if (args is ["pg-root-value-volume", var volumeStorage and ("inline" or "reference"), var volumePattern and ("reused" or "changing"),
    var volumePad, var volumeTurns] && volumePad.StartsWith("pad=", StringComparison.Ordinal) &&
    volumeTurns.StartsWith("turns=", StringComparison.Ordinal) && int.TryParse(volumePad.AsSpan(4), out var padBytes) && padBytes > 0 &&
    int.TryParse(volumeTurns.AsSpan(6), out var measuredTurns) && measuredTurns is > 1 and < int.MaxValue - 2)
    return await PostgresValueVolume.RunAsync(volumeStorage == "reference", volumePattern == "changing", padBytes, measuredTurns);
if (args is ["pg-timed-job-worker", var jobFence and ("checked" or "in-batch")])
    return await PostgresTimedJobChecks.RunAsync(jobFence == "in-batch");
if (args is ["pg-timed-job-worker", var jobWriter and ("owned" or "ef"), var physicalJobFence and ("checked" or "in-batch"), .. var physicalJobOptions] &&
    (physicalJobOptions is [] or ["without-trial-withdrawal"]))
    return await PostgresTimedJobChecks.RunAsync(physicalJobFence == "in-batch", jobWriter == "owned", physicalJobOptions.Length == 0);
if (args is ["pg-root-timed-job-issuance", var timedFence and ("checked" or "in-batch"), .. var timedPhase])
    return await PostgresRootTimedJobIssuance.RunAsync(timedFence, timedPhase);
if (args is ["pg-root-timed-job-settlement", var settleFence and ("checked" or "in-batch"), var winner and ("complete" or "timeout" or "late-clock" or "unhandled-timeout")])
    return await PostgresRootTimedJobSettlement.RunAsync(settleFence == "in-batch", winner);
if (args is [var runtimeCluster and ("pg-runtime-cluster" or "pg-runtime-cluster-member"), .. var clusterOptions])
{
    var wake = clusterOptions.LastOrDefault() == "wake";
    var sizeArguments = wake ? clusterOptions[..^1] : clusterOptions;
    var partitions = PostgresRuntimeCluster.DefaultPartitions;
    if (sizeArguments.Length > 1 || sizeArguments is [var count] && (!int.TryParse(count, out partitions) ||
        partitions is < 4 or > Defaults.ServerPartitions || partitions % 2 != 0))
    {
        Console.Error.WriteLine("Usage: pg-runtime-cluster [even partition count from 4 to 1024] [wake]");
        return 2;
    }
    return runtimeCluster == "pg-runtime-cluster"
        ? await PostgresRuntimeCluster.RunAsync(partitions, wake)
        : await PostgresRuntimeCluster.MemberAsync(partitions, wake);
}
if (args is ["pg-root-list", var listWriter and ("owned" or "ef"), var listFence and ("checked" or "in-batch"), .. var listArgs])
{
    if (listArgs is ["birth"]) return await PostgresRootListChecks.RunAsync(listWriter == "owned", listFence == "in-batch", null);
    if (listArgs is ["restart", var token]) return await PostgresRootListChecks.RunAsync(listWriter == "owned", listFence == "in-batch", new(token));
}
if (args is ["pg-root-workflow-history", var runWriter and ("owned" or "ef"), var runFence and ("checked" or "in-batch"), .. var runArgs])
{
    if (runArgs is ["birth"]) return await PostgresWorkflowHistoryChecks.RunAsync(runWriter == "owned", runFence == "in-batch", "birth");
    if (runArgs is ["restart", var runToken])
        return await PostgresWorkflowHistoryChecks.RunAsync(runWriter == "owned", runFence == "in-batch", "restart", new(runToken));
    if (runArgs is ["tenant", var tenantToken, var admittedReference])
        return await PostgresWorkflowHistoryChecks.RunAsync(runWriter == "owned", runFence == "in-batch", "tenant", new(tenantToken), admittedReference);
}
if (args is ["pg-root-history", var historyWriter and ("owned" or "ef"), var historyFence and ("checked" or "in-batch")])
    return await PostgresRootHistoryChecks.RunAsync(historyWriter == "owned", historyFence == "in-batch");
if (args is ["pg-statechart-preview", "birth"]) return await PostgresStatechartPreview.RunAsync(null);
if (args is ["pg-statechart-preview", "restart", var orderRef, var orderBookmark, var orderEpoch, var orderRevision, var orderHash, var orderOccurrence] &&
    long.TryParse(orderEpoch, out var previousOrderEpoch) && previousOrderEpoch > 0 &&
    long.TryParse(orderRevision, out var savedOrderRevision) && savedOrderRevision > 0)
    return await PostgresStatechartPreview.RunAsync(new(orderRef, orderBookmark, previousOrderEpoch, savedOrderRevision, orderHash, orderOccurrence));
if (args is ["pg-core-preview", var birthPhase and ("birth" or "hold-owner")]) return await PostgresPublicPreview.RunAsync(birthPhase);
if (args is ["pg-core-preview", "standby", var standbyRoot, var standbyBookmark, var priorEpoch, var priorPid, var initialLease] &&
    long.TryParse(priorEpoch, out var epoch) && epoch > 0 && int.TryParse(priorPid, out var peerPid) && peerPid > 0 &&
    long.TryParse(initialLease, out var originalUntil) && originalUntil > 0)
    return await PostgresPublicPreview.RunAsync("standby", standbyRoot, standbyBookmark, epoch, peerPid, originalUntil);
if (args is ["pg-core-preview", var probePhase and ("tenant-probe" or "format-probe"), var probeReference])
    return await PostgresPublicPreview.RunAsync(probePhase, probeReference);
if (args is ["pg-core-preview", var replayPhase and ("restart" or "terminal" or "install-probe"), var actorReference, var journalBookmark])
    return await PostgresPublicPreview.RunAsync(replayPhase, actorReference, journalBookmark);
if (args is ["pg-flowchart-preview", "birth"]) return await PostgresPublicPreview.RunAsync("birth", flowchart: true);
if (args is ["pg-flowchart-preview", "restart", var flowchartActor, var flowchartBookmark])
    return await PostgresPublicPreview.RunAsync("restart", flowchartActor, flowchartBookmark, flowchart: true);
if (args is ["pg-root-inbox", var inboxWriter and ("owned" or "ef"), var inboxFence and ("in-batch" or "checked"),
    var inboxProfile and ("ordinary" or "operators")])
    return await PostgresRootInboxChecks.RunAsync(inboxWriter == "owned", inboxFence == "in-batch", inboxProfile == "operators");
if (args is ["pg-held-operator", var heldOperatorWriter and ("owned" or "ef"), var heldOperatorFence and ("in-batch" or "checked")])
    return await PostgresHeldOperatorChecks.RunAsync(heldOperatorWriter == "owned", heldOperatorFence == "in-batch");
if (args is ["pg-root-park-arrivals", var arrivalWriter and ("owned" or "ef"), var arrivalFence and ("checked" or "in-batch")])
    return await PostgresRootParkOpeningChecks.RunAsync(arrivalWriter == "owned", arrivalFence == "in-batch", inspection: true, arrivals: true);
if (args is ["pg-root-park-recovery", var recoverWriter and ("owned" or "ef"), var recoverFence and ("checked" or "in-batch")])
    return await PostgresRootParkOpeningChecks.RunAsync(recoverWriter == "owned", recoverFence == "in-batch", recovery: true);
if (args is ["pg-root-park-opening", var parkWriter and ("owned" or "ef"), var parkFence and ("checked" or "in-batch")])
    return await PostgresRootParkOpeningChecks.RunAsync(parkWriter == "owned", parkFence == "in-batch");
if (args is ["pg-root-park-inspection", var parkReadWriter and ("owned" or "ef"), var parkReadFence and ("checked" or "in-batch")])
    return await PostgresRootParkOpeningChecks.RunAsync(parkReadWriter == "owned", parkReadFence == "in-batch", inspection: true);
if (args is ["pg-root-composition", var compositionWriter and ("owned" or "ef"), var compositionFence and ("checked" or "in-batch")])
    return await PostgresRootCompositionChecks.RunAsync(compositionWriter == "owned", compositionFence == "in-batch");
if (args is ["pg-root-composed-install", var installWriter and ("owned" or "ef"), var installFence and ("checked" or "in-batch")])
    return await PostgresRootComposedInstallChecks.RunAsync(installWriter == "owned", installFence == "in-batch");
if (args is ["pg-root-parked-operators", var parkedWriter and ("owned" or "ef"), var parkedFence and ("checked" or "in-batch")])
    return await PostgresRootParkedOperatorChecks.RunAsync(parkedWriter == "owned", parkedFence == "in-batch");
if (args is ["pg-root-state-family", var familyWriter and ("owned" or "ef"), var familyFence and ("checked" or "in-batch")])
    return await PostgresRootStateFamilyChecks.RunAsync(familyWriter == "owned", familyFence == "in-batch");
if (args is ["pg-root-retention", var policyWriter and ("owned" or "ef"), var policyFence and ("in-batch" or "checked")])
    return await PostgresRootRetentionChecks.RunAsync(policyWriter == "owned", policyFence == "in-batch");
if (args is ["pg-root-plan-selection", var planWriter and ("owned" or "ef"), var planFence and ("in-batch" or "checked")])
    return await PostgresRootPlanSelectionChecks.RunAsync(planWriter == "owned", planFence == "in-batch");
if (args is ["pg-root-then-delete", var thenWriter and ("owned" or "ef"), var thenFence and ("in-batch" or "checked")])
    return await PostgresRootThenDeleteChecks.RunAsync(thenWriter == "owned", thenFence == "in-batch");
if (args is ["pg-root-retirement", var retirementWriter and ("owned" or "ef"), var retirementFence and ("in-batch" or "checked")])
    return await PostgresRootRetirementChecks.RunAsync(retirementWriter == "owned", retirementFence == "in-batch");
if (args is ["pg-root-terminate", var terminateWriter and ("owned" or "ef"), var fenceMode and ("in-batch" or "checked")])
    return await PostgresRootTerminateChecks.RunAsync(terminateWriter == "owned", fenceMode == "in-batch");
if (args is ["pg-root-terminate", var recoveryWriter and ("owned" or "ef"), var recoveryFence and ("in-batch" or "checked"),
    var terminationProfile and ("fallback" or "stable-body" or "stable-body-fallback" or "terminal-selection" or "terminal-selection-fallback")])
    return await PostgresRootTerminateChecks.RunAsync(recoveryWriter == "owned", recoveryFence == "in-batch",
        fallback: terminationProfile is "fallback" or "stable-body-fallback" or "terminal-selection-fallback",
        stableBody: terminationProfile != "fallback", terminalSelection: terminationProfile.StartsWith("terminal-", StringComparison.Ordinal));
if (args is ["pg-root-worker", "budget-birth", var budgetMode] && budgetMode is Modes.Balanced or Modes.Durable)
    return await PostgresRootWorkerChecks.RunAsync("budget-birth", null, budgetMode);
if (args is ["pg-root-worker", "budget-restart", var budgetReference])
    return await PostgresRootWorkerChecks.RunAsync("budget-restart", budgetReference);
if (args is ["pg-root-worker", "birth"]) return await PostgresRootWorkerChecks.RunAsync("birth", null);
if (args is ["pg-root-worker", var phase and ("restart" or "bounds"), var reference]) return await PostgresRootWorkerChecks.RunAsync(phase, reference);
if (args is ["deadlines"]) return await Deadlines.RunAsync();
if (args is ["smoke"]) return await Smoke.RunAsync();
if (args is ["variants", var variantMode]) return await VariantChecks.RunAsync(variantMode);
if (args is ["replay-variants"]) return await ReplayVariantChecks.RunAsync();
if (args is ["join-variants", var joinMode]) return await JoinVariantChecks.RunAsync(joinMode);
if (args is ["join-replay-variants"]) return await JoinReplayVariantChecks.RunAsync();
if (args is [var birthMode and ("pg-flowchart-preview" or "pg-durable-flowchart-preview"), "birth", var birthRoute and ("manual" or "automatic")])
    return await PostgresFlowchartPreview.RunAsync(birthRoute, durable: birthMode == "pg-durable-flowchart-preview");
if (args is ["pg-durable-flowchart-preview", "wait", var waitRoute and ("manual" or "automatic"), var seededRef, var seededEpoch] &&
    long.TryParse(seededEpoch, out var priorFlowEpoch) && priorFlowEpoch > 0)
    return await PostgresFlowchartPreview.RunAsync(waitRoute, seededRef, previousEpoch: priorFlowEpoch, durable: true, reachWait: true);
if (args is ["pg-flowchart-preview", "restart", var resumedRoute and ("manual" or "automatic"), var flowRef, var flowBookmark, var flowEpoch] &&
    long.TryParse(flowEpoch, out var previousEpoch) && previousEpoch > 0)
    return await PostgresFlowchartPreview.RunAsync(resumedRoute, flowRef, flowBookmark, previousEpoch);
if (args is ["pg-durable-flowchart-preview", "restart", var durableRoute and ("manual" or "automatic"), var durableRef, var durableBookmark, var durableEpoch, var revision] &&
    long.TryParse(durableEpoch, out var heldEpoch) && heldEpoch > 0 && long.TryParse(revision, out var waitRevision) && waitRevision > 0)
    return await PostgresFlowchartPreview.RunAsync(durableRoute, durableRef, durableBookmark, heldEpoch, durable: true, waitingRevision: waitRevision);
if (args is ["pg-durable-flowchart-preview", "restart", ..])
{
    Console.Error.WriteLine("Durable restart requires a route, actor, bookmark, positive previous epoch and positive waiting revision.");
    return 2;
}
if (args is [var timerPreview and ("pg-delay-preview" or "pg-durable-delay-preview" or "pg-delay-recall-preview"), var timerWriter and ("owned" or "ef"), var timerFence and ("checked" or "in-batch"), .. var timerArgs])
{
    var persistence = timerPreview == "pg-durable-delay-preview" ? Modes.Durable : Modes.Balanced;
    var recall = timerPreview == "pg-delay-recall-preview";
    if (timerArgs is ["birth"]) return await PostgresDelayPreview.RunAsync(timerWriter == "owned", timerFence == "in-batch", persistence, null, recall);
    if (timerArgs is ["restart", var timerRef, var timerEpoch, var timerDue, var timerHandle, var timerCursor] &&
        long.TryParse(timerEpoch, out var oldTimerEpoch) && oldTimerEpoch > 0 && long.TryParse(timerDue, out var savedDue))
        return await PostgresDelayPreview.RunAsync(timerWriter == "owned", timerFence == "in-batch", persistence, new(timerRef, oldTimerEpoch, savedDue, timerHandle, timerCursor), recall);
    Console.Error.WriteLine("Use birth or restart <actor> <positive previous epoch> <due> <handle> <cursor>.");
    return 2;
}
if (args is [var selfMode and ("pg-root-self" or "pg-root-timer"), var selfWriter and ("ef" or "owned"), var selfFence and ("checked" or "in-batch")])
    return await PostgresRootSelfChecks.RunAsync(selfWriter == "owned", selfFence == "in-batch", selfMode == "pg-root-timer");
if (args is ["pg-root-self-set", var setWriter and ("ef" or "owned"), var setFence and ("checked" or "in-batch")])
    return await PostgresSelfSetChecks.RunAsync(setWriter == "owned", setFence == "in-batch");
if (args is ["pg-flowchart-preview", "merge-early"])
{
    try { return await PostgresFlowchartPreview.RunAsync("automatic", mergeEarly: true); }
    catch (InvalidOperationException error) { Console.Error.WriteLine(error.Message); return 1; }
}
if (args is ["park"]) return await ParkChecks.RunAsync();
if (args is ["expr", var text, .. var vars])
{
    var scope = new Probe(vars.Select(v => v.Split('=')).ToDictionary(p => p[0], p => JsonNode.Parse(p[1])));
    Console.WriteLine(Kernel.Native.Expression.Evaluate(text, scope)?.ToJsonString() ?? "null");
    return 0;
}
if (args is ["cost", "sqlite"])
{
    var file = Path.Combine(Path.GetTempPath(), $"kernel-native-{Guid.NewGuid():N}.db");
    var failures = 0;
    foreach (var inBatch in new[] { false, true }) failures += await Cost.RunAsync(false, $"Data Source={file}", inBatch);
    File.Delete(file);
    return failures;
}
if (args is ["cost", "postgres", var connection])
{
    var failures = 0;
    foreach (var inBatch in new[] { false, true }) failures += await Cost.RunAsync(true, connection, inBatch);
    return failures;
}
if (args is ["memory", ..]) return await Memory.RunAsync(args.Length > 1 ? int.Parse(args[1]) : 2000);
Console.Error.WriteLine("smoke | variants <mode> | replay-variants | join-variants <mode> | join-replay-variants | cost sqlite | cost postgres <connection> | pg-write-budget <owned|ef> <checked|in-batch> [--require-budget] | memory [runs] | pg-runtime-cluster [partitions] [wake]");
return 2;

internal static class Cost
{
    public static async Task<int> RunAsync(bool postgres, string connection, bool fenceInBatch)
    {
        var counter = new Counting();
        var builder = new DbContextOptionsBuilder<KernelDb>();
        if (postgres) builder.UseNpgsql(connection);
        else builder.UseSqlite(connection);
        var options = builder.AddInterceptors(counter, counter.Transactions).Options;
        KernelDb Create() => new(options);
        await using (var db = Create()) await db.Database.EnsureDeletedAsync();
        var ef = new EfStore(Create, postgres, fenceInBatch);
        await ef.CreateAsync(2);
        var store = new Sizing(ef);
        var registry = Reference.Registry();
        var network = new Network();
        // A cost run counts the work, not the lease's upkeep: with an hour's lease no pass renews it inside a row, however slow the engine.
        var owner = new Owner("A", 0, store, registry, network, lease: TimeSpan.FromHours(1));
        var other = new Owner("B", 1, store, registry, network, lease: TimeSpan.FromHours(1));
        await owner.StartAsync();
        await other.StartAsync();
        var failures = 0;
        var n = 0;
        var keep = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3_600_000;

        Console.WriteLine($"\n{(postgres ? "PostgreSQL" : "SQLite")}, fence {(fenceInBatch ? "in the SaveChanges batch" : "as its own checked statement")}");
        Console.WriteLine("| Run | Mode | Commands | Commits | Round trips | State bytes per commit (max) | Check |");
        Console.WriteLine("|---|---|---:|---:|---:|---:|---|");
        async Task Row(string name, string mode, Func<Task<bool>> body)
        {
            // Renew the leases outside the cost measurement.
            var lease = TimeSpan.FromHours(1);
            await new NodeHeartbeat(owner.Worker.Node, ef, [owner.Worker], lease, lease).BeatAsync();
            await new NodeHeartbeat(other.Worker.Node, ef, [other.Worker], lease, lease).BeatAsync();
            counter.Reset();
            store.Reset();
            var trace = Environment.GetEnvironmentVariable("NATIVE_TRACE") is { Length: > 0 } t && $"{name}, {mode}".StartsWith(t, StringComparison.Ordinal);
            counter.Trace = trace ? [] : null;
            var ok = await body();
            if (counter.Trace is { } lines) Console.WriteLine("    " + string.Join("\n    ", lines));
            // PostgreSQL: Npgsql sends BEGIN with the first command, so a round trip is a command or a COMMIT/ROLLBACK.
            // SQLite: no wire; BEGIN, each command and COMMIT are each one call into the engine.
            var trips = counter.Commands + counter.Transactions.Ended + (postgres ? 0 : counter.Transactions.Begun);
            var bytes = store.Rows == 0 ? "-" : $"{store.StateBytes / store.Rows} ({store.MaxStateBytes})";
            Console.WriteLine($"| {name} | {mode} | {counter.Commands} | {store.Commits} | {trips} | {bytes} | {(ok ? "ok" : "FAILED")} |");
            if (!ok) failures++;
        }

        InstanceRef Next(string name) => InstanceRef.In(0, $"{name}-{++n}");
        bool Finished(Owner o, InstanceRef id) => o.Status(id.Id) == InstanceStatus.Finished;

        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
            foreach (var lines in new[] { 5, 10, 20, 40 })
                await Row($"writeline-sequence, {lines}", mode, async () =>
                {
                    var id = Next("wl");
                    owner.Worker.Post(new Message($"{id}#start", id, Kinds.Start, Start: NativeModel.Spec($"writeline-sequence-{lines}", mode)), fresh: true);
                    await owner.PumpAsync();
                    return mode == Modes.Ephemeral ? owner.Status(id.Id) is null : Finished(owner, id);
                });

        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
        {
            // Today's resume is a direct dispatch to the execution; the nearest here is an outside message to the instance.
            await Row("fork-join-resume, start and a direct resume", mode, async () =>
            {
                var id = Next("fj");
                owner.Worker.Post(new Message($"{id}#start", id, Kinds.Start, Start: NativeModel.Spec("fork-join-resume", mode, Wf.Object(("event", $"ev-{id}")))), fresh: true);
                await owner.PumpAsync();
                await store.AddAsync(new Message($"{id}#resume", id, NativeKinds.Resume, Json.Write(new ResumeStimulus($"ev-{id}")), KeepUntil: keep));
                await owner.PumpAsync(poll: true);
                return Finished(owner, id);
            });
            await Row("fork-join-resume, start and a published event", mode, async () =>
            {
                var id = Next("fj");
                owner.Worker.Post(new Message($"{id}#start", id, Kinds.Start, Start: NativeModel.Spec("fork-join-resume", mode, Wf.Object(("event", $"ev-{id}")))), fresh: true);
                await owner.PumpAsync();
                await Events.PublishAsync(store, new EventRow($"event:ev-{id}", $"e-{id}", null, keep));
                await owner.PumpAsync(poll: true);
                return Finished(owner, id);
            });
            await Row("fork-join, three event branches, three direct resumes", mode, async () =>
            {
                var id = Next("fj3");
                owner.Worker.Post(new Message($"{id}#start", id, Kinds.Start, Start: NativeModel.Spec("fork-join-3-events", mode, Wf.Object(("event", $"ev-{id}")))), fresh: true);
                await owner.PumpAsync();
                for (var i = 1; i <= 3; i++)
                    await store.AddAsync(new Message($"{id}#resume{i}", id, NativeKinds.Resume, Json.Write(new ResumeStimulus($"ev-{id}-{i}")), KeepUntil: keep));
                await owner.PumpAsync(poll: true);
                return Finished(owner, id);
            });
        }

        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
            await Row("flowchart, three event branches and an All join, three direct resumes", mode, async () =>
            {
                var id = Next("fc3");
                owner.Worker.Post(new Message($"{id}#start", id, Kinds.Start, Start: NativeModel.Spec("flowchart-3-events", mode, Wf.Object(("event", $"ev-{id}")))), fresh: true);
                await owner.PumpAsync();
                for (var i = 1; i <= 3; i++)
                    await store.AddAsync(new Message($"{id}#resume{i}", id, NativeKinds.Resume, Json.Write(new ResumeStimulus($"ev-{id}-{i}")), KeepUntil: keep));
                await owner.PumpAsync(poll: true);
                return Finished(owner, id);
            });

        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
            await Row("http-reference (case 1)", mode, async () =>
            {
                var response = await owner.Http.SendAsync(new HttpRequest("POST", Reference.HttpPath, Wf.Object(("firstName", "Ada"), ("lastName", "Lovelace"))), owner.Worker, mode, () => owner.PumpAsync());
                return response.StatusCode == 200 && response.Body?.GetValue<string>() == "Ada Lovelace";
            });

        await Row("respond, then continue (case 2)", Modes.Balanced, async () =>
        {
            string? id = null;
            network.OnAnswer = reply => id = reply.Key[..reply.Key.IndexOf('#')];
            var response = await owner.Http.SendAsync(new HttpRequest("POST", "orders/respond-continue", Wf.Object(("id", 7))), owner.Worker, Modes.Balanced, () => owner.PumpAsync(until: () => id is not null));
            await owner.PumpAsync(until: () => owner.Status(id!) == InstanceStatus.Finished);
            network.OnAnswer = null;
            return response.Body?.GetValue<string>() == "accepted 7" && owner.State(id!)!.Variables["output"]?.GetValue<string>() == "shipped 7";
        });
        foreach (var (path, what) in new[] { ("quotes", "a background job"), ("slow", "a 20 ms Delay") })
            foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced })
                await Row($"{what} before the response (case 3)", mode, async () =>
                {
                    var released = false;
                    network.OnAnswer = _ => released = true;
                    var response = await owner.Http.SendAsync(new HttpRequest("POST", path), owner.Worker, mode, () => owner.PumpAsync(until: () => released));
                    network.OnAnswer = null;
                    return response.StatusCode == 200;
                });
        await Row("resume at an HttpEndpoint, one node: both requests (case 4)", Modes.Balanced, async () =>
        {
            var order = 1000 + ++n;
            var first = await owner.Http.SendAsync(new HttpRequest("POST", "orders", Wf.Object(("id", order))), owner.Worker, Modes.Balanced, () => owner.PumpAsync());
            var second = await owner.Http.SendAsync(new HttpRequest("POST", $"orders/{order}/approve", Wf.Object(("by", "sam"))), owner.Worker, Modes.Balanced, () => owner.PumpAsync(poll: true));
            return first.StatusCode == 200 && second.Body?.GetValue<string>() == $"approved {order} by sam";
        });
        await Row("resume at an HttpEndpoint, second request on another node (case 4)", Modes.Balanced, async () =>
        {
            var order = 1000 + ++n;
            var first = await owner.Http.SendAsync(new HttpRequest("POST", "orders", Wf.Object(("id", order))), owner.Worker, Modes.Balanced, () => owner.PumpAsync());
            var second = await other.Http.SendAsync(new HttpRequest("POST", $"orders/{order}/approve", Wf.Object(("by", "kim"))), other.Worker, Modes.Balanced, async () =>
            {
                await owner.PumpAsync(poll: true);
                await other.PumpAsync();
            });
            return first.StatusCode == 200 && second.Body?.GetValue<string>() == $"approved {order} by kim";
        });

        await using var check = Create();
        var left = await check.Messages.CountAsync(m => !m.Done);
        Console.WriteLine($"check: {await check.Instances.CountAsync()} instances, {left} messages left, {await check.Subscriptions.CountAsync()} subscriptions left");
        return failures + (left == 0 ? 0 : 1);
    }
}

/// <summary>The model's own work, in memory: no database, one worker, the in-memory store.</summary>
internal static class Memory
{
    public static async Task<int> RunAsync(int runs)
    {
        var registry = Reference.Registry();
        Console.WriteLine($"| Run | Mode | Allocated per run | Time per run | Commits | State bytes per commit (max) |");
        Console.WriteLine("|---|---|---:|---:|---:|---:|");
        async Task Measure(string name, string mode, Func<Owner, Sizing, int, Task> body)
        {
            var time = 1_000_000L;
            var sizing = new Sizing(new InMemoryStore(1, () => time));
            var owner = new Owner("n1", 0, sizing, registry, new Network(), () => time);
            await owner.StartAsync();
            for (var i = 0; i < 200; i++) await body(owner, sizing, -i - 1);     // warm up
            sizing.Reset();
            GC.Collect();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var clock = Stopwatch.StartNew();
            for (var i = 0; i < runs; i++) await body(owner, sizing, i);
            clock.Stop();
            var allocated = (GC.GetAllocatedBytesForCurrentThread() - before) / runs;
            var bytes = sizing.Rows == 0 ? "-" : $"{sizing.StateBytes / sizing.Rows} ({sizing.MaxStateBytes})";
            Console.WriteLine($"| {name} | {mode} | {allocated / 1024.0:F1} KiB | {clock.Elapsed.TotalMicroseconds / runs:F0} µs | {(double)sizing.Commits / runs:F1} | {bytes} |");
        }

        Message Start(string workflow, string mode, int i) => new($"{workflow}-{mode}-{i}#start", InstanceRef.In(0, $"{workflow}-{mode}-{i}"), Kinds.Start, Start: NativeModel.Spec(workflow, mode, Wf.Object(("event", "ev"))));
        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
        {
            await Measure("toy model: ten no-ops (the kernel alone)", mode, async (o, _, i) =>
            {
                var toy = mode switch { Modes.Ephemeral => Kernel.Sim.Sequence.Ephemeral, Modes.Durable => Kernel.Sim.Sequence.Durable, _ => Kernel.Sim.Sequence.Balanced };
                o.Worker.Post(new Message($"toy-{mode}-{i}", InstanceRef.In(0, $"toy-{mode}-{i}"), Kinds.Start, Start: Kernel.Sim.Sequence.Spec(10, toy)), fresh: true);
                await o.PumpAsync();
            });
            foreach (var lines in new[] { 10, 40 })
                await Measure($"writeline-sequence, {lines}", mode, async (o, _, i) =>
                {
                    o.Worker.Post(Start($"writeline-sequence-{lines}", mode, i), fresh: true);
                    await o.PumpAsync();
                });
            await Measure("fork-join-resume, start and direct resume", mode, async (o, _, i) =>
            {
                var start = Start("fork-join-resume", mode, i);
                o.Worker.Post(start, fresh: true);
                await o.PumpAsync();
                o.Worker.Post(new Message($"{start.Target}#resume", start.Target, NativeKinds.Resume, Json.Write(new ResumeStimulus("ev"))));
                await o.PumpAsync();
            });
            await Measure("http-reference", mode, async (o, _, _) =>
            {
                var response = await o.Http.SendAsync(new HttpRequest("POST", Reference.HttpPath, Wf.Object(("firstName", "Ada"), ("lastName", "Lovelace"))), o.Worker, mode, () => o.PumpAsync());
                if (response.StatusCode != 200) throw new InvalidOperationException("no answer");
            });
        }
        return 0;
    }
}

internal sealed class Probe(Dictionary<string, JsonNode?> variables) : IScope
{
    public JsonNode? Variable(string name) => variables.GetValueOrDefault(name);
    public JsonNode? Result(string node) => null;
}
