using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresFlowchartPreview
{
    private const string Definition = "purchase-flowchart";

    internal static async Task<int> RunAsync(string route, string? reference = null, string? bookmark = null,
        long previousEpoch = 0, bool mergeEarly = false)
    {
        var birth = reference is null;
        var manual = route == "manual";
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var stop = deadline.Token;
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Supply a dedicated preview database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => new(options);
        var store = new EfStore(Open, postgres: true);
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        if (birth)
        {
            await store.CreateAsync(1);
            await admission.InstallAsync(stop);
        }

        var scope = FixedTenantScope.Bind("purchase");
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
        if (birth)
        {
            var waiting = await DriveAsync(1);
            var state = State(waiting);
            Check(waiting.Status == InstanceStatus.Running && state.Frames.Values.Any(frame => frame.Node == "finance") &&
                state.Frames.Values.Any(frame => frame.Node == "risk") == manual &&
                (state.Variables["bypassed"]?.GetValue<bool>() == true) == !manual,
                "The decision must select exactly its intended route and wait for finance.");
            Check(state.Variables["ready"]?.GetValue<bool>() == false, "The join must wait for finance.");
            if (manual)
            {
                await ApproveAsync("risk-approval", Frame(state, "risk"), "Risk accepted", Contract.AdmissionOutcome.Accepted);
                waiting = await DriveAsync(2);
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
        Check(before.Revision == (manual ? 2 : 1), "The cold owner must resume the exact waiting revision.");
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
        var finished = await DriveAsync(before.Revision + 1);
        var result = State(finished);
        var (tail, completion) = await HistoryAsync(saved);
        Check(finished.Revision == before.Revision + 1 && (await api.GetAsync(scope, actor, stop)) is { Header.Status: Contract.ActorStatus.Finished } &&
            result.Variables["ready"]?.GetValue<bool>() == true &&
            result.Results["finance"]?.GetValue<string>() == "Finance accepted" &&
            (manual ? result.Results["risk"]?.GetValue<string>() == "Risk accepted" : !result.Results.ContainsKey("risk")),
            "The original actor must reach its join with precisely the selected approvals.");
        Check(completion is not null && completion != saved &&
            tail.Count(IsJoin) == 1 && tail.All(entry => entry.Revision == finished.Revision),
            "One persisted join must appear after the saved bookmark at the final revision.");
        await ApproveAsync("finance-approval", finance, "Replay", Contract.AdmissionOutcome.Duplicate);
        await PassAsync();
        var replayed = await ImageAsync();
        Check(replayed is not null && replayed.Revision == finished.Revision && replayed.State == finished.State &&
            (await HistoryAsync(completion)).Entries.Count == 0, "The repeated final approval must preserve the checkpoint and journal.");
        Console.WriteLine($"finished {Environment.ProcessId} {actor.Id} revision {finished.Revision} join 1 route {route} epoch {epoch}");
        return 0;

        async Task<string> StartAsync()
        {
            var runtime = new WorkflowRuntime(api, api);
            var spec = NativeExecutionModel.Start(Definition, Modes.Balanced, Wf.Object(("manualReview", manual)));
            var started = await runtime.StartAsync(scope, "purchase-birth", spec, cancellationToken: stop);
            Check(started is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "Use a fresh preview database.");
            return started.Actor!.Value.Id;
        }

        Task<EfStore.RootImage?> ImageAsync() => store.ReadFirstRootAsync(scope.Tenant, actor.Id, stop);

        async Task<EfStore.RootImage> DriveAsync(long revision)
        {
            while (true)
            {
                if (await ImageAsync() is { } available && available.Revision >= revision) return available;
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

        Task ApproveAsync(string operation, string frame, string value, Contract.AdmissionOutcome expected) =>
            PostgresMultiRootClient.Send(api, scope, actor, operation, JsonSerializer.Serialize(new CompositeSignal(frame, "approve", value)),
                expected, stop);

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
        protected override void Execute(CompositeContext context) { }
        public bool CanAccept(CompositeContext context) => true;
        public void Accept(CompositeContext context, string messageId, CompositeSignal signal)
        {
            if (signal is { Name: "approve", Value: { } value }) context.Complete(result: Values.From(value));
        }
        protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) =>
            throw new InvalidOperationException("An approval has no children.");
    }
}
