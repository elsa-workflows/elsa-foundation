using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
Console.WriteLine("Exact BpmnModelRuntime.cs compiled from fcf551c789c37c722b3b07d0c1cf4aecb56a16ad");
Console.WriteLine("Dependency product: " + typeof(BpmnModelProtocol).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
var state = new RuntimeModelExecutionState(1, new("tenant-a"), "execution", "tenant-a", new("elsa.bpmn",1),
    new("artifact","definition","definition-version","1.0.0","hash"),"incarnation",1,RuntimeModelLifecycle.Running,
    new("bpmn-runtime","actor"),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,JsonSerializer.SerializeToElement(new{}),null);
var runtime = new BpmnModelRuntime(null!,null!,null!,null!,null!,new Access(),new Codec(),TimeProvider.System,new Queue(),states:new Reader(state));
await Probe("StartAsync owned queued invalid command", async()=>{_ = await runtime.StartAsync("source", "request",null,new("actor","tenant-a"));});
await Probe("FindCommandAsync owned queued invalid command", async()=>{_ = await runtime.FindCommandAsync("execution", "request",new("actor","tenant-a"));});
static async Task Probe(string name, Func<Task> action) { try { await action(); Console.WriteLine(name+": NO ERROR"); } catch(Exception ex) {Console.WriteLine(name+": "+ex.GetType().FullName+"; inner="+ex.InnerException?.GetType().FullName+"; message="+ex.Message);} }
sealed class Access:IPersistenceAccessContextAccessor {public PersistenceAccessContext Current {get;}=PersistenceAccessContext.Scoped(new("tenant-a"));}
sealed class Codec:IRuntimeRecoveryContinuationCodec {public string Encode(string purpose,ReadOnlySpan<byte> payload)=>Convert.ToBase64String(payload);public byte[] Decode(string purpose,string token)=>Convert.FromBase64String(token);}
sealed class Reader(RuntimeModelExecutionState state):IRuntimeModelExecutionReader {public ValueTask<RuntimeModelExecutionState?> FindAsync(string id,CancellationToken ct=default)=>ValueTask.FromResult<RuntimeModelExecutionState?>(state);public ValueTask<RuntimeModelInputReceipt?> FindInputAsync(string id,string input,CancellationToken ct=default)=>ValueTask.FromResult<RuntimeModelInputReceipt?>(null); public ValueTask<RuntimeStorePage<RuntimeModelExecutionState>> ListPageAsync(RuntimeStorePageRequest req,CancellationToken ct=default)=>ValueTask.FromResult(new RuntimeStorePage<RuntimeModelExecutionState>(req,[state],null));}
sealed class Queue:IWorkflowSchedulerWorkQueue {
 public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery q,CancellationToken ct=default) {var at=DateTimeOffset.UtcNow;var item=new RuntimeSchedulerWorkItem("item",q.WorkflowExecutionId,"request",WorkflowExecutionCommandKind.ExecuteModel,"envelope","idem",at,at,payload:JsonSerializer.SerializeToElement("invalid-model-command"));return ValueTask.FromResult(new RuntimeStorePage<RuntimeSchedulerWorkItem>(q,[item],null));}
 public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem w,CancellationToken ct=default)=>throw new NotImplementedException();
 public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string id,CancellationToken ct=default)=>throw new NotImplementedException();
 public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit,CancellationToken ct=default)=>throw new NotImplementedException();
}
