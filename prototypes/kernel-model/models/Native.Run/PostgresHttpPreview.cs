using System.Net;
using System.Text;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.EntityFramework;
using Kernel.Native;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresHttpPreview
{
    private const string Receiver = "http-approval-receiver";
    private const string Operation = "approval";

    internal static async Task<int> RunAsync(bool inBatch, string schedule)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var stop = deadline.Token;
        var late = schedule == "late";
        var disconnect = schedule == "disconnect";
        var clockDelay = schedule == "clock-delay";
        var delayedClock = 0;
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var clientStop = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Supply a fresh PostgreSQL preview database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => KernelDb.ForPostgresRootReplies(options);
        await using var db = Open();
        Require(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "The HTTP preview needs a fresh database.");
        EfStore Store() => new(Open, postgres: true, fenceInBatch: inBatch) { CommitSql = CommitSql.Owned };
        var store = Store();
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        await store.CreateAsync(1);
        await admission.InstallAsync(stop);
        var tenant = FixedTenantScope.Bind("approval");
        var receiver = new PostgresDirectRequests(admission, store, Receiver);
        var preview = new PostgresPreviewFoundation(admission, store, new(store, await admission.OpenJournalCursorAsync(stop), 16),
            WorkflowActor.WorkflowActor.ActorType, directRequests: receiver);
        Contract.IIngress ingress = preview;
        var registry = new WorkflowRegistry().Add(PostgresHeldDirectChecks.Build());
        var transport = new CapturedLink();
        var native = NativeExecutionModel.Start(PostgresHeldDirectChecks.Definition);
        var start = await new WorkflowRuntime(ingress, preview).StartAsync(tenant, "order", native, cancellationToken: stop);
        var foreignStart = WorkflowActor.WorkflowActor.Start(native);
        var foreign = await admission.AdmitAsync("other", "foreign-order",
            new(foreignStart.Type, foreignStart.TypeData, foreignStart.Input, History: (History)foreignStart.History),
            cancellationToken: stop);
        var foreignActor = new Contract.ActorRef(foreign.Reference);
        var actor = start is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } admitted }
            ? admitted : throw new InvalidOperationException("The Native order must be admitted.");
        var first = await PostgresHeldDirectChecks.TakeAsync(store, "first-http-owner", registry, transport, stop);
        var firstEpoch = first.Fences.Single().Epoch;

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        Message pending;
        Contract.PageToken before;
        Task<HttpResponseMessage> request;
        using var client = new HttpClient();
        await using (first)
        {
            var waiting = await DriveAsync(first, store, actor, tenant.Tenant, 1);
            var otherWaiting = await DriveAsync(first, store, foreignActor, "other", 1);
            var otherFrame = Json.Read<NativeState>(otherWaiting.State!).Frames.Values.Single(f => f.Node == "approve").Id;
            var frame = Json.Read<NativeState>(waiting.State!).Frames.Values.Single(f => f.Node == "approve").Id;
            var born = await preview.TailAsync(tenant, actor, cancellationToken: stop);
            before = born.Next ?? throw new InvalidOperationException("The waiting workflow needs a journal bookmark.");
            Require(waiting.Status == InstanceStatus.Running && born.Items.All(item => item.Revision == 1),
                "The first HTTP owner must leave a committed approval wait.");
            app.MapPost("/approval", (Delegate)((HttpContext context) => DispatchAsync(context, actor, frame)));
            app.MapPost("/approval/foreign", (Delegate)((HttpContext context) => DispatchAsync(context, foreignActor, otherFrame)));
            await app.StartAsync(stop);
            client.BaseAddress = new Uri(app.Urls.Single());

            using var wrongTenant = await PostAsync("/approval/foreign", "Ada", 2_000);
            Require(wrongTenant.StatusCode == HttpStatusCode.NotFound && (await PendingAsync()).Count == 0 &&
                (await admission.ReadPendingDirectAsync("other", foreignActor.Id, stop)).Count == 0,
                "A request for another tenant's live reference must leave both workflow inboxes empty.");

            var expired = await ingress.RequestAsync(tenant, new Contract.Target.Ref(actor), Operation, CompositeSignal.Kind,
                null, null, new(null, await admission.ReadReplyTimeAsync(stop) - 1), stop);
            Require(expired is { Error: "timeout", Status: Contract.ActorStatus.Running } && (await PendingAsync()).Count == 0,
                "An already-expired public request must report the running actor without admitting input.");

            if (schedule == "slow-body") await CheckSlowBodyAsync();
            if (schedule == "blocked") await CheckBlockedAdmissionAsync(waiting.Actor.Id);
            request = PostAsync("/approval", "Ada", late || clockDelay ? 4_000 : 15_000, clientStop.Token);
            while (true)
            {
                var inputs = await PendingAsync();
                if (inputs is [var input])
                {
                    pending = input;
                    break;
                }
                Require(!request.IsCompleted, "The HTTP connection must remain open until the admitted input is visible.");
                await Task.Delay(15, stop);
            }

            Require(pending.ReplyTo is { } route && ReplyTo.Parse(route).Node == Receiver && !request.IsCompleted && transport.Count == 0,
                "The original reply route must be persisted on the input while the HTTP connection waits.");

            if (disconnect)
            {
                clientStop.Cancel();
                try
                {
                    using var unexpected = await request;
                    throw new InvalidOperationException("The disconnected client received an HTTP response.");
                }
                catch (OperationCanceledException) when (clientStop.IsCancellationRequested) { }
                await disconnected.Task.WaitAsync(stop);
                Require((await PendingAsync()).SequenceEqual([pending]),
                    "Disconnect must leave the admitted input intact.");
            }
        }

        var successorStore = Store();
        transport.CheckLanded = () => ResponseHasCommittedAsync().GetAwaiter().GetResult();
        await using var successor = await PostgresHeldDirectChecks.TakeAsync(successorStore, "successor-http-owner", registry, transport, stop,
            (turn, _, _) =>
            {
                if (turn.Row is { Revision: 2 }) Require((disconnect || !request.IsCompleted) && transport.Count == 0,
                    "The actual HTTP connection must stay open through checkpoint preparation.");
            });
        var epoch = successor.Fences.Single().Epoch;
        Require(epoch > firstEpoch && (await PendingAsync()).SequenceEqual([pending]),
            "The surviving owner must find the same admitted occurrence at a higher epoch.");

        var done = await DriveAsync(successor, successorStore, actor, tenant.Tenant, 2);
        var suffix = await preview.TailAsync(tenant, actor, before, stop);
        Require(done is { Status: InstanceStatus.Finished, Revision: 2 } && suffix.Items.Count > 0 &&
            suffix.Items.All(item => item.Revision == 2) && transport.Count > 0 && transport.LandedAtSend,
            "The surviving owner must commit one terminal revision before releasing the reply link.");

        if (!late && !disconnect) Require(await DeliverAsync() == 1, "One live carrier must complete the original request.");
        if (!disconnect)
        {
            using var response = await request.WaitAsync(stop);
            var body = await response.Content.ReadAsStringAsync(stop);
            Require(late ? response.StatusCode == HttpStatusCode.GatewayTimeout : response.StatusCode == HttpStatusCode.Accepted && body == "approved",
                "The original connection must receive the expected Native response or the gateway timeout.");
        }
        if (late || disconnect) Require(await DeliverAsync() == 0, "A closed registration must accept no late carrier.");

        var originalCarriers = transport.Count;
        using var replay = await PostAsync("/approval", "changed", 500);
        Require(replay.StatusCode == HttpStatusCode.GatewayTimeout && (await PendingAsync()).Count == 0 &&
            transport.Count == originalCarriers && (await preview.TailAsync(tenant, actor, before, stop)).Items.SequenceEqual(suffix.Items),
            "A duplicate operation must leave the journal unchanged and cannot attach to an earlier reply.");

        var replayStatus = await ingress.RequestAsync(tenant, new Contract.Target.Ref(actor), Operation, CompositeSignal.Kind,
            "changed", null, new(null, await admission.ReadReplyTimeAsync(stop) + 250), stop);
        Require(replayStatus is { Error: "timeout", Status: Contract.ActorStatus.Finished },
            "The public contract must include the finished status when a duplicate times out.");
        var outcome = disconnect ? "disconnected" : late ? "504" : "202 approved";
        Console.WriteLine($"ok PostgreSQL HTTP {(inBatch ? "in-batch" : "checked")} {schedule}: epoch {firstEpoch}->{epoch}, one revision, HTTP {outcome}, replay 504, other tenant 404.");
        return 0;

        Task<HttpResponseMessage> PostAsync(string path, string approver, int ms, CancellationToken? clientToken = null) =>
            client.PostAsJsonAsync($"{path}?wait={ms}", new { approver }, clientToken ?? stop);

        Task<IReadOnlyList<Message>> PendingAsync() => admission.ReadPendingDirectAsync(tenant.Tenant, actor.Id, stop);

        async Task CheckSlowBodyAsync()
        {
            using var body = new SlowApprovalBody();
            using var response = await client.PostAsync("/approval?wait=500", body, stop);
            Require(response.StatusCode == HttpStatusCode.GatewayTimeout && (await PendingAsync()).Count == 0,
                "A valid body that exceeds the route budget must return 504 and add no input.");
            Console.WriteLine("Slow upload returned 504 before approval admission.");
        }

        async Task CheckBlockedAdmissionAsync(string id)
        {
            await using var locking = Open();
            await using var gate = await locking.Database.BeginTransactionAsync(stop);
            await locking.Database.SqlQuery<string>($"""
                SELECT "Id" AS "Value" FROM "Instances" WHERE "Id" = {id} FOR UPDATE
                """).SingleAsync(stop);
            var blocked = PostAsync("/approval", "Ada", 1_500);
            while (!await db.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS (
                    SELECT 1 FROM pg_stat_activity
                    WHERE datname = current_database() AND wait_event_type = 'Lock'
                        AND query LIKE '%FoundationReference%' AND query LIKE '%FOR SHARE%') AS "Value"
                """).SingleAsync(stop))
            {
                Require(!blocked.IsCompleted, "The request must reach PostgreSQL and wait for the held row.");
                await Task.Delay(15, stop);
            }

            using var response = await blocked.WaitAsync(TimeSpan.FromSeconds(3), stop);
            Require(response.StatusCode == HttpStatusCode.GatewayTimeout && (await PendingAsync()).Count == 0,
                "The HTTP deadline must return 504 while admission is still blocked by the held row.");
            await gate.RollbackAsync(stop);
            Require((await PendingAsync()).Count == 0, "Releasing the row must not admit the expired request.");
            Console.WriteLine("Admission returned 504 while PostgreSQL still held the actor row.");
        }

        async Task<bool> ResponseHasCommittedAsync()
        {
            await using var observer = Open();
            return await observer.Instances.AsNoTracking().AnyAsync(row => row.Id == pending.Target.Id && row.Revision == 2, stop) &&
                await observer.Messages.AsNoTracking().AnyAsync(row => row.Id == pending.InboxKey.Id && row.Done, stop);
        }

        async Task<int> DeliverAsync()
        {
            var accepted = 0;
            while (transport.Pending.TryDequeue(out var item))
                if (await receiver.ReceiveAsync(item.Node, item.Reply, stop)) accepted++;
            return accepted;
        }

        async Task<EfStore.RootImage> DriveAsync(Worker worker, EfStore source, Contract.ActorRef reference, string targetTenant, long revision)
        {
            for (var pass = 0; pass < 20; pass++)
            {
                await worker.WokenAsync(null, stop);
                await worker.RunAsync(stop);
                Require(worker.Owns, "The owner must keep its fence between fixture passes.");
                if (await source.ReadFirstRootAsync(targetTenant, reference.Id, stop) is { } image && image.Revision >= revision)
                    return image;
                await Task.Delay(15, stop);
            }
            throw new InvalidOperationException($"No committed checkpoint at revision {revision}.");
        }

        async Task<IResult> DispatchAsync(HttpContext context, Contract.ActorRef reference, string frame)
        {
            if (!int.TryParse(context.Request.Query["wait"], out var wait) || wait is < 1 or > 15_000)
                return Results.BadRequest();

            using var budget = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stop);
            budget.CancelAfter(wait);
            var token = budget.Token;
            var handling = ReadAndDispatchAsync();
            Contract.Reply reply;
            try { reply = await handling.WaitAsync(token); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                await ObserveCanceledAsync();
                disconnected.TrySetResult();
                throw;
            }
            catch (OperationCanceledException) when (budget.IsCancellationRequested && !stop.IsCancellationRequested)
            {
                // A canceled database call can still be waiting for its cancellation acknowledgment.
                _ = ObserveCanceledAsync();
                return Results.StatusCode(504);
            }

            return reply switch
            {
                { Error: "body" } => Results.BadRequest(),
                { Error: "missing" } => Results.NotFound(),
                { Error: "timeout" } => Results.StatusCode(504),
                { Error: "lost" } => Results.StatusCode(503),
                { Error: null, Channel: "response", Item: JsonNode response } when Values.To<Kernel.Native.HttpResponse>(response) is { } output =>
                    Results.Text(output.Body is JsonValue text && output.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) &&
                        text.TryGetValue<string>(out var value) ? value : output.Body?.ToJsonString(), output.ContentType, statusCode: output.StatusCode),
                { Error: null, Status: not Contract.ActorStatus.Faulted, Item: null } => Results.NoContent(),
                _ => Results.StatusCode(500)
            };

            async Task<Contract.Reply> ReadAndDispatchAsync()
            {
                var now = await admission.ReadReplyTimeAsync(token);
                token.ThrowIfCancellationRequested();
                // Use the whole interval with the database's entry sample. The independent HTTP timer
                // measures the actual connected lifetime from handler entry, including query latency.
                if (clockDelay && reference == actor && Interlocked.CompareExchange(ref delayedClock, 1, 0) == 0)
                    await Task.Delay(2_400, token);
                var routeDeadline = now + wait;
                JsonNode? body;
                try { body = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: token); }
                catch (JsonException) { return new(null, null, Error: "body"); }
                if (body is not JsonObject fields || fields["approver"] is not JsonValue name || !name.TryGetValue<string>(out var approver))
                    return new(null, null, Error: "body");

                token.ThrowIfCancellationRequested();
                var payload = Json.Write(new CompositeSignal(frame, "approve", approver));
                return await ingress.RequestAsync(tenant, new Contract.Target.Ref(reference), Operation, CompositeSignal.Kind,
                    payload, null, new(null, routeDeadline), token);
            }

            async Task ObserveCanceledAsync()
            {
                try { await handling; }
                catch (Exception) when (token.IsCancellationRequested) { }
            }
        }
    }

    private sealed class SlowApprovalBody : HttpContent
    {
        private static readonly byte[] Prefix = Encoding.UTF8.GetBytes("{\"approver\":");
        private static readonly byte[] Suffix = Encoding.UTF8.GetBytes("\"Ada\"}");

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync(Prefix);
            await stream.FlushAsync();
            await Task.Delay(1_200);
            await stream.WriteAsync(Suffix);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = Prefix.Length + Suffix.Length;
            return true;
        }
    }

    private sealed class CapturedLink : IReplyTransport
    {
        internal int Count { get; private set; }
        internal bool LandedAtSend { get; private set; }
        internal Func<bool>? CheckLanded { get; set; }
        internal Queue<(string Node, Reply Reply)> Pending { get; } = new();
        public void Send(string node, Reply reply)
        {
            LandedAtSend = (Count == 0 || LandedAtSend) && CheckLanded?.Invoke() == true;
            Count++;
            Pending.Enqueue((node, reply));
        }
    }

    private static void Require(bool condition, string detail)
    {
        if (!condition) throw new InvalidOperationException(detail);
    }
}
