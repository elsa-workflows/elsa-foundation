namespace Kernel.Store.Conformance;

internal static class BatchIntegrityChecks
{
    internal static readonly Case Atomic = new("batch integrity: exact row identity, one mutation and raw retirement delete",Level.Required,Run);
    internal static readonly Case Capability = new("physical retirement: capability independent of positive creation",Level.Required,CapabilityRun);
    private static async Task Run(Context c)
    {
        const string a="g0/key/1",b="g0/key/2",other="0/other";
        var lease=await c.Take(0);
        InstanceRow Row(string id)=>Make.Row(id,1,status:InstanceStatus.Finished);
        await c.Lands(()=>c.Commit(lease,0,[Make.Turn(a,Row(a)),Make.Turn(b,Row(b)),Make.Turn(other,Row(other))]));
        var original=await c.S.ReadJournalAsync(0,0,100);
        var retireA=new TurnCommit(a,CreationProtocol.Retire(Row(a)),1,[],[],[],[],[],"[]",Delete:true);
        async Task Refuse(params TurnCommit[] turns)
        {
            await c.Refused(()=>c.Commit(0,lease.Epoch,3,[Make.New("0/unrelated"),..turns]));
            Context.That((await c.S.LoadAsync(["0/unrelated"])).Count==0,"partial unrelated row after malformed mixed batch");
            Context.That(original.SequenceEqual(await c.S.ReadJournalAsync(0,0,100)),"malformed batch changed history");
            Context.Equal(3L,(await c.S.ProjectedAsync(0)).Written,"malformed batch changed position");
            Context.Equal(Row(a),(await c.S.LoadAsync([a])).Single(),"malformed batch changed A");
            Context.Equal(Row(b),(await c.S.LoadAsync([b])).Single(),"malformed batch changed B");
        }
        await Refuse(retireA with {Row=CreationProtocol.Retire(Row(b))});
        await Refuse(retireA with {Instance=other});
        await Refuse(Make.Turn(other,Row(a) with {Revision=2},expected:1));
        await Refuse(retireA,Make.Turn(a,Row(a) with {Revision=2},expected:1));
        await c.Lands(()=>c.Commit(0,lease.Epoch,3,[retireA]));
        await c.Refused(()=>c.Commit(0,lease.Epoch,4,[Make.New("0/unrelated"),Make.Turn(other,Row(a) with {Revision=3},expected:2)]));
        Context.That((await c.S.LoadAsync(["0/unrelated"])).Count==0 && (await c.S.LoadAsync([a])).Single().Retired,"retired split update escaped atomic rejection");
        var racers=Enumerable.Range(0,8).Select(i=>c.Refused(()=>c.Commit(0,lease.Epoch,4,
            [Make.New("0/race-"+i),Make.Turn(a,Row(a) with {Revision=3},expected:2)])));
        await Task.WhenAll(racers);
        Context.That((await c.S.LoadAsync(Enumerable.Range(0,8).Select(i=>"0/race-"+i).ToArray())).Count==0,
            "racing rebirth leaked an unrelated row");
        Context.That((await c.S.LoadAsync([a])).Single().Retired,"racing rebirth replaced retirement");
        await c.Refused(()=>c.Commit(0,lease.Epoch,4,[Make.Delete(a,1)]));
        await c.Lands(()=>c.Commit(0,lease.Epoch,4,[Make.Delete(a,2)]));
        Context.That((await c.S.LoadAsync([a])).Count==0,"trusted raw delete did not remove marker at exact revision");
    }
    private static async Task CapabilityRun(Context c)
    {
        foreach(var positive in new[]{false,true})
        {
            var id="g0/key/"+(positive?19:18);
            var lease=await c.Take(0);
            var row=Make.Row(id,1,status:InstanceStatus.Finished) with {Model="counter"};
            await c.Lands(()=>c.Commit(lease,0,[Make.Turn(id,row)]));await c.S.ReleaseAsync(0,lease.Epoch);
            var wrapped=System.Reflection.DispatchProxy.Create<IKernelStore,NoPhysical>();
            var proxy=(NoPhysical)(object)wrapped;proxy.Inner=c.S;proxy.Positive=positive;
            await using var owner=new Worker("capability-"+positive,0,wrapped,new WorkflowInstance([new Kernel.Sim.Counter()]));
            await new DurableHost([owner]).ActivateAsync(0);
            await c.S.AddAsync(new Message("delete-"+positive,new(id),Kinds.Delete));await owner.WokenAsync(null);
            for(var i=0;i<8;i++)await owner.RunAsync();
            Context.Equal(0,proxy.Markers,"unsupported physical marker reached store");
            Context.That(!(await c.S.LoadAsync([id])).Single().Retired,"unsupported physical marker landed");
        }
    }
    public class NoPhysical:System.Reflection.DispatchProxy
    {
        public IKernelStore Inner=null!;public bool Positive;public int Markers;
        protected override object? Invoke(System.Reflection.MethodInfo? method,object?[]? args)
        {
            if(method!.Name=="get_PhysicalRetirementAvailable")return false;
            if(method.Name=="get_CreationIdentityAvailable")return Positive;
            if(method.Name=="CommitAsync" && args![0] is CommitBatch batch && batch.Turns.Any(t=>t.Row is {Retired:true,Creation:0}))Markers++;
            return method.Invoke(Inner,args);
        }
    }
}
