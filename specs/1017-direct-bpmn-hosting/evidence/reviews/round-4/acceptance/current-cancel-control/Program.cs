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

        if (args.Contains("--recover"))
        {
            const string modelId = "bpmn:759a49f25aafc2f18846e939615e947ade8ed03d0af813339db2423af2e4bc9e";
            var api = harness.Services.GetRequiredService<IBpmnModelRuntime>();
            var who = new BpmnModelCaller("probe-user", null);
            Console.WriteLine("BEFORE MODEL " + JsonSerializer.Serialize(await api.FindExecutionAsync(modelId, who), Web));
            Console.WriteLine("EXPLICIT CANCEL " + JsonSerializer.Serialize(await api.CancelAsync(modelId, "review-explicit-cleanup-control", "Recover the retained waiting worker", who), Web));
            var pump = harness.Services.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
            for (var pass = 0; pass < 80; pass++)
            {
                await pump.ProcessAsync(new(limit: 100));
                var identities = (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync()).Select(s => s.WorkflowExecutionId).Append(modelId);
                foreach (var identity in identities)
                {
                    var at = DateTimeOffset.UtcNow;
                    var agent = await harness.Services.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(new(identity, WorkflowExecutionActorActivationReason.Recovery, at, "probe-user", WorkflowExecutionActorCapabilities.InProcessMailbox, partition: new(PersistenceScope.DefaultValue)));
                    var request = "review-control-drain-" + pass + "-" + identity;
                    await agent.EnqueueAsync(new(request, identity, new(request, identity, WorkflowExecutionCommandKind.RunSchedulerWork, at, null, new Dictionary<string, string>()), request, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, at, partition: new(PersistenceScope.DefaultValue)));
                }
                await Task.Delay(80);
            }
            Console.WriteLine("CONTROL FINAL MODEL " + JsonSerializer.Serialize(await api.FindExecutionAsync(modelId, who), Web));
            Console.WriteLine("CONTROL FINAL WORKERS " + JsonSerializer.Serialize(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync(), Web));
            Console.WriteLine("CONTROL FINAL OUTBOX " + JsonSerializer.Serialize(await harness.Services.GetRequiredService<IRuntimePostCommitOutboxStore>().GetDeliverableAsync(new(DateTimeOffset.UtcNow.AddDays(1), 100)), Web));
            return;
        }
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
        var start = await runtime.StartAsync(published.SourceReferenceId, "review-start-concurrent-wait", null, caller);
        var id = start.ExecutionId; Console.WriteLine("MODEL " + id);
        var processor = harness.Services.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        Console.WriteLine("ADMIT " + JsonSerializer.Serialize(await processor.ProcessAsync(new(workflowExecutionId: id, intentKind: "Elsa.Bpmn.StartNativeWork.v1", limit: 10))));
        var workers = await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync();
        Console.WriteLine("WAITING WORKERS " + JsonSerializer.Serialize(workers, Web));
        if (workers.Count != 2 || workers.Any(w => w.Status.IsTerminal())) throw new Exception("The two normally admitted Event workers must both be active.");
        Console.WriteLine("RELEASE FIRST " + JsonSerializer.Serialize(await harness.Services.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(new(workers.First().WorkflowExecutionId, EventStimulus.StimulusType, EventStimulus.Hash("review.release.one"), JsonSerializer.SerializeToElement(new { eventName = "review.release.one" }), "review-release-first")), Web));
        Console.WriteLine("RESULT " + JsonSerializer.Serialize(await processor.ProcessAsync(new(intentKind: "Elsa.Bpmn.NativeWorkResult.v1", limit: 1))));
        var now = DateTimeOffset.UtcNow; var actor = await harness.Services.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(new(id, WorkflowExecutionActorActivationReason.Start, now, "probe-user", WorkflowExecutionActorCapabilities.InProcessMailbox, partition: new(PersistenceScope.DefaultValue)));
        const string drain = "review-drain-first-result";
        Console.WriteLine("DRAIN " + JsonSerializer.Serialize(await actor.EnqueueAsync(new(drain, id, new(drain, id, WorkflowExecutionCommandKind.RunSchedulerWork, now, null, new Dictionary<string, string>()), drain, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now, partition: new(PersistenceScope.DefaultValue)))));
        Console.WriteLine("FINAL MODEL " + JsonSerializer.Serialize(await runtime.FindExecutionAsync(id, caller), Web));
        Console.WriteLine("FINAL WORKERS " + JsonSerializer.Serialize(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync(), Web));
        Console.WriteLine("FINAL OUTBOX " + JsonSerializer.Serialize(await harness.Services.GetRequiredService<IRuntimePostCommitOutboxStore>().GetDeliverableAsync(new(DateTimeOffset.UtcNow.AddDays(1), 100)), Web));
    }
}
