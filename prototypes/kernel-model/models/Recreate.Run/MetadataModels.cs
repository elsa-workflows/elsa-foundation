using Kernel.Durable;
using Kernel.Workflows;

public struct ValueState{public int Value{get;set;}}
public sealed class ValueProbe:IExecutionModel,IModelMetadata
{
    public string Key=>"value-probe";public Type StateType=>typeof(ValueState);public int StateVersion=>1;
    public object CreateState(StartSpec s)=>new ValueState{Value=1};
    public object UpgradeState(string state,int from)=>Json.Read<ValueState>(state);
    public ValueTask TurnAsync(ITurn turn,CancellationToken ct){turn.SetStatus(InstanceStatus.Finished);return ValueTask.CompletedTask;}
    public ValueTask<object?> ExecuteJobAsync(IJob job,CancellationToken ct)=>throw new NotSupportedException();
    public bool HandlesMetadata(Message m)=>m.Kind=="query";
    public ValueTask MetadataAsync(ITurn turn,InstanceStatus status,CancellationToken ct)=>ValueTask.CompletedTask;
    public void TerminateMetadata(object state){}
    public bool ProtectsDeletion(object state,long now,ICommitClock clock)=>false;
}

public sealed class StatefulValueProbe : IExecutionModel,IModelMetadata
{
    public bool Protect=true;public int TypedCalls;
    public string Key=>"stateful-value";public Type StateType=>typeof(ValueState);public int StateVersion=>1;
    public object CreateState(StartSpec s)=>new ValueState{Value=1};public object UpgradeState(string s,int v)=>Json.Read<ValueState>(s);
    public ValueTask TurnAsync(ITurn t,CancellationToken c){t.SetStatus(InstanceStatus.Finished);return ValueTask.CompletedTask;}
    public ValueTask<object?> ExecuteJobAsync(IJob j,CancellationToken c)=>throw new NotSupportedException();
    public bool HandlesMetadata(Message m)=>m.Kind=="query";
    public ValueTask MetadataAsync(ITurn t,InstanceStatus s,CancellationToken c){TypedCalls++;foreach(var m in t.Inbox)t.Reply(m,Request.Response,((ValueState)t.State).Value);return ValueTask.CompletedTask;}
    public void TerminateMetadata(object s){}
    public bool ProtectsDeletion(object s,long n,ICommitClock clock){TypedCalls++;if(((ValueState)s).Value!=1)throw new InvalidOperationException("lost metadata checkpoint");return Protect;}
}
public sealed class StateFreeValueProbe : IExecutionModel,IStateFreeTerminalMetadata
{
    public bool Protect=true;public int TypedCalls,IdentityCalls;
    public string Key=>"state-free-value";public Type StateType=>typeof(ValueState);public int StateVersion=>1;
    public object CreateState(StartSpec s)=>new ValueState{Value=1};public object UpgradeState(string s,int v)=>Json.Read<ValueState>(s);
    public ValueTask TurnAsync(ITurn t,CancellationToken c){t.SetStatus(InstanceStatus.Finished);return ValueTask.CompletedTask;}
    public ValueTask<object?> ExecuteJobAsync(IJob j,CancellationToken c)=>throw new NotSupportedException();
    public bool HandlesMetadata(Message m)=>m.Kind is "query" or "unsupported";
    public ValueTask MetadataAsync(ITurn t,InstanceStatus s,CancellationToken c){TypedCalls++;foreach(var m in t.Inbox)t.Reply(m,Request.Response,((ValueState)t.State).Value);return ValueTask.CompletedTask;}
    public void TerminateMetadata(object s){}
    public bool ProtectsDeletion(object s,long n,ICommitClock c){TypedCalls++;if(((ValueState)s).Value!=1)throw new InvalidOperationException("lost typed value");return Protect;}
    public bool CanUseIdentityOnly(object s,InstanceStatus status,ICommitClock c)=>((ValueState)s).Value==1;
    public bool ProtectsIdentityDeletion(TerminalIdentity identity,long now,ICommitClockObservation c){IdentityCalls++;if(c is ICommitClock)throw new InvalidOperationException("protection exposed mutable clock");return Protect;}
    public ValueTask IdentityMetadataAsync(IIdentityMetadataTurn t,CancellationToken c)
    {
        IdentityCalls++;foreach(var m in t.Inbox)if(m.Kind=="query")t.Reply(m,t.Identity.Status);
        return ValueTask.CompletedTask; // unsupported messages must be refused automatically
    }
}
public sealed class LegacyIdentityFacade(StateFreeValueProbe model):IExecutionModel,IModelMetadata
{
    public string Key=>model.Key;public Type StateType=>model.StateType;public int StateVersion=>model.StateVersion;
    public object CreateState(StartSpec s)=>model.CreateState(s);public object UpgradeState(string s,int v)=>model.UpgradeState(s,v);
    public ValueTask TurnAsync(ITurn t,CancellationToken c)=>model.TurnAsync(t,c);
    public ValueTask<object?> ExecuteJobAsync(IJob j,CancellationToken c)=>model.ExecuteJobAsync(j,c);
    public bool HandlesMetadata(Message m)=>model.HandlesMetadata(m);
    public ValueTask MetadataAsync(ITurn t,InstanceStatus s,CancellationToken c)=>model.MetadataAsync(t,s,c);
    public void TerminateMetadata(object s)=>model.TerminateMetadata(s);
    public bool ProtectsDeletion(object s,long n,ICommitClock c)=>model.ProtectsDeletion(s,n,c);
}

public sealed class NullCheckpointProbe:IExecutionModel,IModelMetadata
{
    public bool Protect=true;public int TypedCalls;
    public string Key=>"null-checkpoint";public Type StateType=>typeof(object);public int StateVersion=>1;
    public object CreateState(StartSpec s)=>null!;public object UpgradeState(string s,int v)=>null!;
    public ValueTask TurnAsync(ITurn t,CancellationToken c){t.SetStatus(InstanceStatus.Finished);return ValueTask.CompletedTask;}
    public ValueTask<object?> ExecuteJobAsync(IJob j,CancellationToken c)=>throw new NotSupportedException();
    public bool HandlesMetadata(Message m)=>m.Kind=="query";
    public ValueTask MetadataAsync(ITurn t,InstanceStatus s,CancellationToken c){TypedCalls++;if(t.State is not null)throw new InvalidOperationException("ordinary null was reinterpreted");foreach(var m in t.Inbox)t.Reply(m,Request.Response,"typed-null");return ValueTask.CompletedTask;}
    public void TerminateMetadata(object s){}
    public bool ProtectsDeletion(object s,long n,ICommitClock c){TypedCalls++;if(s is not null)throw new InvalidOperationException("ordinary null was reinterpreted");return Protect;}
}
