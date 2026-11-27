using System.Text.Json.Nodes;
using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Hosting;
using Kernel.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using WorkflowActorType = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

internal static class InlineRootPromotionChecks
{
    internal static async Task<int> RunAsync(bool postgres, bool fenceInBatch, bool laterRevision = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var scope = ContractScopes.Bind("inline-proof");
        var directory = postgres ? null : Directory.CreateTempSubdirectory("inline-promotion-");
        try
        {
            var options = new DbContextOptionsBuilder<KernelDb>();
            if (postgres)
                options.UseNpgsql(Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ??
                    throw new InvalidOperationException("Supply a fresh PostgreSQL fixture."));
            else
                options.UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(directory!.FullName, "inline-promotion.db"), ForeignKeys = true, Pooling = false
                }.ToString());
            var sqliteDefinitions = new SqliteAdmissionDefinitions();
            KernelDb Open() => postgres
                ? laterRevision ? KernelDb.ForPostgresRootInlinePromotionQualification(options.Options)
                    : KernelDb.ForPostgresRootPreparedStartsQualification(options.Options)
                : KernelDb.ForSqliteRootRuntime(options.Options, sqliteDefinitions, laterRevision
                    ? StoreFormat.RootInlinePromotionQualification : StoreFormat.RootPreparedStartsQualification);

            var store = new EfStore(Open, postgres, fenceInBatch) { CommitSql = CommitSql.Owned };
            await using (var db = Open())
            {
                if (postgres)
                    Check(await PostgresFixtureDatabase.IsEmptyAsync(db, token), "the promotion fixture uses an empty PostgreSQL database");
                else await EfStore.InstallSqliteShippedAsync(db, 1, token);
            }
            if (postgres)
            {
                await store.CreateAsync(1);
                await store.InstallPreviewRootSchemaAsync(token);
            }
            var lease = TimeSpan.FromSeconds(10);
            var fence = await TakeAsync("inline-first");
            byte[] key;
            await using (var db = Open())
                key = postgres ? await PostgresRootAdmission.ReadRootIssuerKeyAsync(db, token)
                    : await SqliteRootAdmission.ReadRootIssuerKeyAsync(db, token);

            if (!laterRevision)
            {
                var (later, laterTurn) = Sealed("late-first-write", 3);
                try
                {
                    await PromoteAsync(later, laterTurn, fence, (_, _) =>
                        throw new InvalidOperationException("An unsupported revision entered protection."));
                    throw new InvalidOperationException("An unsupported initial revision was accepted.");
                }
                catch (NotSupportedException)
                {
                    Check(await CountAsync(later, "FoundationPreparedRootStarts", "Actor", token) == 0,
                        "a revision that the installed history projector cannot seed is refused before preparation");
                }
            }

            var (creator, turn) = Sealed("selected-operation", 1);
            var selected = creator.FoundationRootBinding!.Value.Reference;
            var result = await PromoteAsync(creator, turn, fence, async (offer, ct) =>
            {
                Check(offer.Tenant == scope.Tenant && offer.Actor.Id == selected &&
                    await CountAsync(creator, "FoundationPreparedRootStarts", "Actor", ct) == 1 &&
                    await CountAsync(creator, "FoundationRootBirths", "Actor", ct) == 0 &&
                    await store.ReadFirstRootAsync(scope.Tenant, selected, ct) is null,
                    "the callback sees committed preparation and no admitted run");
                return true;
            });
            Check(result is { Landed: true } &&
                await store.ReadFirstRootAsync(scope.Tenant, selected, token) is { Revision: 1, Status: InstanceStatus.Running } &&
                await store.ReadFirstRootAsync("other-tenant", selected, token) is null &&
                await CountAsync(creator, "Subscriptions", "Instance", token) == 1 &&
                await CountAsync(creator, "Journal", "Instance", token) == 1 &&
                await CountAsync(creator, "Messages", "Target", token) == 0 &&
                await CountAsync(creator, "FoundationPreparedRootStarts", "Actor", token) == 0 &&
                await store.ReadRootSequencedJournalAsync(scope.Tenant, selected, 0, null, 16, token) is
                    { CapturedFromStart: true, RetainedFromSequence: 1, Items.Count: 2 },
                "the exact actor, revision, wait and journal commit without a replay creator");

            fence = fence with { Position = fence.Position + 1 };
            var (rejected, rejectedTurn) = Sealed("rejected-material", 1);
            Check(await PromoteAsync(rejected, rejectedTurn, fence, (_, _) => ValueTask.FromResult(false)) is { Landed: false } &&
                await CountAsync(rejected, "FoundationPreparedRootStarts", "Actor", token) == 0 &&
                await CountAsync(rejected, "FoundationRootBirths", "Actor", token) == 0 &&
                await CountAsync(rejected, "Instances", "Id", token) == 0,
                "a definitive protector refusal disarms the exact stage without admitting the actor");

            var (duplicate, duplicateTurn) = Sealed("selected-operation", 1);
            Check(await PromoteAsync(duplicate, duplicateTurn, fence, (_, _) => ValueTask.FromResult(true)) is { Landed: false } &&
                await CountAsync(duplicate, "FoundationRootBirths", "Actor", token) == 0 &&
                await CountAsync(duplicate, "Instances", "Id", token) == 0 &&
                await CountAsync(duplicate, "Subscriptions", "Instance", token) == 0 &&
                await CountAsync(duplicate, "Journal", "Instance", token) == 0,
                "a live competing receipt rolls back every promoted row");

            var (stale, staleTurn) = Sealed("stale-owner", 1);
            var staleFence = fence;
            Check(await PromoteAsync(stale, staleTurn, staleFence, async (_, ct) =>
            {
                await store.ReleaseAsync(staleFence.Partition, staleFence.Epoch, ct);
                fence = await TakeAsync("inline-successor");
                return true;
            }) is { Landed: false } &&
                await CountAsync(stale, "FoundationRootBirths", "Actor", token) == 0 &&
                await CountAsync(stale, "Instances", "Id", token) == 0 &&
                await CountAsync(stale, "Subscriptions", "Instance", token) == 0 &&
                await CountAsync(stale, "Journal", "Instance", token) == 0,
                "owner movement during executable protection leaves no durable continuation");
            await store.ReleaseAsync(fence.Partition, fence.Epoch, token);
            if (laterRevision)
            {
                fence = await TakeAsync("inline-stage-change");
                var (changed, changedTurn) = Sealed("changed-stage-revision", 2);
                Check(await PromoteAsync(changed, changedTurn, fence, async (offer, ct) =>
                {
                    await using var db = Open();
                    return await db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE "FoundationPreparedRootStarts" SET "FirstRevision" = 3 WHERE "Reference" = {offer.Actor.Id}
                        """, ct) == 1;
                }) is { Landed: false } &&
                    await CountAsync(changed, "Instances", "Id", token) == 0 &&
                    await CountAsync(changed, "Journal", "Instance", token) == 0,
                    "a changed physical first revision cannot consume a previously authenticated preparation");
                await store.ReleaseAsync(fence.Partition, fence.Epoch, token);
                await QualifyLaterNativeAsync();
            }
            return 0;

            async Task<PartitionFence> TakeAsync(string node) =>
                (await store.TakeAsync(0, node, lease, token))?.For(0) ??
                throw new InvalidOperationException("The fixture could not take the owner fence.");

            ValueTask<Committed> PromoteAsync(Message message, TurnCommit sealedTurn, PartitionFence owned, ProtectRootStartAsync protect) =>
                store.PromoteInlineRootAsync(scope, message, sealedTurn, owned, lease, TimeSpan.FromHours(1), protect, token);

            async Task QualifyLaterNativeAsync()
            {
                var workflow = new Workflow("inline-native", Wf.Sequence("steps", Wf.WriteLine("before", "volatile work"),
                    Wf.Delay("one", 100), Wf.Delay("two", 100), Wf.Event("wait", "resume"), Wf.WriteLine("after", "cold work")))
                    { ExecutableSelection = "private-125-material" };
                var shortWait = new Workflow("inline-wait", Wf.Event("event", "no-message")) { ExecutableSelection = "private-125-wait" };
                var pure = new Workflow("inline-pure", Wf.WriteLine("pure", "pure work")) { ExecutableSelection = "private-125-pure" };
                var longTimer = new Workflow("inline-long-timer", Wf.Sequence("run", Wf.Delay("long", 2_800),
                    Wf.WriteLine("finish", "durable timer finished"))) { ExecutableSelection = "private-125-timer" };
                var earlyTimers = new Workflow("inline-earlier-timer", Wf.Parallel("both", null,
                    Wf.Delay("first", 100), Wf.Delay("earlier", 1_100))) { ExecutableSelection = "private-125-earlier-timer" };
                var job = new Node("timed", typeof(CallService)) { JobTimeout = TimeSpan.FromSeconds(3) };
                job.Inputs.Add("Service", "inline-service");
                job.Inputs.Add("Milliseconds", 250);
                var timedJob = new Workflow("inline-timed", Wf.Sequence("run", Wf.Delay("short", 50), job,
                    Wf.SetOutput("job-output", "result", Binding.Result("timed")))) { ExecutableSelection = "private-125-timed" };
                var terminal = new Workflow("inline-terminal", Wf.Sequence("run", Wf.Delay("short", 50),
                    Wf.Node<FinishAcrossCutoff>("finish-across-cutoff"),
                    Wf.SetOutput("set-output", "result", "committed terminal"))) { ExecutableSelection = "private-125-terminal" };
                var selectedChild = new Workflow("inline-selected-child", Wf.SetOutput("child", "value", "selected inline child"))
                    { ExecutableSelection = "private-125-child" };
                var parent = new Workflow("inline-parent", Wf.Sequence("run", Wf.Delay("short", 60),
                    Wf.Dispatch("dispatch", selectedChild.Id, wait: true), Wf.SetOutput("parent", "result", Binding.Result("dispatch", "Output.value"))))
                    { ExecutableSelection = "private-125-parent" };
                var parkWorkflow = new Workflow("inline-park", Wf.Sequence("run", Wf.WriteLine("once", "before inline Park"), Wf.Delay("failed-timer", 90),
                    Wf.Node<FailAfterLocalTimer>("trigger-failure"), Wf.WriteLine("after", "after inline Park")))
                    { ExecutableSelection = "private-125-park" };
                var initialParkWorkflow = new Workflow("inline-park-first", Wf.Sequence("run", Wf.Node<FailInitialTurn>("first-failure"),
                    Wf.SetOutput("result", "input", Binding.Var("input", "marker")))) { ExecutableSelection = "private-125-initial-park" };
                var orderedParkWorkflow = new Workflow("inline-park-ordered", Wf.Parallel("fork", null,
                    Wf.Sequence("left", Wf.Delay("left-timer", 90), Wf.Node<FailOrderedTurn>("ordered-failure"), Wf.WriteLine("left-output", "left after recovery")),
                    Wf.Sequence("right", Wf.Delay("right-timer", 90), Wf.WriteLine("right-output", "right after recovery")),
                    Wf.Sequence("later", Wf.Delay("later-timer", 1_200), Wf.WriteLine("later-output", "later after recovery"))))
                    { ExecutableSelection = "private-125-ordered-park" };
                var shortJob = new Node("short-job", typeof(PostgresRootTimedJobSettlement.ControlledJob)) { JobTimeout = TimeSpan.FromMilliseconds(900) };
                var catchTimeout = Wf.Node<PostgresRootTimedJobSettlement.CatchTimeout>("catch-timeout").With(shortJob);
                var volatileJobWorkflow = new Workflow("inline-volatile-job", Wf.Sequence("run", catchTimeout,
                    Wf.SetOutput("job-result", "result", Binding.Result("catch-timeout")))) { ExecutableSelection = "private-125-volatile-job" };
                var mixedJob = new Node("mixed-job", typeof(CallService)) { JobTimeout = TimeSpan.FromMilliseconds(900) };
                mixedJob.Inputs.Add("Milliseconds", 100);
                var mixedMailbox = new Workflow("inline-mixed-mailbox", Wf.Parallel("parallel", null, Wf.Delay("independent-timer", 350), mixedJob))
                    { ExecutableSelection = "private-125-mixed-mailbox" };
                Workflow[] inlineWorkflows = [workflow, shortWait, pure, longTimer, earlyTimers, timedJob, terminal, parent,
                    parkWorkflow, initialParkWorkflow, orderedParkWorkflow, volatileJobWorkflow, mixedMailbox];
                var permittedStarts = inlineWorkflows.ToDictionary(
                    selected => WorkflowActorType.Start(NativeExecutionModel.Start(selected, Modes.Ephemeral)).TypeData,
                    StringComparer.Ordinal);
                var registry = new WorkflowRegistry().Add(selectedChild);
                foreach (var inlineWorkflow in inlineWorkflows) registry.Add(inlineWorkflow);
                var childStart = WorkflowActorType.Start(NativeExecutionModel.Start(selectedChild));
                var model = new NativeExecutionModel(registry, new NativeExecutionOptions { UseStructuralJournal = true });
                var actor = new WorkflowActorType([model]);
                var spec = WorkflowActorType.Start(NativeExecutionModel.Start(workflow, Modes.Ephemeral));
                long unwrittenRevision = 0;
                string? unwrittenActor = null;
                var jobPublications = 0;
                TurnCommit? checkpoint = null;
                Message? firstCreator = null;
                PendingRootStart? offered = null;
                string? acceptedWait = null;
                string? refusedWait = null;
                string? refusedActor = null;
                string? protectionDeadline = null;
                var protectorCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var preparedBatchActors = new HashSet<string>(StringComparer.Ordinal);
                var volatileCohorts = new Dictionary<string, string[]>(StringComparer.Ordinal);
                var firstRevisions = new Dictionary<string, long>(StringComparer.Ordinal);
                var childOffers = new Dictionary<string, (PendingChildBirth Offer, string RootIssuance, string Child, int Attempts)>(StringComparer.Ordinal);
                string? refusedParent = null;
                var rateClock = new RootAdmissionPressureChecks.RateClock();
                var pressure = new RootAdmissionPressure(new(2, 1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)),
                    ordinaryCapacity: 1, operatorCapacity: 1, ordinaryRate: new(1, 1), operatorRate: new(1, 1), rateClock);
                var physical = new EfStore(Open, postgres, fenceInBatch)
                    { CommitSql = CommitSql.Owned, ProtectChildBirths = ProtectChildrenAsync, AdmissionPressure = pressure };
                var checkingPressure = false;
                var protectionCalls = 0;
                var requests = new InlineRootRequests(scope, WorkflowActorType.ActorType, (requested, material) =>
                    permittedStarts.TryGetValue(requested.TypeData, out var selected) &&
                    model.MatchesInlineMaterial(material, selected.Id, selected.Definition, selected.ExecutableSelection),
                    async (offer, ct) =>
                    {
                        if (checkingPressure)
                        {
                            protectionCalls++;
                            return true;
                        }
                        if (offer.Actor.Id == protectionDeadline)
                        {
                            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                            finally { if (ct.IsCancellationRequested) protectorCancelled.TrySetResult(); }
                            return true;
                        }
                        if (offer.Actor.Id == acceptedWait || offer.Actor.Id == refusedWait)
                        {
                            Check(preparedBatchActors.Contains(acceptedWait!) && preparedBatchActors.Contains(refusedWait!),
                                "the owner prepares both inline promotions together before protecting either one");
                            return offer.Actor.Id == acceptedWait;
                        }
                        offered = offer;
                        return firstRevisions.TryGetValue(offer.Actor.Id, out var firstRevision) &&
                            RootPreparedStarts.Verify(key, scope, offer.Actor, offer.Issuance) is { } stage &&
                            stage.FirstRevision == firstRevision && await CreateCanReadPreparationAsync(offer, stage.Actor, ct);
                    });
                await requests.PrepareAsync(physical, token);
                var ownerOptions = new WorkerOptions
                {
                    ShortWait = TimeSpan.FromMilliseconds(1_400),
                    OnKeptInMemory = (instance, revision) => (unwrittenActor, unwrittenRevision) = (instance, revision),
                    AfterJobPublication = () => Interlocked.Increment(ref jobPublications),
                    OnTurn = (instance, messages, _) =>
                    {
                        if (messages.All(message => message.Due is not null)) volatileCohorts[instance.Id] = messages.Select(message => message.InboxKey.Id).ToArray();
                    },
                    OnPreparedCheckpoint = (sealedTurn, _, accepted) =>
                    {
                        if (accepted?.FoundationRootBinding is { } identity)
                        {
                            preparedBatchActors.Add(identity.Reference);
                            if (identity.Reference == refusedWait) refusedActor = identity.Actor;
                        }
                        checkpoint = sealedTurn;
                        firstCreator = accepted;
                    }
                };
                await physical.EnableRootSelfSendsAsync(token);
                await physical.EnableRootJobsAsync(token);
                await physical.EnableRootChildrenAsync(token);
                var firstRoots = postgres ? physical.BindWorkerNode(ActorAdapter.RootWithholding(actor, null))
                    : physical.BindSqliteWorkerNode(ActorAdapter.RootWithholding(actor, null));
                await using var owner = Worker.ForNode("inline-promoting-owner", physical,
                    new ActorAdapter(physical.WorkerHeaders(firstRoots), actor, physical.RootSelfIssuer, physical.RootJobHandle,
                        rootTimedJobClock: physical.ReadRootTimedJobPublicationTimeAsync, rootTerminalOutputLimit: 1_024), ownerOptions);
                requests.Attach(physical, owner, _ => { });
                Check(await StartAsync("unowned-pressure", pure) is { Disposition: InlineStartDisposition.Unavailable, RefusalReason: null },
                    "a start without an owned fence releases its unused rate reservation");
                Check(await owner.GainAsync(0, token), "the real provider owner claims the inline partition");
                var originalTrace = WriteLine.Out;
                using var trace = new StringWriter();
                WriteLine.Out = trace;
                try
                {
                    await QualifyPressureAsync();
                    (checkpoint, firstCreator) = (null, null);
                    trace.GetStringBuilder().Clear();
                    var firstPosition = owner.PositionOf(0);
                    var admitted = physical.QueueInlineRoot(owner, "later-native", spec, model.CaptureInlineMaterial(workflow)) ??
                        throw new InvalidOperationException("Could not admit the local fixture turn.");
                    var reference = admitted.Reference.Id;
                    var receipt = admitted.Outcome;
                    firstRevisions.Add(reference, 3);
                    await WaitUnwrittenAsync(1);
                    Check(!receipt.IsCompleted && checkpoint is null && await store.ReadFirstRootAsync(scope.Tenant, reference, token) is null,
                        "the first short Delay has no physical checkpoint or receipt");
                    await WaitUnwrittenAsync(2);
                    Check(!receipt.IsCompleted && checkpoint is null && await store.ReadFirstRootAsync(scope.Tenant, reference, token) is null,
                        "the second short Delay still has no physical checkpoint or receipt");
                    await WaitForAsync(receipt);
                    Check(await receipt == new InlineRootResult(InlineRootDisposition.Committed, InstanceStatus.Running) &&
                        checkpoint?.Row?.Revision == 3 && checkpoint?.RootCreator is InlineRootCreatorWitness &&
                        firstCreator?.FoundationRootBinding is { } birth && birth.Reference == reference && offered?.Actor.Id == reference,
                        "the normal owned batch promotes logical revision three under the original authenticated creator");
                    var local = firstCreator!;
                    var address = local.Target;
                    var before = await store.ReadRootSequencedJournalAsync(scope.Tenant, reference, 0, null, 64, token);
                    Check(before is { CapturedFromStart: true, RetainedFromSequence: 1 } && owner.PositionOf(0) == firstPosition + 1 &&
                        before.Items.Count > 2 && before.Items.All(entry => entry.Revision == 3) &&
                        await CountAsync(local, "Journal", "Instance", token) == 1 &&
                        await CountAsync(local, "Messages", "Target", token) == 0,
                        "the first physical journal and cursor contain only sealed revision three");
                    var playback = await store.ReadRootPlaybackAsync(scope.Tenant, reference, new(16, null, null, null), token);
                    Check(playback is { Availability: Contract.ActorPlaybackAvailability.Available } &&
                        playback.Items.Count > 0 && playback.Items.All(entry => entry.Revision == 3),
                        "Playback starts at the committed occurrence sequence without pseudo revisions");
                    var listed = await store.ReadRootActorListAsync(scope.Tenant, "", actor.Type, (int)InstanceStatus.Running,
                        [], null, null, 16, token);
                    var history = await store.ReadRootWorkflowHistoryAsync(scope.Tenant, actor.Type, workflow.Definition,
                        (int)InstanceStatus.Running, null, null, null, null, 16, token);
                    Check(listed.Items.Any(item => item.Reference == reference) && history.Items.Any(item => item.Reference == reference),
                        "the normal actor list and workflow history project the exact promoted run");

                    await using (var db = Open())
                    {
                        var changed = false;
                        try
                        {
                            changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                                UPDATE "FoundationRootBirths" SET "FirstRevision" = 2 WHERE "Reference" = {reference}
                                """, token) != 0;
                        }
                        catch (System.Data.Common.DbException error) when (error.Message.Contains("first revision is immutable", StringComparison.Ordinal)) { }
                        Check(!changed && await db.Database.SqlQuery<long>($"""
                            SELECT "FirstRevision" AS "Value" FROM "FoundationRootBirths" WHERE "Reference" = {reference}
                            """).SingleAsync(token) == 3, "the physical birth protects the authenticated first revision from update");
                    }
                    var beforeMixed = owner.PositionOf(0);
                    var refused = Queue("refused-inline-timer", longTimer);
                    var accepted = Queue("accepted-inline-wait", shortWait);
                    var writeFree = Queue("write-free-inline", pure);
                    acceptedWait = accepted.Reference.Id;
                    refusedWait = refused.Reference.Id;
                    await WaitForAsync(Task.WhenAll(refused.Outcome, accepted.Outcome, writeFree.Outcome));
                    Check(await refused.Outcome is { Disposition: InlineRootDisposition.Refused } &&
                        await accepted.Outcome == new InlineRootResult(InlineRootDisposition.Committed, InstanceStatus.Running) &&
                        await physical.ReadFirstRootAsync(scope.Tenant, refusedWait, token) is null && owner.PositionOf(0) == beforeMixed + 1,
                        "the accepted sibling alone advances the position when the first promotion is refused");
                    await using (var db = Open())
                    {
                        var written = await db.Database.SqlQuery<long>($"""
                            SELECT "Position" AS "Value" FROM "Journal" j JOIN "FoundationRootBirths" b ON b."Actor" = j."Instance"
                            WHERE b."Reference" = {acceptedWait}
                            """).SingleAsync(token);
                        var residue = await db.Database.SqlQuery<int>($"""
                            SELECT CAST(count(*) AS integer) AS "Value" FROM (
                                SELECT "Reference" FROM "FoundationPreparedRootStarts"
                                UNION ALL SELECT "Reference" FROM "FoundationRootReceipts"
                                UNION ALL SELECT "Reference" FROM "FoundationRootBirths"
                            ) physical WHERE "Reference" = {refusedWait} OR "Reference" = {writeFree.Reference.Id}
                            """).SingleAsync(token);
                        Check(written == owner.PositionOf(0) && residue == 0 && refusedActor is not null &&
                            !await db.Messages.AnyAsync(message => message.Target == refusedActor, token) &&
                            await writeFree.Outcome == new InlineRootResult(InlineRootDisposition.WriteFree, InstanceStatus.Finished) &&
                            await physical.ReadFirstRootAsync(scope.Tenant, writeFree.Reference.Id, token) is null &&
                            trace.ToString().Split("pure work", StringSplitOptions.None).Length == 2,
                            "the committed journal uses the fenced position and pure completion leaves no durable run or preparation");
                    }
                    await QualifyTerminalAsync();
                    var delayed = QueueExpected("long-timer", longTimer, 1);
                    var runningJob = QueueExpected("timed-job", timedJob, 2);
                    await WaitForAsync(Task.WhenAll(delayed.Outcome, runningJob.Outcome));
                    Check(await delayed.Outcome == new InlineRootResult(InlineRootDisposition.Committed, InstanceStatus.Running) &&
                        await physical.ReadFirstRootAsync(scope.Tenant, delayed.Reference.Id, token) is { Revision: 1 },
                        "a long authored Delay promotes on its first turn");
                    var jobCheckpoint = await physical.ReadFirstRootAsync(scope.Tenant, runningJob.Reference.Id, token) ??
                        throw new InvalidOperationException($"The timed job did not promote: {await runningJob.Outcome}.");
                    await using (var db = Open())
                    {
                        var rows = await db.Messages.AsNoTracking().Where(message => message.Target == jobCheckpoint.Actor.Id)
                            .OrderBy(message => message.Seq).ToArrayAsync(token);
                        Check(await runningJob.Outcome == new InlineRootResult(InlineRootDisposition.Committed, InstanceStatus.Running) &&
                            jobCheckpoint is { Revision: 2, NextJobGeneration: 1, Jobs: not null } &&
                            rows is [{ Kind: Kinds.JobDeadline, InboxScope: EfStore.RootTimedJobScope, JobGeneration: 0, Due: > 0 },
                                { Kind: Kinds.Job, InboxScope: EfStore.RootTimedJobScope, JobGeneration: 0, Due: 0 }],
                            "the first durable timed-job checkpoint commits its exact generation, deadline and ticket without a creator row");
                    }
                    await WaitForFinishAsync(owner, physical, runningJob.Reference.Id);
                    Check(await physical.ReadFirstRootAsync(scope.Tenant, runningJob.Reference.Id, token) is
                        { Revision: 4, NextJobGeneration: 1, Jobs: null, Status: InstanceStatus.Finished } jobEnd &&
                        await physical.ReadRootTerminalOutputAsync(scope.Tenant, runningJob.Reference.Id, jobEnd.TerminalAt!.Value, true, token) is { Values: not null } jobOutput &&
                        System.Text.Encoding.UTF8.GetString(jobOutput.Values).Contains("answer of inline-service", StringComparison.Ordinal),
                        "the normal owner records and delivers the job result and stores the terminal output exactly once");
                    var refusedChild = QueueExpected("refused-child", parent, 2);
                    refusedParent = refusedChild.Reference.Id;
                    await WaitForAsync(refusedChild.Outcome);
                    Check(await refusedChild.Outcome is { Disposition: InlineRootDisposition.Refused } &&
                        await physical.ReadFirstRootAsync(scope.Tenant, refusedParent, token) is null &&
                        !await HasChildBirthAsync(childOffers[refusedParent].Offer.Actor, token),
                        "definitive host refusal prevents the parent checkpoint and child birth together");

                    var parkedRun = await QualifyParkAsync();
                    var orderedPark = await QualifyOrderedParkAsync();
                    var jobWinner = await QualifyVolatileJobAsync(callbackWins: true);
                    var jobDeadline = await QualifyVolatileJobAsync(callbackWins: false);
                    var mixed = QueueExpected("mixed-mailbox-promotes", mixedMailbox, 1);
                    unwrittenRevision = 0;
                    await WaitForAsync(mixed.Outcome);
                    var mixedRoot = await physical.ReadFirstRootAsync(scope.Tenant, mixed.Reference.Id, token);
                    Check(await mixed.Outcome == new InlineRootResult(InlineRootDisposition.Committed, InstanceStatus.Running) &&
                        mixedRoot is { Revision: 1, Jobs: not null } && unwrittenRevision == 0,
                        "an independent short timer and job commit their mixed mailbox before either can create an unsafe volatile Park");
                    await WaitForFinishAsync(owner, physical, mixed.Reference.Id);
                    var initialPark = Queue("park-before-first-checkpoint", initialParkWorkflow, values: new() { ["marker"] = "original initial input" });
                    firstRevisions.Add(initialPark.Reference.Id, 1);
                    await WaitForAsync(initialPark.Outcome);
                    Check(await initialPark.Outcome == new InlineRootResult(InlineRootDisposition.Committed, InstanceStatus.Faulted) &&
                        await physical.ReadFirstRootAsync(scope.Tenant, initialPark.Reference.Id, token) is
                            { Status: InstanceStatus.Faulted, Revision: 1, ParkAt: > 0, State: null, TerminalAt: null } && FailInitialTurn.Entries == 1,
                        "a first-turn fault commits one null-state Park under the protected material pin");
                    var early = await QualifyEarlierTimerAsync();
                    var parentRun = QueueExpected("accepted-child", parent, 2);
                    await WaitForAsync(parentRun.Outcome);
                    var protectedChild = childOffers[parentRun.Reference.Id];
                    Check(await parentRun.Outcome == new InlineRootResult(InlineRootDisposition.Committed, InstanceStatus.Running) &&
                        await physical.ReadFirstRootAsync(scope.Tenant, parentRun.Reference.Id, token) is { Revision: 2, Children: { } children } &&
                        ChildRegistry.Read(children) is [var member] && member.Id == protectedChild.Child && protectedChild.Attempts == 2 &&
                        await HasChildBirthAsync(protectedChild.Offer.Actor, token),
                        "the protected first parent checkpoint and exact child creator land atomically after an identical retry");
                    requests.Close();
                    await owner.ReleaseAsync(token);

                    var cold = new EfStore(Open, postgres, fenceInBatch) { CommitSql = CommitSql.Owned };
                    var coldModel = new NativeExecutionModel(new WorkflowRegistry().Add(workflow).Add(longTimer).Add(earlyTimers)
                        .Add(parent).Add(selectedChild).Add(parkWorkflow).Add(initialParkWorkflow).Add(orderedParkWorkflow).Add(volatileJobWorkflow),
                        new NativeExecutionOptions { UseStructuralJournal = true });
                    var coldActor = new WorkflowActorType([coldModel]);
                    await cold.EnableRootSelfSendsAsync(token);
                    await cold.EnableRootJobsAsync(token);
                    await cold.EnableRootChildrenAsync(token);
                    var roots = postgres ? cold.BindWorkerNode(ActorAdapter.RootWithholding(coldActor, null))
                        : cold.BindSqliteWorkerNode(ActorAdapter.RootWithholding(coldActor, null));
                    var adapter = new ActorAdapter(cold.WorkerHeaders(roots), coldActor, cold.RootSelfIssuer, cold.RootJobHandle,
                        rootTimedJobClock: cold.ReadRootTimedJobPublicationTimeAsync, rootTerminalOutputLimit: 1_024);
                    var observed = 0;
                    var turns = 0;
                    var replayedOrder = new List<string>();
                    await using var coldOwner = Worker.ForNode("inline-cold-owner", cold, adapter, new WorkerOptions
                    {
                        OnObservedDelivery = _ => observed++,
                        OnTurn = (instance, messages, _) =>
                        {
                            turns++;
                            if (instance.Id == orderedPark.Actor) replayedOrder.AddRange(messages.Where(message => message.Due is not null)
                                .Select(message => message.InboxKey.Id));
                        }
                    });
                    Check(await coldOwner.GainAsync(0, token), "a fresh physical owner claims the promoted root's partition");
                    var input = Durable.Json.Write(new ResumeStimulus("resume"));
                    var resumed = postgres
                        ? await new PostgresRootAdmission(Open, TimeSpan.FromHours(1)).AdmitDirectAsync(scope.Tenant, reference,
                            "resume-inline", NativeKinds.Resume, input, cancellationToken: token) == true
                        : (await new SqliteRootAdmission(Open, TimeSpan.FromHours(1), sqliteDefinitions).SendAsync(scope.Tenant,
                            new(reference), "resume-inline", NativeKinds.Resume, input, null, null, token)).Outcome == Contract.AdmissionOutcome.Accepted;
                    Check(resumed, "trusted ordinary ingress admits the cold continuation");
                    await coldOwner.WokenAsync(address, token);
                    for (var attempt = 0; attempt < 20 &&
                         await cold.ReadFirstRootAsync(scope.Tenant, reference, token) is { Status: InstanceStatus.Running }; attempt++)
                    {
                        token.ThrowIfCancellationRequested();
                        await coldOwner.RunAsync(token);
                    }
                    var settled = await cold.ReadFirstRootAsync(scope.Tenant, reference, token);
                    Check(settled is { Status: InstanceStatus.Finished },
                        $"the cold continuation finishes (checkpoint {settled?.Status}, {observed} observed, {turns} turns, {settled?.Fault})");
                    var after = await cold.ReadRootSequencedJournalAsync(scope.Tenant, reference, before!.UpperSequence, null, 64, token);
                    Check(after is { CapturedFromStart: true } && after.Items.Count > 0 && after.Items.All(entry => entry.Revision == 4) &&
                        trace.ToString().Split("volatile work", StringSplitOptions.None).Length == 2 &&
                        trace.ToString().Split("cold work", StringSplitOptions.None).Length == 2,
                        "a restarted owner appends revision four and does not replay the earlier volatile work");
                    await WaitForFinishAsync(coldOwner, cold, early.Id);
                    await WaitForFinishAsync(coldOwner, cold, delayed.Reference.Id);
                    Check(await cold.ReadFirstRootAsync(scope.Tenant, early.Id, token) is { Revision: 3, Status: InstanceStatus.Finished } &&
                        await cold.ReadFirstRootAsync(scope.Tenant, delayed.Reference.Id, token) is { Revision: 2, Status: InstanceStatus.Finished } &&
                        trace.ToString().Split("durable timer finished", StringSplitOptions.None).Length == 2,
                        "the cold owner authenticates both timer origins and settles each without replaying the creator");
                    await WaitForFinishAsync(coldOwner, cold, parentRun.Reference.Id);
                    Check(await cold.ReadFirstRootAsync(scope.Tenant, parentRun.Reference.Id, token) is
                        { Revision: 3, TerminalAt: { } parentEnd, Status: InstanceStatus.Finished, Children: null } &&
                        await cold.ReadRootTerminalOutputAsync(scope.Tenant, parentRun.Reference.Id, parentEnd, true, token) is { Values: not null } childOutput &&
                        System.Text.Encoding.UTF8.GetString(childOutput.Values).Contains("selected inline child", StringComparison.Ordinal),
                        "a fresh owner resolves the exact selected child and resumes the parent without replaying its creator");
                    await RecoverParkAsync(coldOwner, cold, parkedRun);
                    await RecoverInitialParkAsync(coldOwner, cold, initialPark.Reference);
                    await CheckColdJobAsync(jobWinner, "captured before deadline");
                    await CheckColdJobAsync(jobDeadline, "timeout");
                    Check(PostgresRootTimedJobSettlement.ControlledJob.Attempts.IsEmpty,
                        "the successor never starts a new job for either already selected volatile winner");
                    FailOrderedTurn.Allow = true;
                    await AdmitRecoveryAsync(coldOwner, cold, orderedPark.Reference, "recover-ordered-mailbox");
                    EfStore.RootImage? orderedResult;
                    do
                    {
                        await coldOwner.RunAsync(token);
                        await Task.Delay(5, token);
                        orderedResult = await cold.ReadFirstRootAsync(scope.Tenant, orderedPark.Reference.Id, token);
                    } while (orderedResult?.Status != InstanceStatus.Finished);
                    Check(replayedOrder.SequenceEqual(orderedPark.Expected) && FailOrderedTurn.Entries == 2 &&
                        trace.ToString().Split("left after recovery", StringSplitOptions.None).Length == 2 &&
                        trace.ToString().Split("right after recovery", StringSplitOptions.None).Length == 2 &&
                        trace.ToString().Split("later after recovery", StringSplitOptions.None).Length == 2,
                        "cold Recover offers the exact failed cohort and later timer in their original order without losing a branch");
                    await coldOwner.ReleaseAsync(token);

                    var shortLease = TimeSpan.FromMilliseconds(1_200);
                    await using var expiringOwner = Worker.ForNode("inline-protection-deadline", physical,
                        new ActorAdapter(physical.WorkerHeaders(firstRoots), actor, physical.RootSelfIssuer, physical.RootJobHandle,
                            rootTimedJobClock: physical.ReadRootTimedJobPublicationTimeAsync), new WorkerOptions { Lease = shortLease });
                    Check(await expiringOwner.GainAsync(0, token), "a fresh owner claims a short physical lease for the protection deadline");
                    var blocked = Queue("blocked-protection", shortWait, expiringOwner);
                    protectionDeadline = blocked.Reference.Id;
                    using var deadlineCheck = CancellationTokenSource.CreateLinkedTokenSource(token);
                    deadlineCheck.CancelAfter(shortLease * 4);
                    while (!blocked.Outcome.IsCompleted)
                        await expiringOwner.RunAsync(deadlineCheck.Token);
                    await protectorCancelled.Task.WaitAsync(deadlineCheck.Token);
                    Check(await blocked.Outcome is { Disposition: InlineRootDisposition.Refused or InlineRootDisposition.OwnershipLost } &&
                        await physical.ReadFirstRootAsync(scope.Tenant, protectionDeadline, token) is null,
                        "the owner lease bounds an unresponsive protector and no continuation is reported committed");

                    async Task CheckColdJobAsync(Contract.ActorRef actor, string value)
                    {
                        await WaitForFinishAsync(coldOwner, cold, actor.Id);
                        var finished = await cold.ReadFirstRootAsync(scope.Tenant, actor.Id, token);
                        Check(finished is { Status: InstanceStatus.Finished, Revision: 2, TerminalAt: { } ended } &&
                            await cold.ReadRootTerminalOutputAsync(scope.Tenant, actor.Id, ended, true, token) is { Values: not null } output &&
                            System.Text.Encoding.UTF8.GetString(output.Values).Contains(value, StringComparison.OrdinalIgnoreCase),
                            "a fresh owner runs the authored callback once with the committed volatile winner");
                    }
                }
                finally { WriteLine.Out = originalTrace; }

                (Contract.ActorRef Reference, Task<InlineRootResult> Outcome) Queue(string operation, Workflow selected, Worker? target = null, JsonObject? values = null) =>
                    physical.QueueInlineRoot(target ?? owner, operation, WorkflowActorType.Start(NativeExecutionModel.Start(selected, Modes.Ephemeral, values)),
                        model.CaptureInlineMaterial(selected)) ?? throw new InvalidOperationException("Could not queue an inline workflow.");

                (Contract.ActorRef Reference, Task<InlineRootResult> Outcome) QueueExpected(string operation, Workflow selected, long revision)
                {
                    var queued = Queue(operation, selected);
                    firstRevisions.Add(queued.Reference.Id, revision);
                    return queued;
                }

                async Task QualifyTerminalAsync()
                {
                    unwrittenRevision = 0;
                    var queued = QueueExpected("terminal-first-write", terminal, 2);
                    await WaitUnwrittenAsync(1);
                    Check(!queued.Outcome.IsCompleted && await physical.ReadFirstRootAsync(scope.Tenant, queued.Reference.Id, token) is null,
                        "the finishing workflow begins with an entirely volatile Delay");
                    // Enter before the cutoff, then let the in-flight turn finish during owner drain.
                    await WaitForAsync(FinishAcrossCutoff.Entered.Task);
                    await Task.Delay(ownerOptions.ShortWait + TimeSpan.FromMilliseconds(25), token);
                    FinishAcrossCutoff.Release.SetResult();
                    await owner.DrainEnteredTurnsAsync(token);
                    var disposition = await queued.Outcome.WaitAsync(token);
                    var row = await physical.ReadFirstRootAsync(scope.Tenant, queued.Reference.Id, token);
                    Check(disposition == new InlineRootResult(InlineRootDisposition.Committed, InstanceStatus.Finished) &&
                        row is
                            { Revision: 2, TerminalRevision: 2, TerminalAt: { } ended, Status: InstanceStatus.Finished } &&
                        await physical.ReadRootTerminalOutputAsync(scope.Tenant, queued.Reference.Id, ended, true, token) is { Values: not null } output &&
                        System.Text.Encoding.UTF8.GetString(output.Values).Contains("committed terminal", StringComparison.Ordinal),
                        $"the first durable terminal checkpoint records its output and terminal time in the same commit ({disposition}, row status {row?.Status}, prepared {checkpoint?.Row?.Status})");
                }

                async Task<Contract.ActorRef> QualifyEarlierTimerAsync()
                {
                    unwrittenRevision = 0;
                    var queued = QueueExpected("promote-earlier-timer", earlyTimers, 2);
                    await WaitUnwrittenAsync(2);
                    Check(!queued.Outcome.IsCompleted && await physical.ReadFirstRootAsync(scope.Tenant, queued.Reference.Id, token) is null,
                        "the first branch finishes without writing its parallel sibling's earlier timer");
                    await Task.Delay(ownerOptions.ShortWait + TimeSpan.FromMilliseconds(25), token);
                    await owner.FlushAsync(token);
                    Check(await queued.Outcome == new InlineRootResult(InlineRootDisposition.Committed, InstanceStatus.Running) &&
                        checkpoint?.Sent is [var earlier] && FoundationSelfSend.AuthoredRevision(earlier) == 1 &&
                        await physical.ReadFirstRootAsync(scope.Tenant, queued.Reference.Id, token) is { Revision: 2 },
                        "flush promotes the pending timer under its original authored revision and the checkpoint's first durable revision");
                    return queued.Reference;
                }

                async Task<Contract.ActorRef> QualifyParkAsync()
                {
                    unwrittenRevision = 0;
                    var queued = QueueExpected("park-after-volatile-work", parkWorkflow, 2);
                    await WaitUnwrittenAsync(1);
                    Check(await physical.ReadFirstRootAsync(scope.Tenant, queued.Reference.Id, token) is null,
                        "the exact pre-failure Native state has not been written");
                    await WaitForAsync(queued.Outcome);
                    var disposition = await queued.Outcome;
                    var parked = await physical.ReadFirstRootAsync(scope.Tenant, queued.Reference.Id, token);
                    Check(disposition == new InlineRootResult(InlineRootDisposition.Committed, InstanceStatus.Faulted) &&
                        parked is { Status: InstanceStatus.Faulted, Revision: 2, ParkAt: > 0, TerminalAt: null } &&
                        await physical.ReadRootActorAsync(scope.Tenant, queued.Reference.Id, token) is { Item.ParkRevision: 2 },
                        $"the first physical checkpoint is the selected Park ({disposition}, row status {parked?.Status})");
                    var actor = parked!.Actor.Id;
                    await using var db = Open();
                    var kept = await db.Database.SqlQuery<bool>($"""
                        SELECT (SELECT count(*) = 1 FROM "Messages" WHERE "Target" = {actor})
                            AND EXISTS (SELECT 1 FROM "Messages" WHERE "Target" = {actor} AND "Done"
                                AND "InboxScope" = {EfStore.RootSelfScope} AND "FoundationKeptBy" = {(short)ParkKeepReason.Arrived})
                            AND EXISTS (SELECT 1 FROM "FoundationRootBirths" WHERE "Reference" = {queued.Reference.Id} AND "FirstRevision" = 2)
                            AND (SELECT count(*) = 1 FROM "Journal" WHERE "Instance" = {actor} AND "Revision" = 2) AS "Value"
                        """).SingleAsync(token);
                    Check(kept && await physical.ReadRootSequencedJournalAsync(scope.Tenant, queued.Reference.Id, 0, null, 64, token) is
                            { CapturedFromStart: true } history && history.Items.All(entry => entry.Revision == 2) &&
                        trace.ToString().Split("before inline Park", StringSplitOptions.None).Length == 2,
                        "Park keeps the exact failed local timer and committed history without a replay creator or pseudo revision");
                    return queued.Reference;
                }

                async Task<(Contract.ActorRef Reference, string Actor, string[] Expected)> QualifyOrderedParkAsync()
                {
                    unwrittenRevision = 0;
                    var queued = QueueExpected("park-ordered-mailbox", orderedParkWorkflow, 2);
                    await WaitUnwrittenAsync(1);
                    await WaitForAsync(queued.Outcome);
                    var parked = await physical.ReadFirstRootAsync(scope.Tenant, queued.Reference.Id, token);
                    Check(await queued.Outcome == new InlineRootResult(InlineRootDisposition.Committed, InstanceStatus.Faulted) &&
                        parked is { Revision: 2, Status: InstanceStatus.Faulted, ParkAt: > 0 } && FailOrderedTurn.Entries == 1,
                        "several volatile inputs can open one protected Park at the last sealed checkpoint");
                    await using var db = Open();
                    var actor = parked!.Actor.Id;
                    var rows = await db.Messages.Where(row => row.Target == actor).OrderBy(row => row.Seq).AsNoTracking().ToArrayAsync(token);
                    Check(rows is [{ Done: true }, { Done: true }, { Done: false }] && rows.All(row => row.InboxScope == EfStore.RootSelfScope) &&
                        volatileCohorts[actor].SequenceEqual(rows.Take(2).Select(row => row.Id)),
                        "the first commit keeps the selected two occurrences and preserves the not-yet-due timer after them");
                    return (queued.Reference, actor, rows.Select(row => row.Id).ToArray());
                }

                async Task<Contract.ActorRef> QualifyVolatileJobAsync(bool callbackWins)
                {
                    unwrittenRevision = 0;
                    var quotedPublications = Volatile.Read(ref jobPublications);
                    var queued = QueueExpected(callbackWins ? "volatile-job-winner" : "volatile-deadline-winner", volatileJobWorkflow, 1);
                    await WaitUnwrittenAsync(1);
                    var actor = unwrittenActor!;
                    while (!PostgresRootTimedJobSettlement.ControlledJob.Attempts.TryPeek(out _)) await Task.Delay(5, token);
                    Check(PostgresRootTimedJobSettlement.ControlledJob.Attempts.TryDequeue(out var attempt) &&
                        await physical.ReadFirstRootAsync(scope.Tenant, queued.Reference.Id, token) is null,
                        "the timed activity enters as a volatile attempt before its first durable checkpoint");
                    if (callbackWins)
                    {
                        attempt.Exit!.SetResult("captured before deadline");
                        while (Volatile.Read(ref jobPublications) == quotedPublications) await Task.Delay(5, token);
                    }
                    else await Task.Delay(950, token);

                    owner.RunTurn(actor);
                    await owner.FlushAsync(token);
                    Check(await queued.Outcome.WaitAsync(token) == new InlineRootResult(InlineRootDisposition.Committed, InstanceStatus.Running),
                        "the selected timed winner promotes the last sealed checkpoint before running the authored callback");
                    await using var db = Open();
                    var stored = await db.Messages.AsNoTracking().Where(message => message.Target == actor && !message.Done).ToArrayAsync(token);
                    Check(stored is [{ Kind: Kinds.JobFinished, JobGeneration: 0, Payload: not null } result] &&
                        (callbackWins ? Durable.Json.Read<JobFinished>(result.Payload).Result?.Contains("captured before deadline", StringComparison.Ordinal) == true
                            : Durable.Json.Read<JobFinished>(result.Payload) is { Result: null, Error: "timeout" }),
                        "the first durable write contains exactly the selected recorded result and no execution ticket or deadline");
                    return queued.Reference;
                }

                async Task RecoverParkAsync(Worker successor, EfStore backend, Contract.ActorRef parked)
                {
                    FailAfterLocalTimer.Allow = true;
                    await AdmitRecoveryAsync(successor, backend, parked, "recover-inline-park");
                    EfStore.RootImage? current;
                    do
                    {
                        await successor.RunAsync(token);
                        await Task.Delay(5, token);
                        current = await backend.ReadFirstRootAsync(scope.Tenant, parked.Id, token);
                    } while (current?.Status != InstanceStatus.Finished);
                    Check(current is { ParkAt: null, Revision: >= 4 } && FailAfterLocalTimer.Entries == 2 &&
                        trace.ToString().Split("before inline Park", StringSplitOptions.None).Length == 2 &&
                        trace.ToString().Split("after inline Park", StringSplitOptions.None).Length == 2,
                        "cold Recover retries the one failed timer and never replays the earlier Native work");
                }

                async Task RecoverInitialParkAsync(Worker successor, EfStore backend, Contract.ActorRef parked)
                {
                    var initial = await backend.ReadFirstRootAsync(scope.Tenant, parked.Id, token) ??
                        throw new InvalidOperationException("The initial Park is missing.");
                    var key = FoundationInlineRetry.Key(backend.RootSelfIssuer!, initial.Actor.Id);
                    await using var db = Open();
                    var content = await db.Messages.AsNoTracking().SingleAsync(row => row.Target == initial.Actor.Id &&
                        row.InboxScope == EfStore.RootSelfScope && row.Id == key.Id, token);
                    var wrong = content.Payload!.Replace("original initial input", "different initial input", StringComparison.Ordinal);
                    Check(wrong != content.Payload && await ChangeRetryAsync(content.Payload, wrong) == 1,
                        "the fixture changes only the retained retry body behind its original identity");
                    await AdmitRecoveryAsync(successor, backend, parked, "recover-initial-wrong-body");
                    EfStore.RootImage? current;
                    do
                    {
                        await successor.RunAsync(token);
                        await Task.Delay(5, token);
                        current = await backend.ReadFirstRootAsync(scope.Tenant, parked.Id, token);
                    } while (current?.Revision < 2);
                    Check(current is { Revision: 2, Status: InstanceStatus.Faulted, ParkAt: > 0, State: null } && FailInitialTurn.Entries == 1 &&
                        await backend.ReadRootActorAsync(scope.Tenant, parked.Id, token) is { Item.ParkRevision: 1 } &&
                        await ChangeRetryAsync(wrong, content.Payload) == 1,
                        "the changed initial body refuses Recover without entering the model or changing its Park mark");

                    await AdmitRecoveryAsync(successor, backend, parked, "recover-initial-still-failing");
                    do
                    {
                        await successor.RunAsync(token);
                        await Task.Delay(5, token);
                        current = await backend.ReadFirstRootAsync(scope.Tenant, parked.Id, token);
                    } while (current is not { Status: InstanceStatus.Faulted, Revision: >= 4 });
                    Check(current is { Revision: 4, ParkAt: > 0, State: null } && FailInitialTurn.Entries == 2,
                        "a failed explicit retry opens a new exact Park and keeps the initial input recoverable");

                    FailInitialTurn.Allow = true;
                    await AdmitRecoveryAsync(successor, backend, parked, "recover-initial-ready");
                    do
                    {
                        await successor.RunAsync(token);
                        await Task.Delay(5, token);
                        current = await backend.ReadFirstRootAsync(scope.Tenant, parked.Id, token);
                    } while (current?.Status != InstanceStatus.Finished);
                    Check(current is { Revision: 6, ParkAt: null, TerminalAt: { } ended } && FailInitialTurn.Entries == 3 &&
                        await backend.ReadRootTerminalOutputAsync(scope.Tenant, parked.Id, ended, true, token) is { Values: not null } output &&
                        System.Text.Encoding.UTF8.GetString(output.Values).Contains("original initial input", StringComparison.Ordinal),
                        "cold Recover re-enters the original uncommitted start with the exact input and same actor identity");

                    Task<int> ChangeRetryAsync(string? expected, string? replacement) => db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE "Messages" SET "Payload" = {replacement} WHERE "Target" = {initial.Actor.Id}
                            AND "InboxScope" = {EfStore.RootSelfScope} AND "Id" = {key.Id} AND "Payload" = {expected}
                        """, token);
                }

                async Task AdmitRecoveryAsync(Worker successor, EfStore backend, Contract.ActorRef parked, string operation)
                {
                    var (projected, _) = await backend.ReadRootActorAsync(scope.Tenant, parked.Id, token) ??
                        throw new InvalidOperationException("The committed Park is missing.");
                    var window = TimeSpan.FromHours(1);
                    var pg = postgres ? new PostgresRootAdmission(Open, window) : null;
                    await using var db = Open();
                    var cursor = pg is not null ? await pg.OpenJournalCursorAsync(token) : await SqliteRootAdmission.ReadSelectionCursorAsync(db, token);
                    var selection = PostgresRootSelection.SealPark(cursor, scope.Tenant, parked.Id, projected.ParkRevision!.Value, projected.ParkAt!.Value);
                    var admitted = pg is not null
                        ? await pg.AdmitRootOperatorAsync(scope.Tenant, parked.Id, operation, Kinds.Recover, null, null, token, selection) is not null
                        : (await new SqliteRootAdmission(Open, window, sqliteDefinitions).OperateAsync(scope.Tenant, parked, operation,
                            Kinds.Recover, selection, null, null, token)).Outcome == Contract.AdmissionOutcome.Accepted;
                    Check(admitted, "the cold store accepts Recover for the exact Park selection");
                    var physicalActor = await backend.ReadFirstRootAsync(scope.Tenant, parked.Id, token);
                    await successor.WokenAsync(new InstanceRef(physicalActor!.Actor.Id), token);
                }

                async Task WaitForFinishAsync(Worker selected, EfStore backend, string reference)
                {
                    while (await backend.ReadFirstRootAsync(scope.Tenant, reference, token) is { Status: InstanceStatus.Running })
                    {
                        await selected.RunAsync(token);
                        await Task.Delay(5, token);
                    }
                }

                async ValueTask<ChildBirthProtectionOutcome> ProtectChildrenAsync(IReadOnlyList<PendingChildBirth> births, CancellationToken ct)
                {
                    if (births is not [var child] || FoundationChildIssuance.Verify(key, scope.Tenant, child.Actor, child.Issuance) is not
                        { ParentRevision: 2 } issue)
                        throw new InvalidOperationException("The inline owner issued an unexpected child.");
                    var parentReference = FoundationRootCreator.ReferenceFor(key, scope.Tenant, new(issue.Parent, 1));
                    if (offered is not { } offer || offer.Actor.Id != parentReference ||
                        child.Type != childStart.Type || child.TypeData != childStart.TypeData)
                        throw new InvalidOperationException("The child material did not follow its exact parent protection.");
                    await using var db = Open();
                    db.Database.SetCommandTimeout(1);
                    await using var write = await db.Database.BeginTransactionAsync(ct);
                    if (postgres) await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '1s'", ct);
                    Check(await db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE "Partitions" SET "Position" = "Position" WHERE "Id" = {issue.Fence().Partition}
                        """, ct) == 1 && !await HasChildBirthAsync(child.Actor, ct) &&
                        await db.Database.SqlQuery<bool>($"""
                            SELECT EXISTS (SELECT 1 FROM "FoundationPreparedRootStarts"
                                WHERE "Reference" = {parentReference} AND "FirstRevision" = 2)
                                AND NOT EXISTS (SELECT 1 FROM "FoundationRootBirths" WHERE "Reference" = {parentReference}) AS "Value"
                            """).SingleAsync(ct),
                        "the child protector can lock the owner partition and sees only the exact parent preparation");
                    await write.RollbackAsync(ct);
                    if (childOffers.TryGetValue(parentReference, out var prior))
                    {
                        Check(prior.Offer == child && prior.RootIssuance == offer.Issuance,
                            "a transient child protection retry keeps the same parent and child issuances");
                        childOffers[parentReference] = prior with { Attempts = prior.Attempts + 1 };
                        return ChildBirthProtectionOutcome.Protected;
                    }
                    childOffers.Add(parentReference, (child, offer.Issuance, issue.Child, 1));
                    return parentReference == refusedParent ? ChildBirthProtectionOutcome.Refused : ChildBirthProtectionOutcome.Retry;
                }

                async Task<bool> HasChildBirthAsync(Contract.ActorRef child, CancellationToken ct)
                {
                    await using var db = Open();
                    return await db.Database.SqlQuery<bool>($"""
                        SELECT EXISTS (SELECT 1 FROM "FoundationRootBirths" WHERE "Tenant" = {scope.Tenant}
                            AND "Reference" = {child.Id}) AS "Value"
                        """).SingleAsync(ct);
                }

                Task<InlineStartOutcome> StartAsync(string operation, Workflow selected, string? input = null)
                {
                    var start = WorkflowActorType.Start(NativeExecutionModel.Start(selected, Modes.Ephemeral));
                    return requests.StartAsync(operation, input is null ? start : start with { Input = input },
                        model.CaptureInlineMaterial(selected), token).AsTask();
                }

                async Task QualifyPressureAsync()
                {
                    checkingPressure = true;
                    await SetBacklogAsync(pressure.Backlog.HighWater + 1);
                    var before = await CountDurableRowsAsync();
                    using (pressure.Begin(scope.Tenant, RootAdmissionKind.Input))
                    {
                        var admitted = StartAsync("volatile-through-pressure", pure);
                        await WaitForAsync(admitted);
                        Check(await admitted is { Disposition: InlineStartDisposition.Volatile, Actor: null, Status: Contract.ActorStatus.Finished } &&
                            await CountDurableRowsAsync() == before && protectionCalls == 0,
                            "the bound inline edge finishes write-free despite a full physical pressure list and occupied ordinary capacity");
                    }
                    const string preferred = "preferred-first-fence";
                    var prefersZero = InstanceRef.Spread(preferred, 2) == 0 ? preferred : preferred + "1";
                    Check(RootAdmissionPressure.PlaceInlineStart(prefersZero, [0, 1], pressure) == 1,
                        "placement prefers an under-water owned partition ahead of a full one");
                    Check(await StartAsync("rate-limited-before-turn", pure) is
                            { Disposition: InlineStartDisposition.Unavailable, RefusalReason: Contract.RefusalReason.RateLimited, RetryAfter: { } retry }
                            && retry > TimeSpan.Zero && owner.QueuedInlineRootCount == 0 && await CountDurableRowsAsync() == before &&
                            trace.ToString().Split("pure work", StringSplitOptions.None).Length == 2,
                        "the same bound tenant gets a definite rate refusal and retry interval before queueing any work");
                    await SetBacklogAsync(0);
                    var durable = WorkflowActorType.Start(NativeExecutionModel.Start(pure));
                    var durableStart = new StartSpec(durable.Type, durable.TypeData, durable.Input);
                    try
                    {
                        if (postgres)
                            await new PostgresRootAdmission(Open, TimeSpan.FromHours(1), pressure: pressure)
                                .AdmitProtectedRootStartAsync(scope.Tenant, "same-tenant-durable", durableStart,
                                    (_, _) => ValueTask.FromResult(true), token, token);
                        else
                            await new SqliteRootAdmission(Open, TimeSpan.FromHours(1), sqliteDefinitions, pressure)
                                .StartAsync(scope.Tenant, "same-tenant-durable", durableStart, null, null, token);
                        throw new InvalidOperationException("The inline and durable starts did not share their tenant rate bucket.");
                    }
                    catch (RootAdmissionRefused refusal) when (refusal.Admission.Reason == Contract.RefusalReason.RateLimited)
                    {
                        Check(await CountDurableRowsAsync() == before, "the ordinary provider admission sees the same consumed tenant start permit");
                    }

                    rateClock.Advance(TimeSpan.FromSeconds(10));
                    await SetBacklogAsync(pressure.Backlog.HighWater + 1);
                    var refused = StartAsync("promotion-refused-by-backlog", shortWait);
                    await WaitForAsync(refused);
                    Check(await refused is { Disposition: InlineStartDisposition.Refused, Actor: null,
                            RefusalReason: Contract.RefusalReason.InboxFull, RetryAfter: null } &&
                        await CountDurableRowsAsync() == before && protectionCalls == 0,
                        "a needed promotion checks the exact partition before protection and refuses without a replay hint");

                    rateClock.Advance(TimeSpan.FromSeconds(10));
                    await SetBacklogAsync(0);
                    var promoted = StartAsync("promotion-spends-one-permit", shortWait);
                    await WaitForAsync(promoted);
                    Check(await promoted is { Disposition: InlineStartDisposition.Committed, Actor: { } reference } &&
                        await physical.ReadFirstRootAsync(scope.Tenant, reference.Id, token) is { Status: InstanceStatus.Running } &&
                        protectionCalls == 1 && await StartAsync("post-promotion-rate-limit", pure) is
                            { Disposition: InlineStartDisposition.Unavailable, RefusalReason: Contract.RefusalReason.RateLimited },
                        "relief admits the fenced promotion with exactly one tenant permit");

                    rateClock.Advance(TimeSpan.FromSeconds(10));
                    var firstLarge = StartAsync("maximum-inline-input-1", pure, QualifiedInlineRootCoreChecks.MaximumInput());
                    rateClock.Advance(TimeSpan.FromSeconds(10));
                    var secondLarge = StartAsync("maximum-inline-input-2", pure, QualifiedInlineRootCoreChecks.MaximumInput());
                    rateClock.Advance(TimeSpan.FromSeconds(10));
                    var durableBeforeLimit = await CountDurableRowsAsync();
                    Check(!firstLarge.IsCompleted && !secondLarge.IsCompleted && owner.QueuedInlineRootCount == 2 &&
                        await StartAsync("over-owner-byte-budget", pure, QualifiedInlineRootCoreChecks.MaximumInput()) is
                            { Disposition: InlineStartDisposition.Unavailable, RefusalReason: null },
                        "maximum legal ASCII starts fit individually but their UTF-16 captures exhaust the owner budget before the count limit");
                    var small = StartAsync("small-after-byte-refusal", pure);
                    Check(!small.IsCompleted && owner.QueuedInlineRootCount == 3 && await CountDurableRowsAsync() == durableBeforeLimit &&
                        await StartAsync("rate-after-small", pure) is { RefusalReason: Contract.RefusalReason.RateLimited },
                        "a byte refusal refunds its reservation, and the same tenant can spend it on a small start without provider writes");
                    await owner.ReleaseAsync(token);
                    Check(await firstLarge is { Disposition: InlineStartDisposition.Unknown } &&
                        await secondLarge is { Disposition: InlineStartDisposition.Unknown } &&
                        await small is { Disposition: InlineStartDisposition.Unknown } &&
                        await owner.GainAsync(0, token),
                        "stand-down releases every queued capture and settles the original callers after ownership loss");
                    rateClock.Advance(TimeSpan.FromSeconds(10));
                    var largeAfterReset = StartAsync("maximum-after-reset", pure, QualifiedInlineRootCoreChecks.MaximumInput());
                    await WaitForAsync(largeAfterReset);
                    Check(await largeAfterReset is { Disposition: InlineStartDisposition.Volatile } && protectionCalls == 1,
                        "the next owner interval admits a maximum input and completes it without invoking durable protection");
                    checkingPressure = false;

                    async Task SetBacklogAsync(int depth)
                    {
                        await using var db = Open();
                        await db.Partitions.Where(partition => partition.Id == 0)
                            .ExecuteUpdateAsync(update => update.SetProperty(partition => partition.Backlog, depth), token);
                        await physical.PrimeAdmissionPressureAsync(token);
                    }

                    async Task<long> CountDurableRowsAsync()
                    {
                        await using var db = Open();
                        return await db.Database.SqlQueryRaw<long>("""
                            SELECT (SELECT count(*) FROM "FoundationPreparedRootStarts") + (SELECT count(*) FROM "FoundationRootReceipts") +
                                (SELECT count(*) FROM "FoundationRootBirths") + (SELECT count(*) FROM "Instances") +
                                (SELECT count(*) FROM "Messages") + (SELECT count(*) FROM "Journal") AS "Value"
                            """).SingleAsync(token);
                    }
                }

                async Task WaitForAsync(Task receipt)
                {
                    while (!receipt.IsCompleted)
                    {
                        token.ThrowIfCancellationRequested();
                        await owner.RunAsync(token);
                        if (!receipt.IsCompleted) await Task.Delay(5, token);
                    }
                }

                async Task WaitUnwrittenAsync(long revision)
                {
                    while (unwrittenRevision < revision)
                    {
                        token.ThrowIfCancellationRequested();
                        await owner.RunAsync(token);
                        if (unwrittenRevision < revision) await Task.Delay(5, token);
                    }
                }

                async ValueTask<bool> CreateCanReadPreparationAsync(PendingRootStart offer, string exactActor, CancellationToken ct)
                {
                    await using var db = Open();
                    return await db.Database.SqlQuery<int>($"""
                        SELECT CAST(count(*) AS integer) AS "Value" FROM "FoundationPreparedRootStarts"
                        WHERE "Reference" = {offer.Actor.Id}
                        """).SingleAsync(ct) == 1 && !await db.Messages.AnyAsync(message => message.Target == exactActor, ct) &&
                        await store.ReadFirstRootAsync(scope.Tenant, offer.Actor.Id, ct) is null;
                }
            }

            (Message Creator, TurnCommit Turn) Sealed(string operation, long revision)
            {
                var address = InstanceRef.In(0, scope.Tenant + "_" + Guid.NewGuid().ToString("N"));
                var reference = FoundationRootCreator.ReferenceFor(key, scope.Tenant, new(address.Id, 1));
                var storedOperation = RootPreparedStarts.InlineOperation(operation);
                var creator = FoundationRootCreator.Create(address, storedOperation, FoundationRootCreator.OperationDigest(storedOperation),
                    new StartSpec("workflow", "sealed", Ephemeral: true) { FoundationInlineTenant = scope.Tenant }, null, null)
                    with { FoundationRootBinding = (address.Id, scope.Tenant, reference) };
                var header = new FoundationHeaderChange("selected-model", new Dictionary<string, string> { ["definition"] = "sealed" });
                var row = new InstanceRow(address.Id, "workflow", "sealed", InstanceStatus.Running, """{"wait":true}""", revision,
                    Parent: null, StateVersion: 1, Creation: 1,
                    StartIntent: CreationProtocol.Fingerprint(creator.Start, creator.Kind, creator.Payload))
                {
                    FoundationInlineBirth = (scope.Tenant, creator.InboxKey),
                    FoundationDispatchHeader = new(scope.Tenant, reference, header)
                };
                var wait = new SubscriptionRow(scope.Tenant + "_event", address.Id, "resume", Creation: 1) { FoundationTenant = scope.Tenant };
                return (creator, new(address.Id, row, null, [], [], [], [wait], [], """["start","waiting"]""")
                    { FoundationHeader = header });
            }

            async Task<int> CountAsync(Message message, string table, string column, CancellationToken ct)
            {
                await using var db = Open();
                return await db.Database.SqlQueryRaw<int>($$"""
                    SELECT CAST(count(*) AS integer) AS "Value" FROM "{{table}}" WHERE "{{column}}" = {0}
                    """, message.Target.Id).SingleAsync(ct);
            }
        }
        finally { directory?.Delete(recursive: true); }
    }

    public sealed class FinishAcrossCutoff : Activity
    {
        internal static readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal static readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async ValueTask<Transition> ExecuteAsync(ActivityContext context)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(context.CancellationToken);
            return Complete();
        }
    }

    public sealed class FailAfterLocalTimer : CompositeActivity
    {
        internal static bool Allow;
        internal static int Entries;

        protected override void Execute(CompositeContext context)
        {
            Interlocked.Increment(ref Entries);
            if (!Allow) throw new InvalidOperationException("The Native timer continuation could not execute.");
            context.Complete();
        }

        protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) =>
            throw new InvalidOperationException("This activity does not start any children.");
    }

    public sealed class FailInitialTurn : CompositeActivity
    {
        internal static bool Allow;
        internal static int Entries;

        protected override void Execute(CompositeContext context)
        {
            Interlocked.Increment(ref Entries);
            if (!Allow) throw new InvalidOperationException("The Native first turn could not execute.");
            context.Complete();
        }

        protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) =>
            throw new InvalidOperationException("This activity does not start any children.");
    }

    public sealed class FailOrderedTurn : CompositeActivity
    {
        internal static bool Allow;
        internal static int Entries;

        protected override void Execute(CompositeContext context)
        {
            Interlocked.Increment(ref Entries);
            if (!Allow) throw new InvalidOperationException("The ordered Native cohort could not execute.");
            context.Complete();
        }

        protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) =>
            throw new InvalidOperationException("This activity does not start any children.");
    }

    private static void Check(bool result, string description)
    {
        if (!result) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }
}
