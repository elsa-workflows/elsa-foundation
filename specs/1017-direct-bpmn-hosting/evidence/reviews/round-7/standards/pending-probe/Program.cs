using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
Console.WriteLine("Current compiled BpmnModelRuntime from9508; no copied implementation");
Console.WriteLine("Dependency product: " + typeof(BpmnModelProtocol).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
var state = new RuntimeModelExecutionState(1, new("tenant-a"), "execution", "tenant-a", new("elsa.bpmn",1),
    new("artifact","definition","definition-version","1.0.0","hash"),"incarnation",1,RuntimeModelLifecycle.Running,
    new("bpmn-runtime","actor"),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,JsonSerializer.SerializeToElement(new{}),null);
Console.WriteLine("BPMN implementation product: " + typeof(Elsa.Activities.Bpmn.Contracts.IBpmnProcessRuntime).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
var failed = 0;
var runtime = new BpmnModelRuntime(null!,null!,null!,null!,null!,new Access(),new Codec(),TimeProvider.System,new Queue(),states:new Reader(state));
await Probe("StartAsync owned queued invalid command", async()=>{_ = await runtime.StartAsync("source", "request",null,new("actor","tenant-a"));});
await Probe("FindCommandAsync owned queued invalid command", async()=>{_ = await runtime.FindCommandAsync("execution", "request",new("actor","tenant-a"));});
Console.WriteLine($"Domain boundary controls: 2 executed, {failed} failed");
Environment.ExitCode = failed == 0 ? 0 : 1;
async Task Probe(string name, Func<Task> action) {
 try { await action(); Console.WriteLine(name+": NO ERROR"); failed++; }
 catch(Exception ex) {
  var passed = ex is BpmnModelValidationException && ex.InnerException is ArgumentException or JsonException && ex.Message.Contains("request") && ex.Message.Contains("execution") && ex.Message.Contains("item");
  Console.WriteLine(name+": "+ex.GetType().FullName+"; inner="+ex.InnerException?.GetType().FullName+"; expectedDomain="+passed+"; message="+ex.Message);
  if (!passed) failed++;
 }
}

sealed class Access:IPersistenceAccessContextAccessor {public PersistenceAccessContext Current {get;}=PersistenceAccessContext.Scoped(new("tenant-a"));}
sealed class Codec:IRuntimeRecoveryContinuationCodec {public string Encode(string purpose,ReadOnlySpan<byte> payload)=>Convert.ToBase64String(payload);public byte[] Decode(string purpose,string token)=>Convert.FromBase64String(token);}
sealed class Reader(RuntimeModelExecutionState state):IRuntimeModelExecutionReader {public ValueTask<RuntimeModelExecutionState?> FindAsync(string id,CancellationToken ct=default)=>ValueTask.FromResult<RuntimeModelExecutionState?>(state);public ValueTask<RuntimeModelInputReceipt?> FindInputAsync(string id,string input,CancellationToken ct=default)=>ValueTask.FromResult<RuntimeModelInputReceipt?>(null); public ValueTask<RuntimeStorePage<RuntimeModelExecutionState>> ListPageAsync(RuntimeStorePageRequest req,CancellationToken ct=default)=>ValueTask.FromResult(new RuntimeStorePage<RuntimeModelExecutionState>(req,[state],null));}
sealed class Queue:IWorkflowSchedulerWorkQueue {
 static JsonElement BrokenPayload() {
  if (Environment.GetEnvironmentVariable("PROBE_SHAPE")=="string") return JsonSerializer.SerializeToElement("invalid-model-command");
  var good=new RuntimeModelExecutionCommand(new("artifact","definition","definition-version","1.0.0","hash"),"incarnation","request","start",JsonSerializer.SerializeToElement(new{}),new("bpmn-runtime","actor"),"tenant-a");
  var node=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(good,new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
  node["inputKind"]="";
  var payload = JsonSerializer.SerializeToElement(node);
  Console.WriteLine("OwnedCommandInput=" + payload.GetRawText());
  return payload;
 }

 public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery q,CancellationToken ct=default) {var at=DateTimeOffset.UtcNow;var item=new RuntimeSchedulerWorkItem("item",q.WorkflowExecutionId,"request",WorkflowExecutionCommandKind.ExecuteModel,"envelope","idem",at,at,payload:BrokenPayload());return ValueTask.FromResult(new RuntimeStorePage<RuntimeSchedulerWorkItem>(q,[item],null));}
 public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem w,CancellationToken ct=default)=>throw new NotImplementedException();
 public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string id,CancellationToken ct=default)=>throw new NotImplementedException();
 public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit,CancellationToken ct=default)=>throw new NotImplementedException();
}
