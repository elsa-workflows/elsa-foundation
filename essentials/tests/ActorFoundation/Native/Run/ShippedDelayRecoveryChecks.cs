using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using Elsa.ActorFoundation.TestSupport;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;
using PublicActor = Kernel.WorkflowActor.WorkflowActor;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;

namespace Kernel.Native.Run;

internal static class ShippedDelayRecoveryChecks
{
    private const string NativeDefinition = "authored-native-delay";
    private const string FlowDefinition = "authored-flowchart-delay";
    private const string TerminalDefinition = "authored-far-future-delay";
    private const string Fault = "park-delay-probe";
    private const string Immediate = "pending-metadata";
    private const string Oversized = "record-large-journal-entry";
    private const int NativeDelay = 10_000;
    private const int FlowDelay = 4_000;
    private const int FarFutureDelay = 120_000;

    internal static async Task<int> RunAsync(bool postgres, bool inBatch, bool sequencedJournal = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = timeout.Token;
        var directory = postgres ? null : Directory.CreateTempSubdirectory("shipped-delay");
        try
        {
            var path = Path.Combine(directory?.FullName ?? "", "delays.db");
            var options = new DbContextOptionsBuilder<KernelDb>();
            if (postgres)
                options.UseNpgsql(Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ??
                    throw new InvalidOperationException("Use an empty dedicated PostgreSQL database."));
            else options.UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ConnectionString);
            var definitions = new SqliteAdmissionDefinitions();
            StoreFormat? qualification = sequencedJournal ? StoreFormat.RootSequencedJournalQualification : null;
            KernelDb Open() => OpenWith(options.Options);
            KernelDb OpenWith(DbContextOptions<KernelDb> configuration) => postgres
                ? sequencedJournal ? KernelDb.ForPostgresRootSequencedJournalQualification(configuration)
                    : PostgresPreviewHosting.CreateShippedContext(configuration)
                : KernelDb.ForSqliteRootRuntime(configuration, definitions, qualification);

            var cold = new EfStore(Open, postgres);
            if (postgres)
            {
                await using var database = Open();
                Check(await PostgresFixtureDatabase.IsEmptyAsync(database, token), "the fixture uses an empty PostgreSQL database");
                await cold.CreateAsync(1);
                await cold.InstallPreviewRootSchemaAsync(token);
            }
            else await SqliteEmbeddedHosting.InstallShippedFileAsync(path, token, qualification);

            var scope = EmbeddedTenant.Bind("shipped-delay");
            bool Authorize(Contract.BoundScope caller, Contract.ActorRef _, string kind) =>
                ReferenceEquals(caller, scope) && kind is Contract.Kinds.Recover or Contract.Kinds.Terminate or Contract.Kinds.Delete;
            Contract.IIngress ingress;
            Contract.IActorReads reads;
            if (postgres)
            {
                var foundation = await PostgresPreviewHosting.OpenFoundationWithOperatorsAsync(Open, PublicActor.ActorType,
                    Authorize, cancellationToken: token);
                (ingress, reads) = (foundation, foundation);
            }
            else
            {
                ingress = await SqliteEmbeddedHosting.OpenFoundationWithOperatorsAsync(path, PublicActor.ActorType,
                    Authorize, SqliteEmbeddedHosting.CreateRequests(path, "delay-caller", PublicActor.ActorType,
                        qualificationFormat: qualification), token);
                reads = SqliteEmbeddedHosting.CreateFoundationReads(path, qualificationFormat: qualification);
            }

            var capabilities = new KernelHostCapabilities(Schedules: false,
                Timers: postgres ? PostgresPreviewHosting.TakesTimers(Open) : SqliteEmbeddedHosting.TakesTimers,
                Children: false,
                SelfSends: postgres ? PostgresPreviewHosting.TakesSelfSends(Open) : SqliteEmbeddedHosting.TakesSelfSends,
                HttpWaits: false);
            var serving = new KernelServing(capabilities: capabilities);
            var native = Wf.Workflow(NativeDefinition, Wf.Sequence("root",
                Wf.Delay("wait", NativeDelay), Wf.Set("after-delay", "finished", true)));
            var flowchart = NativeExecutableMapper.Map(ExecutableOf(FlowDefinition, FlowDefinition,
                Node("flow", Types.Flowchart, structure: Structure("elsa.flowchart.structure", new
                {
                    startNodeId = "wait",
                    connections = new[] { new { source = new { nodeId = "wait", port = "Done" }, target = new { nodeId = "after-delay" } } }
                }),
                    slots: [Slot("Flowchart.Activities",
                        Node("wait", Types.Delay, [Literal("Duration", TimeSpan.FromMilliseconds(FlowDelay).ToString("c", CultureInfo.InvariantCulture), "TimeSpan")]),
                        Set("after-delay", "v-finished", Literal("value", true, "Boolean")))]),
                Variable("v-finished", "finished", false, "Boolean")), capabilities);
            await serving.ServeHostWorkflowAsync(native, token);
            await serving.ServeHostWorkflowAsync(flowchart, token);
            await serving.ServeHostWorkflowAsync(Wf.Workflow(TerminalDefinition, Wf.Sequence("root",
                Wf.Delay("wait", FarFutureDelay), Wf.Set("never-reached", "finished", true))), token);
            Check(capabilities is { Timers: true, SelfSends: true, Schedules: false, Children: false, HttpWaits: false },
                "the shipped capabilities serve Native and mapped Flowchart Delay with no schedule-actor dependency");

            var runtime = new WorkflowRuntime(ingress, reads);
            var runs = new KernelRuns(scope, _ => ValueTask.FromResult(reads));
            var timers = new ConcurrentQueue<(Contract.ActorRef Actor, string Id, long Now)>();
            var failures = new ConcurrentQueue<string>();
            var loop = new LoopOptions
            {
                BalanceEvery = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(30),
                Window = TimeSpan.FromMilliseconds(400)
            };
            await using var db = Open();
            var marker = await db.StoreVersions.AsNoTracking().SingleAsync(row => row.Id == StoreVersionEntity.MarkerId, token);
            Check(marker.Format == (qualification ?? StoreFormat.RootShipped), "the journey verifies its exact store marker");
            Check(await runs.SupportsSequencedJournalAsync(token) == sequencedJournal,
                "the optional reader is available only on the sequenced journal profile");
            var sequencedPrefixes = new Dictionary<Contract.ActorRef, Contract.ActorSequencedJournalPage>();
            Contract.ActorRef nativeRoot, flowRoot;
            Contract.Page<Contract.JournalEntry> nativePrefix, flowPrefix;
            DelayWait nativeWait, flowWait;
            Contract.ActorView nativePark, flowPark;
            long originalEpoch;
            await using (var first = await StartOwnerAsync("delay-original"))
            {
                nativeRoot = await AdmitAsync(NativeDefinition);
                flowRoot = await AdmitAsync(FlowDefinition);
                nativeWait = await WaitingAsync(nativeRoot, NativeDelay);
                flowWait = await WaitingAsync(flowRoot, FlowDelay);
                nativePrefix = await reads.TailAsync(scope, nativeRoot, cancellationToken: token);
                flowPrefix = await reads.TailAsync(scope, flowRoot, cancellationToken: token);
                Check(nativePrefix is { JournalHeadRevision: > 0, JournalCatchUpToken: not null } &&
                    flowPrefix is { JournalHeadRevision: > 0, JournalCatchUpToken: not null },
                    "both waiting workflows expose the committed journal head and a catch-up bookmark");
                foreach (var prefix in new[] { nativePrefix, flowPrefix })
                {
                    Check(prefix.Items.Count > 0 && prefix.Items.GroupBy(entry => entry.Revision).All(commit =>
                        commit.Select(entry => entry.CommitOrdinal).SequenceEqual(Enumerable.Range(0, commit.Count()).Select(ordinal => (int?)ordinal))),
                        "the journal exposes stable per-commit entry ordinals for the authored workflow");
                }
                if (sequencedJournal)
                {
                    await CheckSequencedStartAsync(nativeRoot, nativePrefix);
                    await CheckSequencedStartAsync(flowRoot, flowPrefix);
                }
                var idle = await reads.TailAsync(scope, nativeRoot, nativePrefix.JournalCatchUpToken, token);
                Check(idle is { Items.Count: 0, Next: null, JournalCatchUpToken: not null } &&
                    idle.JournalHeadRevision == nativePrefix.JournalHeadRevision,
                    "an idle journal read retains the actor's committed head without replaying entries");
                originalEpoch = await EpochAsync();
                nativePark = await ParkAsync(nativeRoot);
                flowPark = await ParkAsync(flowRoot);
                Check(timers.IsEmpty && nativePark is { Header.Status: Contract.ActorStatus.Faulted, Selection: not null } &&
                    flowPark is { Header.Status: Contract.ActorStatus.Faulted, Selection: not null },
                    "both authored instances Park before the Delay fires");
                await first.StopAsync(token);
            }

            // Represent the missing-footprint recovery case while no owner has cached the timer.
            Check(await db.Messages.Where(row => row.Target == nativeWait.Actor && row.InboxScope == EfStore.RootSelfScope &&
                row.Id == nativeWait.PhysicalKey && !row.Done).ExecuteDeleteAsync(token) == 1,
                "the replacement owner will find the Native actor's saved timer missing");

            await using var successor = await StartOwnerAsync("delay-successor");
            Check(await RecoverAsync("recover-native", nativePark) is { Outcome: Contract.AdmissionOutcome.Accepted },
                "selected Recover is admitted for the parked Native run");
            var rearmed = await UntilAsync(() => FindTimerAsync(nativeWait.Actor, nativeWait.PhysicalKey));
            Check(rearmed.Due == nativeWait.Due && rearmed.Sequence > nativeWait.Sequence && !rearmed.Done &&
                rearmed.KeptBy is null && await EpochAsync() > originalEpoch,
                "the replacement owner re-arms the exact Native timer at its original due time with a fresh sequence");

            var kept = await UntilAsync(async () => await FindTimerAsync(flowWait.Actor, flowWait.PhysicalKey)
                is { Done: true, KeptBy: (short)ParkKeepReason.Arrived } row ? row : null);
            Check(kept.Due == flowWait.Due && kept.Sequence == flowWait.Sequence,
                "the mapped Flowchart timer matures into Park with its original due time and sequence");
            var selectedFlow = await reads.GetAsync(scope, flowRoot, token);
            Check(selectedFlow is { Park: not null, Selection: not null } &&
                await RecoverAsync("recover-flowchart", selectedFlow) is { Outcome: Contract.AdmissionOutcome.Accepted },
                "selected Recover is admitted for the parked Flowchart run");

            var nativeFinished = await UntilAsync(() => FinishedAsync(nativeRoot));
            var flowFinished = await UntilAsync(() => FinishedAsync(flowRoot));
            foreach (var (actor, wait, result, prefix) in new[]
                { (nativeRoot, nativeWait, nativeFinished, nativePrefix), (flowRoot, flowWait, flowFinished, flowPrefix) })
            {
                Check(Json.Read<NativeState>(result.State!).Variables["finished"]!.GetValue<bool>() &&
                    timers.Where(timer => timer.Actor == actor).ToArray() is [var timer] &&
                    timer.Id == wait.Key && timer.Now >= wait.Due &&
                    await FindTimerAsync(wait.Actor, wait.PhysicalKey) is not { Done: false },
                    "the recovered Delay runs once, at or after its original due time, and completes its authored continuation");
                var visible = await reads.GetAsync(scope, actor, token);
                Check(visible is { Park: null, Header.Status: Contract.ActorStatus.Finished },
                    "the public actor read shows a finished run with its Park cleared");
                var caughtUp = await reads.TailAsync(scope, actor, prefix.JournalCatchUpToken, token);
                Check(caughtUp.JournalHeadRevision == result.Revision && caughtUp.Items.Count > 0 &&
                    caughtUp.Items.All(entry => entry.Revision > prefix.JournalHeadRevision) &&
                    caughtUp.Items.Any(entry => entry.Revision == result.Revision),
                    "the earlier bookmark reads only newer committed entries across owner replacement");
                if (sequencedJournal)
                {
                    var initial = sequencedPrefixes[actor];
                    var pinned = await runs.ReadSequencedJournalAsync(actor, 1, initial.UpperSequence, 500, token);
                    Check(pinned is not null && pinned.UpperSequence == initial.UpperSequence &&
                        pinned.LastRecordedAt == initial.LastRecordedAt &&
                        pinned.Items.Select(item => item.Entry).SequenceEqual(prefix.Items.Skip(1).Select(item => item.Entry)),
                        "appending Park and completion keeps the earlier window pinned through the within-commit split");
                    var fresh = await runs.ReadSequencedJournalAsync(actor, initial.UpperSequence, null, 500, token);
                    Check(fresh is not null && fresh.ScannedThroughSequence == fresh.UpperSequence &&
                        fresh.Items.Select(item => (item.Revision, item.Entry)).SequenceEqual(caughtUp.Items.Select(item => (item.Revision, (string?)item.Entry))) &&
                        fresh.Items.Select(item => item.Sequence).SequenceEqual(Enumerable.Range(1, fresh.Items.Count)
                            .Select(index => initial.UpperSequence + index)),
                        "an earlier upper bound catches up with dense run-local sequences after ownership replacement");
                }
            }
            await CheckTerminationAsync();
            Contract.ActorRef? oversizedActor = null;
            if (sequencedJournal)
            {
                await CheckIndexedWindowAsync();
                oversizedActor = await CheckOversizedAsync();
            }
            var retaining = await AdmitAsync(TerminalDefinition, "retain-immediate-at-end");
            var retainingTimer = await WaitingAsync(retaining, FarFutureDelay);
            await successor.StopAsync(token);
            if (sequencedJournal)
            {
                // Model an existing retained journal prefix when the new projector starts tracking commits.
                Check(await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM "FoundationSequencedJournal" WHERE "Actor" = {retainingTimer.Actor}
                    """, token) == 1, "the old-prefix witness retains a real earlier commit without its segment");
                var old = await runs.ReadSequencedJournalAsync(retaining, 0, null, 1, token);
                Check(old is { Items.Count: 0, UpperSequence: 0, RetainedFromSequence: 0, CapturedFromStart: false, LastRecordedAt: null } &&
                    (await reads.TailAsync(scope, retaining, cancellationToken: token)).Items.Count > 0,
                    "an unsequenced old prefix remains in the raw journal but cannot masquerade as standard coverage");
            }
            await CheckRetainsImmediateAsync(retaining, retainingTimer);
            if (sequencedJournal)
            {
                var newer = await runs.ReadSequencedJournalAsync(retaining, 0, null, 1, token);
                Check(newer is { Items: [{ Sequence: 1, Revision: > 1 }], CapturedFromStart: false, LastRecordedAt: > 0 },
                    "a newly sequenced suffix starts a stable range with partial coverage");
                var refused = false;
                try
                {
                    if (postgres)
                    {
                        var ordinary = new EfStore(() => PostgresPreviewHosting.CreateShippedContext(options.Options), postgres: true);
                        await ordinary.InstallPreviewRootSchemaAsync(token);
                    }
                    else await SqliteEmbeddedHosting.InstallShippedAsync(path, token);
                }
                catch (StoreFormatException) { refused = true; }
                Check(refused, "the ordinary shipped install refuses the private sequenced journal store");
                if (postgres)
                {
                    await CheckSourceNamespaceAsync(retaining);
                    await CheckProjectorRefusalAsync(oversizedActor!.Value);
                }
            }
            Check(failures.IsEmpty, "the retained provider owners stop cleanly");
            return 0;

            async Task<RuntimeNode> StartOwnerAsync(string node)
            {
                var model = new NativeExecutionModel(serving.Registry, new() { SelfSends = serving.Capabilities.SelfSends });
                var actor = new ParkProbe(new PublicActor([model]), timers);
                void Faulted(Exception error) => failures.Enqueue(error.Message);
                return postgres
                    ? await PostgresPreviewHosting.StartAsync(Open, node, actor, failures.Enqueue, loop, token, Faulted,
                        (IReplyTransport)PostgresPreviewHosting.CreateRequests(Open, node, PublicActor.ActorType), fenceInBatch: inBatch)
                    : await SqliteEmbeddedHosting.StartFoundationAsync(path, node, actor, failures.Enqueue, loop, inBatch,
                        token, Faulted, SqliteEmbeddedHosting.CreateRequests(path, node, PublicActor.ActorType,
                            qualificationFormat: qualification));
            }

            async Task CheckSequencedStartAsync(Contract.ActorRef actor, Contract.Page<Contract.JournalEntry> raw)
            {
                var page = await runs.ReadSequencedJournalAsync(actor, 0, null, 1, token);
                Check(page is { CapturedFromStart: true, RetainedFromSequence: 1, ScannedThroughSequence: 1,
                        LastRecordedAt: > 0, Items: [{ Sequence: 1, CommitOrdinal: 0, RecordedAt: > 0 }] } &&
                    page.UpperSequence == raw.Items.Count && page.Items[0].Entry == raw.Items[0].Entry,
                    "each authored run starts its own stable sequence and a one-entry page keeps the same-snapshot head");
                sequencedPrefixes.Add(actor, page!);
                Check(await ((Contract.IActorSequencedJournalReads)reads).ReadSequencedJournalAsync(
                    ContractScopes.Bind("other-tenant"), actor, 0, null, 1, token) is null,
                    "the sequenced journal is scoped to the exact tenant");
            }

            async Task<Contract.ActorRef> CheckOversizedAsync()
            {
                var actor = await AdmitAsync(TerminalDefinition, "oversized-journal");
                await WaitingAsync(actor, FarFutureDelay);
                var first = (await runs.ReadSequencedJournalAsync(actor, 0, null, 1, token))!;
                Check(await ingress.SendAsync(scope, new Contract.Target.Ref(actor), "large-entry", Oversized, cancellationToken: token)
                    is { Outcome: Contract.AdmissionOutcome.Accepted }, "the fixture admits an oversized journal body");
                var after = await UntilAsync(async () =>
                    await runs.ReadSequencedJournalAsync(actor, first.UpperSequence, null, 1, token) is { Items.Count: 1 } page ? page : null);
                Check(after.Items is [{ Entry: null, CommitOrdinal: 0 }] && after.UpperSequence == first.UpperSequence + 1 &&
                    after.ScannedThroughSequence == after.UpperSequence,
                    "the bounded source advances through the oversized commit without loading its body");
                return actor;
            }

            async Task CheckIndexedWindowAsync()
            {
                var plan = new SequencedJournalPlan(postgres);
                var observed = new DbContextOptionsBuilder<KernelDb>(options.Options).AddInterceptors(plan);
                var probe = new EfStore(() => OpenWith(observed.Options), postgres);
                await probe.ReadRootSequencedJournalAsync(scope.Tenant, nativeRoot.Id, 1, sequencedPrefixes[nativeRoot].UpperSequence, 1, token);
                foreach (var line in plan.Lines) Console.WriteLine(line);
                Check(plan.Lines.Any(line => postgres
                    ? line.Contains("Index Cond:", StringComparison.Ordinal) &&
                        line.Contains("\"LastSequence\" >", StringComparison.Ordinal) && line.Contains("\"LastSequence\" <=", StringComparison.Ordinal)
                    : line.Contains("Partition=? AND Actor=? AND LastSequence>? AND LastSequence<?", StringComparison.Ordinal)),
                    "the installed plan bounds both ends of the actor's sequenced segment index");
            }

            async Task CheckSourceNamespaceAsync(Contract.ActorRef actor)
            {
                var settings = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString())
                {
                    SearchPath = "sequenced_shadow,public"
                };
                var mixed = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(settings.ConnectionString);
                var reader = new EfStore(() => OpenWith(mixed.Options), postgres: true);
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE SCHEMA sequenced_shadow;
                    CREATE TABLE sequenced_shadow."FoundationRootBirths" (LIKE public."FoundationRootBirths" INCLUDING ALL);
                    """, token);
                try
                {
                    var birthRefused = await RefusesMixedReadAsync();
                    await db.Database.ExecuteSqlRawAsync("""
                        DROP TABLE sequenced_shadow."FoundationRootBirths";
                        CREATE TABLE sequenced_shadow."Instances" (LIKE public."Instances" INCLUDING ALL);
                        """, token);
                    Check(birthRefused && await RefusesMixedReadAsync(),
                        "the sequenced read refuses birth or instance relations outside the physical format marker schema");
                }
                finally
                {
                    await db.Database.ExecuteSqlRawAsync("DROP SCHEMA sequenced_shadow CASCADE", token);
                }

                async Task<bool> RefusesMixedReadAsync()
                {
                    try { await reader.ReadRootSequencedJournalAsync(scope.Tenant, actor.Id, 0, null, 1, token); }
                    catch (StoreFormatException) { return true; }
                    return false;
                }
            }

            async Task CheckProjectorRefusalAsync(Contract.ActorRef actor)
            {
                var prior = await reads.TailAsync(scope, actor, cancellationToken: token);
                var writer = new EfStore(Open, postgres: true, fenceInBatch: inBatch) { CommitSql = CommitSql.Owned };
                await writer.EnableRootSelfSendsAsync(token);
                await writer.EnableRootJobsAsync(token);
                var model = new ParkProbe(new PublicActor([new NativeExecutionModel(serving.Registry)]), timers);
                var roots = writer.BindWorkerNode(model.Withholds);
                var adapter = new ActorAdapter(writer.WorkerHeaders(roots), model, writer.RootSelfIssuer,
                    rootJobHandle: writer.RootJobHandle, rootTimedJobClock: writer.ReadRootTimedJobPublicationTimeAsync, rootHeldReplies: true);
                var observed = System.Reflection.DispatchProxy.Create<IKernelStore, PostgresRootParkOpeningChecks.ObservedCommit>();
                var proxy = (PostgresRootParkOpeningChecks.ObservedCommit)observed;
                proxy.Store = writer;
                var refusedUnsequencedWrite = false;
                proxy.Write = async (batch, writeToken) =>
                {
                    try { return await writer.CommitAsync(batch, writeToken); }
                    catch (StoreException error) when (error.InnerException is PostgresException
                        { ConstraintName: "kernel_require_root_sequenced_journal" })
                    {
                        refusedUnsequencedWrite = true;
                        return new Committed(false, 0, []);
                    }
                };
                await using var live = new Worker("before-projector-disabled", 0, observed, adapter, new WorkerOptions { PassTurnBudget = 1 });
                Check(await new DurableHost([live]).ActivateAsync(0, token) == live,
                    "the worker owns the partition while the sequenced projector is still enabled");
                await db.Database.ExecuteSqlRawAsync("""
                    ALTER TABLE "Journal" DISABLE TRIGGER kernel_project_root_sequenced_journal
                    """, token);
                try
                {
                    var readRefused = false;
                    try { await runs.ReadSequencedJournalAsync(actor, 0, null, 1, token); }
                    catch (StoreFormatException) { readRefused = true; }
                    var ownerRefused = false;
                    try { await using var blocked = await StartOwnerAsync("disabled-projector"); }
                    catch (StoreFormatException) { ownerRefused = true; }
                    Check(readRefused && ownerRefused,
                        "a disabled journal projector fails both an exact history read and a replacement owner start");
                    Check(await ingress.SendAsync(scope, new Contract.Target.Ref(actor), "journal-without-projector", Oversized,
                        cancellationToken: token) is { Outcome: Contract.AdmissionOutcome.Accepted },
                        "the fixture admits an input while the existing owner is running and the projector is disabled");
                    while (!refusedUnsequencedWrite)
                    {
                        await live.WokenAsync(null, token);
                        await live.RunAsync(token);
                        if (!refusedUnsequencedWrite) await Task.Delay(25, token);
                    }
                    var rejectedTail = await reads.TailAsync(scope, actor, cancellationToken: token);
                    Check(rejectedTail.JournalHeadRevision == prior.JournalHeadRevision && rejectedTail.Items.SequenceEqual(prior.Items),
                        "the already-running owner's fenced commit refuses the unsequenced journal row and preserves its checkpoint");
                }
                finally
                {
                    await db.Database.ExecuteSqlRawAsync("""
                        ALTER TABLE "Journal" ENABLE TRIGGER kernel_project_root_sequenced_journal
                        """, token);
                }
                Check(await runs.ReadSequencedJournalAsync(actor, 0, null, 1, token) is { UpperSequence: > 0 },
                    "reenabling the journal projector restores the same retained page");
            }

            async Task<Contract.ActorRef> AdmitAsync(string definition, string? operation = null)
            {
                var receipt = await runtime.StartAsync(scope, operation ?? definition, NativeExecutionModel.Start(definition), cancellationToken: token);
                Check(receipt is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "the public ingress admits " + definition);
                return receipt.Actor!.Value;
            }

            async Task CheckTerminationAsync()
            {
                var plain = await AdmitAsync(TerminalDefinition, "separate-termination");
                var compound = await AdmitAsync(TerminalDefinition, "compound-termination");
                var unrelated = await AdmitAsync(TerminalDefinition, "unrelated-running-delay");
                var plainTimer = await WaitingAsync(plain, FarFutureDelay);
                var compoundTimer = await WaitingAsync(compound, FarFutureDelay);
                var unrelatedTimer = await WaitingAsync(unrelated, FarFutureDelay);

                var runningPlain = (await reads.GetAsync(scope, plain, token))!;
                Check(await runtime.TerminateAsync(scope, "terminate-delay", runningPlain, cancellationToken: token)
                    is { Outcome: Contract.AdmissionOutcome.Accepted }, "the shipped provider admits Terminate of a running Delay");
                var ended = await UntilAsync(async () => await cold.ReadFirstRootAsync(scope.Tenant, plain.Id, token)
                    is { Status: InstanceStatus.Cancelled, TerminalRevision: not null } row ? row : null);
                Check(ended.AsOf < plainTimer.Due && await FindTimerAsync(plainTimer.Actor, plainTimer.PhysicalKey) is null,
                    "Terminate clears the authentic far-future private timer in the terminal checkpoint");

                var terminal = (await reads.GetAsync(scope, plain, token))!.Selection;
                var endedHistory = await reads.TailAsync(scope, plain, cancellationToken: token);
                var endBookmark = endedHistory.JournalCatchUpToken;
                var emptyEnd = await reads.TailAsync(scope, plain, endBookmark, token);
                Check(endBookmark is not null && emptyEnd is { Items.Count: 0, JournalCancellation: { } cancellation } &&
                    cancellation.Revision == ended.TerminalRevision && emptyEnd.JournalHeadRevision == ended.Revision,
                    "an empty terminal tail retains its cancellation and committed revision");
                Check(terminal is not null && await ingress.SendAsync(scope, new Contract.Target.Ref(plain), "delete-delay",
                    Contract.Kinds.Delete, Json.Write(new Contract.OperatorCommand(terminal)), cancellationToken: token)
                    is { Outcome: Contract.AdmissionOutcome.Accepted }, "the shipped provider admits Delete of the exact terminal selection");
                await RetiredAsync(plain, plainTimer);
                var retiredTail = await reads.TailAsync(scope, plain, endBookmark, token);
                Check(retiredTail is { Items.Count: 0, JournalHeadRevision: null, JournalCatchUpToken: null },
                    "a bookmark cannot make a retired run's journal appear live");
                if (sequencedJournal)
                    Check(await runs.ReadSequencedJournalAsync(plain, 1, 1, 1, token) is null &&
                        !await db.Database.SqlQuery<bool>($"""
                            SELECT EXISTS(SELECT 1 FROM "FoundationSequencedJournal" WHERE "Actor" = {ended.Actor.Id}) AS "Value"
                            """).SingleAsync(token), "retirement removes the actor's sequenced ranges atomically");

                var runningCompound = (await reads.GetAsync(scope, compound, token))!;
                Check(await runtime.TerminateThenDeleteAsync(scope, "terminate-and-delete-delay", runningCompound, token)
                    is { Outcome: Contract.AdmissionOutcome.Accepted }, "the shipped provider admits qualified compound Terminate and Delete");
                var retiredAt = await RetiredAsync(compound, compoundTimer);
                Check(retiredAt < compoundTimer.Due &&
                    await FindTimerAsync(unrelatedTimer.Actor, unrelatedTimer.PhysicalKey) is { Done: false } alive &&
                    alive.Due == unrelatedTimer.Due && alive.Sequence == unrelatedTimer.Sequence &&
                    timers.All(timer => timer.Actor != plain && timer.Actor != compound && timer.Actor != unrelated),
                    "both deletion paths leave the other actor's far-future timer intact and fire none of them");
            }

            async Task CheckRetainsImmediateAsync(Contract.ActorRef reference, DelayWait timer)
            {
                var image = (await cold.ReadFirstRootAsync(scope.Tenant, reference.Id, token))!;
                var selected = (await reads.GetAsync(scope, reference, token))!;
                Check(await runtime.TerminateAsync(scope, "terminate-with-immediate", selected, cancellationToken: token)
                    is { Outcome: Contract.AdmissionOutcome.Accepted },
                    "the provider queues the selected Terminate before an immediate self-send");

                var keeper = new EfStore(Open, postgres, fenceInBatch: inBatch) { CommitSql = CommitSql.Owned };
                await keeper.EnableRootSelfSendsAsync(token);
                await keeper.EnableRootJobsAsync(token);
                var issuer = keeper.RootSelfIssuer!;
                // Place a legitimately issued immediate send behind the command while no owner is polling.
                var key = FoundationSelfSend.Issue(issuer.Handle, CreationProtocol.Origin(timer.Actor, 1), image.Revision, 1, null);
                var wire = keeper.EncodeRootSelf(timer.Actor, image.Revision, 1,
                    FoundationSelfSend.Create(timer.Actor, 1, key, Immediate, null));
                if (postgres)
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO "Messages" ("Id", "InboxScope", "Partition", "Target", "Kind", "Sender", "Payload", "Due",
                            "Done", "Seen", "Attempts", "Trial", "JobGeneration")
                        VALUES ({wire.Id}, {wire.InboxScope}, 0, {timer.Actor}, {wire.Kind}, {wire.Sender}, {wire.Payload}, 0,
                            false, 0, 0, 0, 0)
                        """, token);
                else
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO "Messages" ("Id", "InboxScope", "Partition", "Target", "Kind", "Sender", "Payload", "Due", "Seq",
                            "Done", "Seen", "Attempts", "Trial", "JobGeneration")
                        VALUES ({wire.Id}, {wire.InboxScope}, 0, {timer.Actor}, {wire.Kind}, {wire.Sender}, {wire.Payload}, 0,
                            (SELECT coalesce(max("Seq"), 0) + 1 FROM "Messages"), 0, 0, 0, 0, 0)
                        """, token);

                var actor = new PublicActor([new NativeExecutionModel(serving.Registry)]);
                var roots = postgres ? keeper.BindWorkerNode(actor.Withholds) : keeper.BindSqliteWorkerNode(actor.Withholds);
                var adapter = new ActorAdapter(new EfStore.RootHeaders(roots), actor, issuer, rootJobHandle: keeper.RootJobHandle,
                    rootTimedJobClock: keeper.ReadRootTimedJobPublicationTimeAsync, rootHeldReplies: true);
                var observed = System.Reflection.DispatchProxy.Create<IKernelStore, PostgresRootParkOpeningChecks.ObservedCommit>();
                var proxy = (PostgresRootParkOpeningChecks.ObservedCommit)observed;
                proxy.Store = keeper;
                var sampled = false;
                proxy.Write = async (batch, writeToken) =>
                {
                    var committed = await keeper.CommitAsync(batch, writeToken);
                    if (committed.Landed && batch.Turns.Any(turn => turn.Instance == timer.Actor && turn.Row?.Status == InstanceStatus.Cancelled))
                    {
                        sampled = await FindTimerAsync(timer.Actor, wire.Id) is { Done: false, Due: 0 } &&
                            await FindTimerAsync(timer.Actor, timer.PhysicalKey) is null;
                    }
                    return committed;
                };
                await using var owner = new Worker("retain-immediate", 0, observed, adapter, new WorkerOptions { PassTurnBudget = 1 });
                Check(await new DurableHost([owner]).ActivateAsync(0, token) == owner, "the witness owner takes the partition");
                await UntilAsync(async () =>
                {
                    await owner.WokenAsync(null, token);
                    await owner.RunAsync(token);
                    return await cold.ReadFirstRootAsync(scope.Tenant, reference.Id, token)
                        is { Status: InstanceStatus.Cancelled } ended ? ended : null;
                });
                Check(sampled, "the exact terminal commit keeps the immediate self-send and clears the private timer");
            }

            async Task<long> RetiredAsync(Contract.ActorRef reference, DelayWait timer)
            {
                await UntilAsync(async () => await db.Instances.AsNoTracking().SingleOrDefaultAsync(i => i.Id == timer.Actor, token)
                    is { Retired: true, Status: InstanceStatus.Cancelled } retired ? retired : null);
                Check(await reads.GetAsync(scope, reference, token) is null && await FindTimerAsync(timer.Actor, timer.PhysicalKey) is null &&
                    (await cold.ReadJournalOfAsync(0, timer.Actor, 0, token)).Any(entry => entry.Revision == -1 && entry.Entries == """["delete"]"""),
                    "the terminal root retires with its deletion marker and no private timer");
                return await db.Database.SqlQuery<long>($"""
                    SELECT "FoundationTerminalAt" AS "Value" FROM "Instances" WHERE "Id" = {timer.Actor}
                    """).SingleAsync(token);
            }

            async Task<DelayWait> WaitingAsync(Contract.ActorRef root, int duration) =>
                await UntilAsync(async () =>
                {
                    if (await cold.ReadFirstRootAsync(scope.Tenant, root.Id, token) is not { Status: InstanceStatus.Running } image ||
                        Json.Read<NativeState>(image.State!).Waits.Values.SingleOrDefault(wait => wait.Kind == "timer") is not { } wait)
                        return null;
                    var physical = CreationProtocol.Origin(image.Actor.Id, image.Actor.Creation) + "#" + wait.Key;
                    var timer = await FindTimerAsync(image.Actor.Id, physical);
                    Check(timer is { Done: false } && timer.Due == wait.Due && wait.Due - image.AsOf > 0 && wait.Due - image.AsOf <= duration,
                        "the authored Delay checkpoint and its private timer agree on the absolute deadline");
                    return new DelayWait(image.Actor.Id, physical, wait.Key, timer!.Due, timer.Sequence);
                });

            async Task<Contract.ActorView> ParkAsync(Contract.ActorRef actor)
            {
                Check(await ingress.SendAsync(scope, new Contract.Target.Ref(actor), "park", Fault, cancellationToken: token)
                    is { Outcome: Contract.AdmissionOutcome.Accepted }, "the fixture admits the faulting input");
                return await UntilAsync(async () => await reads.GetAsync(scope, actor, token) is { Park: not null } parked ? parked : null);
            }

            ValueTask<Contract.Admission> RecoverAsync(string operation, Contract.ActorView parked) =>
                ingress.SendAsync(scope, new Contract.Target.Ref(parked.Header.Ref), operation, Contract.Kinds.Recover,
                    Json.Write(new Contract.OperatorCommand(parked.Selection)), cancellationToken: token);

            async Task<EfStore.RootImage?> FinishedAsync(Contract.ActorRef actor) =>
                await cold.ReadFirstRootAsync(scope.Tenant, actor.Id, token) is { Status: InstanceStatus.Finished } row ? row : null;

            Task<long> EpochAsync() => db.Partitions.AsNoTracking().Select(row => row.Epoch).SingleAsync(token);

            Task<PhysicalTimer?> FindTimerAsync(string actor, string id) => db.Database.SqlQuery<PhysicalTimer>($"""
                SELECT "Due", "Seq" AS "Sequence", "Done", "FoundationKeptBy" AS "KeptBy"
                FROM "Messages" WHERE "Target" = {actor} AND "InboxScope" = {EfStore.RootSelfScope} AND "Id" = {id}
                """).SingleOrDefaultAsync(token);

            async Task<T> UntilAsync<T>(Func<Task<T?>> read) where T : class
            {
                while (true)
                {
                    if (await read() is { } value) return value;
                    if (failures.TryPeek(out var error)) throw new InvalidOperationException(error);
                    await Task.Delay(20, token);
                }
            }
        }
        finally { directory?.Delete(recursive: true); }
    }

    private sealed record DelayWait(string Actor, string PhysicalKey, string Key, long Due, long Sequence);
    private sealed record PhysicalTimer(long Due, long Sequence, bool Done, short? KeptBy);

    private sealed class ParkProbe(PublicActor actor, ConcurrentQueue<(Contract.ActorRef Actor, string Id, long Now)> timers) : Contract.IActorType
    {
        public string Type => actor.Type;
        public bool Withholds(Contract.ActorHeader header) => actor.Withholds(header);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => actor.ExecuteJobAsync(job, token);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => actor.RestoreWaits(header, state);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => actor.TerminalGrowth(header, state);
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => actor.ProtectsDeletion(header, state, now);

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken token)
        {
            if (turn.Inbox.Any(input => input.Kind == Fault)) throw new InvalidOperationException(Fault);
            if (turn.Inbox.Any(input => input.Kind == Oversized))
            {
                turn.Record(new string('x', 300 * 1024));
                return ValueTask.CompletedTask;
            }
            foreach (var timer in turn.Inbox.Where(input => input.Kind == NativeKinds.Timer && input.Sender == turn.Header.Ref))
                timers.Enqueue((turn.Header.Ref, timer.Id, turn.Now));
            return actor.TurnAsync(turn, token);
        }
    }

    private static void Check(bool pass, string description)
    {
        if (!pass) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }

    private sealed class SequencedJournalPlan(bool postgres) : DbCommandInterceptor
    {
        internal List<string> Lines { get; } = [];

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.Contains("/* root-sequenced-journal */", StringComparison.Ordinal)) return result;
            await using var explain = command.Connection!.CreateCommand();
            explain.Transaction = command.Transaction;
            explain.CommandText = (postgres ? "EXPLAIN " : "EXPLAIN QUERY PLAN ") + command.CommandText;
            foreach (DbParameter parameter in command.Parameters)
            {
                var copy = explain.CreateParameter();
                copy.ParameterName = parameter.ParameterName;
                copy.DbType = parameter.DbType;
                copy.Value = parameter.Value;
                explain.Parameters.Add(copy);
            }
            await using var rows = await explain.ExecuteReaderAsync(cancellationToken);
            while (await rows.ReadAsync(cancellationToken)) Lines.Add(rows.GetString(postgres ? 0 : 3));
            return result;
        }
    }

}
