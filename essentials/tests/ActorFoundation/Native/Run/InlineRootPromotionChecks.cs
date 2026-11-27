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
                var registry = new WorkflowRegistry().Add(workflow);
                var model = new NativeExecutionModel(registry, new NativeExecutionOptions { UseStructuralJournal = true });
                var actor = new WorkflowActorType([model]);
                var spec = WorkflowActorType.Start(NativeExecutionModel.Start(workflow, Modes.Ephemeral));
                var address = InstanceRef.In(0, scope.Tenant + "_" + Guid.NewGuid().ToString("N"));
                var reference = FoundationRootCreator.ReferenceFor(key, scope.Tenant, new(address.Id, 1));
                var operation = RootPreparedStarts.InlineOperation("later-native");
                var local = FoundationRootCreator.Create(address, operation, FoundationRootCreator.OperationDigest(operation),
                    new StartSpec(spec.Type, spec.TypeData, spec.Input, Ephemeral: true), null, null)
                    with { FoundationRootBinding = (address.Id, scope.Tenant, reference) };
                long now = 10_000;
                long unwrittenRevision = 0;
                TurnCommit? checkpoint = null;
                Message? firstCreator = null;
                var headers = new QualifiedInlineRootCoreChecks.InlineHeaders((requested, material) =>
                    WorkflowActorType.DescribeStart(requested.Model, requested.Definition) is { } selected &&
                    model.MatchesInlineMaterial(material, selected.Workflow, selected.Definition, selected.ExecutableSelection));
                var inMemory = new InMemoryStore(1, () => now);
                var memoryOptions = new WorkerOptions
                {
                    Monotonic = () => now,
                    OnKeptInMemory = (_, revision) => unwrittenRevision = revision,
                    OnPreparedCheckpoint = (sealedTurn, _, accepted) =>
                    {
                        checkpoint = sealedTurn;
                        firstCreator = accepted;
                    }
                };
                var self = new FoundationSelfIssuer(seed => FoundationSelfSend.Handle(key, seed), SupportsTimers: true);
                await using var volatileOwner = new Worker("inline-volatile", 0, inMemory, new ActorAdapter(headers, actor, self), memoryOptions);
                await volatileOwner.TakeAsync(token);
                var originalTrace = WriteLine.Out;
                using var trace = new StringWriter();
                WriteLine.Out = trace;
                try
                {
                    var receipt = volatileOwner.TryQueueQualifiedInlineStart(local, model.CaptureInlineMaterial(workflow)) ??
                        throw new InvalidOperationException("Could not admit the local fixture turn.");
                    await WaitUnwrittenAsync(1);
                    Check(!receipt.IsCompleted && checkpoint is null && await store.ReadFirstRootAsync(scope.Tenant, reference, token) is null,
                        "the first short Delay has no physical checkpoint or receipt");
                    now += 100;
                    await WaitUnwrittenAsync(2);
                    Check(!receipt.IsCompleted && checkpoint is null && await store.ReadFirstRootAsync(scope.Tenant, reference, token) is null,
                        "the second short Delay still has no physical checkpoint or receipt");
                    now += 100;
                    while (!receipt.IsCompleted)
                    {
                        token.ThrowIfCancellationRequested();
                        await volatileOwner.RunAsync(token);
                    }
                    Check(checkpoint?.Row?.Revision == 3 && firstCreator?.InboxKey == local.InboxKey,
                        "the existing local owner seals the external wait at logical revision three with its original creator");
                    var promotedFence = await TakeAsync("inline-promoting-owner");
                    Check(await PromoteAsync(firstCreator!, checkpoint!, promotedFence, (offer, _) =>
                        ValueTask.FromResult(RootPreparedStarts.Verify(key, scope, new(reference), offer.Issuance) is { FirstRevision: 3 })) is { Landed: true },
                        "protocol 125 persists the sealed Native wait without running the workflow again");
                    var before = await store.ReadRootSequencedJournalAsync(scope.Tenant, reference, 0, null, 64, token);
                    Check(before is { CapturedFromStart: true, RetainedFromSequence: 1 } &&
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
                    await store.ReleaseAsync(promotedFence.Partition, promotedFence.Epoch, token);
                    await volatileOwner.ReleaseAsync(token);

                    var cold = new EfStore(Open, postgres, fenceInBatch) { CommitSql = CommitSql.Owned };
                    var coldModel = new NativeExecutionModel(new WorkflowRegistry().Add(workflow),
                        new NativeExecutionOptions { UseStructuralJournal = true });
                    var coldActor = new WorkflowActorType([coldModel]);
                    await cold.EnableRootSelfSendsAsync(token);
                    await cold.EnableRootJobsAsync(token);
                    await cold.EnableRootChildrenAsync(token);
                    var roots = postgres ? cold.BindWorkerNode(ActorAdapter.RootWithholding(coldActor, null))
                        : cold.BindSqliteWorkerNode(ActorAdapter.RootWithholding(coldActor, null));
                    var adapter = new ActorAdapter(cold.WorkerHeaders(roots), coldActor, cold.RootSelfIssuer, cold.RootJobHandle,
                        rootTimedJobClock: cold.ReadRootTimedJobPublicationTimeAsync);
                    var observed = 0;
                    var turns = 0;
                    await using var coldOwner = Worker.ForNode("inline-cold-owner", cold, adapter, new WorkerOptions
                    {
                        OnObservedDelivery = _ => observed++,
                        OnTurn = (_, _, _) => turns++
                    });
                    Check(await coldOwner.GainAsync(0, token), "a fresh physical owner claims the promoted root's partition");
                    var input = Durable.Json.Write(new ResumeStimulus("resume"));
                    var accepted = postgres
                        ? await new PostgresRootAdmission(Open, TimeSpan.FromHours(1)).AdmitDirectAsync(scope.Tenant, reference,
                            "resume-inline", NativeKinds.Resume, input, cancellationToken: token) == true
                        : (await new SqliteRootAdmission(Open, TimeSpan.FromHours(1), sqliteDefinitions).SendAsync(scope.Tenant,
                            new(reference), "resume-inline", NativeKinds.Resume, input, null, null, token)).Outcome == Contract.AdmissionOutcome.Accepted;
                    Check(accepted, "trusted ordinary ingress admits the cold continuation");
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
                }
                finally { WriteLine.Out = originalTrace; }

                async Task WaitUnwrittenAsync(long revision)
                {
                    while (unwrittenRevision < revision)
                    {
                        token.ThrowIfCancellationRequested();
                        await volatileOwner.RunAsync(token);
                    }
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

    private static void Check(bool result, string description)
    {
        if (!result) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }
}
