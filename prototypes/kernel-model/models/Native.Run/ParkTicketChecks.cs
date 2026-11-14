using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Native.Run;

// Worker-created rows and tickets, driven exclusively through public model and metadata operations.
internal static class ParkTicketChecks
{
    private sealed class Probe(bool copy) : Kernel.Sim.Model<List<string>>, IModelMetadata
    {
        public override string Key => "park-ticket-identity";
        public readonly Dictionary<string, TaskCompletionSource<object?>> Pending = [];
        public TaskCompletionSource<object?>? Copy;
        public string? CopyId;
        public int Queries;
        public override ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellation)
        {
            var result = new TaskCompletionSource<object?>();
            if (Pending.ContainsKey(job.Key)) Copy = result;
            else Pending.Add(job.Key, result);
            cancellation.Register(() => result.TrySetCanceled(cancellation));
            return new(result.Task);
        }
        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var message in turn.Inbox)
                if (message.Kind == Kinds.Start)
                {
                    state.Add("checkpoint");
                    turn.StartJob("first");
                    turn.StartJob("sibling");
                    if (copy) CopyId = turn.Send(turn.Instance.Ref, Kinds.Job,
                        new { Model = Key, Key = "sibling", Arguments = (string?)null });
                }
                else if (message.Kind == Kinds.JobFinished) throw new InvalidOperationException("original fault");
        }
        public bool HandlesMetadata(Message message) => message.Kind == "metadata";
        public ValueTask MetadataAsync(ITurn turn, InstanceStatus status, CancellationToken cancellation)
        {
            Queries++;
            if (!copy && Queries == 1)
            {
                turn.SetStatus(InstanceStatus.Running); // the actor must preserve its actual terminal status
                foreach (var message in turn.Inbox.Where(m => m.Kind == Kinds.JobFinished)) turn.Hold(message);
            }
            return ValueTask.CompletedTask;
        }
        public void TerminateMetadata(object state) { }
        public bool ProtectsDeletion(object state, long now, ICommitClock clock) => false;
    }

    private sealed class Fixture(bool sqlite, bool qualified, Probe model) : IAsyncDisposable
    {
        public readonly InstanceRef Id = InstanceRef.In(0, "ticket-park");
        public IKernelStore Store = null!;
        public Worker Owner = null!;
        public long Generation => qualified ? 1 : 0;
        public string Ticket => Worker.TicketId(Id.Id, "sibling", Generation);
        public async Task Start()
        {
            if (sqlite)
            {
                var file = Path.Combine(Path.GetTempPath(), $"kernel-park-ticket-{Guid.NewGuid():N}.db");
                var options = new DbContextOptionsBuilder<KernelDb>().UseSqlite("Data Source=" + file).Options;
                var ef = new EfStore(() => new(options), false);
                await ef.CreateAsync(1);
                Store = ef;
            }
            else Store = new InMemoryStore(1, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            Owner = new Worker("ticket-owner", 0, Store, new WorkflowInstance([model]));
            await new DurableHost([Owner]).ActivateAsync(0);
            await Store.AddAsync(new("start", Id, Kinds.Start, Start: new(model.Key, "d",
                Creation: qualified ? new(1, 0, "ticket-check") : null)));
            await Owner.WokenAsync(null);
            await Pump(() => Task.FromResult(model.Pending.ContainsKey("sibling")));
            var completed = Owner.JobCompletion(Id.Id, "first", Generation)!;
            model.Pending["first"].SetResult("fault");
            await completed;
            await Pump(async () => (await Row()).Status == InstanceStatus.Faulted);
        }
        public async Task<InstanceRow> Row() => (await Store.LoadAsync([Id.Id])).Single();
        public async Task<bool> Has(string id) => (await Store.PollAsync(0, long.MaxValue, TimeSpan.Zero, 100))
            .Messages.Any(m => m.Message.Id == id);
        public async Task Pump(Func<Task<bool>> done)
        {
            for (var i = 0; i < 80 && Owner.Owns && !await done(); i++) await Owner.RunAsync();
            Require(await done(), "public worker sequence did not settle or ownership reset");
        }
        public async Task Query(string id)
        {
            await Store.AddAsync(new(id, Id, "metadata", TargetCreation: qualified ? Generation : null));
            await Owner.PollAsync();
            await Owner.LoadAsync();
            await Owner.FlushAsync(); // mark only: query and completion share the next model turn
        }
        public async Task Stop()
        {
            await Store.AddAsync(new("stop", Id, Kinds.Terminate, TargetCreation: qualified ? Generation : null));
            await Owner.WokenAsync(null);
            await Pump(async () => !await Has("stop"));
            Require(Owner.Owns, "termination must not reset ownership");
        }
        public ValueTask DisposeAsync() => Owner.DisposeAsync();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static async Task CopyAsync(bool sqlite, bool qualified, bool metadata)
    {
        var model = new Probe(copy: true);
        await using var f = new Fixture(sqlite, qualified, model);
        await f.Start();
        var before = await f.Row();
        Require(before.Jobs == Json.Write(new[] { "sibling" }), "Park must retain the genuine sibling");
        if (metadata) await f.Query("query");
        await f.Owner.PollAsync();
        await f.Owner.LoadAsync();
        await f.Owner.FlushAsync();
        await f.Pump(() => Task.FromResult(model.Copy is not null));
        model.Copy!.SetResult("copy only");
        await f.Pump(async () => !await f.Has(model.CopyId!));
        var after = await f.Row();
        Require(after.Status == before.Status && after.Fault == before.Fault && after.Jobs == before.Jobs,
            "a different durable wire ID with the same payload key must not retire the genuine registry entry");
        Require(await f.Has(f.Ticket) && !model.Pending["sibling"].Task.IsCompleted,
            "the genuine sibling must retain its ticket and running job");
        await f.Stop();
        Require(!await f.Has(f.Ticket) && model.Pending["sibling"].Task.IsCanceled && (await f.Row()).Jobs is null,
            "termination must consume the genuine ticket and cancel its task after the copy completes");
    }

    public static async Task HoldAsync(bool sqlite, bool qualified)
    {
        var model = new Probe(copy: false);
        await using var f = new Fixture(sqlite, qualified, model);
        await f.Start();
        var before = await f.Row();
        await f.Query("query-hold");
        var completed = f.Owner.JobCompletion(f.Id.Id, "sibling", f.Generation)!;
        model.Pending["sibling"].SetResult("held result");
        await completed;
        f.Owner.RunTurn(f.Id.Id);
        await f.Owner.FlushAsync();
        var held = await f.Row();
        Require(held.Status == before.Status && held.Fault == before.Fault && model.Queries == 1,
            "metadata cannot reopen or replace the original fault");
        Require(held.Jobs == Json.Write(new[] { "sibling" }) && await f.Has(f.Ticket),
            "one held completion must produce one registry key and one physical ticket");
        await f.Query("query-release");
        await f.Pump(async () => (await f.Row()).Revision > held.Revision);
        Require((await f.Row()).Jobs is null && !await f.Has(f.Ticket) && model.Queries == 2,
            "releasing the result must remove its exact ticket and sole registry entry");
        await f.Stop();
        Require((await f.Row()).Fault == before.Fault, "later termination must retain the original fault");
    }
}
