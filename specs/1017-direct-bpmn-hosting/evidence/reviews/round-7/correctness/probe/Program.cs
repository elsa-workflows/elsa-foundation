using System.Reflection;
using System.Text.Json;
using Bpmn.Model.State;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;

var scope = new PersistenceScope(PersistenceScope.DefaultValue);
var now = DateTimeOffset.UtcNow;
var modelPin = new WorkflowExecutableIdentity("model-artifact", "model-definition", "model-version", "1.0.0", "model-hash");
var nativePin = new WorkflowExecutableIdentity("native-artifact", "native-definition", "native-version", "1.0.0", "native-hash");
var childPin = new WorkflowExecutableIdentity("child-artifact", "child-definition", "child-version", "1.0.0", "child-hash");
var execution = "bpmn-embedded:" + BpmnModelIdentity.Create(scope.Value, "outer-native-worker", "runbpmn-invocation");
var invocation = new BpmnEmbeddedInvocation(1, scope, execution, new("published-model-source", modelPin, "incarnation"), nativePin,
    "outer-native-worker", "runbpmn-invocation", "runbpmn-node", "resume-target", "registration", "attempt");
var protocol = new RuntimeExecutionProtocol(BpmnModelProtocol.Key, BpmnModelProtocol.Version);
var origin = new RuntimeModelNativeWorkOrigin(1, scope, execution, protocol, modelPin, "incarnation", "worker", "child-invocation", "child-attempt", childPin,
    BpmnNativeWorkBoundary.WorkerIdentity(scope, execution, "child-invocation", "child-attempt", childPin).WorkflowExecutionId,
    "start-commit:child-start", "child-start", BpmnNativeWorkBoundary.ResultKind, "child-result");
var cancellation = new BpmnNativeWorkCancellation(origin, "cancel-commit:" + BpmnNativeWorkBoundary.CancellationIntentId(origin));
var payload = JsonSerializer.SerializeToElement(new BpmnDirectExecutionDriver.Document(6, new BpmnExecutionState(), [], NativeParent:new(BpmnEmbeddingJson.Write(invocation)), Cancellations:[cancellation]), new JsonSerializerOptions(JsonSerializerDefaults.Web));
var model = new RuntimeModelExecutionState(1, scope, execution, null, protocol, modelPin, "incarnation", 2,
    RuntimeModelLifecycle.TerminalPendingSettlement, new("system", "operator"), now, now, payload, JsonSerializer.SerializeToElement(new {status="Cancelled"}));
var doc = BpmnDirectExecutionDriver.Read(model);
var unsettled = doc.Cancellations!.Count(c => !c.Settled);
var intent = BpmnEmbeddingCheckpointEnricher.Intent(invocation, true, now);
var current = new RuntimePostCommitOutboxItem("parent-cancel-commit:" + intent.IntentId, intent,
    RuntimePostCommitOutboxStatus.Delivering, now, now, RuntimePostCommitRetryPolicy.None, 1, "delivery-owner", now, null, null, null, 1, now.AddMinutes(5), 1, scope, null);
var source = new PostCommitDeliveryContext(new RuntimePostCommitOutboxClaim(current, "delivery-owner", 1, now, now.AddMinutes(5)), scope);
var outbox = Proxy.Create<CombinedOutbox>((m,a)=>m.Name=="FindDurableAsync"?ValueTask.FromResult<RuntimePostCommitOutboxItem?>(current):throw new InvalidOperationException(m.Name));
var access = Proxy.Create<IPersistenceAccessContextAccessor>((m,a)=>m.Name=="get_Current"?PersistenceAccessContext.Scoped(scope):throw new InvalidOperationException(m.Name));
var checkpoints = Proxy.Create<IRuntimeCheckpointCommitStore>((m,a)=>m.Name is "get_SupportsModelAdmission" or "CanCommitModelAdmission" or "get_SupportsModelParentAdmission" or "CanCommitExactBookmarkResume"?true:throw new InvalidOperationException(m.Name));
var reader = Proxy.Create<IRuntimeModelExecutionReader>((m,a)=>m.Name=="FindAsync"?ValueTask.FromResult<RuntimeModelExecutionState?>(model):throw new InvalidOperationException(m.Name));
var materials = Proxy.Create<IWorkflowExecutableStore>();
var queue = Proxy.Create<IWorkflowSchedulerWorkQueue>();
var native = new BpmnNativeWorkBoundary(materials, outbox, Proxy.Create<IWorkflowExecutionStateStore>(), Proxy.Create<IActivityExecutionStateStore>(), checkpoints, outbox, access, TimeProvider.System, queue, reader);
var boundary = new BpmnEmbeddingBoundary(native, materials, Proxy.Create<IBookmarkStateStore>(), checkpoints, access, TimeProvider.System, new(false), models:reader);
using var actors = new InProcessWorkflowExecutionActorProvider();
var delivery = new BpmnEmbeddingDelivery(boundary, native, queue, Proxy.Create<IBookmarkResumeDispatcher>(), actors, TimeProvider.System, reader);
string guard;
try { BpmnEmbeddingBoundary.RequireWorkerCancellationSettled(model);guard="unexpected pass"; }
catch(Exception e){guard=e.GetType().Name+": "+e.Message;}
await delivery.HandleAsync(source);
Console.WriteLine(JsonSerializer.Serialize(new { sourceHead="9508e25e57672c569f462aff9a1e0282c5780eb9",model.Lifecycle, unsettledCancellations=unsettled, result="CancelEmbedded returned successfully while worker cancellation remains unsettled", normalResultDeliveryGuard=guard, library=typeof(BpmnDirectExecutionDriver).Assembly.FullName, productVersion=System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(BpmnDirectExecutionDriver).Assembly.Location).ProductVersion, productSha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(BpmnDirectExecutionDriver).Assembly.Location))) }));

if (args.Contains("--require-settlement")) throw new InvalidOperationException("ASSERTION FAILED: cancel source must remain owed while the child has unsettled native cancellation responsibility.");

public interface CombinedOutbox: IPostCommitOutboxLookupStore, IRuntimePostCommitOutboxStore;
public class Proxy : DispatchProxy
{
 public Func<MethodInfo,object?[]?,object?> Call=null!;
 public static T Create<T>(Func<MethodInfo,object?[]?,object?>? callback=null) where T:class
 { var p=DispatchProxy.Create<T,Proxy>();((Proxy)(object)p).Call=callback??((m,a)=>throw new InvalidOperationException(m.Name));return p; }
 protected override object? Invoke(MethodInfo? m,object?[]? a)=>Call(m!,a);
}
