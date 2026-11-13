using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Values;
using Kernel.Workflows;

internal static class ValueFaults
{
    public static async Task Run(string root)
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, "faults")).FullName;
        var source = new Source(4096, 11);
        var reference = ValueRef.For(source.Bytes());
        string blob = Path.Combine(directory, reference.Sha256);
        long now = 100;
        var store = new InMemoryStore(1, () => now);
        var values = new DirectoryValues(directory);
        var blocked = new PausedAck(values);
        var actor = new InstanceRef("0/value");
        Worker Owner(string name, IValueStore blobs) => new(name, 0, store,
            new WorkflowInstance([new ValueModel(blobs, true)]), new WorkerOptions { Monotonic = () => now });
        var worker = Owner("first", blocked);
        await new DurableHost([worker]).ActivateAsync(0);
        await worker.PollAsync();
        var start = new StartSpec("value-reference", "faults");
        worker.Post(new("start", actor, Kinds.Start, Json.Write(source), Start: start), fresh: true);
        worker.RunTurn(actor.Id);
        await worker.FlushAsync();
        await blocked.Written.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Require(File.Exists(blob) && State().Reference is null && store.Messages.ContainsKey(Worker.TicketId(actor.Id, "put")),
            "blob exists before acknowledgement with only the durable put intent committed");
        // Owner loses the answer after the put succeeded. Replacement executes the same durable ticket again.
        var previous = worker;
        now += 11_000;
        var replacementValues = new DirectoryValues(directory);
        worker = Owner("second", replacementValues);
        await new DurableHost([worker]).ActivateAsync(0);
        await Pump(() => State().Reference is not null);
        blocked.Release.TrySetResult();
        for (var pass = 0; pass < 1000 && !previous.Ready.Contains(actor.Id) && previous.Owns; pass++)
            await Task.Delay(1);
        await previous.RunAsync();  // any late speculative result must fail its original epoch fence
        Require(State().Reference == reference && replacementValues.Puts == 1 && Directory.GetFiles(directory).Length == 1 &&
            (await values.ReadAsync(reference, default)).SequenceEqual(source.Bytes()), "idempotent put after lost acknowledgement");
        Console.WriteLine("Lost put acknowledgement: replacement committed the same reference and exact bytes.");

        async Task Corruption(string label, Action alter)
        {
            // Each case uses a new embedded worker/store but reads the previously published object.
            store = new InMemoryStore(1, () => now);
            worker = Owner(label, values);
            await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync();
            worker.Post(new("start", actor, Kinds.Start, Json.Write(source), Start: start), fresh: true);
            await Pump(() => State().Reference is not null);
            alter();
            worker.Post(new("read", actor, "read"));
            await Pump(() => store.Instances[actor.Id].Status == InstanceStatus.Faulted);
            var fault = store.Instances[actor.Id].Fault;
            Require(fault?.Contains("Value job failed:") == true && State().Reference == reference, "read fault kept checkpoint");
            Console.WriteLine(label + ": referenced read failed and retained the checkpoint");
        }
        await Corruption("missing", () => File.Delete(blob));
        await Corruption("truncated", () => File.WriteAllBytes(blob, [0]));
        File.Delete(blob);
        await Corruption("corrupt", () =>
        {
            var bytes = File.ReadAllBytes(blob); bytes[0] ^= 1; File.WriteAllBytes(blob, bytes);
        });
        File.Delete(blob);

        store = new InMemoryStore(1, () => now);
        worker = Owner("orphan", values);
        await new DurableHost([worker]).ActivateAsync(0); await worker.PollAsync();
        store.Refuses = row => row.Status == InstanceStatus.Running && Json.Read<ValueState>(row.State).Reference is not null;
        worker.Post(new("start", actor, Kinds.Start, Json.Write(source), Start: start), fresh: true);
        await Pump(() => store.Instances.TryGetValue(actor.Id, out var parked) && parked.Status == InstanceStatus.Faulted);
        Require(File.Exists(blob) && State().Reference is null &&
            store.Journal.Values.All(j => !j.Entries.Contains(reference.Sha256)), "orphan after a refused workflow commit");
        Console.WriteLine("Refused reference checkpoint: published object remains without a state or journal reference.");

        ValueState State() => store.Instances.TryGetValue(actor.Id, out var row) ? Json.Read<ValueState>(row.State) : new();
        async Task Pump(Func<bool> until)
        {
            await worker.WokenAsync(null);
            for (var i = 0; i < 1000 && !until(); i++) { await worker.RunAsync(); await Task.Delay(1); }
            Require(until(), "worker did not reach witness boundary");
        }
    }

    private static void Require(bool value, string label)
    {
        if (!value) throw new Exception(label);
    }

    private sealed class PausedAck(IValueStore inner) : IValueStore
    {
        public TaskCompletionSource Written = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ValueRef> PutAsync(byte[] bytes, CancellationToken ct)
        {
            var saved = await inner.PutAsync(bytes, ct);
            Written.TrySetResult();
            await Release.Task;
            return saved;
        }
        public Task<byte[]> ReadAsync(ValueRef reference, CancellationToken ct) => inner.ReadAsync(reference, ct);
    }
}
