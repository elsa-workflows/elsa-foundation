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

namespace Elsa.Activities.Behavioral.Drives;

/// <summary>Drives the published model, real native wait, shared delivery and actual typed native result.</summary>
public sealed class RunBpmnDrive : IActivityDrive
{
    private const string NodeId = "run-bpmn";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    public Type ActivityType => typeof(RunBpmn);

    public async Task DriveAsync(ActivityDriveRecorder recorder)
    {
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
                new Elsa.Activities.Bpmn.Design.ActivitiesBpmnDesignFeature().ConfigureServices(services);
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
            var process = new BpmnProcessDefinition("behavioural-human", Elements:
                [new("start", BpmnElementTypes.StartEvent), new("human", BpmnElementTypes.UserTask, bindingRef: "work"), new("end", BpmnElementTypes.EndEvent)],
                SequenceFlows: [new("to-work", "start", "human"), new("to-end", "human", "end")]);
            var published = await runtime.PublishAsync(new(1, JsonSerializer.SerializeToElement(process, Web), ["work"]), "publish-drive", caller);
            var node = Nodes.Leaf(NodeId, ActivityType, new Dictionary<string, object?> { [nameof(RunBpmn.Model)] = published.SourceReferenceId });
            var request = new WorkflowExecutableCompileRequest("native-drive-version", WorkflowExecutableReferenceScope.Published,
                WorkflowExecutionHarness.Timestamp, WorkflowExecutionHarness.Timestamp, null, "drive-");
            var source = new WorkflowExecutableCompileSource("native-drive", "native-drive-version", "1.0.0", WorkflowDefinitionState.Empty,
                "WorkflowDefinitionVersion", "native-drive-version", "1.0.0");
            var claims = new List<ExecutableCompilationContribution>();
            foreach (var contributor in harness.Services.GetServices<IExecutableCompilationSource>())
                claims.Add(await contributor.GetContributionAsync(new(request, source, node)));
            node = Nodes.Leaf(NodeId, ActivityType, new Dictionary<string, object?> { [nameof(RunBpmn.Model)] = published.SourceReferenceId },
                claims.SelectMany(item => item.NodeMetadata).ToDictionary(item => item.Key, item => item.Value));
            // Read the activity's declared target; no substitute completion or bookmark is manufactured.
            var method = ActivityType.GetMethod("ResumeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
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
            var bookmark = Assert.Single((await harness.Services.GetRequiredService<IBookmarkStateStore>().ListPageAsync(new(parentId))).Items);
            var modelId = bookmark.StimulusHash;
            await UntilAsync(harness, async () => await runtime.FindExecutionAsync(modelId, caller) is not null);
            var work = Assert.Single((await runtime.ListWorkAsync(modelId, new(), caller)).Items);
            await runtime.CompleteHumanAsync(modelId, "complete-drive", work.InvocationId, work.AttemptId, null, caller);
            await UntilAsync(harness, async () => (await runtime.FindExecutionAsync(modelId, caller))?.Lifecycle == RuntimeModelLifecycle.TerminalSettled);
            var run = await harness.ReadRunAsync(parentId);
            run.AssertWorkflowCompleted(); run.AssertOutcomes(NodeId, "Done");
            recorder.Record(ActivityType, run, NodeId);

            // Keep the required-input guard measured through a real engine start, separately from model publication.
            try
            {
                var missing = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(Nodes.Leaf("missing-model", ActivityType),
                    new WorkflowExecutableIdentity("missing-model", "missing-model", "missing-model-v1", "1.0.0", "sha256:missing-model")),
                    allowPendingWorkOnTerminalCompletion: true);
                recorder.RecordRequiredInputFault(ActivityType, nameof(RunBpmn.Model), missing, "missing-model");
            }
            catch (InvalidOperationException rejection)
            {
                recorder.RecordRequiredInputFault(ActivityType, nameof(RunBpmn.Model), null, "missing-model", rejection);
            }
        }
        finally
        {
            foreach (var file in new[] { path, path + "-wal", path + "-shm" }) File.Delete(file);
        }
    }

    private static async Task UntilAsync(WorkflowExecutionHarness harness, Func<Task<bool>> accepted)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (!await accepted())
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, "The real RunBpmn drive did not consume its model/native responsibility.");
            await harness.SweepAsync(); await Task.Delay(100);
        }
    }
}
