using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Bpmn.Run;

/// <summary>
/// What each case of <see cref="BpmnCases"/> costs on the EF store (BPMN.md, section 4), counted as NATIVE.md section 5
/// counts: PostgreSQL round trips are command executions plus COMMITs (Npgsql sends BEGIN with the first command); SQLite
/// counts are calls into the engine (BEGIN, each command, COMMIT). Instances are started on their owner and handed over
/// in memory; what comes from outside (a published message) is counted with the poll that finds it.
/// </summary>
public static class Cost
{
    public static async Task<int> RunAsync(bool postgres, string connection, bool fenceInBatch)
    {
        var counter = new Counting();
        var builder = new DbContextOptionsBuilder<KernelDb>();
        if (postgres) builder.UseNpgsql(connection);
        else builder.UseSqlite(connection);
        var options = builder.AddInterceptors(counter, counter.Transactions).Options;
        KernelDb Create() => new(options);
        await using (var db = Create()) await db.Database.EnsureDeletedAsync();
        var ef = new EfStore(Create, postgres, fenceInBatch);
        await ef.CreateAsync(1);
        var store = new Sizing(ef);
        var held = new ConcurrentDictionary<string, TaskCompletionSource>(StringComparer.Ordinal);
        var owner = new Owner("A", 0, store, BpmnCases.Registry(deadline: 1000), options: new BpmnOptions
        {
            // A held service waits until the measurement releases it, or its job is cancelled.
            Gate = async (job, cancel) =>
            {
                if (held.TryGetValue(Json.Read<JobArgs>(job.Arguments!).Service, out var gate)) await gate.Task.WaitAsync(cancel);
            },
        });
        await owner.StartAsync();
        var failures = 0;
        var n = 0;
        long Keep() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3_600_000;

        Console.WriteLine($"\n{(postgres ? "PostgreSQL" : "SQLite")}, fence {(fenceInBatch ? "in the SaveChanges batch" : "as its own checked statement")}");
        Console.WriteLine("| Case | Commands | Commits | Round trips | State bytes per commit (max) | Check |");
        Console.WriteLine("|---|---:|---:|---:|---:|---|");
        async Task Row(string name, string process, JsonObject? variables, Func<InstanceRef, Task>? before = null, Func<InstanceRef, Task>? meanwhile = null, Func<BpmnState, bool>? check = null)
        {
            var id = InstanceRef.In(0, $"{process}-{++n}");
            counter.Reset();
            store.Reset();
            var trace = Environment.GetEnvironmentVariable("BPMN_TRACE") is { Length: > 0 } t && name.StartsWith(t, StringComparison.Ordinal);
            counter.Trace = trace ? [] : null;
            if (before is not null) await before(id);     // an outsider's part, counted and traced too
            owner.Worker.Post(new Message($"{id}#start", id, Kinds.Start, Start: BpmnModel.Spec(process, variables)), fresh: true);
            if (meanwhile is not null) await meanwhile(id);
            await owner.PumpAsync(() => owner.Status(id.Id) == InstanceStatus.Finished, keepPolling: false);
            if (counter.Trace is { } lines) Console.WriteLine("    " + string.Join("\n    ", lines));
            var ok = check?.Invoke(owner.State(id.Id)!) ?? true;
            var trips = counter.Commands + counter.Transactions.Ended + (postgres ? 0 : counter.Transactions.Begun);
            var bytes = store.Rows == 0 ? "-" : $"{store.StateBytes / store.Rows} ({store.MaxStateBytes})";
            Console.WriteLine($"| {name} | {counter.Commands} | {store.Commits} | {trips} | {bytes} | {(ok ? "ok" : "FAILED")} |");
            if (!ok) failures++;
        }
        static bool Has(BpmnState s, string name) => s.Variables.ContainsKey(name);

        await Row("parallel: fork, two jobs, join", "b-parallel", null, check: s => Has(s, "a") && Has(s, "b"));
        await Row("exclusive: a condition, one job", "b-exclusive", new JsonObject { ["amount"] = 150 }, check: s => s.Variables["size"]!.GetValue<string>() == "big");
        await Row("inclusive: two of three flows, two jobs, join", "b-inclusive", new JsonObject { ["express"] = true, ["insured"] = true }, check: s => Has(s, "express-done") && Has(s, "insured-done"));
        await Row("boundary timer, the job finishes first", "b-boundary", null, check: s => Has(s, "slow-done"));
        held["slow"] = new TaskCompletionSource();
        await Row("boundary timer fires first and cancels the job", "b-boundary", null, check: s => Has(s, "escalated") && !Has(s, "slow-done"));
        held.Clear();
        await Row("message published before the catch (kept event)", "b-early", new JsonObject { ["order"] = "o1" },
            before: _ => Events.PublishAsync(store, new EventRow(BpmnModel.MessageKey("paid", "o1"), "paid-o1", "{\"paid\":true}", Keep())).AsTask(),
            meanwhile: async id =>
            {
                // The catch's commit carries a meet ticket; the meet inserts the delivery into the table (friction F-B2),
                // so the owner finds its own delivery only by a poll.
                await owner.PumpAsync(() => owner.State(id.Id)?.Live.Values.Any(w => w.Kind == WorkKinds.Message) == true, keepPolling: false);
                await owner.Worker.WokenAsync(null);
            },
            check: s => Has(s, "paid"));
        held["prepare"] = new TaskCompletionSource();
        await Row("message published after the catch subscribed", "b-early", new JsonObject { ["order"] = "o2" },
            meanwhile: async id =>
            {
                await owner.PumpAsync(() => store.Commits > 0, keepPolling: false);
                held["prepare"].SetResult();
                await owner.PumpAsync(() => owner.State(id.Id)?.Live.Values.Any(w => w.Kind == WorkKinds.Message) == true, keepPolling: false);
                await Events.PublishAsync(store, new EventRow(BpmnModel.MessageKey("paid", "o2"), "paid-o2", "{\"paid\":true}", Keep()));
                await owner.Worker.WokenAsync(null);     // the poll that finds the delivery
            },
            check: s => Has(s, "paid"));
        held.Clear();
        await Row("call activity: a child with one job", "b-call", new JsonObject { ["order"] = "c1" }, check: s => Has(s, "child-out"));
        held["child"] = new TaskCompletionSource();
        await Row("call activity: the boundary timer terminates the child", "b-call", new JsonObject { ["order"] = "c2" }, check: s => !Has(s, "child-out"));
        held.Clear();
        await Row($"loop: {BpmnCases.Rounds} rounds of fork, two jobs, join", "b-loop", null, check: s => s.Variables["count"]!.GetValue<int>() == BpmnCases.Rounds);

        await owner.PumpAsync(() => true, keepPolling: false);
        await using var check = Create();
        var left = await check.Messages.CountAsync(m => !m.Done);
        var running = await check.Instances.CountAsync(i => i.Status == InstanceStatus.Running);
        Console.WriteLine($"check: {await check.Instances.CountAsync()} instances ({running} running), {left} messages left, {await check.Subscriptions.CountAsync()} subscriptions left");
        return failures + (left == 0 && running == 0 ? 0 : 1);
    }
}
