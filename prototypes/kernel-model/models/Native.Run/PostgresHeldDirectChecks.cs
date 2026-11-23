using System.Text.Json.Nodes;
using Kernel.EntityFramework;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresHeldDirectChecks
{
    private const string Tenant = "approval";
    private const string Definition = "held-approval";
    private const string Receiver = "approval-receiver";
    private const string Operation = "approve";

    internal static async Task<int> RunAsync(bool inBatch, bool afterDeadline)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var stop = deadline.Token;
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Supply a fresh PostgreSQL fixture database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => KernelDb.ForPostgresRootReplies(options);
        await using var db = Open();
        Check(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "The fixture needs a fresh database.");
        var store = Store();
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        await store.CreateAsync(1);
        await admission.InstallAsync(stop);
        var scope = FixedTenantScope.Bind(Tenant);
        var api = new PostgresPreviewFoundation(admission, store, new(store, await admission.OpenJournalCursorAsync(stop), 16),
            WorkflowActor.WorkflowActor.ActorType);
        var registry = new WorkflowRegistry().Add(Wf.Workflow(Definition, Wf.Sequence("flow",
            Wf.Node<ApprovalSignal>("approve"), Wf.Respond("response", "approved", 202))));
        var started = await new WorkflowRuntime(api, api).StartAsync(scope, "start", NativeExecutionModel.Start(Definition), cancellationToken: stop);
        Check(started.Outcome == Contract.AdmissionOutcome.Accepted, "The public workflow start must be admitted.");
        var actor = started.Actor!.Value;
        var requests = new Requests();
        var transport = new EdgeLink(requests);
        var first = await Take(store, "first");
        EfStore.RootImage waiting;
        Contract.PageToken bookmark;
        Message pending;
        Request request;
        Task<Reply> answer;
        long oldEpoch;
        await using (first)
        {
            oldEpoch = first.Fences.Single().Epoch;
            waiting = await Drive(first, store, 1);
            var frame = Json.Read<NativeState>(waiting.State!).Frames.Values.Single(f => f.Node == "approve").Id;
            var history = await api.TailAsync(scope, actor, cancellationToken: stop);
            Check(waiting.Revision == 1 && waiting.Status == InstanceStatus.Running && history.Items.All(e => e.Revision == 1),
                "The first owner's checkpoint must be waiting for approval.");
            bookmark = history.Next ?? throw new InvalidOperationException("Expected a public journal bookmark before approval.");
            var ordinary = new EfStore(() => new KernelDb(options), postgres: true);
            try
            {
                await ordinary.TakeAsync(0, "ordinary", Defaults.Lease, stop);
                throw new InvalidOperationException("The ordinary owner must refuse the held-reply protocol marker.");
            }
            catch (StoreFormatException) { }
            try
            {
                await ordinary.ReadFirstRootAsync(Tenant, actor.Id, stop);
                throw new InvalidOperationException("The ordinary point reader must refuse the held-reply checkpoint family.");
            }
            catch (StoreException error) when (error.Failure == Failure.Definite) { }

            var signal = Json.Write(new CompositeSignal(frame, Operation, "Ada"));
            var now = await DatabaseNow();
            request = requests.OpenBound(Operation, now + (afterDeadline ? 4_000 : 30_000), Tenant, null);
            answer = requests.CompletionOf(request);
            var expired = requests.OpenBound("expired", now - 1, Tenant, null);
            Check(await admission.AdmitDirectRequestAsync(Tenant, actor.Id, CompositeSignal.Kind, signal, Receiver, expired, false, stop)
                == PostgresRootAdmission.DirectResult.Refused, "The database must refuse an already-expired registration.");
            requests.Disconnect(expired);
            var other = requests.OpenBound(Operation, now + 30_000, "other", null);
            Check(await admission.AdmitDirectRequestAsync(Tenant, actor.Id, CompositeSignal.Kind, signal, Receiver, other, false, stop)
                == PostgresRootAdmission.DirectResult.Refused, "A registration can only be used in its own tenant.");
            Check(await admission.AdmitDirectRequestAsync("other", actor.Id, CompositeSignal.Kind, signal, Receiver, other, false, stop)
                == PostgresRootAdmission.DirectResult.MissingTarget, "A different tenant cannot admit to this root.");
            requests.Disconnect(other);
            Check(await admission.AdmitDirectRequestAsync(Tenant, actor.Id, CompositeSignal.Kind, signal, Receiver, request, false, stop)
                == PostgresRootAdmission.DirectResult.Accepted, "The bound request must be admitted to the exact root.");
            pending = (await admission.ReadPendingDirectAsync(Tenant, actor.Id, stop)).Single();
            Check(pending is { Id: Operation } && pending.ReplyTo == request.ReplyAddress(Receiver) && pending.Payload == signal &&
                !answer.IsCompleted && (await api.TailAsync(scope, actor, bookmark, stop)).Items.Count == 0,
                "The route must be durable with the admitted input while the receiver still waits.");
            var oldAdmission = new PostgresRootAdmission(() => new KernelDb(options), TimeSpan.FromHours(1));
            try
            {
                await oldAdmission.ReadPendingDirectAsync(Tenant, actor.Id, stop);
                throw new InvalidOperationException("The ordinary reader must refuse the pending held-reply envelope.");
            }
            catch (StoreException error) when (error.Failure == Failure.Definite) { }
        }

        var successorStore = Store();
        transport.CheckLanded = () => ResponseHasCommitted().GetAwaiter().GetResult();
        var prepared = 0;
        await using var successor = await Take(successorStore, "successor", (turn, _, _) =>
        {
            if (turn.Row is not { Revision: 2 }) return;
            Check(!answer.IsCompleted && transport.Attempts == 0, "Preparing the new checkpoint must leave the reply held.");
            prepared++;
        });
        Check(successor.Fences.Single().Epoch > oldEpoch &&
            (await admission.ReadPendingDirectAsync(Tenant, actor.Id, stop)).SequenceEqual([pending]),
            "A new epoch must find the same durable occurrence and registration.");
        var final = await Drive(successor, successorStore, 2);
        Check(prepared == 1 && transport.Attempts == 1 && transport.Landed && !answer.IsCompleted,
            "The outgoing reply must first observe the committed revision and settled input on an independent connection.");
        var receivedAt = await DatabaseNow();
        Check(receivedAt < request.Deadline, "The response must be ready on the wire while the original registration is open.");
        if (afterDeadline)
        {
            do
            {
                await Task.Delay(20, stop);
                receivedAt = await DatabaseNow();
            } while (receivedAt < request.Deadline);
        }
        var committedCarriers = transport.Carriers;
        transport.Deliver(receivedAt);
        var response = await answer.WaitAsync(stop);
        var suffix = await api.TailAsync(scope, actor, bookmark, stop);
        if (afterDeadline)
            Check(response.Error == "timeout" && transport.Accepted == 0 && request.Responses == 0,
                "A response delivered after the database deadline must close the receiver with timeout.");
        else
            Check(transport.Accepted == 1 && request.Responses == 1 &&
                response is { Channel: "response", Tenant: Tenant, Request: var registration, Item: JsonNode output } &&
                registration == request.Registration && Values.To<HttpResponse>(output) is { StatusCode: 202, Body: JsonValue body } &&
                body.GetValue<string>() == "approved", "The original bound receiver must get one real Native response after the commit.");
        Check(final is { Revision: 2, Status: InstanceStatus.Finished } && suffix.Items.Count > 0 &&
            suffix.Items.All(entry => entry.Revision == 2), "The successor must commit exactly one final revision.");
        var row = await db.Messages.AsNoTracking().SingleAsync(m => m.Id == pending.InboxKey.Id, stop);
        Check(row.Done && row.ReplyTo is null && (await admission.ReadPendingDirectAsync(Tenant, actor.Id, stop)).Count == 0,
            "Settling the input must clear the reply route while retaining the receipt.");
        var replay = requests.OpenBound(Operation, await DatabaseNow() + 30_000, Tenant, null);
        Check(await admission.AdmitDirectRequestAsync(Tenant, actor.Id, CompositeSignal.Kind, "changed", Receiver, replay, false, stop)
            == PostgresRootAdmission.DirectResult.Duplicate, "A new registration cannot reopen a settled operation.");
        await Pass(successor);
        Check(transport.Attempts == 1 && transport.Carriers == committedCarriers &&
            transport.Accepted == (afterDeadline ? 0 : 1) && requests.IsOpen(replay) &&
            (await api.TailAsync(scope, actor, bookmark, stop)).Items.SequenceEqual(suffix.Items),
            "The replay must add no reply or journal revision.");
        requests.Disconnect(replay);
        Console.WriteLine($"ok PostgreSQL held direct request {(inBatch ? "in-batch" : "checked")}: epochs {oldEpoch}->{successor.Fences.Single().Epoch}, revision 2, {(afterDeadline ? "timeout at receiver" : "one response")}, replay Duplicate.");
        return 0;

        Task<long> DatabaseNow() => db.Database.SqlQueryRaw<long>("""
            SELECT floor(extract(epoch from clock_timestamp()) * 1000)::bigint AS "Value"
            """).SingleAsync(stop);

        async Task<bool> ResponseHasCommitted()
        {
            await using var cold = Open();
            return await cold.Instances.AsNoTracking().AnyAsync(row => row.Id == waiting.Actor.Id && row.Revision == 2, stop) &&
                await cold.Messages.AsNoTracking().AnyAsync(row => row.Id == pending.InboxKey.Id && row.Done && row.ReplyTo == null, stop);
        }

        EfStore Store() => new(Open, postgres: true, fenceInBatch: inBatch) { CommitSql = CommitSql.Owned };

        async Task<Worker> Take(EfStore source, string node, Action<TurnCommit, object?, Message?>? prepare = null)
        {
            var (roots, count) = await source.BindWorkerPartitionAsync(0, stop);
            var type = new ActorAdapter(new PostgresNativeRootOwner.Headers(roots), new WorkflowActor.WorkflowActor([new NativeExecutionModel(registry)]),
                rootHeldReplies: true);
            var worker = new Worker(node, 0, source, type, new WorkerOptions
            {
                Partitions = count, Transport = transport, OnPreparedCheckpoint = prepare
            });
            var host = new DurableHost([worker]);
            await host.HeartbeatAsync(Defaults.Lease, stop);
            return (Worker?)await host.ActivateAsync(0, stop) ?? throw new InvalidOperationException("The fixture owner did not take.");
        }

        async Task<EfStore.RootImage> Drive(Worker worker, EfStore source, long revision)
        {
            for (var pass = 0; pass < 20; pass++)
            {
                await Pass(worker);
                if (await source.ReadFirstRootAsync(Tenant, actor.Id, stop) is { } image && image.Revision >= revision)
                    return image;
                await Task.Delay(10, stop);
            }
            throw new InvalidOperationException($"The owner did not commit revision {revision}.");
        }

        async Task Pass(Worker worker)
        {
            await worker.WokenAsync(null, stop);
            await worker.RunAsync(stop);
            Check(worker.Owns, "The fixture pass must retain its fence.");
        }
    }

    public sealed class ApprovalSignal : CompositeActivity, ISignalledComposite
    {
        protected override void Execute(CompositeContext context)
        {
            context.SetState(false);
            context.Record("awaiting-approval");
        }
        public bool CanAccept(CompositeContext context) => true;
        public void Accept(CompositeContext context, string messageId, CompositeSignal signal)
        {
            if (signal.Name == Operation && signal.Value == "Ada") context.Complete();
        }
        protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) =>
            throw new InvalidOperationException("This approval has no children.");
    }

    private sealed class EdgeLink(Requests requests) : IReplyTransport
    {
        internal int Attempts { get; private set; }
        internal int Accepted { get; private set; }
        internal int Carriers { get; private set; }
        internal bool Landed { get; private set; }
        internal Func<bool>? CheckLanded { get; set; }
        private readonly Queue<Reply> pending = new();

        public void Send(string node, Reply reply)
        {
            if (node != Receiver) return;
            Landed = (Carriers == 0 || Landed) && CheckLanded?.Invoke() == true;
            Carriers++;
            if (reply.Channel == "response") Attempts++;
            pending.Enqueue(reply);
        }

        internal void Deliver(long receivedAt)
        {
            while (pending.TryDequeue(out var outgoing))
                if (requests.TryReceive(outgoing, receivedAt)) Accepted++;
        }
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
