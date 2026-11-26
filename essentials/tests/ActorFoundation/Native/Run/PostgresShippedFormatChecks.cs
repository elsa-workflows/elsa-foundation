using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Runtime.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

// S7 (#4229): the shipped PostgreSQL format 25, installed and hosted as a host does: PostgresPreviewHosting's shipped
// context, InstallPreviewRootSchemaAsync, a Foundation with held requests and a PostgresPreviewHostedService answering
// through them. A held start is answered from its own turn and then runs a job; a timed job passes its deadline; a selected
// Terminate and Delete reach the roots, with workflow history beside them. The format 25 marker refuses the preview formats
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

    internal static async Task<int> RunAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        var stop = deadline.Token;
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Supply a fresh PostgreSQL preview database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => PostgresPreviewHosting.CreateShippedContext(options);
        await using (var db = Open())
        {
            Require(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "the shipped-format check uses an empty PostgreSQL database");
            Require(db.RequiredFormat == StoreFormat.RootShipped && db.RequiredFormat.MessageProtocol == 25 &&
                    db.Capabilities == RootProfiles.Shipped && RootProfiles.MissingPrerequisites(db.Capabilities) == RootCapabilities.None,
                "the shipped context selects format 25 with its closed capability set");
            Require(db.SupportsCorePreview && db.UsesRootReplyProtocol && db.UsesRootJobProtocol && db.UsesRootTimedJobSettlement &&
                    db.UsesRootTimedJobPreview && db.UsesRootTerminalSelection && db.UsesRootRetirement && db.UsesRootOperatorReplies &&
                    db.UsesRootParkInspection && db.SupportsRootWorkflowHistory,
                "format 25 carries held replies, timed jobs, selected operators with retirement, Park inspection and workflow history");
            Require(!db.UsesRootSelfProtocol && !db.UsesRootTimers && !db.UsesRootValueProtocol && !db.UsesRootJobPreview &&
                    !db.UsesRootTimedJobIssuance && !db.UsesRootParkArrivals && !db.UsesRootParkRecovery,
                "format 25 leaves out self-sends, timers, values, untimed-only jobs, timed-job issuance and Park arrivals and recovery");
        }
        var store = new EfStore(Open, postgres: true);
        await store.CreateAsync(1);
        await store.InstallPreviewRootSchemaAsync(stop);
        await store.InstallPreviewRootSchemaAsync(stop);
        Require(true, "the shipped schema and its workflow history install, and install again over themselves");
        await CheckDefinitionRevisionAsync(Open, store, stop);
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
        var escalated = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var service = new PostgresPreviewHostedService(Open, Node, actor, message => escalated.TrySetResult(message),
            new() { PollInterval = TimeSpan.FromMilliseconds(20) }, requests: foundation.Requests);
        await service.StartAsync(stop);
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

            // A timed job that passes its deadline settles on the Timeout outcome.
            var stuck = (await foundation.SendAsync(scope, new Contract.Target.Start(Start("stuck")), "stuck-1", Contract.Kinds.Start,
                cancellationToken: stop)).Actor!.Value;
            var timedOut = await SettledAsync(stuck);
            Require(timedOut is { Status: InstanceStatus.Finished } && Variable(timedOut, "outcome") == SendHttpRequest.TimeoutOutcome,
                "a timed job that passes its deadline settles on Timeout and the root finishes");


            // A Terminate of a root whose timed job is running ends it Cancelled with nothing of it left pending; a Delete
            // then retires it.
            partner.Reset();
            await TerminateAndDeleteAsync("long-fetch", "long-fetch-1", "a root whose timed SendHttpRequest job is running",
                async () => { while (partner.Calls("/stuck") == 0) await Task.Delay(20, stop); });


            // A selected Terminate of a running root.
            var waiting = (await foundation.SendAsync(scope, new Contract.Target.Start(Start("waits")), "waits-1", Contract.Kinds.Start,
                cancellationToken: stop)).Actor!.Value;
            await RunningAsync(waiting);
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

            Require(!escalated.Task.IsCompleted && service.LastError is null, "the node ran on format 25 without a fault");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
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

    private static async Task CheckDefinitionRevisionAsync(Func<KernelDb> open, EfStore store, CancellationToken token)
    {
        await using var db = open();
        var expected = StoreDefinitionsRevision.Seed(db)!;
        var marker = db.StoreVersions.Where(row => row.Id == expected.Id);
        Require(await marker.ExecuteDeleteAsync(token) == 1, "new PostgreSQL installs have a definitions marker");
        await EfStore.RequireStoreVersionsAsync(db, token);
        Require(await store.PartitionCountAsync(token) == 1,
            "PostgreSQL reads a pre-revision marker as its legacy definitions revision");
        db.StoreVersions.Add(expected);
        await db.SaveChangesAsync(token);
        await marker.ExecuteUpdateAsync(row => row.SetProperty(x => x.MessageProtocol, expected.MessageProtocol + 1), token);
        try
        {
            await RequireMarkerRefusedAsync(db, "PostgreSQL refuses an unsupported definitions revision", token);
            await RefusesRevisionAsync(() => store.PartitionCountAsync(token).AsTask());
            await RefusesRevisionAsync(() => store.ReadRootActorAsync("tenant", "missing", token));
            await RefusesRevisionAsync(() => store.ReadRootActorListAsync("tenant", "", null, null, [], null, null, 1, token));
            await RefusesRevisionAsync(() => store.ReadRootJournalAsync("tenant", "missing", 1, 1, token));
            await RefusesRevisionAsync(() => store.ReadRootInboxAsync("tenant", "missing", 0, "", 1, token));
            await RefusesRevisionAsync(() => store.ReadRootWorkflowHistoryAsync("tenant", "type", null, null, null, null, null, null, 1, token));
            Require(true, "the owner and all shipped snapshot readers reject a mismatched definitions revision");
        }
        finally
        {
            await marker.ExecuteDeleteAsync(token);
        }

        // An operation that waits for the shared marker must see a definitions row inserted by the locker.
        await using var advancing = open();
        await using var advance = await advancing.Database.BeginTransactionAsync(token);
        await advancing.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT "Id" FROM "KernelStoreVersions" WHERE "Id" = {StoreVersionEntity.MarkerId} FOR UPDATE
            """, token);
        await advancing.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "KernelStoreVersions" ("Id", "StoreSchema", "MessageProtocol", "JournalCodec")
            VALUES ({expected.Id}, {expected.StoreSchema}, {expected.MessageProtocol + 1}, {expected.JournalCodec})
            """, token);
        var waiting = store.PartitionCountAsync(token).AsTask();
        while (!waiting.IsCompleted && !await db.Database.SqlQueryRaw<bool>("""
            SELECT EXISTS (SELECT 1 FROM pg_stat_activity
                WHERE datname = current_database() AND pid <> pg_backend_pid() AND wait_event_type = 'Lock') AS "Value"
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
            // Exercise the rest of the hosted journey against a pre-revision installation.
            await marker.ExecuteDeleteAsync(token);
        }

        static async Task RefusesRevisionAsync(Func<Task> query)
        {
            try { await query(); }
            catch (StoreFormatException) { return; }
            throw new InvalidOperationException("The shipped query accepted a mismatched definitions revision.");
        }
    }

    // Format 25 refuses the markers of the preview formats it replaces, and each of them refuses format 25's: their
    // databases are recreated, not migrated (card 3).
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
                RootProfiles.Shipped))
        })
        {
            await using (var older = open(shipped))
                await RequireMarkerRefusedAsync(older, $"the {name} profile refuses the format 25 marker", stop);
            // The older format's own database, in a schema of its own: format 25 refuses its marker.
            await using (var db = PostgresPreviewHosting.CreateShippedContext(shipped))
                await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA " + schema, stop);
            var olderOptions = new DbContextOptionsBuilder<KernelDb>()
                .UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString).Options;
            await using (var created = open(olderOptions))
                await created.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(stop);
            await using (var newer = PostgresPreviewHosting.CreateShippedContext(olderOptions))
                await RequireMarkerRefusedAsync(newer, $"format 25 refuses the {name} marker, so its database is recreated", stop);
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
