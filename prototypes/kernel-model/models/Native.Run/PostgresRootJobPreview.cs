using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Kernel.EntityFramework;
using Kernel.Native;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresRootJobPreview
{
    private const string Definition = "native-root-job";
    private const string ActivityName = "background";
    internal sealed record Restart(string Primary, string Neighbor, long Epoch, long Position, string Bookmark);

    internal static async Task<int> RunAsync(bool inBatch, Restart? restart)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Supply a dedicated PostgreSQL preview database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
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
        await store.EnableRootJobsAsync(stop);
        var api = new PostgresPreviewFoundation(admission, store, new(store, await admission.OpenJournalCursorAsync(stop), 16),
            WorkflowActor.WorkflowActor.ActorType);
        var scope = FixedTenantScope.Bind("jobs");
        var registry = new WorkflowRegistry().Add(Wf.Workflow(Definition, Wf.Sequence("main",
            Wf.Node<ReturnActor>(ActivityName), Wf.Set("advance", "advanced", true)), ("advanced", false)));
        ReturnActor.Entries.Clear();
        ReturnActor.CompleteOnThisProcess = restart is not null;
        var completions = new List<(InstanceRef Actor, Message Result)>();
        var host = await PostgresNativeRootOwner.PreparePartitionAsync(store, $"jobs-{Environment.ProcessId}", 0, registry, 8, stop,
            (actor, items, _) => completions.AddRange(items.Where(input => input.Kind == Kinds.JobFinished).Select(input => (actor, input))));
        await using var activation = await host.TryActivateAsync(stop)
            ?? throw new InvalidOperationException("The PostgreSQL job owner could not take its partition.");
        var worker = (Worker)activation;

        if (restart is null)
        {
            var primary = await Start("primary");
            var neighbor = await Start("neighbor");
            await Drive(async () => await Image(scope, primary) is not null && await Image(scope, neighbor) is not null);
            var first = await ReadWaiting(scope, primary);
            var second = await ReadWaiting(scope, neighbor);
            Check(first.Actor != second.Actor && first.Ticket != second.Ticket && completions.Count == 0,
                "The first owner must commit two distinct pending jobs before any completion.");
            await using var db = Open();
            var atomic = await db.Database.SqlQuery<bool>($"""
                SELECT count(*) = 2 AND bool_and(m.xmin = i.xmin AND i.xmin = j.xmin) AS "Value"
                FROM "Messages" m JOIN "Instances" i ON m."Target" = i."Id" AND m."Partition" = i."Partition"
                JOIN "Journal" j ON j."Instance" = i."Id" AND j."Partition" = i."Partition" AND j."Revision" = i."Revision"
                WHERE i."Id" IN ({first.Actor.Id}, {second.Actor.Id}) AND m."InboxScope" = {EfStore.RootJobScope}
                """).SingleAsync(stop);
            Check(atomic, "Each issued ticket, checkpoint and start journal must share a transaction.");
            var (_, bookmark) = await History(scope, primary);
            var fence = worker.Fences.Single();
            Check(bookmark is not null, "The original public owner must leave a journal bookmark.");
            Console.WriteLine($"waiting {Environment.ProcessId} {primary.Id} {neighbor.Id} {fence.Epoch} {fence.Position} {bookmark!.Value.Value}");
            return 0;
        }

        var actor = new Contract.ActorRef(restart.Primary);
        var other = new Contract.ActorRef(restart.Neighbor);
        var current = worker.Fences.Single();
        Check(current.Epoch > restart.Epoch, "The cold owner must acquire a higher epoch.");
        var left = await ReadWaiting(scope, actor);
        var right = await ReadWaiting(scope, other);
        var stale = new PartitionFence(current.Partition, restart.Epoch, restart.Position);
        var leftRow = (await store.LoadAsync([left.Actor.Id], stop)).Single();
        var rightRow = (await store.LoadAsync([right.Actor.Id], stop)).Single();
        var polled = await store.PollAsync(current.Partition, current.Epoch, TimeSpan.Zero, 8, stop);
        var persisted = polled.Messages.Single(item => item.Message.InboxKey == left.Ticket).Message;
        var result = (persisted with { Kind = Kinds.JobFinished, Payload = Json.Write(new JobFinished(
            persisted.Read<JobTicket>().Key, Json.Write("unqualified"), null)) }).AsKernelTicket();
        await Refuse(() => store.CommitAsync(Claim(current, rightRow, left.Ticket, result), stop),
            "A ticket from another root cannot be consumed here.");
        await Refuse(() => store.CommitAsync(Claim(current, leftRow, left.Ticket, null), stop),
            "A registered job cannot disappear without its Worker completion.");
        Check(!(await store.CommitAsync(Claim(stale, leftRow, left.Ticket, result), stop)).Landed,
            "The stale owner must not consume a real ticket.");
        bool forged = false;
        try { await api.SendAsync(scope, new Contract.Target.Ref(actor), "forged", Kinds.JobFinished, "fake", cancellationToken: stop); }
        catch (NotSupportedException) { forged = true; }
        Check(forged && await ReadWaiting(scope, actor) == left && await ReadWaiting(scope, other) == right,
            "Wrong authority must leave both checkpoints and exact job tickets unchanged.");

        var old = new EfStore(() => new KernelDb(options), postgres: true);
        var refused = false;
        try { await old.TakeAsync(0, "wrong-job-format", TimeSpan.FromSeconds(10), stop); }
        catch (StoreFormatException) { refused = true; }
        Check(refused, "A reader of the ordinary protocol must refuse the job profile.");

        await Drive(async () => (await Image(scope, actor))?.Status == InstanceStatus.Finished &&
            (await Image(scope, other))?.Status == InstanceStatus.Finished);
        Check(ReturnActor.Entries.Order(StringComparer.Ordinal).SequenceEqual(new[] { actor.Id, other.Id }.Order(StringComparer.Ordinal)) &&
            completions.Count == 2, "Each cold callback must receive exactly its own opaque public reference.");
        await CheckFinished(scope, actor, left);
        await CheckFinished(scope, other, right);
        var saved = new Contract.PageToken(restart.Bookmark);
        var (tail, end) = await History(scope, actor, saved);
        Check(end is not null && tail.Count != 0 && tail.All(entry => entry.Revision == 2) && tail.Count(entry => entry.Entry == Json.Write("end")) == 1 &&
            tail.Count(entry => Json.Read<string>(entry.Entry).EndsWith(":" + ActivityName, StringComparison.Ordinal)) == 1,
            "The public journal must contain one completed workflow revision.");
        await Pass();
        Check((await History(scope, actor, end)).Entries.Count == 0 && completions.Count == 2,
            "Draining after completion must append no second result.");
        Console.WriteLine($"finished {Environment.ProcessId} {actor.Id} epoch {current.Epoch} two exact results revision 2");
        return 0;

        async Task<Contract.ActorRef> Start(string operation)
        {
            var receipt = await new WorkflowRuntime(api, api).StartAsync(scope, operation,
                NativeExecutionModel.Start(Definition), cancellationToken: stop);
            return receipt is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } started }
                ? started : throw new InvalidOperationException("Use a fresh preview database.");
        }

        Task<EfStore.RootImage?> Image(Contract.BoundScope tenant, Contract.ActorRef id) => store.ReadFirstRootAsync(tenant.Tenant, id.Id, stop);

        async Task<Waiting> ReadWaiting(Contract.BoundScope tenant, Contract.ActorRef id)
        {
            var image = await Image(tenant, id) ?? throw new InvalidOperationException("The generated root is absent.");
            var native = Json.Read<NativeState>(image.State!);
            var wait = native.Waits.Values.Single();
            var key = InboxKey.Ticket(image.Actor.Id, wait.Key, image.Actor.Creation, 0);
            await using var db = Open();
            var row = await db.Messages.AsNoTracking().SingleAsync(m => m.Target == image.Actor.Id, stop);
            Check(image is { Status: InstanceStatus.Running, Revision: 1, NextJobGeneration: 1 } &&
                JobRegistry.Read(image.Jobs).SequenceEqual([new(wait.Key, 0)]) && wait.Kind == "job" &&
                row is { InboxScope: EfStore.RootJobScope, Kind: Kinds.Job, JobGeneration: 0, Due: 0, Done: false } &&
                Json.Read<JobTicket>(row.Payload!) is { Deadline: null, Timer: null } ticket && ticket.Key == wait.Key,
                "The same exact job must be in the Native wait, registry and physical inbox.");
            return new(image.Actor, key, row.Id, row.Seq, image.State!);
        }

        async Task CheckFinished(Contract.BoundScope tenant, Contract.ActorRef id, Waiting waiting)
        {
            var image = await Image(tenant, id) ?? throw new InvalidOperationException("The finished root is absent.");
            var native = Json.Read<NativeState>(image.State!);
            var result = completions.Single(item => item.Actor.Id == waiting.Actor.Id).Result;
            await using var db = Open();
            Check(image is { Status: InstanceStatus.Finished, Revision: 2, Jobs: null, NextJobGeneration: 1 } &&
                image.Actor == waiting.Actor && native.Waits.Count == 0 && native.Variables["advanced"]?.GetValue<bool>() == true &&
                native.Results[ActivityName]?.GetValue<string>() == id.Id && result.InboxKey == waiting.Ticket &&
                !await db.Messages.AnyAsync(m => m.Target == waiting.Actor.Id, stop),
                "The exact job's result and downstream action must commit once with no remaining physical input.");
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

    private static CommitBatch Claim(PartitionFence fence, InstanceRow row, InboxKey ticket, Message? completion) =>
        new(fence.Partition, fence.Epoch, fence.Position, TimeSpan.FromSeconds(10), false,
            [new(row.Id, row with { Revision = row.Revision + 1, Jobs = null }, row.Revision,
                [ticket], [], [], [], [], """["unqualified result"]""") { FoundationRootJobCompletion = completion }], [], []);

    private static async Task Refuse(Func<ValueTask<Committed>> commit, string message)
    {
        try { Check(!(await commit()).Landed, message); }
        catch (NotSupportedException) { }
    }

    private sealed record Waiting(CreationAddress Actor, InboxKey Ticket, string Wire, long Sequence, string State);

    [Background]
    public sealed class ReturnActor : Activity<string>
    {
        internal static readonly ConcurrentQueue<string> Entries = new();
        internal static bool CompleteOnThisProcess;

        protected override async ValueTask<Transition> ExecuteAsync(ActivityContext context)
        {
            if (!CompleteOnThisProcess) await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
            Entries.Enqueue(context.Instance.Id);
            return Complete(context.Instance.Id);
        }
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}
