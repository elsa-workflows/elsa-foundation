using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresFlowchartPreview
{
    private const string Definition = "purchase-flowchart";

    internal static async Task<int> RunAsync(string route, string? reference = null, string? bookmark = null,
        long previousEpoch = 0, bool mergeEarly = false, bool durable = false, bool reachWait = false)
    {
        var birth = reference is null;
        var manual = route == "manual";
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var stop = deadline.Token;
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Supply a dedicated preview database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => new(options);
        bool? inBatch = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_FENCE") switch
        {
            null => null, "checked" => false, "in-batch" => true,
            _ => throw new ArgumentException("Use checked or in-batch for the preview fence.")
        };
        var store = new EfStore(Open, postgres: true, fenceInBatch: inBatch);
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        if (birth)
        {
            await store.CreateAsync(1);
            await admission.InstallAsync(stop);
        }

        if (durable) await store.EnableRootSelfSendsAsync(stop);

        var scope = new Contract.BoundScope("purchase");
        var api = new PostgresPreviewFoundation(admission, store, new(store, await admission.OpenJournalCursorAsync(stop), 8),
            WorkflowActor.WorkflowActor.ActorType);
        var registry = new WorkflowRegistry().Add(Build(mergeEarly));
        var host = await PostgresNativeRootOwner.PreparePartitionAsync(store, $"flowchart-{Environment.ProcessId}",
            0, registry, 1, stop);
        await using var owner = await host.TryActivateAsync(stop)
            ?? throw new InvalidOperationException("The preview owner could not take its partition.");
        var worker = (Worker)owner;
        var epoch = worker.Fences.Single().Epoch;
        Check(epoch > previousEpoch, "The cold owner must take the partition at a higher epoch.");
        var actor = new Contract.ActorRef(reference ?? await StartAsync());
        if (durable && birth)
        {
            var initial = await DriveAsync(1);
            Check(initial.Status == InstanceStatus.Running && await SharesTransactionWithSelfAsync(initial),
                "The first process must leave one issued continuation for the cold owner.");
            var (_, firstCursor) = await HistoryAsync(null);
            Check(firstCursor is not null, "The creator must have written its journal.");
            Console.WriteLine($"seeded {Environment.ProcessId} {actor.Id} {firstCursor!.Value.Value} {epoch}");
            return 0;
        }
        if (birth || reachWait)
        {
            if (reachWait)
            {
                var cold = await ImageAsync();
                Check(cold is not null && await SharesTransactionWithSelfAsync(cold),
                    "The next process must find the continuation left by the creator.");
            }
            var waiting = await DriveAsync(1, image => State(image).Agenda.Count == 0);
            var state = State(waiting);
            Check(waiting.Status == InstanceStatus.Running && state.Frames.Values.Any(frame => frame.Node == "finance") &&
                state.Frames.Values.Any(frame => frame.Node == "risk") == manual &&
                (state.Variables["bypassed"]?.GetValue<bool>() == true) == !manual,
                "The decision must select exactly its intended route and wait for finance.");
            Check(state.Variables["ready"]?.GetValue<bool>() == false, "The join must wait for finance.");
            if (manual)
            {
                await ApproveAsync("risk-approval", Frame(state, "risk"), "Risk accepted", Contract.AdmissionOutcome.Accepted);
                waiting = await DriveAsync(waiting.Revision + 1, image => State(image).Agenda.Count == 0);
                state = State(waiting);
                Check(waiting.Status == InstanceStatus.Running && state.Frames.Values.Any(frame => frame.Node == "finance") &&
                    state.Frames.Values.All(frame => frame.Node != "risk") && state.Variables["ready"]?.GetValue<bool>() == false,
                    "The join must wait for the other live branch after risk approval.");
            }
            var (journal, end) = await HistoryAsync(null);
            Check(end is not null && journal.All(entry => !IsJoin(entry)), "The waiting Flowchart must have no join entry.");
            Console.WriteLine($"waiting {Environment.ProcessId} {actor.Id} {end!.Value.Value} {epoch}");
            return 0;
        }

        var before = await DriveAsync(manual ? 2 : 1);
        var finance = Frame(State(before), "finance");
        if (manual)
        {
            await ApproveAsync("risk-approval", finance, "The replay must not release finance", Contract.AdmissionOutcome.Duplicate);
            await PassAsync();
            var afterReplay = await ImageAsync();
            Check(afterReplay is not null && afterReplay.Revision == before.Revision && afterReplay.State == before.State,
                "A changed approval replay must leave the live checkpoint unchanged.");
        }
        var saved = new Contract.PageToken(bookmark!);
        Check((await HistoryAsync(saved)).Entries.Count == 0, "Before finance, the saved journal cursor must have an empty suffix.");
        await ApproveAsync("finance-approval", finance, "Finance accepted", Contract.AdmissionOutcome.Accepted);
        var finished = await DriveAsync(before.Revision + 1, image => image.Status == InstanceStatus.Finished);
        var result = State(finished);
        var (tail, _) = await HistoryAsync(saved);
        Check((await api.GetAsync(scope, actor, stop)) is { Header.Status: Contract.ActorStatus.Finished } &&
            result.Variables["ready"]?.GetValue<bool>() == true &&
            result.Results["finance"]?.GetValue<string>() == "Finance accepted" &&
            (manual ? result.Results["risk"]?.GetValue<string>() == "Risk accepted" : !result.Results.ContainsKey("risk")),
            "The original actor must reach its join with precisely the selected approvals.");
        Check(tail.Count(IsJoin) == 1 && tail.All(entry => entry.Revision <= finished.Revision && (durable || entry.Revision == finished.Revision)),
            "One persisted join must appear after the saved bookmark through the final revision.");
        await ApproveAsync("finance-approval", finance, "Replay", Contract.AdmissionOutcome.Duplicate);
        await PassAsync();
        Check((await ImageAsync())?.Revision == finished.Revision && (await HistoryAsync(saved)).Entries.SequenceEqual(tail) &&
            (!durable || await PendingSelfAsync(finished) == 0), "Replay must preserve the finished checkpoint and journal.");
        if (durable)
        {
            await using var db = Open();
            var positions = await db.Journal.Where(j => j.Instance == finished.Actor.Id)
                .OrderBy(j => j.Position).Select(j => j.Position).ToListAsync(stop);
            Check(positions.Count < finished.Revision && positions.SequenceEqual(Enumerable.Range(1, positions.Count).Select(i => (long)i)),
                "A checkpoint without journal entries must advance the actor revision and leave journal positions gapless.");
        }
        Console.WriteLine($"finished {Environment.ProcessId} {actor.Id} revision {finished.Revision} join 1 route {route} epoch {epoch}");
        return 0;

        async Task<string> StartAsync()
        {
            var runtime = new WorkflowRuntime(api, api);
            var spec = NativeExecutionModel.Start(Definition, durable ? Modes.Durable : Modes.Balanced, Wf.Object(("manualReview", manual)));
            var started = await runtime.StartAsync(scope, "purchase-birth", spec, cancellationToken: stop);
            Check(started is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "Use a fresh preview database.");
            return started.Actor!.Value.Id;
        }

        Task<EfStore.RootImage?> ImageAsync() => store.ReadFirstRootAsync(scope.Tenant, actor.Id, stop);

        async Task<bool> SharesTransactionWithSelfAsync(EfStore.RootImage image)
        {
            // A committed checkpoint and the single pending emitted envelope carry the same PostgreSQL transaction id.
            await using var db = Open();
            return await db.Database.SqlQuery<bool>($"""
                SELECT COUNT(*) = 1 AND bool_and(m.xmin = i.xmin) AS "Value"
                FROM "Messages" m JOIN "Instances" i ON i."Id" = m."Target" AND i."Partition" = m."Partition"
                WHERE m."Target" = {image.Actor.Id} AND i."Revision" = {image.Revision}
                  AND m."InboxScope" = {EfStore.RootSelfScope} AND NOT m."Done" AND m."Sender" = m."Target"
                """).SingleAsync(stop);
        }

        async Task<int> PendingSelfAsync(EfStore.RootImage image)
        {
            await using var db = Open();
            return await db.Messages.CountAsync(m => m.Target == image.Actor.Id && m.InboxScope == EfStore.RootSelfScope && !m.Done, stop);
        }

        async Task<EfStore.RootImage> DriveAsync(long revision, Func<EfStore.RootImage, bool>? reached = null)
        {
            while (true)
            {
                if (await ImageAsync() is { } available && available.Revision >= revision && (!durable || reached is null || reached(available))) return available;
                await PassAsync();
                Check(worker.Owns, "The owner must retain its fence while materializing the Flowchart checkpoint.");
                await Task.Delay(10, stop);
            }
        }

        async Task PassAsync()
        {
            await owner.WokenAsync(null, stop);
            await owner.RunAsync(stop);
        }

        async Task ApproveAsync(string operation, string frame, string value, Contract.AdmissionOutcome expected)
        {
            var payload = JsonSerializer.Serialize(new CompositeSignal(frame, "approve", value));
            var accepted = await api.SendAsync(scope, new Contract.Target.Ref(actor), operation, CompositeSignal.Kind, payload,
                cancellationToken: stop);
            Check(accepted.Outcome == expected, $"Approval '{operation}' returned {accepted.Outcome}.");
        }

        async Task<(List<Contract.JournalEntry> Entries, Contract.PageToken? End)> HistoryAsync(Contract.PageToken? from)
        {
            var entries = new List<Contract.JournalEntry>();
            while (true)
            {
                var page = await api.TailAsync(scope, actor, from, stop);
                entries.AddRange(page.Items);
                if (page.Next is not { } next) return (entries, from);
                Check(next != from, "The public journal cursor must advance.");
                from = next;
            }
        }
    }

    private static Workflow Build(bool mergeEarly) => Wf.Workflow(Definition,
        Wf.Flowchart("purchase",
            [Wf.Set("start", "ready", false), Wf.If("decision", Binding.Expr("input.manualReview")),
             Wf.Node<Approval>("risk"), Wf.Set("bypass", "bypassed", true), Wf.Node<Approval>("finance"),
             Wf.Set("join", "ready", true)],
            [new("start", "decision"), new("start", "finance"), new("decision", "risk", "True"),
             new("decision", "bypass", "False"), new("risk", "join"), new("bypass", "join"), new("finance", "join")],
            ("join", mergeEarly ? Gateway.Merge : Gateway.ParallelJoin)),
        ("bypassed", false));

    private static NativeState State(EfStore.RootImage image) => Json.Read<NativeState>(image.State!);
    private static string Frame(NativeState state, string node) => state.Frames.Values.Single(frame => frame.Node == node).Id;
    private static bool IsJoin(Contract.JournalEntry entry) =>
        entry.Entry.StartsWith("\"j:", StringComparison.Ordinal) && entry.Entry.Contains(":join:", StringComparison.Ordinal);

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }

    public sealed class Approval : CompositeActivity, ISignalledComposite
    {
        protected internal override void Execute(CompositeContext context) { }
        public bool CanAccept(CompositeContext context) => true;
        public void Accept(CompositeContext context, string messageId, CompositeSignal signal)
        {
            if (signal is { Name: "approve", Value: { } value }) context.Complete(result: Values.From(value));
        }
        protected internal override void OnChildCompleted(CompositeContext context, ChildCompleted child) =>
            throw new InvalidOperationException("An approval has no children.");
    }
}
