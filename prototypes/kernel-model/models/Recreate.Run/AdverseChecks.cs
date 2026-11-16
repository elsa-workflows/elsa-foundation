using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Workflows;

internal static class AdverseChecks
{
    internal static async Task Run(string label,IKernelStore store,IKeyAdmission ingress)
    {
        foreach (var cut in new[] { "before", "after", "held" })
        {
            var gate=new CutStore(store);
            var request=new KeyRequest("cut-"+Guid.NewGuid().ToString("N"),"first",Kinds.Start,
                Start:new("held-probe",cut=="held"?"held":"terminal",Ephemeral:true),KeepForMillis:1);
            var receipt=await ingress.AcceptAsync(request);
            var id=receipt.RunId!;
            Worker Owner() => new(Guid.NewGuid().ToString("N"),0,gate,new WorkflowInstance([new HeldProbe()]));
            async Task Activate(Worker owner) { await new DurableHost([owner]).ActivateAsync(0); await owner.PollAsync(); }
            async Task Pump(Worker owner) { await owner.WokenAsync(null);for(int i=0;i<20;i++) await owner.RunAsync(); }
            var old=Owner();await Activate(old);await old.LoadAsync();await old.FlushAsync();
            var oldEpoch=old.Epoch;
            old.RunTurn(id);
            if(cut!="held")gate.Cut=cut;
            await old.FlushAsync();
            var row=(await store.LoadAsync([id])).SingleOrDefault();
            Harness.Check(cut switch { "before"=>row is null,"after"=>row is { Status:InstanceStatus.Finished,State:"null" },_=>row is { Status:InstanceStatus.Running } },"cut did not reach its intended durable boundary");
            if(cut!="held")Harness.Check(gate.Hits==1,"unknown commit cut not hit");
            // Explicitly release the persisted old epoch: an unknown answer reset the old Worker's local ownership.
            await store.ReleaseAsync(0,oldEpoch);
            await old.DisposeAsync();
            var next=Owner();await Activate(next);
            if(cut=="held")await store.AddAsync(new(Guid.NewGuid().ToString("N"),new(id),"finish"));
            await Pump(next);
            row=(await store.LoadAsync([id])).Single();
            Harness.Check(row.Status==InstanceStatus.Finished && (cut=="held" || row.State=="null"),"replacement lost terminal identity or held start");
            Harness.Check(await ingress.AcceptAsync(request)==receipt,"unknown commit changed admission receipt");
            Harness.Check((await ingress.AcceptAsync(request with {Operation="new"})).Outcome=="in-use","present identity displaced");
            await store.AddAsync(new(Guid.NewGuid().ToString("N"),new(id),Kinds.Delete));await Pump(next);
            Harness.Check((await store.LoadAsync([id])).Single().Retired,"recovered identity did not retire");
            await next.DisposeAsync();
            Console.WriteLine($"{label}: keyed ephemeral {cut} commit/recovery control passed.");
        }
    }
}
internal sealed class HeldState { public bool Started {get;set;} }
internal sealed class HeldProbe : IExecutionModel
{
    public string Key=>"held-probe";public Type StateType=>typeof(HeldState);public int StateVersion=>1;
    public object CreateState(StartSpec s)=>new HeldState();
    public object UpgradeState(string s,int v)=>throw new NotSupportedException();
    public ValueTask<object?> ExecuteJobAsync(IJob j,CancellationToken c)=>throw new NotSupportedException();
    public ValueTask TurnAsync(ITurn turn,CancellationToken ct)
    {
        foreach(var m in turn.Inbox)
        {
            if(m.Kind==Kinds.Start && turn.Instance.Definition=="held") { ((HeldState)turn.State).Started=true;turn.Hold(m); }
            if(m.Kind=="finish" || turn.Instance.Definition=="terminal")turn.SetStatus(InstanceStatus.Finished);
        }
        return ValueTask.CompletedTask;
    }
}
