using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Bpmn;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Testing;
using Elsa.Cluster.Readability;
using Elsa.Events;
using Elsa.Persistence.EntityFramework;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.Extensions.DependencyInjection;
using Bpmn.Model;
class Program {
static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
static async Task Main(string[] args) {
var path=args.Length>1 && args[0]=="--reopen"?args[1]:"/tmp/elsa-2898-review-r3-correctness/native-probe/fresh-"+Guid.NewGuid().ToString("N")+".sqlite";
Console.WriteLine("STORE "+path);
await using var harness=WorkflowExecutionHarness.Create().ConfigureServices(services=>{
 services.AddLogging();services.AddEfSchemaReadability();new EventsFeature().ConfigureServices(services);new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);new ActivitiesBpmnHostingFeature{AdmitNewExecutions=true}.ConfigureServices(services);
 services.AddRuntimeEntityFrameworkCore(new(){Provider="Sqlite",ConnectionString=$"Data Source={path};Pooling=False",RecoveryContinuationSigningKey=new string('r',32),HierarchyCursorSigningKey=new string('h',32)}).AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
}).Build(Enumerable.Range(1,32).Select(i=>"probe-"+i));
foreach(var initializer in harness.Services.GetServices<IShellInitializer>())await initializer.InitializeAsync();
foreach(var task in harness.Services.GetServices<IStartupTask>())await task.ExecuteAsync(CancellationToken.None);
foreach(var assembly in new[]{typeof(ActivitiesBpmnHostingFeature).Assembly,typeof(RuntimeCheckpointCommit).Assembly,typeof(RuntimeDbContext).Assembly,typeof(WorkflowExecutionHarness).Assembly})Console.WriteLine("PRODUCT "+assembly.GetName().Name+" "+System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion+" "+Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assembly.Location))));
if(args.Length>1 && args[0]=="--reopen") {
var saved=(await harness.Services.GetRequiredService<IRuntimeModelExecutionReader>().ListPageAsync(new())).Items.Single();
var again=harness.Services.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
Console.WriteLine("COLD BEFORE "+JsonSerializer.Serialize(saved,Web));
Console.WriteLine("COLD RETRY "+JsonSerializer.Serialize(await again.ProcessAsync(new(workflowExecutionId:saved.ExecutionId,intentKind:"Elsa.Bpmn.StartNativeWork.v1",limit:10))));
Console.WriteLine("COLD AFTER "+JsonSerializer.Serialize(await harness.Services.GetRequiredService<IRuntimeModelExecutionReader>().FindAsync(saved.ExecutionId),Web));
Console.WriteLine("COLD NATIVE COUNT "+(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync()).Count);return;
}
var worker=new WorkflowExecutable(WorkflowExecutionHarness.Identity,Nodes.Leaf("fault-root",typeof(FaultingActivity),new Dictionary<string,object?>{{"Message","Private review worker fault"}}),new Dictionary<string,WorkflowExecutableResumeTarget>(),WorkflowExecutionHarness.Timestamp,new Dictionary<string,string>(),inputContract:args.Contains("--legacy-contract")?null:new WorkflowExecutableInputContract(1,[]),dependencies:null,incidentStrategy:IncidentStrategyBuiltIns.FaultReference);
var workerPublication=await harness.PublishAsync(worker,"fault-worker-publication");
if(args.Contains("--legacy-contract")){var legacyRun=await harness.StartPublishedAsync(workerPublication,"legacy-native-control",partition:new(PersistenceScope.DefaultValue));Console.WriteLine("LEGACY NATIVE RUN "+JsonSerializer.Serialize(legacyRun));Console.WriteLine("LEGACY NATIVE STATUS "+(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("legacy-native-control"))!.Status);}
var pin=worker.Identity;
var process=new BpmnProcessDefinition("concurrent-services",Elements:[new("start",BpmnElementTypes.StartEvent),new("service",BpmnElementTypes.ServiceTask,bindingRef:"worker"),new("end",BpmnElementTypes.EndEvent)],SequenceFlows:[new("one","start","service"),new("two","start","service"),new("finish","service","end")]);
if(args.Contains("--single"))process=new BpmnProcessDefinition("single-service-control",Elements:process.Elements,SequenceFlows:process.SequenceFlows.Where(f=>f.FlowId!="two").ToArray());
var runtime=harness.Services.GetRequiredService<IBpmnModelRuntime>();var caller=new BpmnModelCaller("probe-user",null);
BpmnPublishedModel model;
try { model=await runtime.PublishAsync(new(args.Contains("--policy")?3:2,JsonSerializer.SerializeToElement(process,Web),[],[new("worker",pin,"test.error",args.Contains("--policy")?new BpmnServiceFaultPolicy(1,BpmnServiceFaultDisposition.FinalFault,1):null)]),"publish-concurrent",caller); }
catch(Exception error) {Console.WriteLine("PUBLICATION REFUSED "+error);return;}
var start=await runtime.StartAsync(model.SourceReferenceId,"start-concurrent",null,caller);var id=start.ExecutionId;Console.WriteLine("START "+JsonSerializer.Serialize(start));
await PrintState("INITIAL");
var processor=harness.Services.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
Console.WriteLine("ONE START "+JsonSerializer.Serialize(await processor.ProcessAsync(new(workflowExecutionId:id,intentKind:"Elsa.Bpmn.StartNativeWork.v1",limit:1))));
await PrintState("AFTER FIRST WORKER");
Console.WriteLine("RESULT "+JsonSerializer.Serialize(await processor.ProcessAsync(new(intentKind:"Elsa.Bpmn.NativeWorkResult.v1",limit:1))));
var now=DateTimeOffset.UtcNow;var actor=await harness.Services.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(new(id,WorkflowExecutionActorActivationReason.Start,now,"probe-user",WorkflowExecutionActorCapabilities.InProcessMailbox,partition:new(PersistenceScope.DefaultValue)));
var cmd="drain-original-native-result";
Console.WriteLine("DRAIN "+JsonSerializer.Serialize(await actor.EnqueueAsync(new(cmd,id,new(cmd,id,WorkflowExecutionCommandKind.RunSchedulerWork,now,null,new Dictionary<string,string>()),cmd,WorkflowExecutionCommandDeliveryMode.AtLeastOnce,now,partition:new(PersistenceScope.DefaultValue)))));
await PrintState("AFTER FAULT ACCEPTANCE");
Console.WriteLine("OTHER START "+JsonSerializer.Serialize(await processor.ProcessAsync(new(workflowExecutionId:id,intentKind:"Elsa.Bpmn.StartNativeWork.v1",limit:10))));
Console.WriteLine("CANCEL TERMINAL "+JsonSerializer.Serialize(await runtime.CancelAsync(id,"cancel-terminal","cleanup requested",caller)));
await Task.Delay(1300);
Console.WriteLine("OTHER START RETRY "+JsonSerializer.Serialize(await processor.ProcessAsync(new(workflowExecutionId:id,intentKind:"Elsa.Bpmn.StartNativeWork.v1",limit:10))));
await PrintState("FINAL");
async Task PrintState(string label){var state=await runtime.FindExecutionAsync(id,caller);Console.WriteLine(label+" MODEL "+JsonSerializer.Serialize(state,Web));Console.WriteLine(label+" NATIVE "+JsonSerializer.Serialize(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync()));Console.WriteLine(label+" OUTBOX "+JsonSerializer.Serialize(await harness.Services.GetRequiredService<IRuntimePostCommitOutboxStore>().GetDeliverableAsync(new(DateTimeOffset.UtcNow,100))));}
}
}
