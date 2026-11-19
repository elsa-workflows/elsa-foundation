using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using System.Text.Json;
using Elsa.Activities.Bpmn;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

var scope = new PersistenceScope(PersistenceScope.DefaultValue);
RuntimePostCommitOutboxItem? current = null;
var store = Fake<BothOutbox>((m,a) => m.Name == "FindDurableAsync" ? ValueTask.FromResult(current) : throw new Exception("Unexpected store call: " + m.Name));
var materials = Fake<IWorkflowExecutableStore>();
var workflows = Fake<IWorkflowExecutionStateStore>();
var activities = Fake<IActivityExecutionStateStore>();
var checkpoints = Fake<IRuntimeCheckpointCommitStore>((m,a) => m.Name is "get_SupportsModelAdmission" or "CanCommitModelAdmission" ? true : throw new Exception(m.Name));
var access = Fake<IPersistenceAccessContextAccessor>((m,a) => m.Name == "get_Current" ? PersistenceAccessContext.Scoped(scope) : throw new Exception(m.Name));
var queue = Fake<IWorkflowSchedulerWorkQueue>();
var reader = Fake<IRuntimeModelExecutionReader>();
var assembly = typeof(ActivitiesBpmnHostingFeature).Assembly;
var boundary = assembly.GetType("Elsa.Activities.Bpmn.Internal.BpmnNativeWorkBoundary",true)!;
var source = boundary.GetConstructors().Single().Invoke([materials,store,workflows,activities,checkpoints,store,access,TimeProvider.System,queue,reader,null,null,null,null]);
var handlerType = assembly.GetType("Elsa.Activities.Bpmn.Internal.BpmnDurationTimerDelivery",true)!;
var handler = (IRuntimePostCommitDurableSourceIntentHandler)handlerType.GetConstructors().Single().Invoke([source,store,queue,access,Fake<IWorkflowExecutionActorProvider>(),TimeProvider.System,reader]);
var services = new ServiceCollection();
services.AddSingleton(handlerType,handler);
using var provider = services.BuildServiceProvider();
var dispatcher = new RuntimePostCommitIntentDispatcher([new RuntimePostCommitIntentHandlerContribution("Elsa.Bpmn.DurationTimer.v1",handlerType,RuntimePostCommitRetryPolicy.None,requiresDurableSource:true)],provider);
var result = new List<object>();
foreach (var bad in new[] { false, true })
{
    var now=DateTimeOffset.UtcNow;
    var intent=new RuntimePostCommitIntent("timer-source", "reviewed-model", "Elsa.Bpmn.DurationTimer.v1", now,null,null,bad?JsonSerializer.SerializeToElement("broken-owned-timer"):null);
    current=new RuntimePostCommitOutboxItem("reviewed-commit:timer-source",intent,RuntimePostCommitOutboxStatus.Delivering,now,now,RuntimePostCommitRetryPolicy.None,1,"review-owner",now,null,null,null,1,now.AddMinutes(5),1,scope,null);
    var delivery=new PostCommitDeliveryContext(new RuntimePostCommitOutboxClaim(current,"review-owner",1,now,now.AddMinutes(5)),scope);
    try { await dispatcher.DispatchAsync(delivery); throw new Exception("Expected refusal"); }
    catch(Exception e) { result.Add(new { corruptTypedPayload=bad,type=e.GetType().FullName,message=e.Message,inner=e.InnerException?.GetType().FullName,stack=e.ToString(),sourceOutboxId=delivery.SourceOutboxItemId,executionId=intent.WorkflowExecutionId }); }
}
Console.WriteLine(JsonSerializer.Serialize(new { asBuiltFrom=System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion, observations=result },new JsonSerializerOptions {WriteIndented=true}));

static T Fake<T>(Func<MethodInfo,object?[]?,object?>? callback=null)where T:class
{var value=DispatchProxy.Create<T,FlexProxy>();((FlexProxy)(object)value).Callback=callback??((m,a)=>throw new Exception("Unexpected call: "+m.Name));return value;}
public interface BothOutbox:IPostCommitOutboxLookupStore,IRuntimePostCommitOutboxStore{}
public class FlexProxy:DispatchProxy
{public Func<MethodInfo,object?[]?,object?> Callback=null!;protected override object? Invoke(MethodInfo? m,object?[]? a)=>Callback(m!,a);}
