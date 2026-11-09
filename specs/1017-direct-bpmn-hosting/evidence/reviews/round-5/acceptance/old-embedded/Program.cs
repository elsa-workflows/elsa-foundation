using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Bpmn;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Testing;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
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
using Elsa.Activities.Bpmn.Activities;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

class Program
{
    static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    static async Task Main(string[] args)
    {
        var path = Path.GetFullPath(args[0]);
        Console.WriteLine("STORE " + path);
        await using var harness = WorkflowExecutionHarness.Create().ConfigureServices(services =>
        {
            services.AddLogging(); services.AddEfSchemaReadability();
            new EventsFeature().ConfigureServices(services);
            new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
            new ActivitiesPrimitivesFeature().ConfigureServices(services);
            new ActivitiesFlowchartFeature().ConfigureServices(services);
            new ActivitiesBpmnHostingFeature { AdmitNewExecutions = true }.ConfigureServices(services);
            services.AddRuntimeEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = $"Data Source={path};Pooling=False", RecoveryContinuationSigningKey = new string('r', 32), HierarchyCursorSigningKey = new string('h', 32) }).AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
        }).Build(Enumerable.Range(1, 100).Select(i => "review-event-" + i));
        foreach (var init in harness.Services.GetServices<IShellInitializer>()) await init.InitializeAsync();
        foreach (var task in harness.Services.GetServices<IStartupTask>()) await task.ExecuteAsync(CancellationToken.None);
        foreach (var assembly in new[] { typeof(ActivitiesBpmnHostingFeature).Assembly, typeof(RuntimeCheckpointCommit).Assembly, typeof(RuntimeDbContext).Assembly, typeof(WorkflowExecutionHarness).Assembly }) Console.WriteLine("PRODUCT " + assembly.GetName().Name + " " + System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion + " " + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assembly.Location))));
        var wait = Nodes.Leaf("wait", typeof(Event), new Dictionary<string, object?> { ["EventName"] = "review.release.one", ["CanStartWorkflow"] = false });
        var fault = Nodes.Leaf("fault", typeof(Fault), new Dictionary<string, object?> { ["code"] = "review.explicit-fault", ["message"] = "First worker deliberately faults after its real event wait." });
        var root = Nodes.Structural("flow", typeof(FlowchartActivity), FlowchartActivity.StructureKind, FlowchartActivity.StructureSchemaVersion,
            new FlowchartStructure([new FlowchartConnection(new FlowchartEndpoint("wait", null), new FlowchartEndpoint("fault"))]), [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, [wait, fault])]);
        var target = WorkflowExecutableResumeTarget.ComposeScopedId("wait", Event.ResumeTargetId);
        var worker = new WorkflowExecutable(WorkflowExecutionHarness.Identity, root,
            new Dictionary<string, WorkflowExecutableResumeTarget> { [target] = new(target, "wait", "ResumeAsync", new Dictionary<string, string>(), Event.ResumeTargetId) },
            WorkflowExecutionHarness.Timestamp, new Dictionary<string, string>(), inputContract: new WorkflowExecutableInputContract(1, []), dependencies: null, incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
        await harness.PublishAsync(worker, "review-wait-then-fault-publication");
        var process = new BpmnProcessDefinition("concurrent-services-waiting", Elements: [new("start", BpmnElementTypes.StartEvent), new("service", BpmnElementTypes.ServiceTask, bindingRef: "worker"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("one", "start", "service"), new("two", "start", "service"), new("finish", "service", "end")]);
        var runtime = harness.Services.GetRequiredService<IBpmnModelRuntime>(); var caller = new BpmnModelCaller("probe-user", null);
        var published = await runtime.PublishAsync(new(2, JsonSerializer.SerializeToElement(process, Web), [], [new("worker", worker.Identity, "test.error")]), "review-publish-concurrent-wait", caller);
        var node = Nodes.Leaf("run-bpmn", typeof(RunBpmn), new Dictionary<string, object?> { ["Model"] = published.SourceReferenceId });
        var compiler = harness.Services.GetServices<IExecutableCompilationSource>().Single(x => x.GetType().Name == "BpmnEmbeddingPinSource");
        var request = new WorkflowExecutableCompileRequest("parent-v1", WorkflowExecutableReferenceScope.Published, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "review-parent");
        var claims = await compiler.GetContributionAsync(new(request, null!, node));
        node = Nodes.Leaf("run-bpmn", typeof(RunBpmn), new Dictionary<string, object?> { ["Model"] = published.SourceReferenceId }, claims.NodeMetadata.ToDictionary(c => c.Key, c => c.Value));
        var resume = WorkflowExecutableResumeTarget.ComposeScopedId("run-bpmn", "bpmn-model-completed");
        var parent = new WorkflowExecutable(new("review-parent-artifact", "review-parent-definition", "review-parent-v1", "1.0.0", "sha256:review-parent"), node,
            new Dictionary<string, WorkflowExecutableResumeTarget> { [resume] = new(resume, "run-bpmn", "ResumeAsync", new Dictionary<string,string>(), "bpmn-model-completed") },
            DateTimeOffset.UtcNow, new Dictionary<string,string>(), inputContract: new WorkflowExecutableInputContract(1, []),
            dependencies: claims.Dependencies.Select(c => new WorkflowExecutableDependency(c.ArtifactId,c.ArtifactHash,[c.ExecutableNodeId])).ToArray(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
        var parentRef = await harness.PublishAsync(parent,"review-parent-publication");
        Console.WriteLine("PARENT START " + JsonSerializer.Serialize(await harness.StartPublishedAsync(parentRef,"review-native-parent",partition: new(PersistenceScope.DefaultValue)), Web));
        var pages = await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListPageAsync(new("review-native-parent"));
        var native = pages.Items.Single(x => x.Execution.ActivityType == typeof(RunBpmn).FullName);
        var encodedId = native.PrivateState!.Value.InlineValue!.Value.GetProperty("Invocation").GetProperty("executionId").GetString()!;
        var id = System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(encodedId[4..]));
        Console.WriteLine("MODEL " + id);
        var processor = harness.Services.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        Console.WriteLine("EMBED ADMIT " + JsonSerializer.Serialize(await processor.ProcessAsync(new(workflowExecutionId: "review-native-parent", intentKind: "Elsa.Bpmn.StartEmbedded.v1", limit: 10))));
        await Drain(id,"review-drain-embedded-start");
        Console.WriteLine("ADMIT " + JsonSerializer.Serialize(await processor.ProcessAsync(new(workflowExecutionId: id, intentKind: "Elsa.Bpmn.StartNativeWork.v1", limit: 10))));
        var workers = (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync()).Where(w => w.WorkflowExecutionId != "review-native-parent").ToArray();
        Console.WriteLine("WAITING WORKERS " + JsonSerializer.Serialize(workers, Web));
        if (workers.Length != 2 || workers.Any(w => w.Status.IsTerminal())) throw new Exception("The two normally admitted Event workers must both be active.");
        Console.WriteLine("RELEASE FIRST " + JsonSerializer.Serialize(await harness.Services.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(new(workers.First().WorkflowExecutionId, EventStimulus.StimulusType, EventStimulus.Hash("review.release.one"), JsonSerializer.SerializeToElement(new { eventName = "review.release.one" }), "review-release-first")), Web));
        Console.WriteLine("RESULT " + JsonSerializer.Serialize(await processor.ProcessAsync(new(intentKind: "Elsa.Bpmn.NativeWorkResult.v1", limit: 1))));
        var now = DateTimeOffset.UtcNow; var actor = await harness.Services.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(new(id, WorkflowExecutionActorActivationReason.Start, now, "probe-user", WorkflowExecutionActorCapabilities.InProcessMailbox, partition: new(PersistenceScope.DefaultValue)));
        const string drain = "review-drain-first-result";
        Console.WriteLine("DRAIN " + JsonSerializer.Serialize(await actor.EnqueueAsync(new(drain, id, new(drain, id, WorkflowExecutionCommandKind.RunSchedulerWork, now, null, new Dictionary<string, string>()), drain, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now, partition: new(PersistenceScope.DefaultValue)))));
        Console.WriteLine("FINAL MODEL " + JsonSerializer.Serialize(await runtime.FindExecutionAsync(id, caller), Web));
        Console.WriteLine("FINAL WORKERS " + JsonSerializer.Serialize(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync(), Web));
        async Task Drain(string executionId,string requestId) {
            var at=DateTimeOffset.UtcNow;
            var agent=await harness.Services.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(new(executionId,WorkflowExecutionActorActivationReason.Recovery,at,"probe-user",WorkflowExecutionActorCapabilities.InProcessMailbox,partition:new(PersistenceScope.DefaultValue)));
            Console.WriteLine("EXTRA DRAIN " + JsonSerializer.Serialize(await agent.EnqueueAsync(new(requestId,executionId,new(requestId,executionId,WorkflowExecutionCommandKind.RunSchedulerWork,at,null,new Dictionary<string,string>()),requestId,WorkflowExecutionCommandDeliveryMode.AtLeastOnce,at,partition:new(PersistenceScope.DefaultValue)))));
        }
        Console.WriteLine("FINAL OUTBOX " + JsonSerializer.Serialize(await harness.Services.GetRequiredService<IRuntimePostCommitOutboxStore>().GetDeliverableAsync(new(DateTimeOffset.UtcNow.AddDays(1), 100)), Web));
    }
}
