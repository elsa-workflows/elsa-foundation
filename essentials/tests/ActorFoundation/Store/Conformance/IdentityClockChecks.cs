namespace Kernel.Store.Conformance;

internal static class IdentityClockChecks
{
    internal static readonly Case Case=new("terminal metadata: state-free clock changes survive cold and queued turns",Level.Required,Run);
    private static async Task Run(Context c)
    {
        const string id="g0/key/901";
        var model=new ClockProbe();var transport=new Replies();
        var lease=await c.Take(0);
        var creator=new Message("clock-creator",new(id),Kinds.Start,Start:new(model.Key,"clock",Ephemeral:true)).WithInboxScope(long.MaxValue);
        await c.Lands(()=>c.Commit(lease,0,[Make.Turn("0/authority",sent:[creator],journal:null)]));
        await c.S.ReleaseAsync(0,lease.Epoch);
        Worker Owner()=>new(Guid.NewGuid().ToString("N"),0,c.S,new WorkflowInstance([model]),new WorkerOptions{Transport=transport});
        async Task Activate(Worker w){await new DurableHost([w]).ActivateAsync(0);await w.PollAsync();}
        async Task Pump(Worker w){await w.WokenAsync(null);for(var i=0;i<6;i++)await w.RunAsync();}
        var owner=Owner();await Activate(owner);await Pump(owner);
        var initial=(await c.S.LoadAsync([id])).Single();
        Console.WriteLine($"clock fixture: identity={initial.IdentityOnly}, revision={initial.Revision}, clock={c.S.CommitClockAvailable}");
        Context.That(initial is {IdentityOnly:true,Status:InstanceStatus.Finished,State:"null"},"clock fixture did not complete state-free");
        var original=CommitClockFacts.Read(initial);
        Context.That(!c.S.CommitClockAvailable || original?.GetValueOrDefault(WorkflowCommitClock.Terminal) is not null,"owner terminal clock missing");
        await owner.DisposeAsync();owner=Owner();await Activate(owner);
        async Task Queue(string kind,string? payload=null)
        {
            var serial=Guid.NewGuid().ToString("N");
            Context.That(owner.Post(new(serial,new(id),kind,payload,ReplyTo:ReplyTo.Of("observer",serial,long.MaxValue))),"clock request post");
            await owner.LoadAsync();owner.RunTurn(id);
        }
        var beforeReplies=transport.Items.Count;
        await Queue("capture");
        Context.Equal(beforeReplies,transport.Items.Count,"metadata replied before its clock commit");
        await owner.FlushAsync();
        var captured=(await c.S.LoadAsync([id])).Single();
        var capturedReply=transport.Items.Last().Item;
        Console.WriteLine($"clock capture: revision={captured.Revision}, replies={transport.Items.Count}, facts={captured.CommitTimes}");
        await Queue(Kinds.Delete);await owner.FlushAsync();
        Context.Equal(captured,(await c.S.LoadAsync([id])).Single(),"observation-only protected Delete wrote a checkpoint");
        if(!c.S.CommitClockAvailable)
        {
            Context.That(transport.Items.Any(r=>r.Item is "clock-unavailable") && captured.CommitTimes is null,"unsupported clock capture appeared successful");
            await owner.DisposeAsync();return;
        }
        var reference=(string)capturedReply!;
        var facts=CommitClockFacts.Read(captured)!;
        Context.That(facts.TryGetValue(reference,out var timestamp) && timestamp is not null,"successful metadata capture lost its commit clock fact");
        Context.That(original!.All(p=>facts.TryGetValue(p.Key,out var value)&&value==p.Value),"metadata restamped an existing/actor-owned fact");
        await owner.DisposeAsync();owner=Owner();await Activate(owner);
        await Queue("resolve",Json.Write(reference));await owner.FlushAsync();
        Context.Equal(timestamp,(long?)transport.Items.Last().Item,"cold metadata could not resolve captured fact");
        await Queue("release",Json.Write(WorkflowCommitClock.Terminal));await owner.FlushAsync();
        Context.That(transport.Items.Last().Item is "owner-fact-protected","metadata released an actor-owned fact");
        // Queue a second metadata delivery before the first flush. The Worker serializes its turns;
        // both replies must wait for their own durable outcome rather than use an invented revision.
        var count=transport.Items.Count;
        await Queue("release",Json.Write(reference));await Queue("capture");
        Context.Equal(count,transport.Items.Count,"queued metadata replied before flush");
        await owner.FlushAsync();await Pump(owner);
        var final=(await c.S.LoadAsync([id])).Single();var remaining=CommitClockFacts.Read(final)!;
        Context.That(!remaining.ContainsKey(reference),"metadata release was lost");
        Context.That(remaining.Count==original!.Count+1 && remaining.All(p=>p.Value is not null),"queued capture was lost or not stamped");
        Context.That(original.All(p=>remaining.TryGetValue(p.Key,out var value)&&value==p.Value),"release changed owner facts");
        await owner.DisposeAsync();var current=await c.Take(0);
        var stale=captured with{Revision=captured.Revision+1};
        await c.Refused(()=>c.Commit(current,0,[Make.Turn(id,stale,expected:captured.Revision),Make.New("0/clock-unrelated")]),"stale metadata revision");
        Context.That((await c.S.LoadAsync(["0/clock-unrelated"])).Count==0 && (await c.S.LoadAsync([id])).Single()==final,"stale metadata partially committed");
        await c.S.ReleaseAsync(0,current.Epoch);
    }
    private sealed class Replies:IReplyTransport
    {
        internal readonly List<Reply> Items=[];
        public void Send(string node,Reply reply)=>Items.Add(reply);
    }
    private sealed class ClockProbe:IExecutionModel,IStateFreeTerminalMetadata
    {
        public string Key=>"state-free-clock";public Type StateType=>typeof(ValueState);public int StateVersion=>1;
        public object CreateState(StartSpec s)=>new ValueState{Value=1};public object UpgradeState(string s,int v)=>throw new NotSupportedException();
        public ValueTask TurnAsync(ITurn t,CancellationToken c){if(t.Clock.Available)t.Clock.Capture();t.SetStatus(InstanceStatus.Finished);return ValueTask.CompletedTask;}
        public ValueTask<object?> ExecuteJobAsync(IJob j,CancellationToken c)=>throw new NotSupportedException();
        public bool HandlesMetadata(Message m)=>m.Kind is "capture" or "release" or "resolve";
        public ValueTask MetadataAsync(ITurn t,InstanceStatus s,CancellationToken c)=>throw new InvalidOperationException("identity-only metadata deserialized state");
        public void TerminateMetadata(object s)=>throw new InvalidOperationException();
        public bool ProtectsDeletion(object s,long now,ICommitClock clock)=>throw new InvalidOperationException("identity-only deletion deserialized state");
        public bool CanUseIdentityOnly(object s,InstanceStatus status,ICommitClock clock)=>true;
        public bool ProtectsIdentityDeletion(TerminalIdentity identity,long now,ICommitClockObservation clock)
        {
            Context.That(clock is not ICommitClock,"identity protection exposed a mutable clock");
            Context.That(!clock.Available || clock.Resolve(WorkflowCommitClock.Terminal) is not null,"identity protection lost owner facts");
            return true;
        }
        public ValueTask IdentityMetadataAsync(IIdentityMetadataTurn t,CancellationToken c)
        {
            foreach(var m in t.Inbox)
            {
                try
                {
                    object? result=m.Kind switch{"capture"=>t.Clock.Capture(),"resolve"=>t.Clock.Resolve(m.Read<string>()),_=>Release(t.Clock,m.Read<string>())};
                    t.Reply(m,result);
                }
                catch(NotSupportedException){t.Reply(m,"clock-unavailable");}
                catch(ArgumentException){t.Reply(m,"owner-fact-protected");}
            }
            return ValueTask.CompletedTask;
        }
        private static string Release(ICommitClock clock,string reference){clock.Release(reference);return "released";}
    }
}
