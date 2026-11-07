using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Scheduling.Tests;

public sealed class ReplaySafeTimerHistoryTests
{
    [Fact]
    public async Task Actual_replay_safe_schedule_retire_completion_discharges_owned_sources_without_creating_a_native_run()
    {
        async Task<string[]> Drive(bool history)
        {
            var probe = new TimerProbe();
            await using var host = WorkflowExecutionHarness.Create()
                .WithFeature(services => new Workflows.Runtime.Scheduling.WorkflowsRuntimeSchedulingFeature().ConfigureServices(services))
                .WithPersistenceMode(PersistenceMode.Ephemeral, recordCompletion: false)
                .ConfigureServices(services =>
                {
                    services.AddSingleton(probe);
                    services.AddSingleton<IPersistenceAccessContextAccessor>(new Access());
                    services.AddRuntimeHistory(options => options.Enabled = history);
                    services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "actual-replay-safe-timer-history-at-least32-bytes");
                }).Build("timer-helper-activity");
            var root = new ExecutableNode("timer-helper", "authored-timer-helper", typeof(ScheduleRetireActivity).FullName!, "1.0.0",
                typeof(ClrActivityDescriptor).FullName!, JsonSerializer.SerializeToElement(new ClrActivityDescriptor(typeof(ScheduleRetireActivity).FullName!)),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
            var source = await host.PublishAsync(WorkflowExecutionHarness.NewExecutable(root), "timer-helper-source");
            await host.StartPublishedAsync(source, WorkflowExecutionHarness.WorkflowExecutionId);

            Assert.NotNull(probe.Timer);
            Assert.Null(await host.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(WorkflowExecutionHarness.WorkflowExecutionId));
            var commits = host.Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits();
            Assert.Empty(commits);
            var timers = host.Services.GetRequiredService<IDurableTimerStore>();
            Assert.Null(await timers.FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, probe.Timer!.TimerId));
            Assert.Empty(await timers.ListDueAsync(DateTimeOffset.MaxValue, 10));
            if (history)
            {
                var owner = Assert.IsAssignableFrom<IDurableTimerHistoryStore>(timers);
                Assert.False(await owner.HasPendingHistoryAsync(WorkflowExecutionHarness.WorkflowExecutionId));
                Assert.Null(await host.Services.GetRequiredService<IRuntimeHistoryStore>().FindHeadAsync(WorkflowExecutionHarness.WorkflowExecutionId));
                var replay = await timers.SaveAsync(probe.Timer);
                Assert.True(replay.HistoryResponsibility!.Retired);
                Assert.NotNull(replay.HistoryResponsibility.NoRecordCompletion);
                Assert.Empty(replay.HistoryResponsibility.PendingEvents);
                Assert.Null(replay.Input);
                var fresh = probe.Timer with { TimerId = "a-new-source-after-unrecorded-completion", HistoryResponsibility = null };
                fresh = fresh with { HistoryResponsibility = new(replay.HistoryResponsibility.Policy,
                    [DurableTimerHistoryFacts.CreateScheduled(fresh, replay.HistoryResponsibility.Policy)!.Event], false) };
                await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => timers.SaveAsync(fresh).AsTask());
                Assert.False(await owner.HasPendingHistoryAsync(WorkflowExecutionHarness.WorkflowExecutionId));
                Assert.Null(await timers.FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, fresh.TimerId));
            }
            return commits.Select(commit => commit.Commit.Checkpoint.Name).ToArray();
        }
        Assert.Equal(await Drive(false), await Drive(true));
    }

    public sealed class TimerProbe { public DurableTimer? Timer { get; set; } }

    [ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
    public sealed class ScheduleRetireActivity(IDurableTimerScheduler scheduler, IDurableTimerStore timers, TimerProbe probe, TimeProvider clock) : Activity<ActivityUnit>
    {
        protected override async ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
        {
            var at = clock.GetUtcNow();
            var timer = await scheduler.ScheduleAsync(new("helper:" + context.InvocationId, context.WorkflowExecutionId, "test-helper-timer", "private-routing-sentinel",
                at, at.AddDays(1), JsonSerializer.SerializeToElement("private-payload-sentinel"), ActivityExecutionId: context.InvocationId), context.CancellationToken);
            probe.Timer = timer;
            if (timer.HistoryResponsibility is not null)
                await ((IDurableTimerHistoryStore)timers).RetireWithHistoryAsync(context.WorkflowExecutionId, timer.TimerId,
                    DurableTimerHistoryFacts.CreateRetired(timer, at, "helper-completed")!, context.CancellationToken);
            else await timers.DeleteAsync(context.WorkflowExecutionId, timer.TimerId, context.CancellationToken);
            return ActivityTransition.Complete(ActivityUnit.Value);
        }
    }
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new(WorkflowExecutionHarness.Partition.Value)); }
}
