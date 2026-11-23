using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kernel.Native.Run;

/// <summary>One Native workflow on a single embedded Worker, with its durable state and journal in PostgreSQL.</summary>
internal static class PostgresPreview
{
    private const string Definition = "two-approvals";
    private const string Signal = "approval";

    internal static async Task<int> RunAsync(string phase, Guid runId)
    {
        if (phase is not ("start" or "resume" or "journal"))
        {
            Console.Error.WriteLine("Choose start, resume or journal.");
            return 2;
        }
        var connection = Environment.GetEnvironmentVariable("ELSA_PREVIEW_POSTGRES");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.Error.WriteLine("Set ELSA_PREVIEW_POSTGRES to the connection string for a dedicated preview database.");
            return 2;
        }
        var settings = new NpgsqlConnectionStringBuilder(connection);
        var inspection = Store(settings);
        var name = $"preview-{runId:N}";
        var instance = InstanceRef.In(InstanceRef.Spread(name, Defaults.ServerPartitions), name);
        if (phase == "journal")
        {
            var entries = await Journal();
            foreach (var entry in entries)
                Console.WriteLine($"vp {entry.Partition}  position {entry.Position}  revision {entry.Revision}  {entry.Entries}");
            return entries.Count == 0 ? 1 : 0;
        }
        if (phase == "start") await inspection.CreateAsync(Defaults.ServerPartitions);
        var current = await Checkpoint();
        if ((phase == "start" && current is not null) || (phase == "resume" && current is null))
        {
            Console.Error.WriteLine(phase == "start" ? "This run already has a checkpoint." : "This run has no saved checkpoint.");
            return 1;
        }

        // The optional wire meter observes the app's database connection. All evidence reads use the independent connection.
        Wire? wire = null;
        if (Environment.GetEnvironmentVariable("ELSA_PREVIEW_METER") == "1")
        {
            if (settings.Host is not ("localhost" or "127.0.0.1"))
            {
                Console.Error.WriteLine("The local wire meter requires a loopback PostgreSQL host.");
                return 2;
            }
            wire = Wire.Start(settings.Host, settings.Port);
            settings.Host = "127.0.0.1";
            settings.Port = wire.LocalPort;
        }
        var store = Store(settings);
        var registry = new WorkflowRegistry().Add(Wf.Workflow(Definition, Wf.Sequence("approvals",
            Wf.Event("first", Signal), Wf.Set("count-first", "count", Binding.Expr("count + 1")),
            Wf.Event("second", Signal), Wf.Set("count-second", "count", Binding.Expr("count + 1"))),
            ("count", 0)));
        var owner = new Owner($"preview-{Environment.ProcessId}", instance.Partition, store, registry, new Network());
        await using var worker = owner.Worker;
        await owner.StartAsync();
        if (!worker.Owns) throw new InvalidOperationException("The embedded host could not take this workflow's partition.");
        Console.WriteLine($"process {Environment.ProcessId}  {phase}  {instance}");
        var before = wire?.Trips ?? 0;

        if (phase == "start")
        {
            var start = new Message($"{name}-start", instance, Kinds.Start, Start: NativeModel.Spec(Definition),
                KeepUntil: worker.Now + (long)TimeSpan.FromDays(1).TotalMilliseconds);
            if (!await store.AddAsync(start)) throw new InvalidOperationException("The start id was already admitted.");
            await owner.PumpAsync(poll: true);
            Require(await Checkpoint(), InstanceStatus.Running, 0, "first");
            PrintCost("start to first wait");
            await SignalAsync("approval-1", expectedNew: true);
            Require(await Checkpoint(), InstanceStatus.Running, 1, "second");
            PrintCost("first approval to second wait");
        }
        else
        {
            Require(current, InstanceStatus.Running, 1, "second");
            var previous = await Journal();
            await SignalAsync("approval-1", expectedNew: false);
            var replay = await Checkpoint();
            Require(replay, InstanceStatus.Running, 1, "second");
            if (replay!.Revision != current!.Revision || replay.State != current.State ||
                !(await Journal()).SequenceEqual(previous))
                throw new InvalidOperationException("Replay changed the saved revision, state or workflow journal.");
            PrintCost("replay of first approval");
            await SignalAsync("approval-2", expectedNew: true);
            var finished = await Checkpoint();
            Require(finished, InstanceStatus.Finished, 2, null);
            var complete = await Journal();
            if (complete.Count <= previous.Count || !complete.Take(previous.Count).SequenceEqual(previous) ||
                complete[^1].Revision != finished!.Revision)
                throw new InvalidOperationException("The second approval did not extend the original workflow journal.");
            PrintCost("second approval to finish");
        }
        return 0;

        async Task SignalAsync(string operation, bool expectedNew)
        {
            var message = new Message($"{name}-{operation}", instance, NativeKinds.Resume, Json.Write(new ResumeStimulus(Signal)),
                KeepUntil: worker.Now + (long)TimeSpan.FromDays(1).TotalMilliseconds);
            if (await store.AddAsync(message) != expectedNew)
                throw new InvalidOperationException($"Unexpected admission for {operation}.");
            await owner.PumpAsync(poll: true);
        }
        async Task<InstanceRow?> Checkpoint() => (await inspection.LoadAsync([instance.Id])).SingleOrDefault();
        async Task<IReadOnlyList<JournalRow>> Journal() => await inspection.ReadJournalOfAsync(instance.Partition, instance.Id, 0);
        void PrintCost(string operation)
        {
            if (wire is null) return;
            var end = wire.Trips;
            Console.WriteLine($"{operation}: {end - before} database round trips");
            before = end;
        }
    }

    private static EfStore Store(NpgsqlConnectionStringBuilder settings)
    {
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(settings.ConnectionString).Options;
        return new EfStore(() => new KernelDb(options), postgres: true);
    }

    private static void Require(InstanceRow? checkpoint, InstanceStatus status, int count, string? waitNode)
    {
        if (checkpoint is null || checkpoint.Status != status)
            throw new InvalidOperationException($"Expected {status}, observed {checkpoint?.Status}: {checkpoint?.Fault}");
        var state = Json.Read<NativeState>(checkpoint.State);
        var waitingNodes = state.Waits.Values.Select(wait => state.Frames[wait.Frame].Node).ToArray();
        var expectedWaits = waitNode is null ? Array.Empty<string>() : [waitNode];
        if (state.Variables["count"]?.GetValue<int>() != count || !waitingNodes.SequenceEqual(expectedWaits))
            throw new InvalidOperationException($"Unexpected workflow state at revision {checkpoint.Revision}.");
        Console.WriteLine($"revision {checkpoint.Revision}  {status}  count {count}  wait {waitNode ?? "none"}");
    }
}
