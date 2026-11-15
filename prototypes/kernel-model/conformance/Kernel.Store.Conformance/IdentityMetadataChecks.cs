namespace Kernel.Store.Conformance;

internal static class IdentityMetadataChecks
{
    internal static readonly Case Case=new("terminal metadata: typed promotion and explicit state-free cold lifecycle",Level.Required,Run);
    private static async Task Run(Context c)
    {
        var serial=200;
        foreach(var history in new[]{History.Full,History.State,History.Minimal})
        foreach(var ephemeral in new[]{false,true})
        foreach(var profile in new[]{"raw-value","stateful","state-free","plain","null-checkpoint"})
        {
            IExecutionModel model=profile switch {"raw-value"=>new ValueProbe(),"stateful"=>new StatefulValueProbe(),"state-free"=>new StateFreeValueProbe(),"plain"=>new PlainIdentityFacade(new ValueProbe()),_=>new NullCheckpointProbe()};
            var id="g0/key/"+(++serial);var start=new StartSpec(model.Key,"terminal",Ephemeral:ephemeral,History:history);
            var lease=await c.Take(0);
            var creator=new Message("creator-"+serial,new(id),Kinds.Start,Start:start).WithInboxScope(long.MaxValue);
            await c.Lands(()=>c.Commit(lease,0,[Make.Turn("0/authority",sent:[creator],journal:null)]));await c.S.ReleaseAsync(0,lease.Epoch);
            var transport=new Replies();
            Worker Owner(IExecutionModel current)=>new(Guid.NewGuid().ToString("N"),0,c.S,new WorkflowInstance([current]),new WorkerOptions{Transport=transport});
            async Task Activate(Worker owner){await new DurableHost([owner]).ActivateAsync(0);await owner.PollAsync();}
            async Task Pump(Worker owner){await owner.WokenAsync(null);for(var i=0;i<12;i++)await owner.RunAsync();}
            var owner=Owner(model);await Activate(owner);await Pump(owner);
            var row=(await c.S.LoadAsync([id])).Single();var stateFree=ephemeral && profile is "state-free" or "plain";
            Context.That(row.Status==InstanceStatus.Finished && row.IdentityOnly==stateFree && row.IdentityMetadataRequired==(ephemeral && profile=="state-free") &&
                (stateFree || profile=="null-checkpoint"?row.State=="null":Json.Read<ValueState>(row.State).Value==1),"terminal profile payload/discriminator mismatch");
            await new Projector(c.S,0).RoundAsync();var view=(await c.S.ReadViewsAsync([id])).SingleOrDefault();
            Context.That(history==History.Minimal?view is null:view==new ViewRow(id,1,1),"terminal profile projection changed");
            await owner.DisposeAsync();owner=Owner(model);await Activate(owner);
            async Task Give(string kind)
            {
                var wire=Guid.NewGuid().ToString("N");
                await c.S.AddAsync(new(wire,new(id),kind,ReplyTo:ReplyTo.Of("observer",wire,long.MaxValue)));await Pump(owner);
            }
            if(profile is not ("raw-value" or "plain"))
            {
                await Give(Kinds.Delete);Context.That(!(await c.S.LoadAsync([id])).Single().Retired,"metadata protection bypassed");
                await Give("query");
                Context.That(transport.Items.Any(r=>stateFree?r.Item is InstanceStatus.Finished:profile=="null-checkpoint"?r.Item is "typed-null":r.Item is int value && value==1),"cold metadata query lost state or identity");
            }
            if(model is StateFreeValueProbe free && stateFree)
            {
                await Give("unsupported");Context.That(transport.Items.Any(r=>r.Item is IdentityMetadataUnavailable),"unsupported state-free metadata not refused");
                foreach(var facade in new IExecutionModel[]{new LegacyIdentityFacade(free),new PlainIdentityFacade(free)})
                {
                await owner.DisposeAsync();owner=Owner(facade);await Activate(owner);
                var previous=transport.Items.Count;
                await Give(Kinds.Delete);await Give("query");
                Context.That(transport.Items.Skip(previous).Count(r=>r.Item is IdentityMetadataUnavailable)==2 &&
                    (await c.S.LoadAsync([id])).Single() is {Retired:false,IdentityOnly:true,IdentityMetadataRequired:true,State:"null"},"missing state-free capability did not fail closed");
                Context.Equal(0,free.TypedCalls,"identity-only path invoked typed state callbacks");
                }
                await owner.DisposeAsync();owner=Owner(model);await Activate(owner);
            }
            if(model is NullCheckpointProbe nullModel)nullModel.Protect=false;
            if(model is StatefulValueProbe typed)typed.Protect=false;
            if(model is StateFreeValueProbe stateless)stateless.Protect=false;
            await Give(Kinds.Delete);
            Context.That((await c.S.LoadAsync([id])).Single() is {Retired:true,IdentityOnly:false},"cold metadata identity did not retire");
            await owner.DisposeAsync();
        }
        // Unkeyed inline completion keeps its existing zero-store rule, including metadata models.
        foreach(IExecutionModel model in new IExecutionModel[]{new ValueProbe(),new StatefulValueProbe(),new StateFreeValueProbe(),new NullCheckpointProbe()})
        {
            var store=System.Reflection.DispatchProxy.Create<IKernelStore,CountCalls>();var calls=(CountCalls)(object)store;calls.Inner=c.S;
            await using var owner=new Worker(Guid.NewGuid().ToString("N"),0,store,new WorkflowInstance([model]),new WorkerOptions{Monotonic=()=>0});
            await new DurableHost([owner]).ActivateAsync(0);await owner.PollAsync();calls.Calls=0;
            var id="0/inline-"+model.Key;
            Context.That(owner.Post(new("inline-"+model.Key,new(id),Kinds.Start,Start:new(model.Key,"terminal",Ephemeral:true)),fresh:true),"inline post");
            for(var i=0;i<8;i++)await owner.RunAsync();
            Context.Equal(0,calls.Calls,"unkeyed inline metadata completion added store IO");
            Context.That((await c.S.LoadAsync([id])).Count==0,"unkeyed inline metadata completion persisted a row");
        }
    }
    private sealed class Replies:IReplyTransport
    {
        internal readonly List<Reply> Items=[];
        public void Send(string node,Reply reply)=>Items.Add(reply);
    }
    public class CountCalls:System.Reflection.DispatchProxy
    {
        public IKernelStore Inner=null!;public int Calls;
        protected override object? Invoke(System.Reflection.MethodInfo? method,object?[]? args)
        {
            if(!method!.Name.StartsWith("get_",StringComparison.Ordinal))Calls++;
            return method.Invoke(Inner,args);
        }
    }
}
