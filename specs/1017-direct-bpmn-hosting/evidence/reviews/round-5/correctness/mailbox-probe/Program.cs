using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text.Json;
using Bpmn.Model;
using CShells.Lifecycle;
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
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;

var directory = args[0]; Directory.CreateDirectory(directory);
int? mailboxes = null;
using var listener = new MeterListener();
listener.InstrumentPublished = (instrument, l) => { if (instrument.Name == InProcessWorkflowExecutionActorProvider.LiveMailboxGaugeName) l.EnableMeasurementEvents(instrument); };
listener.SetMeasurementEventCallback<int>((instrument, value, tags, state) => mailboxes = value);
listener.Start();
int Count() { listener.RecordObservableInstruments(); return mailboxes ?? -1; }
await using var host = WorkflowExecutionHarness.Create().ConfigureServices(services => {
 services.AddLogging(); services.AddEfSchemaReadability();
 new EventsFeature().ConfigureServices(services);
 new ActivitiesBpmnHostingFeature { AdmitNewExecutions = true }.ConfigureServices(services);
 services.AddRuntimeEntityFrameworkCore(new() { Provider="Sqlite", ConnectionString=$"Data Source={directory}/runtime.sqlite;Pooling=False", RecoveryContinuationSigningKey=new string('r',32), HierarchyCursorSigningKey=new string('h',32) }).AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
}).Build("native-control");
foreach (var initializer in host.Services.GetServices<IShellInitializer>()) await initializer.InitializeAsync();
foreach (var task in host.Services.GetServices<IStartupTask>()) await task.ExecuteAsync(CancellationToken.None);
var actors = host.Services.GetRequiredService<IWorkflowExecutionActorProvider>();
var options = host.Services.GetRequiredService<RuntimeActorEvictionOptions>();
var before = Count();
async Task<WorkflowExecutionCommandDispatchResult> Redrive(string executionId, string envelopeId) {
 var at = DateTimeOffset.UtcNow;
 var actor = await actors.GetAgentAsync(new(executionId,WorkflowExecutionActorActivationReason.Start,at,"review-probe",WorkflowExecutionActorCapabilities.InProcessMailbox));
 return await actor.EnqueueAsync(new(envelopeId,executionId,new(envelopeId,executionId,WorkflowExecutionCommandKind.RunSchedulerWork,at,null,new Dictionary<string,string>()),envelopeId,WorkflowExecutionCommandDeliveryMode.AtLeastOnce,at));
}
await using var scope = await host.Services.GetRequiredService<IPersistenceOperationScopeFactory>().CreateAsync(new(PersistenceScope.DefaultValue));
var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
var process = new BpmnProcessDefinition("immediate",Elements:[new("start",BpmnElementTypes.StartEvent),new("end",BpmnElementTypes.EndEvent)],SequenceFlows:[new("finish","start","end")]);
var definition = new BpmnModelDefinition(1,JsonSerializer.SerializeToElement(process,new JsonSerializerOptions(JsonSerializerDefaults.Web)),[]);
var caller = new BpmnModelCaller("operator",null);
var publication = await runtime.PublishAsync(definition,"publication",caller);
var starts = new List<object>(); var modelIds = new List<string>();
for(int i=0;i<8;i++) {
 var start = await runtime.StartAsync(publication.SourceReferenceId,"start-"+i,null,caller);
 modelIds.Add(start.ExecutionId);
 var state = await runtime.FindExecutionAsync(start.ExecutionId,caller);
 var queue = await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new(start.ExecutionId));
 starts.Add(new { start.ExecutionId,start.Pending,lifecycle=state!.Lifecycle.ToString(),status=state.TerminalResult!.Value.GetProperty("status").GetString(),queue=queue.Items.Count,mailboxes=Count() });
}
var beforeModelRedrive=Count();
var modelRedrives = new List<object>();
foreach(var id in modelIds) { var redrive=await Redrive(id,"model-terminal-redrive:"+id); modelRedrives.Add(new {id,status=redrive.Status.ToString(),terminalMetadata=redrive.Metadata.GetValueOrDefault(InProcessWorkflowExecutionActorProvider.WorkflowTerminatedMetadataKey),mailboxes=Count()}); }
var afterModelRedrive=Count();
foreach(var id in modelIds) await actors.PassivateAsync(new(id,WorkflowExecutionActorPassivationBoundary.AfterCheckpointCommit,DateTimeOffset.UtcNow,"explicit-review-control"));
var afterExplicitPassivation=Count();
var replays = new List<object>();
for(int i=0;i<8;i++) { var replay=await runtime.StartAsync(publication.SourceReferenceId,"start-"+i,null,caller);replays.Add(new {replay.ExecutionId,replay.Replay,replay.Pending,mailboxes=Count()}); }
var result = new { beforeModelRedrive,afterModelRedrive,afterExplicitPassivation,replays,modelRedrives, passivateOnTerminal=options.PassivateOnTerminal,before,starts,assemblies=new[]{typeof(IBpmnModelRuntime).Assembly,typeof(ActivitiesBpmnHostingFeature).Assembly,typeof(InProcessWorkflowExecutionActorProvider).Assembly,typeof(RuntimeDbContext).Assembly}.Select(a=>new { name=a.GetName().Name,source=a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion }) };
var output = JsonSerializer.Serialize(result,new JsonSerializerOptions { WriteIndented=true }); Console.WriteLine(output); await File.WriteAllTextAsync(Path.Combine(directory,"result.json"),output+Environment.NewLine);
