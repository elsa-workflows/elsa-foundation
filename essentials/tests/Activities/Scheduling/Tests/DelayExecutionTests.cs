using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Primitives;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Activities.Testing;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Scheduling.Tests;

/// <summary>
/// In-process execution coverage for the <c>Delay</c> activity running through the real workflow agent on the
/// shared <see cref="WorkflowExecutionHarness"/>. Asserts the activity suspends durably: it writes a durable
/// timer (strictly before the bookmark) and creates a matching timer bookmark with the deterministic,
/// replay-stable identity and no expiry.
/// </summary>
public sealed class DelayExecutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Delay_WritesDurableTimer_AndCreatesMatchingBookmark()
    {
        await using var harness = NewHarness();

        var run = await harness.RunAsync(DelayTestExecutable.Create(TimeSpan.FromSeconds(5), Now));

        // The activity suspends rather than completes.
        Assert.DoesNotContain(run.ActivityStates, s =>
            s.Execution.ExecutableNodeId == "node-delay" && s.Status == ActivityExecutionStatus.Completed);
        Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);

        var timer = await harness.Services.GetRequiredService<IDurableTimerStore>()
            .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, DelayTestExecutable.TimerId);
        Assert.True(timer is not null, Describe(run));
        Assert.Equal("DurableTimer", timer!.StimulusType);
        Assert.Equal(DelayTestExecutable.TimerId, timer.StimulusHash);
        Assert.NotNull(timer.PayloadType);
        Assert.Equal(Delay.TimerTriggerProviderId, timer.ProviderId);
        Assert.Equal(WorkflowExecutionHarness.WorkflowExecutionId, timer.WorkflowExecutionId);
        Assert.Equal(TimeSpan.FromSeconds(5), timer.DueTime - timer.CreatedAt);

        var bookmark = await harness.Services.GetRequiredService<IBookmarkStateStore>()
            .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, DelayTestExecutable.BookmarkId);
        Assert.NotNull(bookmark);
        Assert.Equal("DurableTimer", bookmark!.StimulusType);
        Assert.Equal(DelayTestExecutable.TimerId, bookmark.StimulusHash);
        Assert.Null(bookmark.ExpiresAt); // timer owns the deadline, not the bookmark expiry
    }

    [Fact]
    public async Task Delay_UsesDeterministicTimerId_DerivedFromActivityExecutionId()
    {
        await using var harness = NewHarness();

        await harness.RunAsync(DelayTestExecutable.Create(TimeSpan.FromMinutes(1), Now));

        var timer = await harness.Services.GetRequiredService<IDurableTimerStore>()
            .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, DelayTestExecutable.TimerId);
        Assert.NotNull(timer);
        Assert.Equal(DelayTestExecutable.TimerId, timer!.TimerId);
    }

    [Fact]
    public async Task Delay_WithNonPositiveDuration_SetsDueTimeToNow()
    {
        await using var harness = NewHarness();

        await harness.RunAsync(DelayTestExecutable.Create(TimeSpan.Zero, Now));

        var timer = await harness.Services.GetRequiredService<IDurableTimerStore>()
            .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, DelayTestExecutable.TimerId);
        Assert.NotNull(timer);
        Assert.Equal(timer!.CreatedAt, timer.DueTime);
    }

    private static WorkflowExecutionHarness NewHarness() =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new Workflows.Runtime.Scheduling.WorkflowsRuntimeSchedulingFeature().ConfigureServices(services))
            .Build(DelayTestExecutable.ActivityExecutionId);

    private static string Describe(WorkflowExecutionRun run) => string.Join(
        Environment.NewLine,
        run.ActivityStates.Select(state =>
            $"{state.Execution.ExecutableNodeId}: {state.Status}/{state.SubStatus}; fault={state.Metadata.GetValueOrDefault("runtime.faultMessage")}"));
}
