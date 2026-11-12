using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Bpmn.Activities;
using Elsa.Activities.Bpmn.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;

var asm=typeof(RunBpmn).Assembly;
Console.WriteLine("Assembly: "+asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
var t=asm.GetType("Elsa.Activities.Bpmn.Internal.BpmnEmbeddingCheckpointEnricher",true)!;
var enricher=(IRuntimeCheckpointCommitEnricher)Activator.CreateInstance(t,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new object?[]{null,null},null)!;
var at=DateTimeOffset.UtcNow;
foreach(var (name,payload) in new[]{("typed-payload-wrong-format",JsonSerializer.SerializeToElement(new{ FormatVersion="bad",Invocation=new{} })),("typed-payload-not-object",JsonSerializer.SerializeToElement("malformed"))}) {
 var state=new ActivityExecutionState(new("invocation-r6","execution-r6","node-r6","authored-r6",RunBpmn.ActivityType,"1.0.0"), ActivityExecutionStatus.Suspended,null,at,at,null,null,null,null,null,null,[],[],0,0,new Dictionary<string,string>()) {
   PrivateState=new("invocation-r6",1,ValueEnvelope.Inline(new(typeof(RunBpmnState).FullName!,schemaVersion:1),payload,ValueProtectionPolicy.InstanceInline),"attempt-r6",at)
 };
 var bookmark=new BookmarkState("bookmark-r6","execution-r6","invocation-r6","node-r6","resume-r6","stimulus-r6","hash-r6",null,new Dictionary<string,string>(),at,null);
 var changes=new RuntimeCheckpointStateChangeSet(null,null,[new("invocation-r6",RuntimeStateChangeOperation.Upsert,state,new Dictionary<string,string>())],[new("bookmark-r6",RuntimeStateChangeOperation.Upsert,bookmark,new Dictionary<string,string>())],[],[],[]);
 var commit=new RuntimeCheckpointCommit("commit-r6",new("checkpoint-r6",RuntimeCheckpointNames.BookmarkCreated,"execution-r6",at,[],new Dictionary<string,string>()),changes,[],new Dictionary<string,string>());
 try { await enricher.EnrichAsync(commit); Console.WriteLine(name+": NO EXCEPTION"); }
 catch(Exception e) { Console.WriteLine(name+": "+e.GetType().FullName+" inner="+e.InnerException?.GetType().FullName+" message="+e.Message);Console.WriteLine(e.StackTrace); }
}

// Exercise cancellation's retained-predecessor path through the public committer boundary.
var badState=new ActivityExecutionState(new("retained-invocation-r6","native-execution-r6","retained-node-r6","authored-r6",RunBpmn.ActivityType,"1.0.0"),ActivityExecutionStatus.Suspended,null,at,at,null,null,null,null,null,null,[],[],0,0,new Dictionary<string,string>()) {
 PrivateState=new("retained-invocation-r6",1,ValueEnvelope.Inline(new(typeof(RunBpmnState).FullName!,schemaVersion:1),JsonSerializer.SerializeToElement("bad-owned-wrapper"),ValueProtectionPolicy.InstanceInline),"retained-attempt-r6",at)
};
var reads=0; var persistence=0;
var activities=Custom<IActivityExecutionStateStore>((m,a)=>m.Name=="FindAsync"?new ValueTask<ActivityExecutionState?>(ReadRetained()):throw new Exception("Unexpected activity store call: "+m.Name));
ActivityExecutionState ReadRetained(){ reads++; return badState; }
var boundaryType=asm.GetType("Elsa.Activities.Bpmn.Internal.BpmnNativeWorkBoundary",true)!;
var boundary=Activator.CreateInstance(boundaryType,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new object?[]{null,null,null,activities,null,null,null,TimeProvider.System,null,null,null,null,null,null},null)!;
var cancellationEnricher=(IRuntimeCheckpointCommitEnricher)Activator.CreateInstance(t,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new object?[]{activities,boundary},null)!;
var cancelled=badState with {Status=ActivityExecutionStatus.Cancelled,PrivateState=null};
var cancellationChanges=new RuntimeCheckpointStateChangeSet(null,null,[new(cancelled.InvocationId,RuntimeStateChangeOperation.Upsert,cancelled,new Dictionary<string,string>())],[],[],[],[]);
var cancellationCommit=new RuntimeCheckpointCommit("cancel-commit-r6",new("cancel-checkpoint-r6","ActivityCancelled","native-execution-r6",at,[],new Dictionary<string,string>()),cancellationChanges,[],new Dictionary<string,string>());
var store=Custom<IRuntimeCheckpointCommitStore>((m,a)=>{persistence++;throw new Exception("Unexpected persistence: "+m.Name);});
var committer=new RuntimeCheckpointCommitter(Custom<IRuntimeCheckpointPersistencePolicy>((m,a)=>throw new Exception("Unexpected policy")),store,Custom<IRuntimeExecutionOwnershipContextAccessor>((m,a)=>throw new Exception("Unexpected ownership")),[cancellationEnricher],[]);
try { await committer.CommitAsync(cancellationCommit); Console.WriteLine("public committer cancellation: NO EXCEPTION"); }
catch(Exception e) { Console.WriteLine("public committer cancellation: "+e.GetType().FullName+" inner="+e.InnerException?.GetType().FullName+" message="+e.Message); Console.WriteLine(e.StackTrace); }
Console.WriteLine($"Retained predecessor reads={reads}; physical persistence calls={persistence}");
static T Custom<T>(Func<MethodInfo,object?[]?,object?> behavior) where T:class { var proxy=DispatchProxy.Create<T,DelegateProxy>();((DelegateProxy)(object)proxy).Behavior=behavior;return proxy; }
public class DelegateProxy:DispatchProxy {public Func<MethodInfo,object?[]?,object?> Behavior=null!;protected override object? Invoke(MethodInfo? m,object?[]? a)=>Behavior(m!,a);}
