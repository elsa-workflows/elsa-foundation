namespace Kernel.Store.Conformance;

internal static class IdentityTerminalJobsChecks
{
    internal static readonly Case Case=new("terminal metadata: missing protocol still settles exact terminal job tickets",Level.Required,Run);
    private static async Task Run(Context c)
    {
        // Accepted raw profile; this does not assert an ordinary public creator produces the checkpoint.
        var serial=0;
        foreach(var facade in new[]{"capable","plain","typed-only"})
        foreach(var genuine in new[]{true,false})
        {
            var id="g0/key/"+(9700+(++serial));var model=new Trap();
            var a=InboxKey.Ticket(id,"job-a");var b=InboxKey.Ticket(id,"job-b");
            var row=Make.Row(id,3,state:"null",status:InstanceStatus.Finished)with{Model=model.Key,Fault="retained-ended-fault",
                Jobs=DurableKeyJson.WriteKeys(["job-a","job-b"]),CustomHandles=DurableKeyJson.WriteKeys(["unrelated-handle"]),
                IdentityOnly=true,IdentityMetadataRequired=true,
                CommitTimes=c.S.CommitClockAvailable?Json.Write(new Dictionary<string,long?>{{WorkflowCommitClock.Terminal,null},{"turn:retained",null}}):null};
            Message Ticket(InboxKey key,long? due)=>new Message(key.Id,new(id),Kinds.Job,Json.Write(new {Model=model.Key,Key=key==a?"job-a":"job-b",Arguments=(string?)null}),Due:due).WithInboxScope(key.Scope);
            var lookalike=new Message(a.Id,new(id),"ordinary-lookalike");
            var lease=await c.Take(0,"seed-"+serial);var messages=new List<Message>{Ticket(a,genuine?null:long.MaxValue),Ticket(b,long.MaxValue)};
            if(!genuine)messages.Add(lookalike);
            await c.Lands(()=>c.Commit(lease,0,[Make.Turn(id,row,sent:messages)]));await c.S.ReleaseAsync(0,lease.Epoch);
            var before=(await c.S.LoadAsync([id])).Single();
            IExecutionModel cold=facade switch{"capable"=>model,"plain"=>new PlainIdentityFacade(model),_=>new TypedOnly(model)};
            await using(var owner=new Worker("terminal-"+serial,0,c.S,new WorkflowInstance([cold])))
            {await new DurableHost([owner]).ActivateAsync(0);await owner.PollAsync();await owner.WokenAsync(null);for(var i=0;i<16;i++)await owner.RunAsync();}
            var after=(await c.S.LoadAsync([id])).Single();var keys=DurableKeyJson.ReadKeys(after.Jobs!);
            Context.That(keys.SequenceEqual(genuine?new[]{"job-b"}:new[]{"job-a","job-b"}),"terminal job cleanup lost typed provenance or unrelated membership");
            var inbox=(await c.All(0)).Select(s=>s.Message.InboxKey).ToHashSet();
            Context.That(inbox.Contains(b)&&inbox.Contains(a)==!genuine&&!inbox.Contains(lookalike.InboxKey),"terminal ticket/twin consumption mismatch");
            Context.That(after is {Retired:false,IdentityOnly:true,IdentityMetadataRequired:true,State:"null"}&&
                after.Status==before.Status&&after.Fault==before.Fault&&after.CustomHandles==before.CustomHandles&&after.CommitTimes==before.CommitTimes,
                "terminal cleanup changed protocol/status/clock/custom handles");
            Context.Equal(0,model.Work,"terminal cleanup executed model work");
            if(genuine)Context.Equal(before.Revision+1,after.Revision,"terminal cleanup revision");
            var stale=await c.Take(0,"stale-"+serial);var other="0/terminal-stale-"+serial;
            await c.Refused(()=>c.Commit(stale,0,[Make.New(other),Make.Turn(id,after with{Revision=after.Revision+1},expected:after.Revision-1)]));
            Context.That((await c.S.LoadAsync([other])).Count==0&&(await c.S.LoadAsync([id])).Single()==after,"stale cleanup mixed batch partially committed");
            await c.S.ReleaseAsync(0,stale.Epoch);
        }
    }
    private sealed class Trap:IExecutionModel,IStateFreeTerminalMetadata
    {
        public int Work;public string Key=>"terminal-job-probe";public Type StateType=>typeof(ValueState);public int StateVersion=>1;
        public object CreateState(StartSpec s)=>throw new InvalidOperationException("raw terminal fixture must not start");
        public object UpgradeState(string s,int v)=>throw new InvalidOperationException("no terminal model state");
        public ValueTask TurnAsync(ITurn t,CancellationToken c){Work++;throw new InvalidOperationException("no terminal model turn");}
        public ValueTask<object?> ExecuteJobAsync(IJob j,CancellationToken c){Work++;throw new InvalidOperationException("no terminal job execution");}
        public bool HandlesMetadata(Message m)=>m.Kind=="query";
        public ValueTask MetadataAsync(ITurn t,InstanceStatus s,CancellationToken c)=>throw new InvalidOperationException("no typed metadata");
        public void TerminateMetadata(object s)=>throw new InvalidOperationException("no typed metadata");
        public bool ProtectsDeletion(object s,long n,ICommitClock c)=>throw new InvalidOperationException("no typed state");
        public bool CanUseIdentityOnly(object s,InstanceStatus status,ICommitClock c)=>true;
        public bool ProtectsIdentityDeletion(TerminalIdentity i,long n,ICommitClockObservation c)=>true;
        public ValueTask IdentityMetadataAsync(IIdentityMetadataTurn t,CancellationToken c){foreach(var m in t.Inbox)t.Refuse(m);return ValueTask.CompletedTask;}
    }
    private sealed class TypedOnly(Trap model):IExecutionModel,IModelMetadata
    {
        public string Key=>model.Key;public Type StateType=>model.StateType;public int StateVersion=>model.StateVersion;
        public object CreateState(StartSpec s)=>model.CreateState(s);public object UpgradeState(string s,int v)=>model.UpgradeState(s,v);
        public ValueTask TurnAsync(ITurn t,CancellationToken c)=>model.TurnAsync(t,c);public ValueTask<object?> ExecuteJobAsync(IJob j,CancellationToken c)=>model.ExecuteJobAsync(j,c);
        public bool HandlesMetadata(Message m)=>model.HandlesMetadata(m);public ValueTask MetadataAsync(ITurn t,InstanceStatus s,CancellationToken c)=>model.MetadataAsync(t,s,c);
        public void TerminateMetadata(object s)=>model.TerminateMetadata(s);public bool ProtectsDeletion(object s,long n,ICommitClock c)=>model.ProtectsDeletion(s,n,c);
    }
}
