using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Primitives;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

internal static partial class RuntimeHistoryCheckpointProviderContract
{
    // Public workflow publication/admission and actual source capture, without seeded native
    // headers or journal rows. This same-process harness is not a process or composed-host proof.
    private static async Task AuthoredRuntime(Database database, string engine, PersistenceMode mode)
    {
        const string prefix = "history-authored";
        const string run = "history-authored-run";
        var executable = RuntimeEventExecutableTestFixture.Create(prefix);
        await using var host = WorkflowExecutionHarness.Create()
            .WithFeature(services => new Elsa.Events.EventsFeature().ConfigureServices(services))
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new Elsa.Workflows.Runtime.Resumption.WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddWorkflowRuntimeHistory();
                services.AddRuntimeEntityFrameworkCore(new()
                {
                    Provider = engine, ConnectionString = database.Connection, Pooling = false,
                    RecoveryContinuationSigningKey = "history-authored-provider-recovery-signing-key",
                    HierarchyCursorSigningKey = "history-authored-provider-hierarchy-signing-key"
                });
                services.AddWorkflowStimulusRouter();
                services.AddEfModuleMigrations<RuntimeDbContext>(engine);
            })
            .WithPersistenceMode(mode)
            .Build(executable.Identity, run, Enumerable.Range(1, 100).Select(index => $"history-authored-{index}"));
        foreach (var initializer in host.Services.GetServices<IShellInitializer>()) await initializer.InitializeAsync();
        host.InitializeActivityTypes();
        var context = host.Services.GetRequiredService<RuntimeDbContext>();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(8, (await context.Database.GetAppliedMigrationsAsync()).Count());
        var reader = host.Services.GetRequiredService<IRuntimeHistoryStore>();
        var writer = host.Services.GetRequiredService<IRuntimeCheckpointCommitStore>();
        Assert.True(writer.SupportsHistory); Assert.True(writer.CanCommitHistory(reader));

        var published = await host.PublishAsync(executable, "history-authored-source");
        await host.StartPublishedAsync(published, run, tenantId: "default", partition: new("default"));
        await host.SweepUntilQuietAsync();
        var state = await host.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(run);
        Assert.NotNull(state); Assert.Equal(WorkflowExecutionStatus.Running, state.Status);
        Assert.True(state.HistoryCaptureEnabledAtAdmission); Assert.NotNull(state.HistoryCapturePolicy);
        var head = await reader.FindHeadAsync(run); Assert.NotNull(head); Assert.True(head.CapturedFromStart);
        Assert.True(head.Policy.IsEquivalentTo(state.HistoryCapturePolicy)); Assert.False(head.HasGaps);
        var occurrences = (IRuntimeHistoryOccurrenceStore)reader;
        var initial = await occurrences.ReadOccurrencesAsync(new(run, accessBinding: "authored-context"));
        Assert.Equal(RuntimeHistoryOccurrenceAvailability.Available, initial.Availability); Assert.Equal(1, initial.TotalCount);
        var selected = Assert.Single(initial.Items); Assert.NotNull(initial.SequenceToken);
        var activity = Assert.Single((await host.Services.GetRequiredService<IActivityExecutionStateStore>().ListPageAsync(new(run))).Items);
        Assert.Equal(ActivityExecutionStatus.Suspended, activity.Status);
        Assert.Equal(activity.InvocationId, selected.Observation.ActivityExecutionId);
        var wait = Assert.Single((await host.Services.GetRequiredService<IBookmarkStateStore>().ListPageAsync(new(run))).Items);
        Assert.Equal(activity.InvocationId, wait.ActivityExecutionId);

        await host.ResumeAsync(executable.Identity, wait.BookmarkId, wait.ActivityExecutionId,
            wait.ExecutableNodeId, wait.ResumeTargetId, wait.StimulusType, wait.StimulusHash,
            JsonSerializer.SerializeToElement(new { eventName = prefix + "-ready" }));
        await host.SweepUntilQuietAsync();
        Assert.Equal(WorkflowExecutionStatus.Completed, (await host.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(run))!.Status);
        Assert.Empty((await host.Services.GetRequiredService<IBookmarkStateStore>().ListPageAsync(new(run))).Items);
        var completed = await reader.FindHeadAsync(run); Assert.NotNull(completed); Assert.True(completed.LastSequence > head.LastSequence);
        var frozen = await occurrences.ReadOccurrencesAsync(new(run, sequenceToken: initial.SequenceToken, accessBinding: "authored-context"));
        Assert.Equal(RuntimeHistoryOccurrenceAvailability.Available, frozen.Availability);
        Assert.Equal(initial.UpperSequence, frozen.UpperSequence); Assert.Equal(initial.TotalCount, frozen.TotalCount);
        var same = Assert.Single(frozen.Items);
        Assert.Equal(selected.Observation.HistoryEventId, same.Observation.HistoryEventId);
        Assert.Equal(selected.Observation.RecordedAt, same.Observation.RecordedAt);
        Assert.Equal(selected.Observation.Sequence, same.Observation.Sequence);
        var retired = await host.Services.GetRequiredService<IRuntimeHistoryRetentionStore>().PurgeThroughAsync(run, completed.LastSequence);
        Assert.Equal(completed.LastSequence, retired);
        var invalidated = await occurrences.ReadOccurrencesAsync(new(run, sequenceToken: initial.SequenceToken, accessBinding: "authored-context"));
        Assert.Equal(RuntimeHistoryOccurrenceAvailability.Invalidated, invalidated.Availability); Assert.Empty(invalidated.Items);
    }
}
