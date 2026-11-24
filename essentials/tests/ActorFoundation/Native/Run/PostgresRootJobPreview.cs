using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.EntityFramework;
using Kernel.Native;
using Kernel.Native.Preview;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresRootJobPreview
{
    private const string Definition = "native-root-job";
    private const string ActivityName = "background";
    private const string Successor = "successor";
    internal sealed record Restart(string Primary, string Neighbor, string Approval, long Epoch, long Position,
        string PrimaryBookmark, string NeighborBookmark);

    internal static async Task<int> RunAsync(bool inBatch, Restart? restart, bool finish = false)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Supply a dedicated PostgreSQL preview database.");
        var ticketLock = new TicketLockCut();
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).AddInterceptors(ticketLock).Options;
        KernelDb Open() => KernelDb.ForPostgresRootJobs(options);
        var store = new EfStore(Open, postgres: true, fenceInBatch: inBatch) { CommitSql = CommitSql.Owned };
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var stop = timeout.Token;
        if (restart is null)
        {
            await using var db = Open();
            if (!await PostgresFixtureDatabase.IsEmptyAsync(db, stop))
            {
                Console.Error.WriteLine("Use a fresh dedicated PostgreSQL database for the public root job preview.");
                return 2;
            }
            await store.CreateAsync(1);
            await admission.InstallAsync(stop);
        }
        var api = new PostgresPreviewFoundation(admission, new(store, await admission.OpenJournalCursorAsync(stop), 16),
            WorkflowActor.WorkflowActor.ActorType);
        var scope = EmbeddedTenant.Bind("jobs");
        var registry = new WorkflowRegistry().Add(Wf.Workflow(Definition, Wf.Sequence("main",
            Wf.Node<ReturnActor>(ActivityName), Wf.Node<ReturnActor>(Successor), Wf.Set("advance", "advanced", true)), ("advanced", false)))
            .Add(DirectApprovalWorkflow.Build());
        var approval = restart is null ? await Start("job-free", DirectApprovalWorkflow.Definition) : new Contract.ActorRef(restart.Approval);
        if (restart is null) await VerifyMissingInitialization(approval);
        await store.EnableRootJobsAsync(stop);
        ReturnActor.Entries.Clear();
        ReturnActor.ActivityToComplete = restart is null ? null : finish ? Successor : ActivityName;
        var completions = new List<(InstanceRef Actor, Message Result)>();
        var host = await PostgresNativeRootOwner.PreparePartitionAsync(store, $"jobs-{Environment.ProcessId}", 0, registry, 8, stop,
            (actor, items, _) => completions.AddRange(items.Where(input => input.Kind == Kinds.JobFinished).Select(input => (actor, input))));
        if (restart is null)
        {
            var capability = store.RootJobHandle;
            await RefuseLateInitialization(() => store.EnableRootJobsAsync(stop));
            Check(ReferenceEquals(capability, store.RootJobHandle), "A bound owner's job capability must remain unchanged.");
        }
        await using var activation = await host.TryActivateAsync(stop)
            ?? throw new InvalidOperationException("The PostgreSQL job owner could not take its partition.");
        var worker = (Worker)activation;

        if (restart is null)
        {
            var primary = await Start("primary");
            var neighbor = await Start("neighbor");
            var creator = await admission.ReadPendingCreatorAsync(scope.Tenant, primary.Id, stop)
                ?? throw new InvalidOperationException("The public creator is absent before activation.");
            var direct = new EfStore(Open, postgres: true) { CommitSql = CommitSql.Owned };
            await RefuseDirect(() => direct.CommitFirstRootAsync(scope.Tenant, primary.Id, creator, worker.Fences.Single(),
                TimeSpan.FromSeconds(10), "null", 14, Definition, new Dictionary<string, string>(), """["manual start"]""", stop));
            await Drive(async () => await Image(scope, primary) is not null && await Image(scope, neighbor) is not null &&
                await Image(scope, approval) is not null);
            await LoadJobFree(approval, 1);
            var first = await ReadWaiting(scope, primary, 1);
            var second = await ReadWaiting(scope, neighbor, 1);
            Check(first.Actor != second.Actor && first.Ticket != second.Ticket && completions.Count == 0,
                "The first owner must commit two distinct pending jobs before any completion.");
            await CheckAtomicIssuance(first, second, 1);
            var (_, primaryBookmark) = await History(scope, primary);
            var (_, neighborBookmark) = await History(scope, neighbor);
            var fence = worker.Fences.Single();
            Check(primaryBookmark is not null && neighborBookmark is not null, "The first owner must leave a journal bookmark for each root.");
            Console.WriteLine($"waiting {Environment.ProcessId} {primary.Id} {neighbor.Id} {approval.Id} {fence.Epoch} {fence.Position} " +
                $"{primaryBookmark!.Value.Value} {neighborBookmark!.Value.Value}");
            return 0;
        }

        var actor = new Contract.ActorRef(restart.Primary);
        var other = new Contract.ActorRef(restart.Neighbor);
        var current = worker.Fences.Single();
        Check(current.Epoch > restart.Epoch, "The cold owner must acquire a higher epoch.");
        var revision = finish ? 2L : 1L;
        var approvalFrame = await LoadJobFree(approval, revision);
        var left = await ReadWaiting(scope, actor, revision);
        var right = await ReadWaiting(scope, other, revision);
        if (!finish)
        {
            await VerifyRefusals(left, right);
            await Approve(approvalFrame, "first approval");
            await Drive(async () => (await Image(scope, actor))?.Revision == 2 && (await Image(scope, other))?.Revision == 2 &&
                (await Image(scope, approval))?.Revision == 2);
            await LoadJobFree(approval, 2);
            var leftSuccessor = await ReadWaiting(scope, actor, 2);
            var rightSuccessor = await ReadWaiting(scope, other, 2);
            Check(left.Actor == leftSuccessor.Actor && right.Actor == rightSuccessor.Actor &&
                left.Ticket != leftSuccessor.Ticket && right.Ticket != rightSuccessor.Ticket,
                "The exact first ticket must be replaced by a distinct next-generation successor in the same turn.");
            CheckCallbacks();
            await CheckAtomicIssuance(leftSuccessor, rightSuccessor, 2);
            var fence = worker.Fences.Single();
            Console.WriteLine($"successor {Environment.ProcessId} {actor.Id} {other.Id} {approval.Id} {fence.Epoch} {fence.Position} " +
                $"{restart.PrimaryBookmark} {restart.NeighborBookmark}");
            return 0;
        }

        await Approve(approvalFrame, "second approval");
        await Drive(async () => (await Image(scope, actor))?.Status == InstanceStatus.Finished &&
            (await Image(scope, other))?.Status == InstanceStatus.Finished && (await Image(scope, approval))?.Status == InstanceStatus.Finished);
        var jobFree = (await Image(scope, approval))!;
        Check(jobFree is { Revision: 3, NextJobGeneration: 0, Jobs: null } &&
            Json.Read<NativeState>(jobFree.State!).Results["approve"] is JsonArray approvals &&
            approvals.Select(item => item!.GetValue<string>()).SequenceEqual(["first approval", "second approval"]),
            "Both cold approval turns must finish the job-free root and keep its generation at zero.");
        CheckCallbacks();
        var leftEnd = await CheckFinished(scope, actor, left, new(restart.PrimaryBookmark));
        var rightEnd = await CheckFinished(scope, other, right, new(restart.NeighborBookmark));
        await Pass();
        Check((await History(scope, actor, leftEnd)).Entries.Count == 0 &&
            (await History(scope, other, rightEnd)).Entries.Count == 0 && completions.Count == 2,
            "Neither root may append a second result after another pass.");
        Console.WriteLine($"finished {Environment.ProcessId} {actor.Id} epoch {current.Epoch} two sequential results per actor revision 3; job-free approval finished with generation zero");
        return 0;

        void CheckCallbacks() =>
            Check(ReturnActor.Entries.Order(StringComparer.Ordinal).SequenceEqual(new[] { actor.Id, other.Id }.Order(StringComparer.Ordinal)) &&
                completions.Count == 2, "Each cold callback must receive exactly its own opaque public reference.");

        async Task VerifyRefusals(Waiting leftWaiting, Waiting rightWaiting)
        {
            var stale = new PartitionFence(current.Partition, restart.Epoch, restart.Position);
            var leftRow = (await store.LoadAsync([leftWaiting.Actor.Id], stop)).Single();
            var rightRow = (await store.LoadAsync([rightWaiting.Actor.Id], stop)).Single();
            var polled = await store.PollAsync(current.Partition, current.Epoch, TimeSpan.Zero, 8, stop);
            var persisted = polled.Messages.Single(item => item.Message.InboxKey == leftWaiting.Ticket).Message;
            var original = (persisted with { }).AsKernelTicket();
            var job = persisted.Read<JobTicket>();
            var result = (persisted with { Kind = Kinds.JobFinished, Payload = Json.Write(new JobFinished(
                job.Key, Json.Write("unqualified"), null)) }).AsKernelTicket();
            var completion = new RootJobCompletion(original, result);
            var changedArguments = (original with { Payload = Json.Write(job with { Arguments = "changed arguments" }) }).AsKernelTicket();
            await Refuse(() => store.CommitAsync(Claim(current, rightRow, leftWaiting.Ticket, completion), stop),
                "A ticket from another root cannot be consumed here.");
            await Refuse(() => store.CommitAsync(Claim(current, leftRow, leftWaiting.Ticket, null), stop),
                "A registered job cannot disappear without its Worker completion.");
            await Refuse(() => store.CommitAsync(Claim(current, leftRow, leftWaiting.Ticket, completion with { Original = changedArguments }), stop),
                "Different original arguments must refuse completion of the persisted ticket.");
            ticketLock.BeforeRead = async token =>
                Check(await SetTicketPayload(changedArguments.Payload!, token) == 1,
                    "The lock boundary must update the physical job arguments.");
            try
            {
                var changed = await store.CommitAsync(Claim(current, leftRow, leftWaiting.Ticket, completion), stop);
                Check(ticketLock.BeforeRead is null && !changed.Landed,
                    "A physical argument change after preparation must refuse completion under the lock.");
            }
            finally
            {
                ticketLock.BeforeRead = null;
                await SetTicketPayload(original.Payload!, stop);
            }
            Check(!(await store.CommitAsync(Claim(stale, leftRow, leftWaiting.Ticket, completion), stop)).Landed,
                "The stale owner must not consume a real ticket.");

            var direct = new EfStore(Open, postgres: true) { CommitSql = CommitSql.Owned };
            var image = (await Image(scope, actor))!;
            var ordinary = (new Message("manual-finish", new(leftRow.Id), "fixture", TargetCreation: 1)
                { StorageId = actor.Id + "/manual" }).WithInboxScope(InboxKey.FoundationOccurrenceScope);
            await RefuseDirect(() => direct.CommitRootTurnAsync(scope.Tenant, actor.Id, image, [ordinary], current,
                TimeSpan.FromSeconds(10), InstanceStatus.Finished, image.State, image.StateVersion, image.TypeData, image.Labels,
                """["manual end"]""", stop));

            bool forged = false;
            try { await api.SendAsync(scope, new Contract.Target.Ref(actor), "forged", Kinds.JobFinished, "fake", cancellationToken: stop); }
            catch (NotSupportedException) { forged = true; }
            Check(forged && await ReadWaiting(scope, actor, 1) == leftWaiting && await ReadWaiting(scope, other, 1) == rightWaiting,
                "Wrong authority must leave both checkpoints and exact job tickets unchanged.");

            var old = new EfStore(() => new KernelDb(options), postgres: true);
            var refused = false;
            try { await old.TakeAsync(0, "wrong-job-format", TimeSpan.FromSeconds(10), stop); }
            catch (StoreFormatException) { refused = true; }
            Check(refused, "A reader of the ordinary protocol must refuse the job profile.");

            async Task<int> SetTicketPayload(string payload, CancellationToken token)
            {
                await using var db = Open();
                return await db.Messages.Where(row => row.Id == leftWaiting.Wire && row.InboxScope == EfStore.RootJobScope &&
                    row.Target == leftWaiting.Actor.Id && row.Partition == current.Partition && !row.Done)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Payload, payload), token);
            }
        }

        async Task CheckAtomicIssuance(Waiting first, Waiting second, long checkpoint)
        {
            await using var db = Open();
            var atomic = await db.Database.SqlQuery<bool>($"""
                SELECT count(*) = 2 AND bool_and(m.xmin = i.xmin AND i.xmin = j.xmin) AS "Value"
                FROM "Messages" m JOIN "Instances" i ON m."Target" = i."Id" AND m."Partition" = i."Partition"
                JOIN "Journal" j ON j."Instance" = i."Id" AND j."Partition" = i."Partition" AND j."Revision" = i."Revision"
                WHERE i."Id" IN ({first.Actor.Id}, {second.Actor.Id}) AND m."InboxScope" = {EfStore.RootJobScope}
                  AND i."Revision" = {checkpoint} AND m."JobGeneration" = {checkpoint - 1}
                """).SingleAsync(stop);
            Check(atomic, "Each issued ticket, checkpoint and journal must share a transaction.");
        }

        async Task<Contract.ActorRef> Start(string operation, string definition = Definition)
        {
            var receipt = await new WorkflowRuntime(api, api).StartAsync(scope, operation,
                NativeExecutionModel.Start(definition), cancellationToken: stop);
            return receipt is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } started }
                ? started : throw new InvalidOperationException("Use a fresh preview database.");
        }

        async Task VerifyMissingInitialization(Contract.ActorRef id)
        {
            var uninitialized = new EfStore(Open, postgres: true, fenceInBatch: inBatch) { CommitSql = CommitSql.Owned };
            await RefuseBinding(async () => { await uninitialized.BindWorkerPartitionAsync(0, stop); });
            await RefuseBinding(async () => { await uninitialized.BindWorkerRootsAsync([(scope.Tenant, id.Id)], stop); });
            await RefuseBinding(() => { uninitialized.BindWorkerNode(); return Task.CompletedTask; });
            await using var db = Open();
            Check(!uninitialized.CreationIdentityAvailable && !await db.Nodes.AnyAsync(stop) &&
                await db.Partitions.AsNoTracking().AllAsync(partition => partition.Epoch == 0, stop) &&
                await Image(scope, id) is null && await admission.ReadPendingCreatorAsync(scope.Tenant, id.Id, stop) is not null,
                "Missing job initialization must leave scope unbound, epoch zero and the approval creator pending.");
        }

        async Task<string> LoadJobFree(Contract.ActorRef id, long expectedRevision)
        {
            var image = await Image(scope, id) ?? throw new InvalidOperationException("The job-free Native approval is absent.");
            var row = (await store.LoadAsync([image.Actor.Id], stop)).Single();
            Check(image is { Status: InstanceStatus.Running, NextJobGeneration: 0, Jobs: null } && image.Revision == expectedRevision &&
                row is { NextJobGeneration: 0, Jobs: null } && row.Revision == expectedRevision &&
                row.FoundationDispatchHeader?.Reference == id.Id,
                "A job-free Native checkpoint must persist generation zero and cold-load with its exact public reference.");
            return Json.Read<NativeState>(row.State).Frames.Values.Single(frame => frame.Node == "approve").Id;
        }

        Task Approve(string frame, string value) =>
            PostgresMultiRootClient.Send(api, scope, approval, value, JsonSerializer.Serialize(new CompositeSignal(frame, "approve", value)),
                Contract.AdmissionOutcome.Accepted, stop);

        Task<EfStore.RootImage?> Image(Contract.BoundScope tenant, Contract.ActorRef id) => store.ReadFirstRootAsync(tenant.Tenant, id.Id, stop);

        async Task<Waiting> ReadWaiting(Contract.BoundScope tenant, Contract.ActorRef id, long revision)
        {
            var image = await Image(tenant, id) ?? throw new InvalidOperationException("The generated root is absent.");
            var native = Json.Read<NativeState>(image.State!);
            var wait = native.Waits.Values.Single();
            var generation = revision - 1;
            var key = InboxKey.Ticket(image.Actor.Id, wait.Key, image.Actor.Creation, generation);
            await using var db = Open();
            var row = await db.Messages.AsNoTracking().SingleAsync(m => m.Target == image.Actor.Id, stop);
            Check(image.Status == InstanceStatus.Running && image.Revision == revision && image.NextJobGeneration == revision &&
                JobRegistry.Read(image.Jobs).SequenceEqual([new(wait.Key, generation)]) && wait.Kind == "job" &&
                (revision == 1 || native.Results[ActivityName]?.GetValue<string>() == id.Id) &&
                row is { InboxScope: EfStore.RootJobScope, Kind: Kinds.Job, Due: 0, Done: false } && row.JobGeneration == generation &&
                Json.Read<JobTicket>(row.Payload!) is { Deadline: null, Timer: null } ticket && ticket.Key == wait.Key,
                "The same exact job must be in the Native wait, registry and physical inbox.");
            return new(image.Actor, key, row.Id, row.Seq, image.State!, row.Payload!);
        }

        async Task<Contract.PageToken> CheckFinished(Contract.BoundScope tenant, Contract.ActorRef id, Waiting waiting, Contract.PageToken saved)
        {
            var image = await Image(tenant, id) ?? throw new InvalidOperationException("The finished root is absent.");
            var native = Json.Read<NativeState>(image.State!);
            var result = completions.Single(item => item.Actor.Id == waiting.Actor.Id).Result;
            await using var db = Open();
            Check(image is { Status: InstanceStatus.Finished, Revision: 3, Jobs: null, NextJobGeneration: 2 } &&
                image.Actor == waiting.Actor && native.Waits.Count == 0 && native.Variables["advanced"]?.GetValue<bool>() == true &&
                native.Results[ActivityName]?.GetValue<string>() == id.Id && native.Results[Successor]?.GetValue<string>() == id.Id &&
                result.InboxKey == waiting.Ticket &&
                !await db.Messages.AnyAsync(m => m.Target == waiting.Actor.Id, stop),
                "The exact job's result and downstream action must commit once with no remaining physical input.");

            var (tail, end) = await History(tenant, id, saved);
            var entries = tail.Select(entry => (entry.Revision, Text: Json.Read<string>(entry.Entry))).ToArray();
            Check(end is not null && entries.All(entry => entry.Revision is 2 or 3) &&
                entries.Where(entry => entry.Text == "end").Select(entry => entry.Revision).SequenceEqual([3L]) &&
                ActivityRevisions(ActivityName).SequenceEqual([2L]) && ActivityRevisions(Successor).SequenceEqual([3L]),
                $"The public journal for {id.Id} must record one success for each background activity and one end marker.");
            return end!.Value;

            IEnumerable<long> ActivityRevisions(string name) =>
                entries.Where(entry => entry.Text.StartsWith("c:", StringComparison.Ordinal) &&
                    entry.Text.EndsWith(":" + name, StringComparison.Ordinal)).Select(entry => entry.Revision);
        }

        async Task<(List<Contract.JournalEntry> Entries, Contract.PageToken? End)> History(
            Contract.BoundScope tenant, Contract.ActorRef id, Contract.PageToken? from = null)
        {
            var entries = new List<Contract.JournalEntry>();
            while (true)
            {
                var page = await api.TailAsync(tenant, id, from, stop);
                entries.AddRange(page.Items);
                if (page.Next is not { } next) return (entries, from);
                Check(next != from, "The public journal bookmark must advance.");
                from = next;
            }
        }

        async Task Drive(Func<Task<bool>> reached)
        {
            while (!await reached())
            {
                await Pass();
                await Task.Delay(10, stop);
            }
        }

        async Task Pass()
        {
            await activation.WokenAsync(null, stop);
            await activation.RunAsync(stop);
            Check(worker.Owns, "The worker must retain its PostgreSQL fence.");
        }
    }

    private static CommitBatch Claim(PartitionFence fence, InstanceRow row, InboxKey ticket, RootJobCompletion? completion) =>
        new(fence.Partition, fence.Epoch, fence.Position, TimeSpan.FromSeconds(10), false,
            [new(row.Id, row with { Revision = row.Revision + 1, Jobs = null }, row.Revision,
                [ticket], [], [], [], [], """["unqualified result"]""") { FoundationRootJobCompletion = completion }], [], []);

    private static async Task Refuse(Func<ValueTask<Committed>> commit, string message)
    {
        try { Check(!(await commit()).Landed, message); }
        catch (NotSupportedException) { }
    }

    private static async Task RefuseLateInitialization(Func<Task> initialize)
    {
        try { await initialize(); }
        catch (InvalidOperationException error) when (error.Message == "The PostgreSQL owner is already bound.")
        { return; }
        throw new InvalidOperationException("A bound owner must refuse job capability initialization.");
    }

    private static async Task RefuseBinding(Func<Task> bind)
    {
        try { await bind(); }
        catch (StoreFormatException error) when (error.Message == "The PostgreSQL owner must initialize its job capability before binding to the job profile.")
        { return; }
        throw new InvalidOperationException("Binding protocol 6 without its job capability must be refused.");
    }

    private static async Task RefuseDirect(Func<ValueTask<Committed>> commit)
    {
        try { await commit(); }
        catch (NotSupportedException error) when (error.Message == "The PostgreSQL job profile requires the qualified Worker checkpoint writer.")
        { return; }
        throw new InvalidOperationException("A manual checkpoint must be refused by the job profile.");
    }

    private sealed class TicketLockCut : DbCommandInterceptor
    {
        internal Func<CancellationToken, Task>? BeforeRead { get; set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (BeforeRead is { } change && command.CommandText.Contains("AND NOT \"Done\" FOR UPDATE", StringComparison.Ordinal))
            {
                BeforeRead = null;
                await change(cancellationToken);
            }
            return result;
        }
    }

    private sealed record Waiting(CreationAddress Actor, InboxKey Ticket, string Wire, long Sequence, string State, string Payload);

    [Background]
    public sealed class ReturnActor : Activity<string>
    {
        internal static readonly ConcurrentQueue<string> Entries = new();
        internal static string? ActivityToComplete;

        protected override async ValueTask<Transition> ExecuteAsync(ActivityContext context)
        {
            if (ActivityToComplete != context.NodeId) await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
            Entries.Enqueue(context.Instance.Id);
            return Complete(context.Instance.Id);
        }
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}
