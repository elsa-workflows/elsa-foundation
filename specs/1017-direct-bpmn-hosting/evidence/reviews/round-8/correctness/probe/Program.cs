using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var model = JsonSerializer.Deserialize<RuntimeModelExecutionState>(File.ReadAllText("/tmp/elsa-2898-review8-correctness/legacy-pending-fault-model.json"), web)!;
var document = BpmnDirectExecutionDriver.Read(model);
var invocation = BpmnEmbeddingBoundary.Read(new RunBpmnState(1,document.NativeParent!.Invocation));
var now=DateTimeOffset.UtcNow;
var intent=BpmnEmbeddingCheckpointEnricher.Intent(invocation,true,now);
var current=new RuntimePostCommitOutboxItem("review8-parent-cancel:"+intent.IntentId,intent,RuntimePostCommitOutboxStatus.Delivering,now,now,RuntimePostCommitRetryPolicy.None,1,"review8-owner",now,null,null,null,1,now.AddMinutes(5),1,model.Scope,null);
var source=new PostCommitDeliveryContext(new RuntimePostCommitOutboxClaim(current,"review8-owner",1,now,now.AddMinutes(5)),model.Scope);
var outbox=Fake<DeliveryOutbox>((method,_)=> method.Name=="FindDurableAsync" ? ValueTask.FromResult<RuntimePostCommitOutboxItem?>(current) : throw new Exception(method.Name));
var access=Fake<IPersistenceAccessContextAccessor>((method,_)=> method.Name=="get_Current" ? PersistenceAccessContext.Scoped(model.Scope) : throw new Exception(method.Name));
var checkpoints=Fake<IRuntimeCheckpointCommitStore>((method,_)=>method.Name is "get_SupportsModelAdmission" or "CanCommitModelAdmission" or "get_SupportsModelParentAdmission" or "CanCommitExactBookmarkResume" ? true : throw new Exception(method.Name));
var reader=Fake<IRuntimeModelExecutionReader>((method,_)=>method.Name=="FindAsync" ? ValueTask.FromResult<RuntimeModelExecutionState?>(model) : throw new Exception(method.Name));
var materials=Fake<IWorkflowExecutableStore>();
var queueCalls=0;
var queue=Fake<IWorkflowSchedulerWorkQueue>((method,_)=>{queueCalls++;throw new Exception("QUEUE:"+method.Name);});
var native=new BpmnNativeWorkBoundary(materials,outbox,Fake<IWorkflowExecutionStateStore>(),Fake<IActivityExecutionStateStore>(),checkpoints,outbox,access,TimeProvider.System,queue,reader);
var boundary=new BpmnEmbeddingBoundary(native,materials,Fake<IBookmarkStateStore>(),checkpoints,access,TimeProvider.System,new(false),models:reader);
using var actors=new InProcessWorkflowExecutionActorProvider();
#if CONTROL
var delivery=new Review8EmbeddingDelivery(
#else
var delivery=new BpmnEmbeddingDelivery(
#endif
boundary,native,queue,Fake<IBookmarkResumeDispatcher>(),actors,TimeProvider.System,reader);
var result="Acknowledged";
try{await delivery.HandleAsync(source);}catch(Exception error){result=error.GetType().FullName+": "+error.Message;}
Console.WriteLine(JsonSerializer.Serialize(new{model.ExecutionId,model.Revision,lifecycle=model.Lifecycle.ToString(),terminal=model.TerminalResult,document.FormatVersion,nativeWork=document.Work.Count(work=>work.NativeWork is not null),cancellations=document.Cancellations?.Count,queueCalls,result,syntheticSource=true,actualLegacyFrame=true,bpmnAssembly=typeof(BpmnDirectExecutionDriver).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,bpmnAssemblySha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(BpmnDirectExecutionDriver).Assembly.Location)))},new JsonSerializerOptions(web){WriteIndented=true}));
static T Fake<T>(Func<MethodInfo,object?[]?,object?>? action=null) where T:class {var proxy=DispatchProxy.Create<T,Proxy>();((Proxy)(object)proxy).Action=action??((method,_)=>throw new Exception(method.Name));return proxy;}
public interface DeliveryOutbox:IPostCommitOutboxLookupStore,IRuntimePostCommitOutboxStore;
public class Proxy:DispatchProxy{public Func<MethodInfo,object?[]?,object?> Action=null!;protected override object? Invoke(MethodInfo? method,object?[]?args)=>Action(method!,args);}
