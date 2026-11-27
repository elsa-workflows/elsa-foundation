using System.Data.Common;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Runtime.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

// S7 (#4229): the shipped PostgreSQL format, installed and hosted as a host does: PostgresPreviewHosting's shipped
// context, InstallPreviewRootSchemaAsync, a Foundation with held requests and a PostgresPreviewHostedService answering
// through them. A held start is answered from its own turn and then runs a job; a timed job passes its deadline; a selected
// Terminate and Delete reach the roots, with workflow history beside them. The shipped marker refuses the preview formats
// it replaces, which are recreated, and they refuse it.
// A Terminate of a root whose timed job runs settles the job in its terminal commit, and a Delete retires it after.
// Deferred to the next slice, since the PostgreSQL root dispatch refuses both on every format today: output written after a
// job turn (it reaches only a caller whose delivery the turn consumed), and a Native workflow that ends Faulted (a root
// commits only Running, Finished, a timed-job timeout or a termination). Not carried yet: child Dispatch (the root
// dispatch refuses a child start) and self-sends and timers (Delay), which do not compose with timed jobs yet.
internal static class PostgresShippedFormatChecks
{
    private const string Node = "shipped-node";
    private const long RequestTimeout = 20_000;
    private static readonly string[] Outcomes = ["200", SendHttpRequest.TimeoutOutcome, SendHttpRequest.DoneOutcome, SendHttpRequest.FailedOutcome];

    internal static async Task<int> RunAsync(bool inBatch = false)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        var stop = deadline.Token;
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Supply a fresh PostgreSQL preview database.");
        var markSwap = new SwapBeforeSecondMark();
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).AddInterceptors(markSwap).Options;
        KernelDb Open() => PostgresPreviewHosting.CreateShippedContext(options);
        await using (var db = Open())
        {
            Require(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "the shipped-format check uses an empty PostgreSQL database");
            Require(db.RequiredFormat == StoreFormat.RootShipped &&
                    db.Capabilities == RootProfiles.Shipped && RootProfiles.MissingPrerequisites(db.Capabilities) == RootCapabilities.None,
                "the shipped context selects RootShipped with its closed capability set");
            Require(db.SupportsCorePreview && db.UsesRootReplyProtocol && db.UsesRootJobProtocol && db.UsesRootTimedJobSettlement &&
                    db.UsesRootTimedJobPreview && db.UsesRootTerminalSelection && db.UsesRootRetirement && db.UsesRootOperatorReplies &&
                    db.UsesRootParkInspection && db.UsesRootParkArrivals && db.UsesRootComposedParkRecovery && db.SupportsRootWorkflowHistory,
                "the shipped format carries held replies, timed jobs, selected operators with retirement, Park inspection, arrivals, Recover and workflow history");
            Require(db.UsesRootSelfProtocol && db.UsesRootTimers && db.UsesRootTimerRearm,
                "the shipped format carries actor-owned self-sends, durable timers and Park timer re-arm");
            Require(!db.UsesRootValueProtocol && !db.UsesRootJobPreview && !db.UsesRootTimedJobIssuance,
                "the shipped format excludes values, untimed-only jobs and timed-job issuance");
        }
        var store = new EfStore(Open, postgres: true, fenceInBatch: inBatch);
        await store.CreateAsync(1);
        await store.InstallPreviewRootSchemaAsync(stop);
        await store.InstallPreviewRootSchemaAsync(stop);
        Require(true, "the shipped schema and its workflow history install, and install again over themselves");
        await CheckGuardBodyParityAsync(Open, stop);
        await CheckDefinitionRevisionAsync(Open, store, stop);
        await CheckNamespaceReplacementAsync(Open, connection, stop);
        await CheckStartNamespaceReplacementAsync(Open, connection, stop);
        await using (var db = Open())
        {
            await db.Database.ExecuteSqlRawAsync("""DROP TRIGGER kernel_root_self_insert ON "Messages" """, stop);
            try
            {
                await store.EnableRootSelfSendsAsync(stop);
                throw new InvalidOperationException("The shipped owner initialized the self issuer without its installed trigger.");
            }
            catch (StoreFormatException)
            {
                Require(!await db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS (SELECT 1 FROM pg_trigger
                        WHERE tgrelid = '"Messages"'::regclass AND tgname = 'kernel_root_self_insert') AS "Value"
                    """).SingleAsync(stop), "the shipped owner refuses a missing timer trigger without changing the installed definitions");
            }
        }
        await store.InstallPreviewRootSchemaAsync(stop);
        await using (var db = Open())
        {
            var source = await db.Database.SqlQueryRaw<string>("""SELECT pg_catalog.current_schema() AS "Value" """).SingleAsync(stop);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE SCHEMA timer_startup_shadow;
                CREATE VIEW timer_startup_shadow."FoundationRootMessages" AS SELECT "Id" FROM "Messages";
                DROP VIEW "FoundationRootMessages";
                """, stop);
            var shadowOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(new NpgsqlConnectionStringBuilder(connection)
            {
                SearchPath = "timer_startup_shadow," + new NpgsqlCommandBuilder().QuoteIdentifier(source)
            }.ConnectionString).Options;
            var shadowed = new EfStore(() => PostgresPreviewHosting.CreateShippedContext(shadowOptions), postgres: true);
            try
            {
                await shadowed.EnableRootSelfSendsAsync(stop);
                throw new InvalidOperationException("The shipped owner took a private inbox view from another schema.");
            }
            catch (StoreFormatException)
            {
                Require(true, "shipped startup refuses a private inbox view outside the marker schema");
            }
            await db.Database.ExecuteSqlRawAsync("DROP SCHEMA timer_startup_shadow CASCADE", stop);
        }
        await store.InstallPreviewRootSchemaAsync(stop);

        await RefusalsAsync(connection, options, stop);

        var registry = new WorkflowRegistry()
            // Answers its caller from the start's own turn, then runs a job.
            .Add(Wf.Workflow("answer-then-fetch", Wf.Sequence("main",
                Wf.Respond("accepted", "accepted", 202),
                Wf.Http("send", "https://partner.example/ok", expectedStatusCodes: [200]),
                Wf.Set("fetched", "outcome", Binding.Result("send", "StatusCode")))))
            .Add(Records("stuck", Wf.Http("send", "https://partner.example/stuck", timeout: TimeSpan.FromMilliseconds(400))))
            .Add(Wf.Workflow("delayed", Wf.Sequence("main", Wf.Delay("pause", 300L), Wf.Set("after", "outcome", "delayed"))))
            .Add(Wf.Workflow("long-fetch", Wf.Sequence("main",
                Wf.Http("send", "https://partner.example/stuck", timeout: TimeSpan.FromSeconds(60)), Wf.WriteLine("after", "after"))))
            .Add(Wf.Workflow("waits", Wf.Sequence("main",
                Wf.Node<PostgresHeldDirectChecks.ApprovalSignal>("approve"),
                Wf.WriteLine("approved", "approved"))));
        var partner = new Partner();
        var actor = new WorkflowActor.WorkflowActor([new NativeExecutionModel(registry,
            new NativeExecutionOptions { Http = new NativeHttpOptions { Client = new HttpClient(partner) } })]);
        var foundation = await PostgresPreviewHosting.OpenFoundationAsync(Open, WorkflowActor.WorkflowActor.ActorType,
            PostgresPreviewHosting.CreateRequests(Open, Node, WorkflowActor.WorkflowActor.ActorType), cancellationToken: stop);
        var clock = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        var scope = EmbeddedTenant.Bind("shipped");
        var operators = await store.OpenSelectedRootIngressAsync(WorkflowActor.WorkflowActor.ActorType, TimeSpan.FromHours(1),
            (bound, _) => ReferenceEquals(bound, scope), stop, (bound, _) => ReferenceEquals(bound, scope));
        await store.EnableRootSelfSendsAsync(stop);
        await store.EnableRootJobsAsync(stop);
        await store.EnableRootChildrenAsync(stop);
        store.BindWorkerNode();
        var waiting = await StartAsync("waits", "waits-1");
        var waitingActor = (await clock.ResolveAsync(scope.Tenant, waiting.Id, stop))!.Id;
        Require((await store.LoadAsync([waitingActor, waitingActor], stop)).Count == 0,
            "a batched PostgreSQL worker load recognizes a pending life without a checkpoint");
        try
        {
            await store.LoadAsync([waitingActor, InstanceRef.In(0, "unadmitted").Id], stop);
            throw new InvalidOperationException("A batched PostgreSQL worker load accepted an actor without a birth.");
        }
        catch (NotSupportedException)
        {
            Require(true, "a batched PostgreSQL worker load refuses an actor without a birth");
        }
        var neighbor = await StartAsync("waits", "seen-neighbor");
        var neighborActor = (await clock.ResolveAsync(scope.Tenant, neighbor.Id, stop))!.Id;
        await CheckCreatorWitnessRollbackAsync(Open, store, scope.Tenant, waiting.Id, waitingActor, stop);
        await CheckMarkNamespaceReplacementAsync(Open, store, markSwap, waitingActor, neighborActor, stop);
        await CheckRootTrialTakeoverAsync(Open, store, waitingActor, neighborActor, stop);
        var escalated = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var service = new PostgresPreviewHostedService(Open, Node, actor, message => escalated.TrySetResult(message),
            new() { PollInterval = TimeSpan.FromMilliseconds(20) }, requests: foundation.Requests);
        await service.StartAsync(stop);
        Contract.ActorRef retainedAcrossExtension;
        try
        {
            // A held start answered from its own turn; the job it runs afterwards settles and the root finishes.
            var accepted = await foundation.RequestAsync(scope, new Contract.Target.Start(Start("answer-then-fetch")), "answer-1",
                Contract.Kinds.Start, null, null, new(null, await clock.ReadReplyTimeAsync(stop) + RequestTimeout), stop);
            Require(accepted is { Error: null, Channel: "response", Item: JsonNode item } && Values.To<HttpResponse>(item) is { StatusCode: 202 },
                "a held start is answered with the response its first turn writes");
            var answered = await RootOfAsync("answer-then-fetch", "answer-1");
            var fetched = await SettledAsync(answered);
            Require(fetched is { Status: InstanceStatus.Finished } && Variable(fetched, "outcome") == "200" && partner.Calls("/ok") == 1,
                "its SendHttpRequest job runs after the answer, settles, and the root commits Finished");
            var retention = (Contract.IActorRetentionReads)foundation;
            var keep = new Contract.ActorRetentionWindows();
            var expire = new Contract.ActorRetentionWindows(Finished: TimeSpan.Zero);
            var terminalAt = fetched!.TerminalAt!.Value;
            var definition = (await foundation.GetAsync(scope, answered, stop))!.Header.Labels["definition"];
            var quote = await retention.ReadRetentionQuoteAsync(scope, WorkflowActor.WorkflowActor.ActorType, keep, expire,
                new("definition", definition), cancellationToken: stop);
            Require(quote.NewlyEligible == 1 && quote.AsOf >= terminalAt,
                "the PostgreSQL retention quote counts the exact projected terminal definition");
            Require(await retention.ReadRetentionQuoteAsync(scope, WorkflowActor.WorkflowActor.ActorType,
                expire with { Finished = TimeSpan.FromTicks(1) }, expire, asOf: terminalAt, cancellationToken: stop) ==
                new Contract.ActorRetentionQuote(1, terminalAt) &&
                await retention.ReadRetentionQuoteAsync(scope, WorkflowActor.WorkflowActor.ActorType, expire, keep, cancellationToken: stop) is { NewlyEligible: 0 },
                "the PostgreSQL retention quote compares both windows at one clock and excludes extensions");
            Require(await retention.ReadRetentionQuoteAsync(ContractScopes.Bind("other-retention-tenant"), WorkflowActor.WorkflowActor.ActorType,
                keep, expire, cancellationToken: stop) is { NewlyEligible: 0 } &&
                await retention.ReadRetentionQuoteAsync(scope, "WORKFLOW", keep, expire, cancellationToken: stop) is { NewlyEligible: 0 },
                "the PostgreSQL quote returns zero for an unmatched tenant or ordinal actor type");
            await RetentionPolicyChecks.CheckAsync(foundation, scope, ContractScopes.Bind("other-retention-tenant"), Open, terminalAt, stop);
            var fetchedActor = fetched!.Actor.Id;
            await using (var guard = Open())
                await PostgresRootRetirementChecks.CheckRetirementFootprintAsync(guard, fetchedActor, stop);

            // A timed job that passes its deadline settles on the Timeout outcome.
            var stuck = (await foundation.SendAsync(scope, new Contract.Target.Start(Start("stuck")), "stuck-1", Contract.Kinds.Start,
                cancellationToken: stop)).Actor!.Value;
            var timedOut = await SettledAsync(stuck);
            retainedAcrossExtension = stuck;
            Require(timedOut is { Status: InstanceStatus.Finished } && Variable(timedOut, "outcome") == SendHttpRequest.TimeoutOutcome,
                "a timed job that passes its deadline settles on Timeout and the root finishes");


            // A Terminate of a root whose timed job is running ends it Cancelled with nothing of it left pending; a Delete
            // then retires it.
            partner.Reset();
            await TerminateAndDeleteAsync("long-fetch", "long-fetch-1", "a root whose timed SendHttpRequest job is running",
                async () => { while (partner.Calls("/stuck") == 0) await Task.Delay(20, stop); });


            // A selected Terminate of a running root.
            await RunningAsync(waiting);
            Require((await store.LoadAsync([waitingActor, fetchedActor, waitingActor], stop)) is
                [{ Status: InstanceStatus.Running } live, { Status: InstanceStatus.Finished } ended] &&
                live.Id == waitingActor && live.FoundationDispatchHeader?.Reference == waiting.Id &&
                ended.Id == fetchedActor && ended.FoundationDispatchHeader?.Reference == answered.Id,
                "one batched PostgreSQL worker load restores exact checkpoints in distinct request order");
            var running = (await foundation.GetAsync(scope, waiting, stop))!;
            Require((await operators.SendAsync(scope, new Contract.Target.Ref(waiting), "terminate-1", Contract.Kinds.Terminate,
                    JsonSerializer.Serialize(new Contract.OperatorCommand(running.Selection)), cancellationToken: stop)).Outcome ==
                Contract.AdmissionOutcome.Accepted, "a selected Terminate of the running root is admitted");
            Require(await SettledAsync(waiting) is { Status: InstanceStatus.Cancelled }, "the node ends the terminated root Cancelled");

            // A selected Delete of a finished root retires it, and its history row goes with it.
            var finished = (await foundation.GetAsync(scope, answered, stop))!;
            Require(await HistoryRowsAsync(answered) == 1, "workflow history lists the finished root");
            Require((await operators.SendAsync(scope, new Contract.Target.Ref(answered), "delete-1", Contract.Kinds.Delete,
                    JsonSerializer.Serialize(new Contract.OperatorCommand(finished.Selection)), cancellationToken: stop)).Outcome ==
                Contract.AdmissionOutcome.Accepted, "a selected Delete of the finished root is admitted");
            for (var attempt = 0; await store.ReadFirstRootAsync(scope.Tenant, answered.Id, stop) is not null; attempt++)
            {
                if (attempt == 250) throw new InvalidOperationException("The deleted root was not retired.");
                await Task.Delay(20, stop);
            }
            Require(await HistoryRowsAsync(answered) == 0 && await foundation.GetAsync(scope, answered, stop) is null,
                "the node retires the deleted root, and its history row goes with it");
            Require((await store.LoadAsync([fetchedActor], stop)).Count == 0,
                "a retired PostgreSQL life retains its birth but has no worker checkpoint");

            Require(!escalated.Task.IsCompleted && service.LastError is null, "the node ran on the shipped format without a fault");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
        var retainedImage = (await store.ReadFirstRootAsync(scope.Tenant, retainedAcrossExtension.Id, stop))!;
        var selection = (await foundation.GetAsync(scope, retainedAcrossExtension, stop))!.Selection!;
        await RetentionPolicyChecks.CheckPendingAcrossExtensionAsync(foundation, scope, retainedImage, Open,
            () => foundation.AdmitRetentionDeleteAsync(scope, retainedAcrossExtension, selection, stop),
            freshOwner: async check =>
            {
                const string successor = "shipped-retention-successor";
                var replies = PostgresPreviewHosting.CreateRequests(Open, successor, WorkflowActor.WorkflowActor.ActorType);
                await using var restart = await PostgresPreviewHosting.StartAsync(Open, successor, actor,
                    error => throw new InvalidOperationException(error), new() { PollInterval = TimeSpan.FromMilliseconds(20) }, stop,
                    onFault: null, replies, fenceInBatch: inBatch, retentionWindow: static _ => TimeSpan.Zero);
                await check();
                await restart.StopAsync(stop);
            }, extended: new(), stop);
        await CheckQueuedCreatorRecoveryAsync(Open, store, inBatch, stop);
        return 0;

        async Task<Contract.ActorRef> StartAsync(string workflow, string id) =>
            (await foundation.SendAsync(scope, new Contract.Target.Start(Start(workflow)), id, Contract.Kinds.Start, cancellationToken: stop)).Actor!.Value;

        async Task TerminateAndDeleteAsync(string workflow, string id, string what, Func<Task>? ready = null)
        {
            var root = await StartAsync(workflow, id);
            var running = await RunningImageAsync(root);
            if (ready is not null) await ready();
            var view = (await foundation.GetAsync(scope, root, stop))!;
            Require((await operators.SendAsync(scope, new Contract.Target.Ref(root), "terminate-" + id, Contract.Kinds.Terminate,
                    JsonSerializer.Serialize(new Contract.OperatorCommand(view.Selection)), cancellationToken: stop)).Outcome ==
                Contract.AdmissionOutcome.Accepted, $"a selected Terminate of {what} is admitted");
            var ended = await SettledAsync(root);
            Require(ended is { Status: InstanceStatus.Cancelled } && await PendingAsync(running.Actor.Id) == 0,
                $"the Terminate ends {what} Cancelled with nothing of it pending ({ended?.Status}, {await PendingAsync(running.Actor.Id)} pending)");
            var cancelled = (await foundation.GetAsync(scope, root, stop))!;
            Require((await operators.SendAsync(scope, new Contract.Target.Ref(root), "delete-" + id, Contract.Kinds.Delete,
                    JsonSerializer.Serialize(new Contract.OperatorCommand(cancelled.Selection)), cancellationToken: stop)).Outcome ==
                Contract.AdmissionOutcome.Accepted, $"a selected Delete of {what}, terminated, is admitted");
            for (var attempt = 0; await store.ReadFirstRootAsync(scope.Tenant, root.Id, stop) is not null; attempt++)
            {
                if (attempt == 250) throw new InvalidOperationException($"The deleted {what} was not retired.");
                await Task.Delay(20, stop);
            }
            Require(true, $"the Delete retires {what}");
        }

        async Task<EfStore.RootImage> RunningImageAsync(Contract.ActorRef root)
        {
            for (var attempt = 0; ; attempt++)
            {
                if (await store.ReadFirstRootAsync(scope.Tenant, root.Id, stop) is { Status: InstanceStatus.Running } image) return image;
                if (attempt == 250) throw new InvalidOperationException("The root did not commit Running.");
                await Task.Delay(20, stop);
            }
        }

        async Task<int> PendingAsync(string actorId)
        {
            await using var db = Open();
            return await db.Database.SqlQuery<int>($"""
                SELECT count(*)::integer AS "Value" FROM "Messages" WHERE "Target" = {actorId} AND NOT "Done"
                """).SingleAsync(stop);
        }

        // A start's repeat names the root its operation started.
        async Task<Contract.ActorRef> RootOfAsync(string workflow, string id) =>
            (await foundation.SendAsync(scope, new Contract.Target.Start(Start(workflow)), id, Contract.Kinds.Start, cancellationToken: stop)) is
                { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: { } root } ? root : throw new InvalidOperationException($"'{id}' started no root.");

        async Task<EfStore.RootImage?> SettledAsync(Contract.ActorRef root)
        {
            for (var attempt = 0; attempt < 500; attempt++)
            {
                if (await store.ReadFirstRootAsync(scope.Tenant, root.Id, stop) is { Status: not InstanceStatus.Running } done) return done;
                await Task.Delay(20, stop);
            }
            return await store.ReadFirstRootAsync(scope.Tenant, root.Id, stop);
        }

        async Task RunningAsync(Contract.ActorRef root)
        {
            for (var attempt = 0; await store.ReadFirstRootAsync(scope.Tenant, root.Id, stop) is not { Status: InstanceStatus.Running }; attempt++)
            {
                if (attempt == 250) throw new InvalidOperationException("The waiting root did not commit.");
                await Task.Delay(20, stop);
            }
        }

        async Task<int> HistoryRowsAsync(Contract.ActorRef root)
        {
            await using var db = Open();
            return await db.Database.SqlQuery<int>($"""
                SELECT count(*)::integer AS "Value" FROM "FoundationWorkflowHistory" WHERE "Tenant" = {scope.Tenant} AND "Reference" = {root.Id}
                """).SingleAsync(stop);
        }
    }

    private static async Task CheckQueuedCreatorRecoveryAsync(Func<KernelDb> open, EfStore store, bool inBatch, CancellationToken token)
    {
        var actor = new QueuedCreatorActor();
        var scope = EmbeddedTenant.Bind("shipped");
        const string node = "queued-creator-owner";
        var requests = PostgresPreviewHosting.CreateRequests(open, node, actor.Type);
        var foundation = await PostgresPreviewHosting.OpenFoundationWithOperatorsAsync(open, actor.Type,
            (bound, _, kind) => ReferenceEquals(bound, scope) && kind == Contract.Kinds.Recover, requests, cancellationToken: token);
        await using var owner = await PostgresPreviewHosting.StartAsync(open, node, actor,
            reason => throw new InvalidOperationException(reason), new LoopOptions { PollInterval = TimeSpan.FromMilliseconds(20) },
            token, onFault: null, replies: requests, fenceInBatch: inBatch);

        async Task<Contract.Admission> StartAsync(string id) =>
            await foundation.SendAsync(scope, new Contract.Target.Start(new(actor.Type, id)), id, Contract.Kinds.Start, cancellationToken: token);
        var started = await StartAsync("queued-start");
        Require(started is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null },
            "the shipped provider admits the queued creator");
        var actorRef = started.Actor!.Value;
        var parked = await WaitForRevisionAsync(actorRef, 1);
        Require(parked is { Status: InstanceStatus.Faulted, ParkAt: not null, State: null },
            "the ordinary creator faults before its first state");
        var selection = (await foundation.GetAsync(scope, actorRef, token))!.Selection;
        Require(await foundation.SendAsync(scope, new Contract.Target.Ref(actorRef), "recover-queued-creator", Contract.Kinds.Recover,
            JsonSerializer.Serialize(new Contract.OperatorCommand(selection)), cancellationToken: token) is { Outcome: Contract.AdmissionOutcome.Accepted },
            "the exact queued creator Park admits Recover");
        Require(await WaitForRevisionAsync(actorRef, 2) is { Status: InstanceStatus.Running, ParkAt: null },
            "the queued creator uses the actor's recovery plan in the shared inline-capable format");
        await using var db = open();
        Require(!await db.Messages.AnyAsync(row => row.Target == parked.Actor.Id && row.InboxScope == 0, token) &&
            await StartAsync("queued-start") is { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: var duplicate } && duplicate == actorRef,
            "queued recovery clears the creator while retaining the original receipt");

        var nullableStart = await StartAsync("save-null");
        var nullable = nullableStart.Actor!.Value;
        Require(await WaitForRevisionAsync(nullable, 1) is { Status: InstanceStatus.Running, State: null },
            "the ordinary creator can commit a null state before later inputs");
        Require(await foundation.SendAsync(scope, new Contract.Target.Ref(nullable), "fault-later", "fail", cancellationToken: token) is
            { Outcome: Contract.AdmissionOutcome.Accepted }, "a later input is admitted against the null checkpoint");
        Require(await WaitForRevisionAsync(nullable, 2) is { Status: InstanceStatus.Faulted, ParkAt: not null, State: null },
            "a later failure can park the same null checkpoint");
        var later = (await foundation.GetAsync(scope, nullable, token))!.Selection;
        Require(await foundation.SendAsync(scope, new Contract.Target.Ref(nullable), "recover-later", Contract.Kinds.Recover,
            JsonSerializer.Serialize(new Contract.OperatorCommand(later)), cancellationToken: token) is { Outcome: Contract.AdmissionOutcome.Accepted },
            "the later null-state Park admits Recover");
        Require(await WaitForRevisionAsync(nullable, 3) is { Status: InstanceStatus.Running, ParkAt: null },
            "a later null checkpoint uses its ordinary recovery plan");

        async Task<EfStore.RootImage> WaitForRevisionAsync(Contract.ActorRef reference, long revision)
        {
            while (true)
            {
                if (await store.ReadFirstRootAsync(scope.Tenant, reference.Id, token) is { } row && row.Revision >= revision)
                    return row;
                await Task.Delay(20, token);
            }
        }
    }

    private sealed class QueuedCreatorActor : Contract.IActorType
    {
        public string Type => "queued-creator-fault";
        public bool Withholds(Contract.ActorHeader header) => false;
        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            if (turn.Header.TypeData == "save-null" && turn.Inbox is [{ Kind: Contract.Kinds.Start }])
                return ValueTask.CompletedTask;
            throw new InvalidOperationException("The queued fixture faults without changing state.");
        }
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The queued fixture has no jobs.");
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }

    // These fingerprints pin the PostgreSQL definitions revision. A deliberate definition change advances its revision.
    private static async Task CheckGuardBodyParityAsync(Func<KernelDb> open, CancellationToken token)
    {
        await using var db = open();
        var sql = EfStore.RenderPostgresFirstRootGuards(db).ReplaceLineEndings("\n");
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
        Require(fingerprint == "b0b5889fd66fe781935b9364e934b8a7c0c0d7f9b3e5f9a1f0f42d05c43b8943",
            $"the complete emitted PostgreSQL guard installation batch matches its revision ({fingerprint})");
        var bodies = await db.Database.SqlQueryRaw<string>("""
            SELECT md5(p.prosrc) AS "Value"
            FROM pg_catalog.pg_proc p
            WHERE p.pronamespace = pg_catalog.current_schema()::regnamespace
                AND p.proname IN ('kernel_preserve_parked_live_job', 'kernel_project_root_park_metadata', 'kernel_require_root_checkpoint_writer',
                    'kernel_require_root_journal_revision', 'kernel_require_root_outcome_checkpoint')
            ORDER BY p.proname
            """).ToListAsync(token);
        Require(bodies is ["631edb89241ac99b9413e1101d7eea78", "e7baf78be308c2709f13bfda3a6c4a1e",
                "bcd6ab56c79da3232833fa96c6ddeb76", "32455ab166ed6b45a9ecf861da7d4d74", "dc1bee032b85280076623ff9c5561d04"],
            $"the installed PostgreSQL Park job protection, projector, checkpoint, journal and deferred outcome functions match their revision ({string.Join(", ", bodies)})");
    }

    private static async Task CheckDefinitionRevisionAsync(Func<KernelDb> open, EfStore store, CancellationToken token)
    {
        await using var db = open();
        var expected = StoreDefinitionsRevision.Seed(db)!;
        var marker = db.StoreVersions.Where(row => row.Id == expected.Id);
        Require(await marker.ExecuteDeleteAsync(token) == 1, "new PostgreSQL installs have a definitions marker");
        await RequireMarkerRefusedAsync(db, "PostgreSQL treats an absent marker as legacy revision 1 and refuses it after the revision advances", token);
        db.StoreVersions.Add(expected);
        await db.SaveChangesAsync(token);
        await marker.ExecuteUpdateAsync(row => row.SetProperty(x => x.MessageProtocol, expected.MessageProtocol - 1), token);
        await RequireMarkerRefusedAsync(db, "PostgreSQL refuses the previous install-only definitions revision", token);
        await marker.ExecuteUpdateAsync(row => row.SetProperty(x => x.MessageProtocol, expected.MessageProtocol + 1), token);
        try
        {
            await RequireMarkerRefusedAsync(db, "PostgreSQL refuses an unsupported definitions revision", token);
            await RefusesRevisionAsync(() => store.PartitionCountAsync(token).AsTask());
            await RefusesRevisionAsync(() => store.ReadRootActorAsync("tenant", "missing", token));
            await RefusesRevisionAsync(() => store.ReadRootActorListAsync("tenant", "", null, null, [], null, null, 1, token));
            await RefusesRevisionAsync(() => store.ReadRootRetentionQuoteAsync("tenant", "workflow", new(), new(), null, null, token));
            await RefusesRevisionAsync(() => store.ReadRootRetentionPolicyAsync(ContractScopes.Bind("tenant"), "workflow", token));
            await RefusesRevisionAsync(() => store.TryApplyRootRetentionPolicyAsync(ContractScopes.Bind("tenant"), "workflow", 0, new(), 0, confirmation: null, token));
            await RefusesRevisionAsync(() => store.ReadRootJournalAsync("tenant", "missing", 1, 1, token));
            await RefusesRevisionAsync(() => store.ReadRootInboxAsync("tenant", "missing", 0, "", 1, token));
            await RefusesRevisionAsync(() => store.ReadRootWorkflowHistoryAsync("tenant", "type", null, null, null, null, null, null, 1, token));
            Require(true, "the owner and all shipped snapshot readers reject a mismatched definitions revision");
        }
        finally
        {
            await marker.ExecuteUpdateAsync(row => row.SetProperty(x => x.MessageProtocol, expected.MessageProtocol), token);
        }

        // An operation that waits for the shared marker must see the revision committed by the locker.
        await using var advancing = open();
        await using var advance = await advancing.Database.BeginTransactionAsync(token);
        await advancing.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT "Id" FROM "KernelStoreVersions" WHERE "Id" = {StoreVersionEntity.MarkerId} FOR UPDATE
            """, token);
        await advancing.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "KernelStoreVersions" SET "MessageProtocol" = {expected.MessageProtocol + 1}
            WHERE "Id" = {expected.Id}
            """, token);
        var blocker = ((NpgsqlConnection)advancing.Database.GetDbConnection()).ProcessID;
        var waiting = store.PartitionCountAsync(token).AsTask();
        while (!waiting.IsCompleted && !await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM pg_stat_activity
                WHERE datname = current_database() AND {blocker} = ANY(pg_blocking_pids(pid))) AS "Value"
            """).SingleAsync(token))
            await Task.Delay(20, token);
        Require(!waiting.IsCompleted, "the operation waits for the shared PostgreSQL marker lock");
        await advance.CommitAsync(token);
        try
        {
            await RefusesRevisionAsync(() => waiting);
            Require(true, "the operation sees the new definitions revision after its marker lock wait");
        }
        finally
        {
            await marker.ExecuteUpdateAsync(row => row.SetProperty(x => x.MessageProtocol, expected.MessageProtocol), token);
        }

        static async Task RefusesRevisionAsync(Func<Task> query)
        {
            try { await query(); }
            catch (StoreFormatException) { return; }
            throw new InvalidOperationException("The shipped query accepted a mismatched definitions revision.");
        }
    }

    private static async Task CheckNamespaceReplacementAsync(Func<KernelDb> open, string connection, CancellationToken token)
    {
        await using var observer = open();
        await CreateNamespaceSwapAsync(observer, token);
        try
        {
            var settings = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(new NpgsqlConnectionStringBuilder(connection)
                { SearchPath = "pin_swap" }.ConnectionString).Options;
            await using var passing = PostgresPreviewHosting.CreateShippedContext(settings);
            await using var advancing = open();
            await using var advance = await advancing.Database.BeginTransactionAsync(token);
            await advancing.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT 1 FROM pin_swap."KernelStoreVersions" WHERE "Id" = {StoreVersionEntity.MarkerId} FOR UPDATE
                """, token);
            await using var reader = await passing.Database.BeginTransactionAsync(token);
            var blocker = ((NpgsqlConnection)advancing.Database.GetDbConnection()).ProcessID;
            var waiting = PostgresRootAdmission.PinRootNamespaceAsync(passing, token);
            while (!waiting.IsCompleted && !await observer.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM pg_stat_activity
                    WHERE datname = current_database() AND {blocker} = ANY(pg_blocking_pids(pid))) AS "Value"
                """).SingleAsync(token))
                await Task.Delay(20, token);
            Require(!waiting.IsCompleted, "the pin has resolved the original marker and is waiting for its lock");
            await advancing.Database.ExecuteSqlRawAsync(SwapNamespaceSql, token);
            await advance.CommitAsync(token);
            try
            {
                await waiting;
                throw new InvalidOperationException("The pin accepted the replacement marker after locking the original.");
            }
            catch (StoreFormatException error) when (error.Message == "The kernel store version marker changed its physical table.")
            {
                Require(true, "the pin refuses a same-name replacement schema with compatible marker values");
            }
        }
        finally
        {
            await DropNamespaceSwapAsync(observer, token);
        }
    }

    private const string SwapNamespaceSql = """
        ALTER SCHEMA pin_swap RENAME TO pin_departed;
        ALTER SCHEMA pin_replacement RENAME TO pin_swap;
        """;

    private static async Task CreateNamespaceSwapAsync(KernelDb observer, CancellationToken token)
    {
        await using var transaction = await observer.Database.BeginTransactionAsync(token);
        await observer.Database.ExecuteSqlRawAsync("""
            DO $fixture$
            DECLARE source text; relation text;
            BEGIN
                FOREACH source IN ARRAY ARRAY['pin_swap', 'pin_replacement'] LOOP
                    EXECUTE format('CREATE SCHEMA %I', source);
                    EXECUTE format('CREATE TABLE %I."KernelStoreVersions" (LIKE "KernelStoreVersions" INCLUDING ALL)', source);
                    EXECUTE format('INSERT INTO %I."KernelStoreVersions" SELECT * FROM "KernelStoreVersions"', source);
                    FOREACH relation IN ARRAY ARRAY['FoundationRootIssuer', 'FoundationRootBirths', 'Instances', 'Messages',
                        'Partitions', 'Nodes', 'Journal', 'Subscriptions', 'FoundationDirectReceipts', 'FoundationActorList',
                        'FoundationRootReceipts', 'Events', 'FoundationOperatorReceipts', 'FoundationRootChildCascades'] LOOP
                        EXECUTE format('CREATE TABLE %I.%I (LIKE %I INCLUDING ALL)', source, relation, relation);
                    END LOOP;
                    EXECUTE format('INSERT INTO %I."FoundationRootIssuer" SELECT * FROM "FoundationRootIssuer"', source);
                END LOOP;
            END
            $fixture$;
            """, token);
        if (observer.UsesRootScheduleLineage)
        {
            foreach (var schema in new[] { "pin_swap", "pin_replacement" })
            {
                await observer.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_catalog.set_config('search_path', {schema}, true)", token);
                await RootScheduleLineage.InstallPostgresAsync(observer, token);
            }
        }
        await transaction.CommitAsync(token);
    }

    private static Task DropNamespaceSwapAsync(KernelDb observer, CancellationToken token) =>
        observer.Database.ExecuteSqlRawAsync("""
            DROP SCHEMA IF EXISTS pin_departed CASCADE;
            DROP SCHEMA IF EXISTS pin_swap CASCADE;
            DROP SCHEMA IF EXISTS pin_replacement CASCADE;
            """, token);

    private static async Task CheckStartNamespaceReplacementAsync(Func<KernelDb> open, string connection, CancellationToken token)
    {
        var cases = new[] { ("small start", 0, 1), ("large receipt claim", 9_000, 1), ("large creator continuation", 9_000, 2) };
        foreach (var (name, size, write) in cases)
        {
            await using var observer = open();
            await CreateNamespaceSwapAsync(observer, token);
            try
            {
                // The sequence is a nontransactional witness. A write reaching a replacement relation
                // would advance it even if the client subsequently rolled its transaction back.
                await observer.Database.ExecuteSqlRawAsync("""
                    UPDATE pin_replacement."FoundationRootIssuer" SET "Key" = decode(repeat('ab', 32), 'hex');
                    CREATE SEQUENCE pin_replacement.start_write_witness;
                    CREATE FUNCTION pin_replacement.record_start_write() RETURNS trigger LANGUAGE plpgsql AS $fixture$
                    BEGIN
                        PERFORM nextval(format('%I.start_write_witness', TG_TABLE_SCHEMA)::regclass);
                        RETURN NEW;
                    END
                    $fixture$;
                    CREATE TRIGGER witness AFTER INSERT OR UPDATE ON pin_replacement."FoundationRootReceipts"
                        FOR EACH ROW EXECUTE FUNCTION pin_replacement.record_start_write();
                    CREATE TRIGGER witness AFTER INSERT OR UPDATE ON pin_replacement."FoundationRootBirths"
                        FOR EACH ROW EXECUTE FUNCTION pin_replacement.record_start_write();
                    CREATE TRIGGER witness AFTER INSERT OR UPDATE ON pin_replacement."Messages"
                        FOR EACH ROW EXECUTE FUNCTION pin_replacement.record_start_write();
                    """, token);
                var swap = new SwapBeforeStartWrite(observer, write);
                var settings = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(new NpgsqlConnectionStringBuilder(connection)
                    { SearchPath = "pin_swap" }.ConnectionString).AddInterceptors(swap).Options;
                var admission = new PostgresRootAdmission(() => PostgresPreviewHosting.CreateShippedContext(settings),
                    TimeSpan.FromHours(1), lockOrdinaryWrites: true);
                var spec = new StartSpec("native", "waits", size == 0 ? null : Json.Write(new string('a', size)));
                try
                {
                    await admission.AdmitAsync("pin-swap", "start", spec, cancellationToken: token);
                    throw new InvalidOperationException("Startup accepted the replacement schema after the pin returned.");
                }
                catch (StoreFormatException error) when (error.Message == "The PostgreSQL root's pinned startup source changed before the write.")
                {
                    Require(swap.Swapped && await observer.Database.SqlQueryRaw<bool>("""
                        SELECT NOT (SELECT is_called FROM pin_swap.start_write_witness)
                            AND NOT EXISTS (SELECT 1 FROM pin_swap."FoundationRootReceipts")
                            AND NOT EXISTS (SELECT 1 FROM pin_swap."FoundationRootBirths")
                            AND NOT EXISTS (SELECT 1 FROM pin_swap."Messages")
                            AND NOT EXISTS (SELECT 1 FROM pin_departed."FoundationRootReceipts")
                            AS "Value"
                        """).SingleAsync(token), $"the {name} refuses the post-pin replacement without writing there or retaining an earlier claim");
                }
                if (write == 2)
                {
                    var started = await admission.AdmitAsync("pin-swap", "start", spec, cancellationToken: token);
                    var replayed = await admission.AdmitAsync("pin-swap", "start", spec, cancellationToken: token);
                    Require(!started.Duplicate && replayed.Duplicate && started.Reference == replayed.Reference,
                        "the guarded large startup claims, publishes and replays on its current pinned source");
                }
            }
            finally
            {
                await DropNamespaceSwapAsync(observer, token);
            }
        }
    }

    private sealed class SwapBeforeStartWrite(KernelDb observer, int atWrite) : DbCommandInterceptor
    {
        private int _writes;
        internal bool Swapped { get; private set; }

        private async Task SwapIfSelectedAsync(DbCommand command, CancellationToken token)
        {
            if (!command.CommandText.Contains("""INSERT INTO "FoundationRootReceipts" AS r""", StringComparison.Ordinal) || ++_writes != atWrite)
                return;
            await observer.Database.ExecuteSqlRawAsync(SwapNamespaceSql, token);
            Swapped = true;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await SwapIfSelectedAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await SwapIfSelectedAsync(command, cancellationToken);
            return result;
        }
    }

    private static async Task CheckCreatorWitnessRollbackAsync(Func<KernelDb> open, EfStore store, string tenant,
        string reference, string actor, CancellationToken token)
    {
        var leaseTime = TimeSpan.FromSeconds(15);
        var partition = new InstanceRef(actor).Partition;
        var lease = await store.TakeAsync(partition, "creator-witness-owner", leaseTime, token)
            ?? throw new InvalidOperationException("The creator-witness fixture could not take its partition.");
        IOwnerPollLookups ownerRead = store;
        var projected = await ownerRead.PollLookupsAsync([lease.For(partition)], TimeSpan.Zero, 8, _ => [actor], token);
        Require(projected.Lookups is [{ Row: null } lookup] && lookup.Requested == actor && lookup.Canonical == actor,
            "the pinned owner poll projects exact absence for a pending birth");
        var failed = await ownerRead.PollLookupsAsync([lease.For(partition)], TimeSpan.Zero, 8,
            _ => [actor, InstanceRef.In(partition, "unadmitted").Id], token);
        Require(failed.Lookups is null && failed.Poll.Messages.Select(row => (row.Seq, row.Message)).SequenceEqual(projected.Poll.Messages.Select(row => (row.Seq, row.Message))),
            "an unknown speculative lookup discards its projection and preserves the inbox page");
        var admitted = projected.Poll.Messages.Single(item => item.Message.Target.Id == actor);
        var creator = admitted.Message;
        Require(admitted.RootCreator is { } witness && ReferenceEquals(witness.Envelope, creator),
            "the PostgreSQL poll issues a witness for its exact admitted creator");

        var start = creator.Start!;
        var header = new FoundationHeaderChange(start.Definition, new Dictionary<string, string>());
        var row = new InstanceRow(actor, start.Model, start.Definition, InstanceStatus.Running, "null", 1, null,
            History: start.History, StateVersion: 1, Creation: 1, StartIntent: CreationProtocol.Fingerprint(start, creator.Kind, creator.Payload))
        {
            NextJobGeneration = 0,
            FoundationDispatchHeader = new(tenant, reference, header)
        };
        var turn = new TurnCommit(actor, row, null, [creator.InboxKey], [], [], [], [], "[]")
            { FoundationHeader = header, RootCreator = admitted.RootCreator };
        var batch = new CommitBatch(partition, lease.Epoch, lease.Position, leaseTime, false, [turn], [], []);

        // The birth fingerprint omits the reply route. Drift after the poll must reach the full envelope comparison.
        await using var db = open();
        var source = db.Messages.Where(message => message.Partition == partition && message.Target == actor && message.InboxScope == 0);
        var changedRoute = ReplyTo.Of("creator-witness-owner", "changed-registration", long.MaxValue);
        Require(await source.ExecuteUpdateAsync(update => update.SetProperty(message => message.ReplyTo, changedRoute), token) == 1,
            "the creator's persisted route changes after the poll");
        var refused = await store.CommitAsync(batch, token);
        Require(!refused.Landed &&
            await source.AnyAsync(message => message.Id == creator.InboxKey.Id && !message.Done && message.ReplyTo == changedRoute, token) &&
            !await db.Instances.AnyAsync(instance => instance.Id == actor, token) &&
            !await db.Journal.AnyAsync(entry => entry.Instance == actor, token) &&
            await db.Partitions.Where(part => part.Id == partition).Select(part => part.Position).SingleAsync(token) == lease.Position,
            "a drifted polled creator rolls back the checkpoint, journal, message deletion and fence position");
        await source.ExecuteUpdateAsync(update => update.SetProperty(message => message.ReplyTo, creator.ReplyTo), token);
        await store.ReleaseAsync(partition, lease.Epoch, token);
    }

    private static async Task CheckMarkNamespaceReplacementAsync(Func<KernelDb> open, EfStore store,
        SwapBeforeSecondMark swap, string actor, string neighbor, CancellationToken token)
    {
        var partition = new InstanceRef(actor).Partition;
        var span = TimeSpan.FromSeconds(15);
        var lease = await store.TakeAsync(partition, "mark-source-owner", span, token)
            ?? throw new InvalidOperationException("The mark source fixture could not take its partition.");
        await using var observer = open();
        var original = await observer.Database.SqlQueryRaw<string>("""SELECT pg_catalog.current_schema() AS "Value" """).SingleAsync(token);
        var quoted = new NpgsqlCommandBuilder().QuoteIdentifier(original);
        await CreateNamespaceSwapAsync(observer, token);
        try
        {
            // Leave exact matching messages in the replacement so an unguarded update would write there.
            await observer.Database.ExecuteSqlRawAsync("""
                INSERT INTO pin_replacement."Messages" OVERRIDING SYSTEM VALUE SELECT * FROM "Messages";
                CREATE SEQUENCE pin_replacement.mark_write_witness;
                CREATE FUNCTION pin_replacement.record_mark_write() RETURNS trigger LANGUAGE plpgsql AS $fixture$
                BEGIN
                    PERFORM nextval(format('%I.mark_write_witness', TG_TABLE_SCHEMA)::regclass);
                    RETURN NEW;
                END
                $fixture$;
                CREATE TRIGGER witness AFTER UPDATE ON pin_replacement."Messages"
                    FOR EACH ROW EXECUTE FUNCTION pin_replacement.record_mark_write();
                """, token);
            swap.Replace = ct => observer.Database.ExecuteSqlRawAsync(
                "ALTER SCHEMA " + quoted + " RENAME TO pin_departed; ALTER SCHEMA pin_replacement RENAME TO " + quoted, ct);
            var batch = new CommitBatch(partition, lease.Epoch, lease.Position, span, false, [], [new("root:" + neighbor)], [new("root:" + actor)]);
            var refused = await store.CommitAsync(batch, token);
            Require(!refused.Landed && swap.FirstMarkWritten && swap.Swapped,
                "the mark batch refuses a physical source change after its first mark wrote");
            Require(await observer.Database.SqlQueryRaw<bool>("""
                SELECT NOT (SELECT is_called FROM mark_write_witness)
                    AND NOT EXISTS (SELECT 1 FROM "Messages" WHERE "Seen" <> 0 OR "Trial" <> 0)
                    AND NOT EXISTS (SELECT 1 FROM pin_departed."Messages" WHERE "Seen" <> 0 OR "Trial" <> 0) AS "Value"
                """).SingleAsync(token), "a source change rolls back the earlier mark and does not update a replacement message");
        }
        finally
        {
            swap.Replace = null;
            if (swap.Swapped)
                await observer.Database.ExecuteSqlRawAsync(
                    "ALTER SCHEMA " + quoted + " RENAME TO pin_replacement; ALTER SCHEMA pin_departed RENAME TO " + quoted, token);
            await DropNamespaceSwapAsync(observer, token);
            await store.ReleaseAsync(partition, lease.Epoch, token);
        }
    }

    private sealed class SwapBeforeSecondMark : DbCommandInterceptor
    {
        private int _marks;
        internal Func<CancellationToken, Task>? Replace { get; set; }
        internal bool FirstMarkWritten { get; private set; }
        internal bool Swapped { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Replace is { } replace && IsMark(command) && ++_marks == 2)
            {
                await replace(cancellationToken);
                Swapped = true;
            }
            return result;
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (Replace is not null && IsMark(command) && _marks == 1) FirstMarkWritten = result == 1;
            return ValueTask.FromResult(result);
        }

        private static bool IsMark(DbCommand command) => command.CommandText.StartsWith("""UPDATE "Messages" SET "Seen" """, StringComparison.Ordinal);
    }

    private static async Task CheckRootTrialTakeoverAsync(Func<KernelDb> open, EfStore store, string actor, string neighbor,
        CancellationToken token)
    {
        const string departing = "trial-mark-departing";
        var span = TimeSpan.FromSeconds(15);
        var partition = new InstanceRef(actor).Partition;
        var lease = await store.TakeAsync(partition, departing, span, token)
            ?? throw new InvalidOperationException("The trial fixture could not take its partition.");
        InboxKey trial = new("root:" + actor), seen = new("root:" + neighbor);
        var batch = new CommitBatch(partition, lease.Epoch, lease.Position, span, false, [], [seen], [trial]);
        Require((await store.CommitAsync(batch, token)).Landed, "the exact PostgreSQL owner durably marks one trial and its seen neighbor");
        await using var db = open();
        var marked = await ReadMarksAsync();
        Require(marked[actor].Trial == lease.Epoch && marked[actor].Seen == lease.Epoch && marked[actor].Attempts == 0 &&
            marked[neighbor].Seen == lease.Epoch && marked[neighbor].Trial == 0 && marked[neighbor].Attempts == 0,
            "the landed mark charges neither actor before an owner dies");
        var stalePosition = await store.CommitAsync(batch with { Position = lease.Position + 1, Trial = [seen], Seen = [] }, token);
        var afterStalePosition = await ReadMarksAsync();
        Require(!stalePosition.Landed && afterStalePosition[actor].Trial == lease.Epoch && afterStalePosition[neighbor].Trial == 0,
            "the mark-only fence rejects a stale position without marking a new Trial");
        await db.Nodes.Where(node => node.Id == departing).ExecuteUpdateAsync(update => update.SetProperty(node => node.LeaseUntil, 0), token);
        var successor = await store.TakeAsync(partition, "trial-mark-successor", span, token)
            ?? throw new InvalidOperationException("The trial fixture could not replace the expired owner.");
        Require(!(await store.CommitAsync(batch with { Seen = [], Trial = [seen] }, token)).Landed,
            "a stale PostgreSQL owner cannot put its neighbor on trial");
        var replaced = await ReadMarksAsync();
        Require(successor.Epoch > lease.Epoch && replaced[actor].Attempts == 1 &&
            replaced[neighbor].Trial == 0 && replaced[neighbor].Attempts == 0,
            "a cold owner counts the expired trial exactly once and leaves its seen neighbor alone");
        await store.ReleaseAsync(partition, successor.Epoch, token);

        Task<Dictionary<string, MessageEntity>> ReadMarksAsync() => db.Messages.AsNoTracking()
            .Where(row => row.InboxScope == 0 && (row.Target == actor || row.Target == neighbor))
            .ToDictionaryAsync(row => row.Target, token);
    }

    // Each preview format and the current shipped format refuse each other's marker. The preview
    // databases are recreated (card 3).
    private static async Task RefusalsAsync(string connection, DbContextOptions<KernelDb> shipped, CancellationToken stop)
    {
        foreach (var (name, schema, open) in new (string, string, Func<DbContextOptions<KernelDb>, KernelDb>)[]
        {
            ("ordinary 2", "refuse_2", options => new KernelDb(options)), ("Native jobs 6", "refuse_6", KernelDb.ForPostgresRootJobs),
            ("timed Native jobs 18", "refuse_18", KernelDb.ForPostgresRootTimedJobPreview),
            ("direct replies 10", "refuse_10", KernelDb.ForPostgresRootReplies),
            ("operator replies 17", "refuse_17", KernelDb.ForPostgresRootOperatorReplies),
            // S5b-3b: format 25 changed format 24's triggers and stored framing under the same capability set, so a database
            // installed as format 24 is recreated too.
            ("shipped 24", "refuse_24", options => KernelDb.ForUnshippedComposition(options, StoreFormat.Current with { MessageProtocol = 24 },
                RootProfiles.ParkCompositionBase)),
            ("shipped 25", "refuse_25", options => KernelDb.ForUnshippedComposition(options, StoreFormat.Current with { MessageProtocol = 25 },
                RootProfiles.ParkCompositionBase)),
            ("shipped 26", "refuse_26", options => KernelDb.ForUnshippedComposition(options, StoreFormat.Current with { MessageProtocol = 26 },
                RootProfiles.ParkRecoveryComposition)),
            ("shipped 27", "refuse_27", options => KernelDb.ForUnshippedComposition(options, StoreFormat.Current with { MessageProtocol = 27 },
                RootProfiles.TimerRearmQualification)),
            ("shipped 32", "refuse_32", options => KernelDb.ForUnshippedComposition(options, StoreFormat.Current with { MessageProtocol = 32 },
                RootProfiles.LiveEventQualification))
        })
        {
            await using (var older = open(shipped))
                await RequireMarkerRefusedAsync(older, $"the {name} profile refuses the current shipped marker", stop);
            // The older format's own database, in a schema of its own: the current shipped format refuses its marker.
            await using (var db = PostgresPreviewHosting.CreateShippedContext(shipped))
                await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA " + schema, stop);
            var olderOptions = new DbContextOptionsBuilder<KernelDb>()
                .UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString).Options;
            await using (var created = open(olderOptions))
                await created.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(stop);
            await using (var newer = PostgresPreviewHosting.CreateShippedContext(olderOptions))
                await RequireMarkerRefusedAsync(newer, $"the current shipped format refuses the {name} marker, so its database is recreated", stop);
        }
    }

    private static async Task RequireMarkerRefusedAsync(KernelDb db, string description, CancellationToken stop)
    {
        try { await EfStore.RequireStoreVersionsAsync(db, stop); }
        catch (StoreFormatException) { Console.WriteLine($"ok {description}"); return; }
        throw new InvalidOperationException(description);
    }

    private static Contract.StartSpec Start(string workflow) => WorkflowActor.WorkflowActor.Start(NativeExecutionModel.Start(workflow));

    private static string? Variable(EfStore.RootImage image, string name) =>
        image.State is { } state && Json.Read<NativeState>(state).Variables.GetValueOrDefault(name) is JsonValue value ? value.ToString() : null;

    // A send, then one variable assignment per outcome, which names the outcome it followed.
    private static Workflow Records(string id, Node send) => Wf.Workflow(id, Wf.Flowchart("flow",
        [send, .. Outcomes.Select(outcome => Wf.Set($"on-{outcome}", "outcome", outcome))],
        [.. Outcomes.Select(outcome => new Connection(send.Id, $"on-{outcome}", outcome))]));

    // The partner the jobs call: /ok answers, /stuck answers only when cancelled.
    private sealed class Partner : HttpMessageHandler
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> calls = new(StringComparer.Ordinal);

        internal int Calls(string path) => calls.GetValueOrDefault(path);

        internal void Reset() => calls.Clear();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            calls.AddOrUpdate(path, 1, (_, count) => count + 1);
            if (path == "/stuck") await Task.Delay(Timeout.Infinite, cancellationToken);
            return path == "/ok"
                ? new(HttpStatusCode.OK) { Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json") }
                : new(HttpStatusCode.NotFound);
        }
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }
}
