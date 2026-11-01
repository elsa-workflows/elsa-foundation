using System.Reflection;
using System.Text.Json;
using Bpmn.Model;
using CShells.Lifecycle;
using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Bpmn;
using Elsa.Activities.Bpmn.Activities;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Testing;
using Elsa.Cluster.Readability;
using Elsa.Events;
using Elsa.Persistence.EntityFramework;
using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.Extensions.DependencyInjection;
using Xunit;


static class Program { private const string NodeId = "run-bpmn"; private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web); static async Task Main(string[] args) { var human = args.Contains("--human");
 foreach (var asm in new[] { typeof(RunBpmn).Assembly, typeof(RuntimeCheckpointCommit).Assembly, typeof(WorkflowExecutionHarness).Assembly, typeof(RuntimeDbContext).Assembly }) Console.WriteLine("PRODUCT " + asm.GetName().Name + " " + System.Diagnostics.FileVersionInfo.GetVersionInfo(asm.Location).ProductVersion + " " + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(asm.Location))));
        var path = Path.Combine(Path.GetTempPath(), "elsa-runbpmn-drive-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            await using var harness = WorkflowExecutionHarness.Create().ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddEfSchemaReadability();
                new EventsFeature().ConfigureServices(services);
                new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
                new ActivitiesBpmnHostingFeature { AdmitNewExecutions = true }.ConfigureServices(services);
                services.AddRuntimeEntityFrameworkCore(new()
                {
                    Provider = "Sqlite", ConnectionString = $"Data Source={path};Pooling=False",
                    RecoveryContinuationSigningKey = new string('r', 32), HierarchyCursorSigningKey = new string('h', 32)
                }).AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
            }).Build(Enumerable.Range(1, 16).Select(index => "runbpmn-drive-" + index));
            foreach (var initializer in harness.Services.GetServices<IShellInitializer>()) await initializer.InitializeAsync();
            foreach (var task in harness.Services.GetServices<IStartupTask>()) await task.ExecuteAsync(CancellationToken.None);

            var caller = new BpmnModelCaller("activity-behavioural-drive", null);
            var runtime = harness.Services.GetRequiredService<IBpmnModelRuntime>();
            var process = human ? new BpmnProcessDefinition("human-control", Elements: [new("start",BpmnElementTypes.StartEvent),new("work",BpmnElementTypes.UserTask,bindingRef:"human"),new("end",BpmnElementTypes.EndEvent)], SequenceFlows:[new("start-work","start","work"),new("work-end","work","end")]) : new BpmnProcessDefinition("instant", Elements: [new("start", BpmnElementTypes.StartEvent), new("end", BpmnElementTypes.EndEvent)], SequenceFlows: [new("to-end", "start", "end")]);
            var published = await runtime.PublishAsync(new(1, JsonSerializer.SerializeToElement(process, Web), human ? ["human"] : []), "publish-drive", caller);
            var node = Nodes.Leaf(NodeId, typeof(RunBpmn), new Dictionary<string, object?> { [nameof(RunBpmn.Model)] = published.SourceReferenceId });
            var request = new WorkflowExecutableCompileRequest("native-drive-version", WorkflowExecutableReferenceScope.Published,
                WorkflowExecutionHarness.Timestamp, WorkflowExecutionHarness.Timestamp, null, "drive-");
            var source = new WorkflowExecutableCompileSource("native-drive", "native-drive-version", "1.0.0", WorkflowDefinitionState.Empty,
                "WorkflowDefinitionVersion", "native-drive-version", "1.0.0");
            var claims = new List<ExecutableCompilationContribution>();
            foreach (var contributor in harness.Services.GetServices<IExecutableCompilationSource>())
                claims.Add(await contributor.GetContributionAsync(new(request, source, node)));
            node = Nodes.Leaf(NodeId, typeof(RunBpmn), new Dictionary<string, object?> { [nameof(RunBpmn.Model)] = published.SourceReferenceId },
                claims.SelectMany(item => item.NodeMetadata).ToDictionary(item => item.Key, item => item.Value));
            // Read the activity's declared target; no substitute completion or bookmark is manufactured.
            var method = typeof(RunBpmn).GetMethod("ResumeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var local = method.GetCustomAttribute<ResumeTargetAttribute>()!.ResumeTargetId;
            var target = WorkflowExecutableResumeTarget.ComposeScopedId(NodeId, local);
            var executable = new WorkflowExecutable(WorkflowExecutionHarness.Identity, node,
                new Dictionary<string, WorkflowExecutableResumeTarget> { [target] = new(target, NodeId, method.Name, new Dictionary<string, string>(), local) },
                WorkflowExecutionHarness.Timestamp, new Dictionary<string, string>(), inputContract: null,
                dependencies: claims.SelectMany(item => item.Dependencies).Select(edge => new WorkflowExecutableDependency(edge.ArtifactId, edge.ArtifactHash, [edge.ExecutableNodeId])).ToArray(),
                incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
            var reference = await harness.PublishAsync(executable, "native-drive-source");
            const string parentId = "native-runbpmn-drive";
            await harness.StartPublishedAsync(reference, parentId, partition: new(PersistenceScope.DefaultValue));

            var direct = await runtime.StartAsync(published.SourceReferenceId, "direct-instant", null, caller);
            Console.WriteLine("Direct: " + JsonSerializer.Serialize(direct));
            var beforeRetirementReplay = await runtime.StartAsync(published.SourceReferenceId, "direct-instant", null, caller);
            Console.WriteLine("Start replay before retirement: " + JsonSerializer.Serialize(beforeRetirementReplay));
            if(args.Contains("--retire")) { var references = harness.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
            Console.WriteLine("Retire source=" + await references.RetireAsync(published.SourceReferenceId, DateTimeOffset.UtcNow, "private-correctness-probe"));
            try { Console.WriteLine("Start replay after retirement: " + JsonSerializer.Serialize(await runtime.StartAsync(published.SourceReferenceId,"direct-instant",null,caller))); }
            catch(Exception e) { Console.WriteLine("Start replay after retirement failure: " + e.GetType().FullName + " " + e.Message); }
            Console.WriteLine("Retained original start outcome: " + JsonSerializer.Serialize(await runtime.FindCommandAsync(direct.ExecutionId,"direct-instant",caller))); }
            var bookmarks = (await harness.Services.GetRequiredService<IBookmarkStateStore>().ListPageAsync(new(parentId))).Items;
            Console.WriteLine("Native bookmarks=" + bookmarks.Count);
            var modelId = bookmarks.Single().StimulusHash;
            for (var index=0; index<3; index++) {
                try { var sweep=await harness.SweepAsync(); Console.WriteLine("Sweep: " + JsonSerializer.Serialize(sweep)); }
                catch(Exception e) { Console.WriteLine("Sweep exception: " + e); }
                if (human && await runtime.FindExecutionAsync(modelId,caller) is { Lifecycle: RuntimeModelLifecycle.Running }) {
                    var work=(await runtime.ListWorkAsync(modelId,new(),caller)).Items.Single();
                    await runtime.CompleteHumanAsync(modelId,"complete-control",work.InvocationId,work.AttemptId,null,caller);
                }
                await Task.Delay(1200);
            }
            Console.WriteLine("Embedded model: " + JsonSerializer.Serialize(await runtime.FindExecutionAsync(modelId, caller)));
            var run = await harness.ReadRunAsync(parentId);
            Console.WriteLine("Native state: " + JsonSerializer.Serialize(run));
        } finally { foreach(var file in new[] { path,path+"-wal",path+"-shm" }) if(File.Exists(file)) File.Delete(file); }
    }
}
