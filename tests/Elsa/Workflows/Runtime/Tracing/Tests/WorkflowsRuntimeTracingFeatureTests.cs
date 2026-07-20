using System.Diagnostics;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tracing.Tests;

/// <summary>
/// Feature-registration contract for MS-9: the runtime core registers the no-op tracer by default (TryAdd), and the
/// opt-in tracing feature swaps in the ActivitySource-backed tracer — winning regardless of whether the core's TryAdd
/// default runs before or after the feature.
/// </summary>
public sealed class WorkflowsRuntimeTracingFeatureTests
{
    [Fact]
    public void RuntimeCore_RegistersNoOpTracer_ByDefault()
    {
        var services = new ServiceCollection();

        services.AddWorkflowRuntime();

        var tracer = services.BuildServiceProvider().GetRequiredService<IWorkflowEngineTracer>();
        Assert.IsType<NullWorkflowEngineTracer>(tracer);
        Assert.Null(tracer.StartDrainCycle(new("wfexec-1")));
    }

    [Fact]
    public void TracingFeature_ReplacesNoOpWithActivitySourceTracer_WhenComposedAfterCore()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();

        new WorkflowsRuntimeTracingFeature().ConfigureServices(services);

        Assert.IsType<ActivitySourceWorkflowEngineTracer>(services.BuildServiceProvider().GetRequiredService<IWorkflowEngineTracer>());
    }

    [Fact]
    public void TracingFeature_WinsOverCoreTryAdd_WhenComposedBeforeCore()
    {
        var services = new ServiceCollection();

        new WorkflowsRuntimeTracingFeature().ConfigureServices(services);
        services.AddWorkflowRuntime();

        Assert.IsType<ActivitySourceWorkflowEngineTracer>(services.BuildServiceProvider().GetRequiredService<IWorkflowEngineTracer>());
    }

    [Fact]
    public void ActivitySourceTracer_ReturnsNull_WhenNoListenerIsAttached()
    {
        // Zero-overhead default: without a sampling listener the tracer allocates no Activity.
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        new WorkflowsRuntimeTracingFeature().ConfigureServices(services);

        var tracer = services.BuildServiceProvider().GetRequiredService<IWorkflowEngineTracer>();

        Assert.Null(tracer.StartDrainCycle(new("wfexec-1")));
        Assert.Null(Activity.Current);
    }

    [Fact]
    public void TracingFeature_EmitsOnEngineSource_WhenContainerAlsoRegistersAForeignActivitySource()
    {
        // Regression gate: ASP.NET Core hosts register an ActivitySource singleton (named "Microsoft.AspNetCore").
        // The tracer also has an ActivitySource-accepting constructor, so a Type-based descriptor would let DI
        // greedily inject that foreign source and the engine spans would be emitted under the wrong source name —
        // invisible to any listener of the engine source. The feature must pin the parameterless construction path.
        var services = new ServiceCollection();
        using var foreignSource = new ActivitySource("Microsoft.AspNetCore");
        services.AddSingleton(foreignSource);
        services.AddWorkflowRuntime();
        new WorkflowsRuntimeTracingFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var recorded = new List<Activity>();
        using var listener = RecordEngineSpans(recorded);

        var tracer = provider.GetRequiredService<IWorkflowEngineTracer>();
        tracer.StartDrainCycle(new("wfexec-1"))?.Dispose();

        var activity = Assert.Single(recorded);
        Assert.Equal(WorkflowEngineTelemetry.ActivitySourceName, activity.Source.Name);
        Assert.Equal(WorkflowEngineTelemetry.DrainSpanName, activity.OperationName);
    }

    [Fact]
    public async Task CommitterResolvedFromFullCore_EmitsCheckpointSpan_ProvingProductionConstructorThreadsTracer()
    {
        // Hard gate: the committer has two constructors and DI picks the widest it can satisfy. Production registers the
        // ownership services, so the tracer-carrying constructor is selected — but only a real resolve proves it. Build
        // the full core + tracing feature, resolve the committer through DI, and assert a listener sees the span.
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        new WorkflowsRuntimeTracingFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var committer = provider.GetRequiredService<RuntimeCheckpointCommitter>();

        var recorded = new List<Activity>();
        using var listener = RecordEngineSpans(recorded);

        await committer.CommitAsync(NewMinimalCommit());

        Assert.Contains(recorded, activity => activity.OperationName == WorkflowEngineTelemetry.CheckpointCommitSpanName);
    }

    /// <summary>Attaches a listener that records every stopped engine-source span into <paramref name="recorded"/>.</summary>
    private static ActivityListener RecordEngineSpans(List<Activity> recorded)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == WorkflowEngineTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = recorded.Add
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static RuntimeCheckpointCommit NewMinimalCommit() =>
        new(
            CommitId: "commit-1",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: "cp-1",
                Name: "test.checkpoint",
                WorkflowExecutionId: "wfexec-1",
                OccurredAt: DateTimeOffset.UnixEpoch,
                ActivityExecutionIds: [],
                Metadata: new Dictionary<string, string>()),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: []),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());
}
