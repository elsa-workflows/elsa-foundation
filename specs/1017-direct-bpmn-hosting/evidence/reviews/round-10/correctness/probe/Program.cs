using System.Reflection;
using System.Text.Json;
using System.Security.Cryptography;
using Bpmn.Model;
using CShells.Lifecycle;
using Elsa.Activities.Bpmn;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Runtime;
using Elsa.Activities.Primitives;
using Elsa.Activities.Sequence;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Compensation;
using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Models;
using Elsa.Activities.Testing;
using Elsa.Serialization.SystemText;
using Elsa.Events;
using Elsa.Tasks.Core;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
var directory="/tmp/elsa-2898-review10-correctness/delayed-retry";
var web=new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true};
var products=new[]{typeof(ActivitiesBpmnHostingFeature).Assembly,typeof(IWorkflowStartDispatcher).Assembly,typeof(Elsa.Workflows.Runtime.Services.Executions.WorkflowStartDispatcher).Assembly,typeof(RuntimeEntityFrameworkCoreOptions).Assembly,typeof(CompensationScope).Assembly,typeof(ClrActivityContractTestBuilder).Assembly};
await File.WriteAllTextAsync(Path.Combine(directory,"products.json"),JsonSerializer.Serialize(products.Select(a=>new{name=a.GetName().Name,path=a.Location,source=a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(a.Location)))}),web));
var services=new ServiceCollection();services.AddLogging();services.AddWorkflowRuntime();
services.AddSingleton<IPersistenceAccessContextAccessor>(new Access());
new EventsFeature().ConfigureServices(services);new ActivitiesBpmnHostingFeature{AdmitNewExecutions=true}.ConfigureServices(services);
new ActivitiesRuntimeFeature().ConfigureServices(services);new ActivitiesPrimitivesFeature().ConfigureServices(services);
new ActivitiesSequenceFeature().ConfigureServices(services);new ActivitiesFlowchartFeature().ConfigureServices(services);
new ActivitiesCompensationFeature().ConfigureServices(services);new SerializationFeature().ConfigureServices(services);
new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
services.AddRuntimeEntityFrameworkCore(new(){Provider="Sqlite",ConnectionString=$"Data Source={directory}/elsa.db;Mode=ReadWrite;Pooling=False",Pooling=false,CacheWorkflowExecutables=false,RecoveryContinuationSigningKey=new string('r',32),HierarchyCursorSigningKey=new string('r',32)});
services.AddPersistenceModes(o=>o.Mode=PersistenceMode.Durable);
await using var provider=services.BuildServiceProvider();foreach(var initializer in provider.GetServices<IShellInitializer>())await initializer.InitializeAsync();
await using var scope=provider.CreateAsyncScope();var sdk=scope.ServiceProvider;
foreach(var task in sdk.GetServices<IStartupTask>())await task.ExecuteAsync(CancellationToken.None);
var workflows=sdk.GetRequiredService<IWorkflowExecutionStateStore>();var activities=sdk.GetRequiredService<IActivityExecutionStateStore>();var models=sdk.GetRequiredService<IRuntimeModelExecutionReader>();var materials=sdk.GetRequiredService<IWorkflowExecutableStore>();
var outbox=sdk.GetRequiredService<IRuntimePostCommitOutboxStore>();var processor=sdk.GetRequiredService<IRuntimePostCommitOutboxProcessor>();var lookup=sdk.GetRequiredService<IPostCommitOutboxLookupStore>();var queue=sdk.GetRequiredService<IWorkflowSchedulerWorkQueue>();
IReadOnlyDictionary<string,RuntimeInputBinding> Inputs(ActivityContract contract,params (string Key,object Value)[] input){var bindings=new Dictionary<string,RuntimeInputBinding>();foreach(var (key,value) in input){var i=contract.Inputs[key];bindings.Add(key,new(key,i.Type,ValueProtectionPolicy.InstanceInline,RuntimeInputBindingSource.Literal,literal:ValueEnvelope.Inline(i.Type,JsonSerializer.SerializeToElement(value),ValueProtectionPolicy.InstanceInline)));}return ClrActivityContractTestBuilder.CompleteInputBindings(contract,bindings);}
ExecutableNode Leaf<T>(string id,params (string,object)[] values){var c=ClrActivityContractTestBuilder.BuildContract(typeof(T));return new(id,"authored-"+id,typeof(T).FullName!,"1.0.0",c.DescriptorKind,c.DescriptorPayload,Inputs(c,values),activityContract:c);}
var forward=Leaf<Elsa.Activities.Primitives.Activities.WriteLine>("forward",("text","round10 forward"));var handler=Leaf<Elsa.Activities.Primitives.Activities.Fault>("compensate");var reject=Leaf<Elsa.Activities.Primitives.Activities.Fault>("reject");
var contract=ClrActivityContractTestBuilder.BuildContract(typeof(CompensationScope));
var structure=new CompensationScopeExecutableStructure([new("one","forward","compensate"),new("two","reject",null)]);
var root=new ExecutableNode("scope","authored-scope",typeof(CompensationScope).FullName!,"1.0.0",contract.DescriptorKind,contract.DescriptorPayload,
 Inputs(contract,("faultAfterCompensation",false),("handlerMaxAttempts",3),("confirmOnComplete",false),("handlerRetryDelay",TimeSpan.FromHours(1)),("handlerRetryBackoffMultiplier",2),("handlerRetryMaxDelay",TimeSpan.FromHours(1))),
 metadata:new Dictionary<string,string>{[RuntimeMetadataKeys.StructuralFaultHandling]=RuntimeMetadataKeys.StructuralFaultHandlingDescendant},
 childSlots:[new(CompensationScope.StepSlotName("one"),[forward]),new(CompensationScope.CompensationSlotName("one"),[handler]),new(CompensationScope.StepSlotName("two"),[reject])],
 structure:new(CompensationScope.StructureKind,CompensationScope.StructureSchemaVersion,JsonSerializer.SerializeToElement(structure,web)),activityContract:contract);
var hash="sha256:"+Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(root,web)));
var pin=new WorkflowExecutableIdentity("review10-delayed-worker","review10-delayed-definition","review10-delayed-v1","1.0.0",hash);
var native=new WorkflowExecutable(pin,root,[],DateTimeOffset.UtcNow,inputContract:new(1,[]));
await materials.SaveAsync(native);
var now=DateTimeOffset.UtcNow;var reference=new WorkflowExecutableSourceReference("review10-delayed-source",pin.ArtifactId,"WorkflowDefinitionVersion",pin.DefinitionVersionId,pin.ArtifactVersion,pin.DefinitionId,pin.DefinitionVersionId,pin.ArtifactVersion,now,now,WorkflowExecutableReferenceScope.Published);
var sources=sdk.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
await sdk.GetRequiredService<IWorkflowExecutableRootWriteLeaseManager>().ExecuteFencedAsync([pin],"review10-publish",async(grants,token)=>{await sources.SaveWithRootWriteLeasesAsync(reference,pin,grants,token);});
var runtime=sdk.GetRequiredService<IBpmnModelRuntime>();var caller=new BpmnModelCaller("review10",null);
var process=new BpmnProcessDefinition("review10-delayed-cancel",Elements:[new("start",BpmnElementTypes.StartEvent),new("service",BpmnElementTypes.ServiceTask,bindingRef:"worker"),new("end",BpmnElementTypes.EndEvent)],SequenceFlows:[new("begin","start","service"),new("finish","service","end")]);
var publication=await runtime.PublishAsync(new(2,JsonSerializer.SerializeToElement(process,web),[],[new("worker",pin,"error")]),"review10-publish-model",caller);
var started=await runtime.StartAsync(publication.SourceReferenceId,"review10-start",null,caller);var outer=started.ExecutionId;
var outerBefore=(await models.FindAsync(outer))!;var middle=outerBefore.Payload.GetProperty("work")[0].GetProperty("nativeWork").GetProperty("workerExecutionId").GetString()!;
async Task<object> Send(string id,WorkflowExecutionCommandKind kind,JsonElement?payload=null){var at=DateTimeOffset.UtcNow;var transport="round10:"+Guid.NewGuid().ToString("N");var actor=await sdk.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(new(id,WorkflowExecutionActorActivationReason.ControlPlaneCommand,at,"round10-review",WorkflowExecutionActorCapabilities.InProcessMailbox,partition:new("default")));try{return await actor.EnqueueAsync(new(transport,id,new(transport,id,kind,at,payload,new Dictionary<string,string>()),transport,WorkflowExecutionCommandDeliveryMode.AtLeastOnce,at,partition:new("default")));}catch(Exception e){return new{exception=e.GetType().FullName,message=e.Message};}}
string? delayedId=null;
async Task View(string stage,object? operation=null){using(var connection=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={directory}/elsa.db;Mode=ReadOnly;Pooling=False"))using(var backup=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={directory}/{stage}.sqlite;Pooling=False")){await connection.OpenAsync();await backup.OpenAsync();connection.BackupDatabase(backup);}await File.WriteAllTextAsync(Path.Combine(directory,stage+".json"),JsonSerializer.Serialize(new{stage,at=DateTimeOffset.UtcNow,operation,outer=await models.FindAsync(outer),middle=await workflows.FindAsync(middle),activities=await activities.ListAllAsync(middle),delayed=delayedId is null?null:await lookup.FindDurableAsync(delayedId),future=await outbox.GetDeliverableAsync(new(DateTimeOffset.UtcNow.AddHours(2),50,middle)),outerQueue=await queue.ListAsync(new(outer)),middleQueue=await queue.ListAsync(new(middle))},web));Console.WriteLine(stage);}
await View("01-published");await View("02-start-worker",await processor.ProcessAsync(new(1,outer,"Elsa.Bpmn.StartNativeWork.v1")));
var future=await outbox.GetDeliverableAsync(new(DateTimeOffset.UtcNow.AddHours(2),50,middle));delayedId=future.Single(i=>i.Intent.NotBefore>DateTimeOffset.UtcNow).OutboxItemId;
await View("03-waiting");await View("04-cancel-native",await Send(middle,WorkflowExecutionCommandKind.Cancel));
await View("05-deliver-result",await processor.ProcessAsync(new(1,middle,"Elsa.Bpmn.NativeWorkResult.v1")));await View("06-drain-outer",await Send(outer,WorkflowExecutionCommandKind.RunSchedulerWork));
await View("07-normal-recovery",await sdk.GetRequiredService<IRuntimeResumptionService>().SweepAsync());
Console.WriteLine("Probe complete");
class Access:IPersistenceAccessContextAccessor{public PersistenceAccessContext Current=>PersistenceAccessContext.Scoped(new("default"));}
