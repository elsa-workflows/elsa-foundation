using System.Diagnostics;
using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.DispatchWorkflow.Runtime;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DispatchWorkflowActivity = Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// A parent that waits on a dispatched child, run end to end on the EF Runtime stores over SQLite. It mirrors cases B and
/// C of <c>e2e-tests/composition/Test-DispatchWorkflowOutcomes.ps1</c> in-process: the parent is
/// <c>Sequence[DispatchWorkflow(WaitForCompletion), probe]</c>, and the probe running proves the parent resumed past the
/// wait.
/// </summary>
/// <remarks>
/// The in-memory DispatchWorkflow tests cannot catch a rule only a durable store applied: waiting on a child was broken on
/// EF because its checkpoint store refused the child's terminal checkpoint, which carries the parent's resume intent. Neither
/// case failed loudly. A completing child's start was acknowledged as delivered once the refused commit left its drain
/// faulted; a faulting child's refusal was retried as transient until exhaustion, where the existing child made the store
/// record the start as delivered rather than failed. Either way the parent waited forever.
/// </remarks>
public sealed class RuntimeEntityFrameworkCoreDispatchWorkflowTests : IAsyncLifetime
{
    private const string ParentWorkflowExecutionId = "wfexec-parent";
    private const string DispatchNodeId = "node-dispatch";
    private const string AfterDispatchNodeId = "node-after";
    private const string ChildNodeId = "node-child";
    private static readonly TimeSpan TerminalTimeout = TimeSpan.FromSeconds(60);
    private readonly string _databasePath = Path.Join(Path.GetTempPath(), $"elsa-runtime-ef-dispatch-{Guid.NewGuid():N}.db");
    private WorkflowExecutionHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .WithFeature(services => new WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
            .WithFeature(services => new DispatchWorkflowRuntimeFeature().ConfigureServices(services))
            .ConfigureServices(services => services
                .AddLogging()
                .AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite",
                    ConnectionString = $"Data Source={_databasePath};Pooling=False",
                    RecoveryContinuationSigningKey = "ef-runtime-dispatch-recovery-signing-key-32",
                    HierarchyCursorSigningKey = "ef-runtime-dispatch-hierarchy-signing-key-32"
                })
                .AddEfModuleMigrations<BookmarkStateDbContext>("Sqlite"))
            .Build(Enumerable.Range(1, 16).Select(ordinal => $"actexec-{ordinal}"));

        foreach (var initializer in _harness.Services.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
        _harness.InitializeActivityTypes();
    }

    public async Task DisposeAsync()
    {
        await _harness.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
            File.Delete(file);
    }

    [Theory]
    [InlineData(false, WorkflowExecutionStatus.Completed, "Completed")]
    [InlineData(true, WorkflowExecutionStatus.Faulted, "Faulted")]
    public async Task A_parent_waiting_on_its_child_resumes_with_the_child_outcome(
        bool childFaults,
        WorkflowExecutionStatus expectedChildStatus,
        string expectedDispatchOutcome)
    {
        var child = NewChildExecutable(childFaults);
        var childReference = await _harness.PublishAsync(child, "source-child");
        var parentReference = await _harness.PublishAsync(NewParentExecutable(child.Identity, childReference), "source-parent");

        // Published at the root, which runs in the default persistence scope; the parent (and so its child) must run there too.
        await _harness.StartPublishedAsync(
            parentReference,
            ParentWorkflowExecutionId,
            correlationId: "correlation-parent",
            partition: new WorkflowExecutionPartition(PersistenceScope.DefaultValue));
        var parent = await WaitForTerminalParentAsync();

        // A faulted child routes to an ordinary outcome: the parent itself still completes and runs the step after.
        parent.AssertWorkflowCompleted();
        parent.AssertOutcomes(DispatchNodeId, expectedDispatchOutcome);
        parent.AssertCompleted(AfterDispatchNodeId);
        var dispatch = Assert.Single(await ListDispatchesAsync());
        Assert.Equal(expectedChildStatus, (await _harness.ReadRunAsync(dispatch.ChildWorkflowExecutionId)).WorkflowState?.Status);
    }

    /// <summary>
    /// Delivers due post-commit work until the parent is terminal. Delivery retries are real-time, so this polls against a
    /// deadline rather than a fixed number of sweeps, and reports where the chain stopped when it never gets there.
    /// </summary>
    private async Task<WorkflowExecutionRun> WaitForTerminalParentAsync()
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            await _harness.SweepAsync();
            var run = await _harness.ReadRunAsync(ParentWorkflowExecutionId);
            if (run.WorkflowState?.Status is WorkflowExecutionStatus.Completed or WorkflowExecutionStatus.Faulted or WorkflowExecutionStatus.Cancelled)
                return run;
            if (deadline.Elapsed > TerminalTimeout)
                throw new TimeoutException($"The parent did not reach a terminal status within {TerminalTimeout}. {await DescribeAsync(run)}");
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private async Task<string> DescribeAsync(WorkflowExecutionRun parent)
    {
        var dispatches = await ListDispatchesAsync();
        var activities = string.Join(", ", parent.ActivityStates.Select(state => $"{state.Execution.ExecutableNodeId}={state.Status}"));
        return $"Parent: {parent.WorkflowState?.Status}; activities: {activities}; dispatches: {string.Join(", ", dispatches.Select(dispatch => dispatch.Status))}.";
    }

    /// <summary>EF stores are scoped, so each read resolves the dispatch store in a scope of its own.</summary>
    private async Task<IReadOnlyCollection<WorkflowDispatchRecord>> ListDispatchesAsync()
    {
        await using var scope = _harness.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowDispatchStore>().ListAsync(ParentWorkflowExecutionId);
    }

    private static WorkflowExecutable NewParentExecutable(WorkflowExecutableIdentity childIdentity, WorkflowExecutableSourceReference childReference)
    {
        var pin = new DispatchWorkflowPin(childIdentity, WorkflowExecutableSourceProvenance.From(childReference));
        var dispatch = Leaf(
            DispatchNodeId,
            typeof(DispatchWorkflowActivity),
            new Dictionary<string, object?>
            {
                [nameof(DispatchWorkflowActivity.WorkflowDefinitionId)] = childIdentity.DefinitionId,
                [nameof(DispatchWorkflowActivity.WaitForCompletion)] = true
            },
            new Dictionary<string, string>
            {
                [DispatchWorkflowConstants.PinnedTargetMetadataKey] = JsonSerializer.Serialize(pin, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            });
        var after = WorkflowExecutionHarness.NewProbeNode(AfterDispatchNodeId);
        var root = new ExecutableNode(
            executableNodeId: "node-sequence",
            authoredActivityId: "authored-node-sequence",
            activityType: typeof(SequenceActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, [dispatch, after])],
            structure: new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = new[] { DispatchNodeId, AfterDispatchNodeId } })));

        // The resume-target map the publish compiler emits for a waiting dispatch node.
        var resumeTargetId = WorkflowExecutableResumeTarget.ComposeScopedId(DispatchNodeId, DispatchWorkflowConstants.CompletionResumeTargetId);
        return new WorkflowExecutable(
            identity: Identity("parent"),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                [resumeTargetId] = new(resumeTargetId, DispatchNodeId, "OnChildCompletedAsync", new Dictionary<string, string>(), DispatchWorkflowConstants.CompletionResumeTargetId)
            },
            createdAt: WorkflowExecutionHarness.Timestamp,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies: [],
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
    }

    private static WorkflowExecutable NewChildExecutable(bool faults) =>
        new(
            identity: Identity("child"),
            rootActivity: faults ? WorkflowExecutionHarness.NewFaultingNode(ChildNodeId) : WorkflowExecutionHarness.NewProbeNode(ChildNodeId),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: WorkflowExecutionHarness.Timestamp,
            compatibilityMetadata: new Dictionary<string, string>(),
            // DispatchWorkflow refuses a child without a supported input contract; an empty one is the minimum.
            inputContract: new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []),
            dependencies: [],
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

    private static WorkflowExecutableIdentity Identity(string key) =>
        new($"artifact-{key}", $"definition-{key}", $"version-{key}", "1.0.0", $"sha256:{key}");

    /// <summary>A leaf CLR activity with literal inputs; the harness pins the real contract from the activity type.</summary>
    private static ExecutableNode Leaf(
        string nodeId,
        Type activityType,
        IReadOnlyDictionary<string, object?> inputs,
        IReadOnlyDictionary<string, string> metadata) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: inputs.ToDictionary(
                input => input.Key,
                input =>
                {
                    var type = new ValueTypeDescriptor(input.Value is bool ? "Boolean" : "String");
                    return new RuntimeInputBinding(
                        input.Key,
                        type,
                        ValueProtectionPolicy.InstanceInline,
                        RuntimeInputBindingSource.Literal,
                        literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(input.Value), ValueProtectionPolicy.InstanceInline));
                }),
            metadata: metadata);
}
