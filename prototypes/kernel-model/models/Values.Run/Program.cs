using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Runtime;
using Kernel.Native.Run;
using Kernel.Values;
using Kernel.Workflows;
using Microsoft.EntityFrameworkCore;
using System.Text;

var root = Directory.CreateTempSubdirectory("kernel-values-");
try
{
    await ValueFaults.Run(root.FullName);
    foreach (var length in new[] { 4096, 102400 })
    {
        foreach (var reference in new[] { false, true })
        {
            var folder = Directory.CreateDirectory(Path.Combine(root.FullName, length + "-" + reference));
            await Measure(folder.FullName, new Source(length, 4229), reference);
        }
    }
}
finally { root.Delete(recursive: true); }

static async Task Measure(string directory, Source source, bool reference)
{
    var dbFile = Path.Combine(directory, "kernel.db");
    var objects = Directory.CreateDirectory(Path.Combine(directory, "values")).FullName;
    var counts = new Counting();
    var options = new DbContextOptionsBuilder<KernelDb>().UseSqlite($"Data Source={dbFile};Pooling=False")
        .AddInterceptors(counts, counts.Transactions).Options;
    int EngineCalls() => counts.Commands + counts.Transactions.Begun + counts.Transactions.Ended;
    int workerCalls = 0;
    KernelDb Db() => new(options);
    var store = new EfStore(Db, postgres: false, fenceInBatch: true);
    await store.CreateAsync(1);
    var values = new DirectoryValues(objects);
    var model = new ValueModel(values, reference);
    const string id = "0/value";
    Worker Owner(string name, ValueModel current) => new(name, 0, store, new WorkflowInstance([current]));
    var worker = Owner("first", model);
    await new DurableHost([worker]).ActivateAsync(0);
    await worker.PollAsync();
    var actor = new InstanceRef(id);
    worker.Post(new("start", actor, Kinds.Start, Json.Write(source), Start: new(model.Key, "measurement")), fresh: true);
    async Task<InstanceRow> Row(Func<InstanceRow, bool> done)
    {
        for (var i = 0; i < 500; i++)
        {
            int before = EngineCalls();
            await worker.RunAsync();
            workerCalls += EngineCalls() - before;
            var row = (await store.LoadAsync([id])).SingleOrDefault();
            if (row?.Fault is not null) throw new Exception(row.Fault);
            if (row is not null && done(row)) return row;
            await Task.Delay(1);
        }
        throw new TimeoutException("Committed checkpoint not reached.");
    }

    var first = await Row(r => Json.Read<ValueState>(r.State).Checkpoints == 1);
    var creationCalls = workerCalls;
    long stateBytes = 0, journalBytes = 0;
    var row = first;
    var expected = source.Bytes();
    var expectedRef = ValueRef.For(expected);
    var inline = Convert.ToBase64String(expected);
    for (int i = 1; i <= 10; i++)
    {
        if (i > 1)
        {
            worker.Post(new("tick-" + i, actor, "tick"));
            row = await Row(r => r.Revision == first.Revision + i - 1);
        }
        var state = Json.Read<ValueState>(row.State);
        await using var db = Db();
        var journal = await db.Journal.SingleAsync(j => j.Instance == id && j.Revision == row.Revision);
        if (state.Checkpoints != i || (reference
            ? state.Inline is not null || state.Reference != expectedRef || Json.Read<ValueRef[]>(journal.Entries).Single() != expectedRef
            : state.Inline != inline || state.Reference is not null || Json.Read<string[]>(journal.Entries).Single() != inline))
            throw new Exception("Checkpoint or journal does not contain exactly the expected value.");
        stateBytes += Encoding.UTF8.GetByteCount(row.State);
        journalBytes += Encoding.UTF8.GetByteCount(journal.Entries);
    }
    var updateCalls = workerCalls - creationCalls;
    var firstIo = (values.Puts, values.Reads);
    await worker.DisposeAsync();
    var restored = new DirectoryValues(objects);
    worker = Owner("replacement", new ValueModel(restored, reference));
    await new DurableHost([worker]).ActivateAsync(0);
    int beforeAdd = EngineCalls();
    await store.AddAsync(new("read", actor, "read"));
    var addCalls = EngineCalls() - beforeAdd;
    await worker.WokenAsync(null);
    var final = await Row(r => r.Status == InstanceStatus.Finished);
    var last = Json.Read<ValueState>(final.State);
    // Independent direct read after the fresh workflow owner resolved the reference; excluded from workflow I/O counts.
    var restoredReads = restored.Reads;
    var bytes = reference ? await restored.ReadAsync(last.Reference!, default) : Convert.FromBase64String(last.Inline!);
    if (!bytes.SequenceEqual(expected) || (reference && restoredReads != 1)) throw new Exception("Recovered bytes differ.");
    Console.WriteLine($"{source.Length} raw bytes; {(reference ? "reference" : "inline")}; 10 value-bearing commits; " +
        $"state UTF8={stateBytes}; journal UTF8={journalBytes}; puts={firstIo.Puts}; reads during puts={firstIo.Reads}; fresh-owner reads={restoredReads}; " +
        $"SQLite calls create+first={creationCalls}, next nine={updateCalls}, outside read add={addCalls}, " +
        $"read processing={workerCalls - creationCalls - updateCalls}");
}
