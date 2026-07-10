using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
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
    private const string ActivityExecutionId = "actexec-delay";
    private const string ExpectedTimerId = "timer:" + ActivityExecutionId;

    [Fact]
    public async Task Delay_WritesDurableTimer_AndCreatesMatchingBookmark()
    {
        await using var harness = NewHarness();

        var run = await harness.RunAsync(NewDelayExecutable(TimeSpan.FromSeconds(5)));

        // The activity suspends rather than completes.
        Assert.DoesNotContain(run.ActivityStates, s =>
            s.Execution.ExecutableNodeId == "node-delay" && s.Status == ActivityExecutionStatus.Completed);
        Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);

        var timer = await harness.Services.GetRequiredService<IDurableTimerStore>()
            .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, ExpectedTimerId);
        Assert.NotNull(timer);
        Assert.Equal(DurableTimerConstants.TimerStimulusType, timer!.StimulusType);
        Assert.Equal(ExpectedTimerId, timer.StimulusHash);
        Assert.Equal(WorkflowExecutionHarness.WorkflowExecutionId, timer.WorkflowExecutionId);
        Assert.Equal(TimeSpan.FromSeconds(5), timer.DueTime - timer.CreatedAt);

        var bookmark = await harness.Services.GetRequiredService<IBookmarkStateStore>()
            .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, ExpectedTimerId);
        Assert.NotNull(bookmark);
        Assert.Equal(DurableTimerConstants.TimerStimulusType, bookmark!.StimulusType);
        Assert.Equal(ExpectedTimerId, bookmark.StimulusHash);
        Assert.Null(bookmark.ExpiresAt); // timer owns the deadline, not the bookmark expiry
    }

    [Fact]
    public async Task Delay_UsesDeterministicTimerId_DerivedFromActivityExecutionId()
    {
        await using var harness = NewHarness();

        await harness.RunAsync(NewDelayExecutable(TimeSpan.FromMinutes(1)));

        var timer = await harness.Services.GetRequiredService<IDurableTimerStore>()
            .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, ExpectedTimerId);
        Assert.NotNull(timer);
        Assert.Equal(ExpectedTimerId, timer!.TimerId);
    }

    [Fact]
    public async Task Delay_WithNonPositiveDuration_SetsDueTimeToNow()
    {
        await using var harness = NewHarness();

        await harness.RunAsync(NewDelayExecutable(TimeSpan.Zero));

        var timer = await harness.Services.GetRequiredService<IDurableTimerStore>()
            .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, ExpectedTimerId);
        Assert.NotNull(timer);
        Assert.Equal(timer!.CreatedAt, timer.DueTime);
    }

    private static WorkflowExecutionHarness NewHarness() =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new Workflows.Runtime.Scheduling.WorkflowsRuntimeSchedulingFeature().ConfigureServices(services))
            .WithConstructor<DelayConstructor>()
            .Build(ActivityExecutionId);

    private static WorkflowExecutable NewDelayExecutable(TimeSpan duration)
    {
        var inputBindings = new Dictionary<string, RuntimeInputBinding>
        {
            ["Duration"] = new RuntimeInputBinding(
                inputName: "Duration",
                source: RuntimeInputBindingSource.Literal,
                literalValue: JsonSerializer.SerializeToElement(duration),
                metadata: new Dictionary<string, string> { [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = "System.TimeSpan" })
        };

        var root = new ExecutableNode(
            executableNodeId: "node-delay",
            authoredActivityId: "authored-delay",
            activityType: typeof(Delay).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: DelayDescriptor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new DelayDescriptor()),
            inputBindings: inputBindings,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

        // The real WorkflowExecutableCompiler indexes the [ResumeTarget] handler into this map; the harness
        // builds executables directly, so declare the same entry the compiler would emit for the Delay node.
        var resumeTargets = new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
        {
            ["resume-target:durable-timer"] = new WorkflowExecutableResumeTarget(
                ResumeTargetId: "resume-target:durable-timer",
                ExecutableNodeId: "node-delay",
                HandlerKey: "OnTimerElapsedAsync",
                Metadata: new Dictionary<string, string>())
        };

        var now = new DateTimeOffset(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);
        return new WorkflowExecutable(
            identity: WorkflowExecutionHarness.Identity,
            rootActivity: root,
            resumeTargets: resumeTargets,
            createdAt: now,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private sealed record DelayDescriptor
    {
        public static string DescriptorTypeKey => typeof(DelayDescriptor).FullName!;
    }

    private sealed class DelayConstructor : IActivityConstructor<DelayDescriptor>
    {
        public string DescriptorType => DelayDescriptor.DescriptorTypeKey;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            Construct(new DelayDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(DelayDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken)
        {
            var activity = new Delay();
            if (inputs is not null && inputs.TryGetValue("Duration", out var durationInput))
                activity.Duration = (InputArgument<TimeSpan>)durationInput;
            return new(activity);
        }
    }
}
