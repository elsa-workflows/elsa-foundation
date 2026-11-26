using CShells.Lifecycle;
using Elsa.Tasks.Core;
using Elsa.Cluster.Readability;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Activities.Scheduling.Tests;

// Actual publication/admission, Delay suspension and timer-pump resumption; no seeded policy, timer, bookmark or journal rows.
public sealed class DelayHistoryExecutionTests
{
    [Theory]
    [InlineData(PersistenceMode.Durable, false)] [InlineData(PersistenceMode.Balanced, false)] [InlineData(PersistenceMode.Ephemeral, false)]
    [InlineData(PersistenceMode.Durable, true)] [InlineData(PersistenceMode.Balanced, true)] [InlineData(PersistenceMode.Ephemeral, true)]
    public async Task Published_delay_retains_original_timer_and_bookmark_history_for_the_actual_occurrence(PersistenceMode mode, bool sqlite)
    {
        var directory = Directory.CreateTempSubdirectory("elsa-delay-history-");
        try
        {
            var clock = new FakeTimeProvider(WorkflowExecutionHarness.Timestamp);
            await using var harness = WorkflowExecutionHarness.Create()
                .WithFeature(services => new Elsa.Events.EventsFeature().ConfigureServices(services))
                .WithFeature(services => new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services))
                .ConfigureServices(services =>
                {
                    services.AddLogging(); services.AddWorkflowRuntimeHistory(); services.AddWorkflowStimulusRouter();
                    services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "delay-history-recovery-signing-key-at-least-32-bytes");
                    services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
                    if (sqlite)
                    {
                        // First-party single-host readability lets the real gate adopt the protected format.
                        services.AddEfSchemaReadability();
                        services.AddRuntimeEntityFrameworkCore(new()
                        {
                            Provider = "Sqlite", ConnectionString = "Data Source=" + Path.Combine(directory.FullName, "runtime.sqlite") + ";Pooling=False",
                            RecoveryContinuationSigningKey = "delay-history-recovery-signing-key-at-least-32-bytes",
                            HierarchyCursorSigningKey = "delay-history-hierarchy-signing-key-at-least-32-bytes"
                        });
                        services.AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
                    }
                })
                .WithPersistenceMode(mode)
                .Build(DelayTestExecutable.ActivityExecutionId);
            foreach (var initializer in harness.Services.GetServices<IShellInitializer>()) await initializer.InitializeAsync();
            if (sqlite)
            {
                var context = harness.Services.GetRequiredService<RuntimeDbContext>();
                Assert.False(context.Database.HasPendingModelChanges());
                Assert.Equal(8, (await context.Database.GetAppliedMigrationsAsync()).Count());
                var gate = harness.Services.GetRequiredService<EfSchemaFinalizationGates>().FindModuleGate(context.GetType());
                Assert.NotNull(gate);
                Assert.Equal(RuntimeOperationalStateEfModule.TimerHistorySchemaVersion, gate.StateOf(RuntimeOperationalStateEfModule.SchemaFamily)!.WriteVersion);
            }
            harness.InitializeActivityTypes();
            var executable = DelayTestExecutable.Create(TimeSpan.FromMinutes(5), clock.GetUtcNow());
            var published = await harness.PublishAsync(executable, "delay-history-source");
            var started = await harness.StartPublishedAsync(published, harness.ExecutionId, tenantId: "default", partition: new("default"));
            var native = await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId);
            Assert.True(native is not null, System.Text.Json.JsonSerializer.Serialize(started));
            Assert.False(native.Status.IsTerminal(), System.Text.Json.JsonSerializer.Serialize(native));
            Assert.True(native.HistoryCaptureEnabledAtAdmission, System.Text.Json.JsonSerializer.Serialize(native.HistoryCapturePolicy));
            Assert.NotNull(native.HistoryCapturePolicy);
            Assert.All(DurableTimerHistoryFacts.Kinds, kind => Assert.Contains(kind, native.HistoryCapturePolicy.SupportedKinds));
            var source = Assert.IsAssignableFrom<IDurableTimerHistoryStore>(harness.Services.GetRequiredService<IDurableTimerStore>());
            var timer = await source.FindAsync(harness.ExecutionId, DelayTestExecutable.TimerId); Assert.NotNull(timer);
            Assert.Equal(DelayTestExecutable.ActivityExecutionId, timer.ActivityExecutionId);
            var activity = await harness.Services.GetRequiredService<IActivityExecutionStateStore>().FindAsync(harness.ExecutionId, DelayTestExecutable.ActivityExecutionId);
            Assert.NotNull(activity); Assert.Equal(activity.ExecutionScopeId, timer.ExecutionScopeId);
            Assert.NotNull(timer.HistoryResponsibility); Assert.True(native.HistoryCapturePolicy.IsEquivalentTo(timer.HistoryResponsibility.Policy));
            var scheduled = Assert.Single(timer.HistoryResponsibility.PendingEvents);
            Assert.Equal("TimerScheduled", scheduled.Name); Assert.Equal(timer.ActivityExecutionId, scheduled.ActivityExecutionId);
            Assert.Equal(timer.ExecutionScopeId, scheduled.ExecutionScopeId);
            Assert.True(await source.HasPendingHistoryAsync(harness.ExecutionId));
            var bookmark = await harness.Services.GetRequiredService<IBookmarkStateStore>().FindAsync(harness.ExecutionId, DelayTestExecutable.BookmarkId);
            Assert.NotNull(bookmark); Assert.Equal(timer.ActivityExecutionId, bookmark.ActivityExecutionId);
            Assert.Equal(timer.StimulusHash, bookmark.StimulusHash);
            var history = harness.Services.GetRequiredService<IRuntimeHistoryStore>();
            var page = await history.ReadPageAsync(new(harness.ExecutionId, activityExecutionId: timer.ActivityExecutionId));
            Assert.NotNull(page.Head); Assert.True(page.Head.CapturedFromStart); Assert.False(page.Head.HasGaps);
            Assert.Contains(page.Items, item => item.Name == "BookmarkCreated" && item.BookmarkId == bookmark.BookmarkId);
            Assert.DoesNotContain(page.Items, item => item.Name == "TimerDispatched");
            var pump = Assert.Single(harness.Services.GetServices<IRecurringTask>().OfType<DurableTimerPumpTask>());
            await pump.ExecuteAsync(CancellationToken.None);
            Assert.NotNull(await source.FindAsync(harness.ExecutionId, timer.TimerId));
            Assert.False(await source.HasPendingHistoryAsync(harness.ExecutionId));
            var scheduledPage = await history.ReadPageAsync(new(harness.ExecutionId, activityExecutionId: timer.ActivityExecutionId));
            var scheduledEvent = Assert.Single(scheduledPage.Items, item => item.Name == "TimerScheduled");
            Assert.Equal(scheduled.HistoryEventId, scheduledEvent.HistoryEventId);
            Assert.Equal(RuntimeHistoryAuthority.DeliveryObservation, scheduledEvent.Authority);
            Assert.DoesNotContain(scheduledPage.Items, item => item.Name == "TimerDispatched");

            clock.Advance(TimeSpan.FromMinutes(5));
            await pump.ExecuteAsync(CancellationToken.None);
            Assert.Null(await source.FindAsync(harness.ExecutionId, timer.TimerId));
            Assert.False(await source.HasPendingHistoryAsync(harness.ExecutionId));
            Assert.Null(await harness.Services.GetRequiredService<IBookmarkStateStore>().FindAsync(harness.ExecutionId, bookmark.BookmarkId));
            var completed = await harness.Services.GetRequiredService<IActivityExecutionStateStore>().FindAsync(harness.ExecutionId, timer.ActivityExecutionId!);
            Assert.NotNull(completed); Assert.Equal(ActivityExecutionStatus.Completed, completed.Status);
            var resumedPage = await history.ReadPageAsync(new(harness.ExecutionId, activityExecutionId: timer.ActivityExecutionId));
            var dispatched = Assert.Single(resumedPage.Items, item => item.Name == "TimerDispatched");
            Assert.Equal(scheduledEvent.HistoryEventId, dispatched.Causation!.HistoryEventId);
            Assert.Contains(resumedPage.Items, item => item.Name == "BookmarkConsumed" && item.BookmarkId == bookmark.BookmarkId);
            Assert.Contains(resumedPage.Items, item => item.Name == "ActivityResumeClaimed");
            Assert.Contains(resumedPage.Items, item => item.Name == "ActivityCompleted");
            Assert.False(resumedPage.Head!.HasGaps);
            await pump.ExecuteAsync(CancellationToken.None);
            Assert.Equal(resumedPage.Items.Select(item => item.HistoryEventId),
                (await history.ReadPageAsync(new(harness.ExecutionId, activityExecutionId: timer.ActivityExecutionId))).Items.Select(item => item.HistoryEventId));
        }
        finally { directory.Delete(recursive: true); }
    }
}
