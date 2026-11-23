using System.Net;
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
        var otherTenant = FixedTenantScope.Bind("other");
        var receiver = new PostgresDirectRequests(admission, store, Receiver);
        var preview = new PostgresPreviewFoundation(admission, store, new(store, await admission.OpenJournalCursorAsync(stop), 16),
            WorkflowActor.WorkflowActor.ActorType, directRequests: receiver);
        Contract.IIngress ingress = preview;
        var registry = new WorkflowRegistry().Add(PostgresHeldDirectChecks.Build());
        var transport = new CapturedLink();
        var start = await new WorkflowRuntime(ingress, preview).StartAsync(tenant, "order",
            NativeExecutionModel.Start(PostgresHeldDirectChecks.Definition), cancellationToken: stop);
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
            var waiting = await DriveAsync(first, store, 1);
            var frame = Json.Read<NativeState>(waiting.State!).Frames.Values.Single(f => f.Node == "approve").Id;
            var born = await preview.TailAsync(tenant, actor, cancellationToken: stop);
            before = born.Next ?? throw new InvalidOperationException("The waiting workflow needs a journal bookmark.");
            Require(waiting.Status == InstanceStatus.Running && born.Items.All(item => item.Revision == 1),
                "The first HTTP owner must leave a committed approval wait.");
            app.MapPost("/approval", (Delegate)((HttpContext context) => DispatchAsync(context, tenant, frame)));
            app.MapPost("/other/approval", (Delegate)((HttpContext context) => DispatchAsync(context, otherTenant, frame)));
            await app.StartAsync(stop);
            client.BaseAddress = new Uri(app.Urls.Single());

            using var wrongTenant = await PostAsync("/other/approval", "Ada", 2_000);
            Require(wrongTenant.StatusCode == HttpStatusCode.NotFound && (await PendingAsync()).Count == 0,
                "A request on the other tenant's route must leave the workflow inbox empty.");

            request = PostAsync("/approval", "Ada", late ? 4_000 : 15_000, clientStop.Token);
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
        await using var successor = await PostgresHeldDirectChecks.TakeAsync(successorStore, "successor-http-owner", registry, transport, stop,
            (turn, _, _) =>
            {
                if (turn.Row is { Revision: 2 }) Require((disconnect || !request.IsCompleted) && transport.Count == 0,
                    "The actual HTTP connection must stay open through checkpoint preparation.");
            });
        var epoch = successor.Fences.Single().Epoch;
        Require(epoch > firstEpoch && (await PendingAsync()).SequenceEqual([pending]),
            "The surviving owner must find the same admitted occurrence at a higher epoch.");

        var done = await DriveAsync(successor, successorStore, 2);
        var suffix = await preview.TailAsync(tenant, actor, before, stop);
        Require(done is { Status: InstanceStatus.Finished, Revision: 2 } && suffix.Items.Count > 0 &&
            suffix.Items.All(item => item.Revision == 2) && transport.Count > 0,
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

        var outcome = disconnect ? "disconnected" : late ? "504" : "202 approved";
        Console.WriteLine($"ok PostgreSQL HTTP {(inBatch ? "in-batch" : "checked")}: epoch {firstEpoch}->{epoch}, one revision, HTTP {outcome}, replay 504, other tenant 404.");
        return 0;

        Task<HttpResponseMessage> PostAsync(string path, string approver, int ms, CancellationToken? clientToken = null) =>
            client.PostAsJsonAsync($"{path}?wait={ms}", new { approver }, clientToken ?? stop);

        Task<IReadOnlyList<Message>> PendingAsync() => admission.ReadPendingDirectAsync(tenant.Tenant, actor.Id, stop);

        async Task<int> DeliverAsync()
        {
            var accepted = 0;
            while (transport.Pending.TryDequeue(out var item))
                if (await receiver.ReceiveAsync(item.Node, item.Reply, stop)) accepted++;
            return accepted;
        }

        async Task<EfStore.RootImage> DriveAsync(Worker worker, EfStore source, long revision)
        {
            for (var pass = 0; pass < 20; pass++)
            {
                await worker.WokenAsync(null, stop);
                await worker.RunAsync(stop);
                Require(worker.Owns, "The owner must keep its fence between fixture passes.");
                if (await source.ReadFirstRootAsync(tenant.Tenant, actor.Id, stop) is { } image && image.Revision >= revision)
                    return image;
                await Task.Delay(15, stop);
            }
            throw new InvalidOperationException($"No committed checkpoint at revision {revision}.");
        }

        async Task<IResult> DispatchAsync(HttpContext context, Contract.BoundScope scope, string frame)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stop);
            var token = linked.Token;
            if (!int.TryParse(context.Request.Query["wait"], out var wait) || wait is < 1 or > 15_000)
                return Results.BadRequest();
            JsonNode? body;
            try { body = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: token); }
            catch (JsonException) { return Results.BadRequest(); }
            if (body is not JsonObject fields || fields["approver"] is not JsonValue name || !name.TryGetValue<string>(out var approver))
                return Results.BadRequest();

            var now = await admission.ReadReplyTimeAsync(token);
            var payload = Json.Write(new CompositeSignal(frame, "approve", approver));
            Contract.Reply reply;
            try
            {
                reply = await ingress.RequestAsync(scope, new Contract.Target.Ref(actor), Operation, CompositeSignal.Kind,
                    payload, null, new(null, now + wait), token);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                disconnected.TrySetResult();
                throw;
            }
            return reply switch
            {
                { Error: "missing" } => Results.NotFound(),
                { Error: "timeout" } => Results.StatusCode(504),
                { Error: "lost" } => Results.StatusCode(503),
                { Error: null, Channel: "response", Item: JsonNode response } when Values.To<Kernel.Native.HttpResponse>(response) is { } output =>
                    Results.Text(output.Body is JsonValue text && output.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) &&
                        text.TryGetValue<string>(out var value) ? value : output.Body?.ToJsonString(), output.ContentType, statusCode: output.StatusCode),
                { Error: null, Status: not Contract.ActorStatus.Faulted, Item: null } => Results.NoContent(),
                _ => Results.StatusCode(500)
            };
        }
    }

    private sealed class CapturedLink : IReplyTransport
    {
        internal int Count { get; private set; }
        internal Queue<(string Node, Reply Reply)> Pending { get; } = new();
        public void Send(string node, Reply reply)
        {
            Count++;
            Pending.Enqueue((node, reply));
        }
    }

    private static void Require(bool condition, string detail)
    {
        if (!condition) throw new InvalidOperationException(detail);
    }
}
