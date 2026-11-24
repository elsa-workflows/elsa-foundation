using System.Diagnostics;
using System.Net;
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

internal static class PostgresHttpOwnerLoss
{
    private const string Tenant = "approval";
    private const string Receiver = "http-owner-loss-receiver";
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(6);
    private sealed record LeaseImage(string Owner, long Epoch, long Position, long Until, long Now);
    private sealed record Carrier(string Node, Reply Reply);

    internal static async Task<int> RunAsync(string fence, string loss)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        var stop = limit.Token;
        var open = OpenDatabase();
        await using var db = open();
        Require(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "Use a fresh database for the owner-loss fixture.");
        var store = CreateStore(open, fence);
        var admission = new PostgresRootAdmission(open, TimeSpan.FromHours(1));
        await store.CreateAsync(1);
        await admission.InstallAsync(stop);
        var scope = EmbeddedTenant.Bind(Tenant);
        var receiver = new PostgresDirectRequests(admission, store, Receiver);
        var api = new PostgresPreviewFoundation(admission, new(store, await admission.OpenJournalCursorAsync(stop), 16),
            WorkflowActor.WorkflowActor.ActorType, directRequests: receiver);
        var start = await new WorkflowRuntime(api, api).StartAsync(scope, "order",
            NativeExecutionModel.Start(PostgresHeldDirectChecks.Definition), cancellationToken: stop);
        var actor = start is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } reference }
            ? reference : throw new InvalidOperationException("The workflow start must be accepted.");

        var timely = loss == "before";
        var after = loss == "after";
        var wait = timely ? 15_000 : 4_000;
        var (databaseTime, observedAt) = await PostgresHttpPreview.SampleClockAsync(admission, false, stop);
        await using var original = OwnerProcess.Start(fence, after ? "after" : "before", actor);
        var firstEpoch = await original.ReadEpochAsync(stop);
        await original.SendAsync("birth", stop);
        await original.ExpectAsync("checkpoint 1 1", stop);

        var waiting = await ImageAsync();
        var frame = Json.Read<NativeState>(waiting.State!).Frames.Values.Single(f => f.Node == "approve").Id;
        var initial = await api.TailAsync(scope, actor, cancellationToken: stop);
        var bookmark = initial.Next ?? throw new InvalidOperationException("Expected the waiting checkpoint's bookmark.");
        Require(waiting.Revision == 1 && waiting.Status == InstanceStatus.Running &&
            initial.Items.Count > 0 && initial.Items.All(entry => entry.Revision == 1), "The first process must commit the Native approval wait.");

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        app.MapPost("/approval", (Delegate)ApproveAsync);
        await app.StartAsync(stop);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var response = ObserveResponseAsync();
        Message pending;
        while (true)
        {
            if (await PendingAsync() is [var admitted])
            {
                pending = admitted;
                break;
            }
            Require(!response.IsCompleted, "The HTTP connection must be open while waiting for durable admission.");
            await Task.Delay(15, stop);
        }
        var replyRoute = ReplyTo.Parse(pending.ReplyTo ?? throw new InvalidOperationException("Expected a durable reply route."));
        Require(replyRoute.Node == Receiver,
            "The enrolled input must address the original HTTP receiver.");

        await original.SendAsync($"approve {pending.InboxKey.Id}", stop);
        await original.ExpectAsync(after ? "landed" : "prepared", stop);
        Require(!response.IsCompleted, "The caller must still be waiting when the original owner pauses.");
        var boundary = await ImageAsync();
        var suffixAtLoss = await api.TailAsync(scope, actor, bookmark, stop);
        if (after)
            Require(boundary is { Revision: 2, Status: InstanceStatus.Finished } && (await PendingAsync()).Count == 0 &&
                suffixAtLoss.Items.Count > 0 && suffixAtLoss.Items.All(entry => entry.Revision == 2),
                "The original owner must have committed the approval without sending a carrier.");
        else
            Require(boundary.Revision == 1 && boundary.State == waiting.State &&
                (await PendingAsync()).SequenceEqual([pending]) && suffixAtLoss.Items.Count == 0,
                "The original owner must still have the unchanged checkpoint and exact unconsumed input.");

        await using var standby = OwnerProcess.Start(fence, "resume", actor);
        await standby.ExpectAsync("waiting", stop);
        var lease = await ReadLeaseAsync();
        Require(lease.Owner == OwnerNode(original.Id) && lease.Epoch == firstEpoch && lease.Until > lease.Now,
            "The standby process must be refused while the original owner's lease is live.");
        await original.ObserveExitAsync(open, firstEpoch, OwnerNode(standby.Id), stop);

        var secondEpoch = await standby.ReadEpochAsync(stop);
        var taken = await ReadLeaseAsync();
        Require(taken.Owner == OwnerNode(standby.Id) && taken.Now >= lease.Until && taken.Epoch == firstEpoch + 1 &&
            secondEpoch == taken.Epoch && taken.Position == lease.Position,
            "A different process must take the next epoch after the original lease expires.");
        if (!timely)
        {
            var (expired, completedAt) = await response.WaitAsync(stop);
            using (expired)
            {
                // The clock sample is bounded to 25 ms. Allow network and timer scheduling latency after expiry.
                var offset = completedAt - replyRoute.Deadline;
                Require(expired.StatusCode == HttpStatusCode.GatewayTimeout && offset is >= -25 and <= 750 && completedAt < lease.Until,
                    $"Expected HTTP 504 near the recorded deadline and before takeover; deadline offset {offset} ms.");
                Console.WriteLine($"Observed HTTP 504 at deadline offset {offset} ms, {lease.Until - completedAt} ms before lease expiry.");
            }
        }

        await standby.SendAsync($"approve {pending.InboxKey.Id}", stop);
        var emitted = 0;
        var accepted = 0;
        while (true)
        {
            var line = await standby.ReadAsync(stop);
            if (!line.StartsWith("reply ", StringComparison.Ordinal))
            {
                Require(line == (after ? "checkpoint 2 0" : "checkpoint 2 1"), $"Unexpected surviving owner outcome: {line}");
                break;
            }

            emitted++;
            var carrier = Json.Read<Carrier>(line["reply ".Length..]);
            var reply = carrier.Reply.Item is JsonElement item
                ? carrier.Reply with { Item = JsonNode.Parse(item.GetRawText()) } : carrier.Reply;
            Require((await ImageAsync()).Revision == 2 && (await PendingAsync()).Count == 0,
                "The successor's reply must follow the committed checkpoint and disposition.");
            if (await receiver.ReceiveAsync(carrier.Node, reply, stop)) accepted++;
        }
        if (timely)
        {
            var (answered, completedAt) = await response.WaitAsync(stop);
            using (answered)
                Require(answered.StatusCode == HttpStatusCode.Accepted && completedAt < replyRoute.Deadline &&
                    await answered.Content.ReadAsStringAsync(stop) == "approved" && emitted > 0 && accepted == 1,
                    "The successor must deliver exactly one response to the original HTTP connection before its deadline.");
        }
        else
            Require(emitted == 0 && accepted == 0, "The successor must send no output to an expired or already-settled request.");

        var final = await ImageAsync();
        var suffix = await api.TailAsync(scope, actor, bookmark, stop);
        var expectedPosition = after ? lease.Position : lease.Position + 1;
        Require(final is { Revision: 2, Status: InstanceStatus.Finished } && suffix.Items.Count > 0 &&
            suffix.Items.All(entry => entry.Revision == 2) && (await ReadLeaseAsync()).Position == expectedPosition &&
            (await PendingAsync()).Count == 0 && (!after || suffix.Items.SequenceEqual(suffixAtLoss.Items)),
            "The original operation must commit one terminal revision and journal position across process loss.");
        var settled = await db.Messages.AsNoTracking().SingleAsync(row => row.Id == pending.InboxKey.Id, stop);
        Require(settled.Done && settled.ReplyTo is null, "The exact input must settle and clear its live reply route.");
        await standby.SendAsync("stop", stop);
        await standby.ExpectExitAsync(stop);
        Console.WriteLine($"ok PostgreSQL HTTP owner loss {fence} {loss}: processes {original.Id}->{standby.Id}, epochs {firstEpoch}->{secondEpoch}, HTTP {(timely ? 202 : 504)}, one approval revision.");
        return 0;

        async Task<(HttpResponseMessage Response, long CompletedAt)> ObserveResponseAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/approval");
            var observed = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop);
            return (observed, DatabaseNow());
        }

        long DatabaseNow() => databaseTime + (long)Stopwatch.GetElapsedTime(observedAt).TotalMilliseconds;

        Task<IResult> ApproveAsync(HttpContext context)
        {
            var due = DatabaseNow() + wait;
            var payload = Json.Write(new CompositeSignal(frame, "approve", "Ada"));
            return PostgresHttpPreview.ServeAsync(context, wait,
                token => api.RequestAsync(scope, new Contract.Target.Ref(actor), "approve", CompositeSignal.Kind,
                    payload, null, new(null, due), token).AsTask(), stop);
        }

        async Task<EfStore.RootImage> ImageAsync() => await store.ReadFirstRootAsync(Tenant, actor.Id, stop)
            ?? throw new InvalidOperationException("Expected the recorded Native checkpoint.");
        Task<IReadOnlyList<Message>> PendingAsync() => admission.ReadPendingDirectAsync(Tenant, actor.Id, stop);
        Task<LeaseImage> ReadLeaseAsync() => db.Database.SqlQueryRaw<LeaseImage>("""
            SELECT p."Owner", p."Epoch", p."Position", n."LeaseUntil" AS "Until",
                floor(extract(epoch from clock_timestamp()) * 1000)::bigint AS "Now"
            FROM "Partitions" p JOIN "Nodes" n ON n."Id" = p."Owner" WHERE p."Id" = 0
            """).SingleAsync(stop);
    }

    internal static async Task<int> RunOwnerAsync(string fence, string pause, Contract.ActorRef actor)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var stop = limit.Token;
        var open = OpenDatabase();
        var store = CreateStore(open, fence);
        var registry = new WorkflowRegistry().Add(PostgresHeldDirectChecks.Build());
        var turns = 0;
        var terminalPrepared = false;
        string? approvalId = null;
        var (host, worker) = await PostgresHeldDirectChecks.PrepareAsync(store, OwnerNode(Environment.ProcessId), registry,
            new WorkerOptions
            {
                Lease = Lease,
                Transport = new ProcessLink(() => ApprovalSettledAsync().GetAwaiter().GetResult()),
                OnTurn = (_, _, _) => turns++,
                OnPreparedCheckpoint = (turn, _, _) =>
                {
                    if (turn.Row is not { Revision: 2 }) return;
                    terminalPrepared = true;
                    if (pause == "before") Pause("prepared");
                },
                OnJournal = _ => { if (pause == "after" && terminalPrepared) Pause("landed"); }
            }, stop);
        await host.HeartbeatAsync(Lease, stop);
        var activation = await host.ActivateAsync(0, stop);
        if (activation is null)
        {
            Console.WriteLine("waiting");
            do
            {
                await Task.Delay(100, stop);
                await host.HeartbeatAsync(Lease, stop);
                activation = await host.ActivateAsync(0, stop);
            } while (activation is null);
        }

        await using (activation)
        {
            Console.WriteLine($"owned {worker.Fences.Single().Epoch}");
            while (await Console.In.ReadLineAsync(stop) is { } command && command != "stop")
            {
                int revision;
                if (command == "birth") revision = 1;
                else if (command.StartsWith("approve ", StringComparison.Ordinal))
                {
                    approvalId = command["approve ".Length..];
                    revision = 2;
                }
                else throw new InvalidOperationException("Unexpected owner fixture command.");
                await DriveAsync(revision);
                Console.WriteLine($"checkpoint {revision} {turns}");
            }
        }
        return 0;

        async Task<bool> ApprovalSettledAsync()
        {
            await using var db = open();
            return approvalId is not null &&
                await store.ReadFirstRootAsync(Tenant, actor.Id, stop) is { Revision: 2, Status: InstanceStatus.Finished } &&
                await db.Messages.AsNoTracking().SingleOrDefaultAsync(row => row.Id == approvalId, stop)
                    is { Done: true, ReplyTo: null };
        }

        async Task DriveAsync(int revision)
        {
            for (var pass = 0; pass < 20; pass++)
            {
                await worker.WokenAsync(null, stop);
                await worker.RunAsync(stop);
                Require(worker.Owns, "The child must retain the fence while driving the approval.");
                if (await store.ReadFirstRootAsync(Tenant, actor.Id, stop) is { } image && image.Revision == revision)
                    return;
                await Task.Delay(15, stop);
            }
            throw new InvalidOperationException($"The child did not reach revision {revision}.");
        }

        static void Pause(string marker)
        {
            Console.WriteLine(marker);
            Console.In.ReadLine();
            throw new InvalidOperationException("The old owner must exit while paused.");
        }
    }

    private static Func<KernelDb> OpenDatabase()
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Supply a fresh PostgreSQL fixture database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        return () => KernelDb.ForPostgresRootReplies(options);
    }

    private static EfStore CreateStore(Func<KernelDb> open, string fence) =>
        new(open, postgres: true, fenceInBatch: fence == "in-batch") { CommitSql = CommitSql.Owned };

    private static string OwnerNode(int pid) => $"http-owner-{pid}";

    private sealed class ProcessLink(Func<bool> committed) : IReplyTransport
    {
        public void Send(string node, Reply reply) =>
            Console.WriteLine(committed() ? "reply " + Json.Write(new Carrier(node, reply)) : "reply-before-commit");
    }

    private sealed class OwnerProcess(Process process) : IAsyncDisposable
    {
        internal int Id => process.Id;

        internal static OwnerProcess Start(string fence, string pause, Contract.ActorRef actor)
        {
            var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true };
            foreach (var argument in new[] { typeof(PostgresHttpOwnerLoss).Assembly.Location, "pg-http-owner-process", fence, pause, actor.Id })
                info.ArgumentList.Add(argument);
            return new(Process.Start(info) ?? throw new InvalidOperationException("The fixture owner process did not start."));
        }

        internal async Task SendAsync(string value, CancellationToken stop) => await process.StandardInput.WriteLineAsync(value.AsMemory(), stop);
        internal async Task<string> ReadAsync(CancellationToken stop) => await process.StandardOutput.ReadLineAsync(stop)
            ?? throw new InvalidOperationException("The owner process closed its output unexpectedly.");

        internal async Task ExpectAsync(string expected, CancellationToken stop)
        {
            var line = await ReadAsync(stop);
            Require(line == expected, $"Expected {expected}, received {line}.");
        }

        internal async Task<long> ReadEpochAsync(CancellationToken stop)
        {
            var line = await ReadAsync(stop);
            if (!line.StartsWith("owned ", StringComparison.Ordinal) || !long.TryParse(line.AsSpan(6), out var epoch) || epoch < 1)
                throw new InvalidOperationException($"Expected a taken epoch, received {line}.");
            return epoch;
        }

        internal Task ObserveExitAsync(Func<KernelDb> open, long epoch, string successor, CancellationToken stop)
        {
            Require(!process.HasExited, "The original owner must still be live at the process-loss boundary.");
            return PostgresClusterExit.ObserveAsync(open, 0, OwnerNode(process.Id), epoch, successor, process, stop);
        }

        internal async Task ExpectExitAsync(CancellationToken stop)
        {
            var trailingOutput = process.StandardOutput.ReadToEndAsync(stop);
            await process.WaitForExitAsync(stop);
            Require(process.ExitCode == 0, "The successor must exit cleanly.");
            Require((await trailingOutput).Length == 0, "The successor must send no more output on shutdown.");
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            finally { process.Dispose(); }
        }
    }

    private static void Require(bool condition, string detail)
    {
        if (!condition) throw new InvalidOperationException(detail);
    }
}
