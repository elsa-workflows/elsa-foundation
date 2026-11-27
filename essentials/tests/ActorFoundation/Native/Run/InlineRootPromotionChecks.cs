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
                var registry = new WorkflowRegistry().Add(workflow).Add(shortWait).Add(pure).Add(longTimer).Add(earlyTimers).Add(timedJob).Add(terminal)
                    .Add(parent).Add(selectedChild).Add(parkWorkflow);
                var model = new NativeExecutionModel(registry, new NativeExecutionOptions { UseStructuralJournal = true });
                var actor = new WorkflowActorType([model]);
                var spec = WorkflowActorType.Start(NativeExecutionModel.Start(workflow, Modes.Ephemeral));
                long unwrittenRevision = 0;
                TurnCommit? checkpoint = null;
                Message? firstCreator = null;
                PendingRootStart? offered = null;
                string? acceptedWait = null;
                string? refusedWait = null;
                string? refusedActor = null;
                string? protectionDeadline = null;
                var protectorCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var preparedBatchActors = new HashSet<string>(StringComparer.Ordinal);
                var firstRevisions = new Dictionary<string, long>(StringComparer.Ordinal);
                var childOffers = new Dictionary<string, (PendingChildBirth Offer, string RootIssuance, string Child, int Attempts)>(StringComparer.Ordinal);
                string? refusedParent = null;
                var physical = new EfStore(Open, postgres, fenceInBatch) { CommitSql = CommitSql.Owned, ProtectChildBirths = ProtectChildrenAsync };
                await physical.EnableInlinePromotionAsync(scope, (requested, material) =>
                    WorkflowActorType.DescribeStart(requested.Model, requested.Definition) is { } selected &&
                    model.MatchesInlineMaterial(material, selected.Workflow, selected.Definition, selected.ExecutableSelection),
                    async (offer, ct) =>
                    {
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
                    }, TimeSpan.FromHours(1), token);
                var ownerOptions = new WorkerOptions
                {
                    ShortWait = TimeSpan.FromMilliseconds(1_400),
                    OnKeptInMemory = (_, revision) => unwrittenRevision = revision,
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
                Check(await owner.GainAsync(0, token), "the real provider owner claims the inline partition");
                var firstPosition = owner.PositionOf(0);
                var originalTrace = WriteLine.Out;
                using var trace = new StringWriter();
                WriteLine.Out = trace;
                try
                {
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
                    var early = await QualifyEarlierTimerAsync();
                    var parentRun = QueueExpected("accepted-child", parent, 2);
                    await WaitForAsync(parentRun.Outcome);
                    var protectedChild = childOffers[parentRun.Reference.Id];
                    Check(await parentRun.Outcome == new InlineRootResult(InlineRootDisposition.Committed, InstanceStatus.Running) &&
                        await physical.ReadFirstRootAsync(scope.Tenant, parentRun.Reference.Id, token) is { Revision: 2, Children: { } children } &&
                        ChildRegistry.Read(children) is [var member] && member.Id == protectedChild.Child && protectedChild.Attempts == 2 &&
                        await HasChildBirthAsync(protectedChild.Offer.Actor, token),
                        "the protected first parent checkpoint and exact child creator land atomically after an identical retry");
                    await owner.ReleaseAsync(token);

                    var cold = new EfStore(Open, postgres, fenceInBatch) { CommitSql = CommitSql.Owned };
                    var coldModel = new NativeExecutionModel(new WorkflowRegistry().Add(workflow).Add(longTimer).Add(earlyTimers)
                        .Add(parent).Add(selectedChild).Add(parkWorkflow),
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
                    await using var coldOwner = Worker.ForNode("inline-cold-owner", cold, adapter, new WorkerOptions
                    {
                        OnObservedDelivery = _ => observed++,
                        OnTurn = (_, _, _) => turns++
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
                }
                finally { WriteLine.Out = originalTrace; }

                (Contract.ActorRef Reference, Task<InlineRootResult> Outcome) Queue(string operation, Workflow selected, Worker? target = null) =>
                    physical.QueueInlineRoot(target ?? owner, operation, WorkflowActorType.Start(NativeExecutionModel.Start(selected, Modes.Ephemeral)),
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

                async Task RecoverParkAsync(Worker successor, EfStore backend, Contract.ActorRef parked)
                {
                    var (projected, _) = await backend.ReadRootActorAsync(scope.Tenant, parked.Id, token) ??
                        throw new InvalidOperationException("The committed Park is missing.");
                    var window = TimeSpan.FromHours(1);
                    var pg = postgres ? new PostgresRootAdmission(Open, window) : null;
                    await using var db = Open();
                    var cursor = pg is not null ? await pg.OpenJournalCursorAsync(token) : await SqliteRootAdmission.ReadSelectionCursorAsync(db, token);
                    var selection = PostgresRootSelection.SealPark(cursor, scope.Tenant, parked.Id, projected.ParkRevision!.Value, projected.ParkAt!.Value);
                    FailAfterLocalTimer.Allow = true;
                    var admitted = pg is not null
                        ? await pg.AdmitRootOperatorAsync(scope.Tenant, parked.Id, "recover-inline-park", Kinds.Recover, null, null, token, selection) is not null
                        : (await new SqliteRootAdmission(Open, window, sqliteDefinitions).OperateAsync(scope.Tenant, parked, "recover-inline-park",
                            Kinds.Recover, selection, null, null, token)).Outcome == Contract.AdmissionOutcome.Accepted;
                    Check(admitted, "the cold store accepts Recover for the exact Park selection");
                    var physicalActor = await backend.ReadFirstRootAsync(scope.Tenant, parked.Id, token);
                    await successor.WokenAsync(new InstanceRef(physicalActor!.Actor.Id), token);
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
                        WorkflowActorType.DescribeStart(child.Type, child.TypeData) is not { ExecutableSelection: "private-125-child" })
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

    private static void Check(bool result, string description)
    {
        if (!result) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }
}
