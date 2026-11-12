using System.Diagnostics;
using System.Text.Json.Nodes;
using Kernel;
using Kernel.EntityFramework;
using Kernel.Native;
using Kernel.Native.Run;
using Microsoft.EntityFrameworkCore;

// Usage:
//   dotnet models/Native.Run/bin/Release/net10.0/Kernel.Native.Run.dll smoke
//   dotnet models/Native.Run/bin/Release/net10.0/Kernel.Native.Run.dll cost sqlite
//   dotnet models/Native.Run/bin/Release/net10.0/Kernel.Native.Run.dll cost postgres "<connection string>"
//   dotnet models/Native.Run/bin/Release/net10.0/Kernel.Native.Run.dll memory [runs]

WriteLine.Out = null;
if (args is ["smoke"]) return await Smoke.RunAsync();
if (args is ["expr", var text, .. var vars])
{
    var scope = new Probe(vars.Select(v => v.Split('=')).ToDictionary(p => p[0], p => JsonNode.Parse(p[1])));
    Console.WriteLine(Kernel.Native.Expression.Evaluate(text, scope)?.ToJsonString() ?? "null");
    return 0;
}
if (args is ["cost", "sqlite"])
{
    var file = Path.Combine(Path.GetTempPath(), $"kernel-native-{Guid.NewGuid():N}.db");
    var failures = 0;
    foreach (var inBatch in new[] { false, true }) failures += await Cost.RunAsync(false, $"Data Source={file}", inBatch);
    File.Delete(file);
    return failures;
}
if (args is ["cost", "postgres", var connection])
{
    var failures = 0;
    foreach (var inBatch in new[] { false, true }) failures += await Cost.RunAsync(true, connection, inBatch);
    return failures;
}
if (args is ["memory", ..]) return await Memory.RunAsync(args.Length > 1 ? int.Parse(args[1]) : 2000);
Console.Error.WriteLine("smoke | cost sqlite | cost postgres <connection> | memory [runs]");
return 2;

static class Cost
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
        await ef.CreateAsync(2);
        var store = new Sizing(ef);
        var registry = Reference.Registry();
        var network = new LiveNetwork();
        var owner = new Owner("A", 0, store, registry, network);
        var other = new Owner("B", 1, store, registry, network);
        await owner.StartAsync();
        await other.StartAsync();
        var failures = 0;
        var n = 0;
        var keep = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3_600_000;

        Console.WriteLine($"\n{(postgres ? "PostgreSQL" : "SQLite")}, fence {(fenceInBatch ? "in the SaveChanges batch" : "as its own checked statement")}");
        Console.WriteLine("| Run | Mode | Commands | Commits | Round trips | State bytes per commit (max) | Check |");
        Console.WriteLine("|---|---|---:|---:|---:|---:|---|");
        async Task Row(string name, string mode, Func<Task<bool>> body)
        {
            counter.Reset();
            store.Reset();
            var ok = await body();
            // PostgreSQL: Npgsql sends BEGIN with the first command, so a round trip is a command or a COMMIT/ROLLBACK.
            // SQLite: no wire; BEGIN, each command and COMMIT are each one call into the engine.
            var trips = counter.Commands + counter.Transactions.Ended + (postgres ? 0 : counter.Transactions.Begun);
            var bytes = store.Rows == 0 ? "-" : $"{store.StateBytes / store.Rows} ({store.MaxStateBytes})";
            Console.WriteLine($"| {name} | {mode} | {counter.Commands} | {store.Commits} | {trips} | {bytes} | {(ok ? "ok" : "FAILED")} |");
            if (!ok) failures++;
        }

        InstanceRef Next(string name) => InstanceRef.In(0, $"{name}-{++n}");
        bool Finished(Owner o, InstanceRef id) => o.Status(id.Id) == InstanceStatus.Finished;

        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
            foreach (var lines in new[] { 5, 10, 20, 40 })
                await Row($"writeline-sequence, {lines}", mode, async () =>
                {
                    var id = Next("wl");
                    owner.Worker.Post(new Message($"{id}#start", id, Kinds.Start, Start: NativeModel.Spec($"writeline-sequence-{lines}", mode)), fresh: true);
                    await owner.PumpAsync();
                    return mode == Modes.Ephemeral ? owner.Status(id.Id) is null : Finished(owner, id);
                });

        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
        {
            // Today's resume is a direct dispatch to the execution; the nearest here is an outside message to the instance.
            await Row("fork-join-resume, start and a direct resume", mode, async () =>
            {
                var id = Next("fj");
                owner.Worker.Post(new Message($"{id}#start", id, Kinds.Start, Start: NativeModel.Spec("fork-join-resume", mode)), fresh: true);
                await owner.PumpAsync();
                await store.AddAsync(new Message($"{id}#resume", id, NativeKinds.Resume, Json.Write(new ResumeStimulus(Reference.EventName)), KeepUntil: keep));
                await owner.PumpAsync(poll: true);
                return Finished(owner, id);
            });
            await Row("fork-join-resume, start and a published event", mode, async () =>
            {
                var id = Next("fj");
                owner.Worker.Post(new Message($"{id}#start", id, Kinds.Start, Start: NativeModel.Spec("fork-join-resume", mode)), fresh: true);
                await owner.PumpAsync();
                await Events.PublishAsync(store, new EventRow("event:" + Reference.EventName, $"e-{id}", null, keep));
                await owner.PumpAsync(poll: true);
                return Finished(owner, id);
            });
            await Row("fork-join, three event branches, three direct resumes", mode, async () =>
            {
                var id = Next("fj3");
                owner.Worker.Post(new Message($"{id}#start", id, Kinds.Start, Start: NativeModel.Spec("fork-join-3-events", mode)), fresh: true);
                await owner.PumpAsync();
                for (var i = 1; i <= 3; i++)
                    await store.AddAsync(new Message($"{id}#resume{i}", id, NativeKinds.Resume, Json.Write(new ResumeStimulus($"{Reference.EventName}-{i}")), KeepUntil: keep));
                await owner.PumpAsync(poll: true);
                return Finished(owner, id);
            });
        }

        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
            await Row("http-reference (case 1)", mode, async () =>
            {
                var response = await owner.Http.SendAsync(new HttpRequest("POST", Reference.HttpPath, Wf.Object(("firstName", "Ada"), ("lastName", "Lovelace"))), owner.Worker, mode, () => owner.PumpAsync());
                return response.StatusCode == 200 && response.Body?.GetValue<string>() == "Ada Lovelace";
            });

        await Row("respond, then continue (case 2)", Modes.Balanced, async () =>
        {
            string? id = null;
            owner.Live.OnRelease = i => id = i;
            var response = await owner.Http.SendAsync(new HttpRequest("POST", "orders/respond-continue", Wf.Object(("id", 7))), owner.Worker, Modes.Balanced, () => owner.PumpAsync(until: () => id is not null));
            await owner.PumpAsync(until: () => owner.Status(id!) == InstanceStatus.Finished);
            owner.Live.OnRelease = null;
            return response.Body?.GetValue<string>() == "accepted 7" && owner.State(id!)!.Variables["output"]?.GetValue<string>() == "shipped 7";
        });
        foreach (var (path, what) in new[] { ("quotes", "off-step work"), ("slow", "a 20 ms Delay") })
            foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced })
                await Row($"{what} before the response (case 3)", mode, async () =>
                {
                    var released = false;
                    owner.Live.OnRelease = _ => released = true;
                    var response = await owner.Http.SendAsync(new HttpRequest("POST", path), owner.Worker, mode, () => owner.PumpAsync(until: () => released));
                    owner.Live.OnRelease = null;
                    return response.StatusCode == 200;
                });
        await Row("resume at an HttpEndpoint, one node: both requests (case 4)", Modes.Balanced, async () =>
        {
            var order = 1000 + ++n;
            var first = await owner.Http.SendAsync(new HttpRequest("POST", "orders", Wf.Object(("id", order))), owner.Worker, Modes.Balanced, () => owner.PumpAsync());
            var second = await owner.Http.SendAsync(new HttpRequest("POST", $"orders/{order}/approve", Wf.Object(("by", "sam"))), owner.Worker, Modes.Balanced, () => owner.PumpAsync(poll: true));
            return first.StatusCode == 200 && second.Body?.GetValue<string>() == $"approved {order} by sam";
        });
        await Row("resume at an HttpEndpoint, second request on another node (case 4)", Modes.Balanced, async () =>
        {
            var order = 1000 + ++n;
            var first = await owner.Http.SendAsync(new HttpRequest("POST", "orders", Wf.Object(("id", order))), owner.Worker, Modes.Balanced, () => owner.PumpAsync());
            var second = await other.Http.SendAsync(new HttpRequest("POST", $"orders/{order}/approve", Wf.Object(("by", "kim"))), other.Worker, Modes.Balanced, async () =>
            {
                await owner.PumpAsync(poll: true);
                await other.PumpAsync();
            });
            return first.StatusCode == 200 && second.Body?.GetValue<string>() == $"approved {order} by kim";
        });

        await using var check = Create();
        var left = await check.Messages.CountAsync(m => !m.Done);
        Console.WriteLine($"check: {await check.Instances.CountAsync()} instances, {left} messages left, {await check.Subscriptions.CountAsync()} subscriptions left");
        return failures + (left == 0 ? 0 : 1);
    }
}

/// <summary>The model's own work, in memory: no database, one worker, the in-memory store.</summary>
static class Memory
{
    public static async Task<int> RunAsync(int runs)
    {
        var registry = Reference.Registry();
        Console.WriteLine($"| Run | Mode | Allocated per run | Time per run | Commits | State bytes per commit (max) |");
        Console.WriteLine("|---|---|---:|---:|---:|---:|");
        async Task Measure(string name, string mode, Func<Owner, Sizing, int, Task> body)
        {
            var time = 1_000_000L;
            var sizing = new Sizing(new InMemoryStore(1, () => time));
            var owner = new Owner("n1", 0, sizing, registry, new LiveNetwork(), () => time);
            await owner.StartAsync();
            for (var i = 0; i < 200; i++) await body(owner, sizing, -i - 1);     // warm up
            sizing.Reset();
            GC.Collect();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var clock = Stopwatch.StartNew();
            for (var i = 0; i < runs; i++) await body(owner, sizing, i);
            clock.Stop();
            var allocated = (GC.GetAllocatedBytesForCurrentThread() - before) / runs;
            var bytes = sizing.Rows == 0 ? "-" : $"{sizing.StateBytes / sizing.Rows} ({sizing.MaxStateBytes})";
            Console.WriteLine($"| {name} | {mode} | {allocated / 1024.0:F1} KiB | {clock.Elapsed.TotalMicroseconds / runs:F0} µs | {(double)sizing.Commits / runs:F1} | {bytes} |");
        }

        Message Start(string workflow, string mode, int i) => new($"{workflow}-{mode}-{i}#start", InstanceRef.In(0, $"{workflow}-{mode}-{i}"), Kinds.Start, Start: NativeModel.Spec(workflow, mode));
        foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
        {
            await Measure("toy model: ten no-ops (the kernel alone)", mode, async (o, _, i) =>
            {
                var toy = mode switch { Modes.Ephemeral => Kernel.Sim.Sequence.Ephemeral, Modes.Durable => Kernel.Sim.Sequence.Durable, _ => Kernel.Sim.Sequence.Balanced };
                o.Worker.Post(new Message($"toy-{mode}-{i}", InstanceRef.In(0, $"toy-{mode}-{i}"), Kinds.Start, Start: Kernel.Sim.Sequence.Spec(10, toy)), fresh: true);
                await o.PumpAsync();
            });
            foreach (var lines in new[] { 10, 40 })
                await Measure($"writeline-sequence, {lines}", mode, async (o, _, i) =>
                {
                    o.Worker.Post(Start($"writeline-sequence-{lines}", mode, i), fresh: true);
                    await o.PumpAsync();
                });
            await Measure("fork-join-resume, start and direct resume", mode, async (o, _, i) =>
            {
                var start = Start("fork-join-resume", mode, i);
                o.Worker.Post(start, fresh: true);
                await o.PumpAsync();
                o.Worker.Post(new Message($"{start.Target}#resume", start.Target, NativeKinds.Resume, Json.Write(new ResumeStimulus(Reference.EventName))));
                await o.PumpAsync();
            });
            await Measure("http-reference", mode, async (o, _, _) =>
            {
                var response = await o.Http.SendAsync(new HttpRequest("POST", Reference.HttpPath, Wf.Object(("firstName", "Ada"), ("lastName", "Lovelace"))), o.Worker, mode, () => o.PumpAsync());
                if (response.StatusCode != 200) throw new InvalidOperationException("no answer");
            });
        }
        return 0;
    }
}

sealed class Probe(Dictionary<string, JsonNode?> variables) : IScope
{
    public JsonNode? Variable(string name) => variables.GetValueOrDefault(name);
    public JsonNode? Result(string node) => null;
}
