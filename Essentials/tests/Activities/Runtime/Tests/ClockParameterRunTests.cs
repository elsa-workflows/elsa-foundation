using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations.Handlers;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Spec 260 (ADR 0088 D2), T012: a JavaScript input that declares <c>Clock</c> and <c>UniqueId</c> parameters reads the
/// instant its inputs are evaluated at, through the real agent, scheduler and Jint evaluator. The host clock is a
/// <see cref="FakeTimeProvider"/>, so every expected value is exact. The values are pinned by the commit that records
/// the evaluated input: a retry of a faulted activity sees them again, after the clock has moved on.
/// </summary>
// Runs WriteLine activities, which write to the real Console.Out that ConsoleCapture redirects process-wide.
[Collection("ConsoleCapture")]
public sealed class ClockParameterRunTests
{
    private static readonly ValueTypeDescriptor StringType = new("String");
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 8, 30, 15, 123, TimeSpan.Zero);
    private static readonly Regex UuidV7 = new("^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$");

    [Fact]
    public async Task An_activity_input_records_its_materialization_instant_and_a_v7_id_in_the_committed_snapshot()
    {
        var clock = new FakeTimeProvider(Start);
        await using var harness = NewHarness(clock, configure: null, "actexec-write");

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(WriteNowAndId("node-write")));

        run.AssertWorkflowCompleted();
        var snapshot = run.AssertCompleted("node-write").InputSnapshot!;
        Assert.Equal(Start, snapshot.MaterializedAt);
        var (now, id) = Split(snapshot.Values["text"].InlineValue!.Value.GetString());
        Assert.Equal(Iso(snapshot.MaterializedAt), now);
        AssertV7At(id, snapshot.MaterializedAt);
    }

    [Fact]
    public async Task A_retried_faulted_activity_sees_the_clock_and_id_its_first_attempt_committed()
    {
        var clock = new FakeTimeProvider(Start);
        await using var fixture = await RetryFixture.StartAsync(clock);
        var source = fixture.Run.State("node-f");
        Assert.Equal(ActivityExecutionStatus.Faulted, source.Status);
        var firstText = source.InputSnapshot!.Values["text"].InlineValue!.Value.GetString();
        var (firstNow, firstId) = Split(firstText);
        Assert.Equal(Iso(Start), firstNow);
        AssertV7At(firstId, Start);

        // The clock moves on before the operator retries; a re-evaluation would read the later instant.
        clock.Advance(TimeSpan.FromMinutes(5));
        var job = await fixture.RetryAsync(source.Execution.ActivityExecutionId, "retry-clock");

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
        var run = await fixture.ReadRunAsync();
        run.AssertWorkflowCompleted();
        var successor = Assert.Single(run.States("node-f"), state => state.Status == ActivityExecutionStatus.Completed);
        Assert.Equal(source.InputSnapshot.MaterializedAt, successor.InputSnapshot!.MaterializedAt);
        Assert.Equal(firstText, successor.InputSnapshot.Values["text"].InlineValue!.Value.GetString());
        // Both attempts were hydrated with the same committed text, so the activity body itself saw the same values.
        Assert.Equal([firstText, firstText], fixture.Texts.Values);
    }

    [Fact]
    public async Task An_intrinsic_set_of_the_clock_commits_the_instant_read_before_evaluation()
    {
        var clock = new FakeTimeProvider(Start);
        await using var harness = NewHarness(clock, configure: null, "actexec-sequence", "actexec-set");
        var root = Sequence("node-sequence", SetFromClock("node-set", "stamp"));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root, StringVariable("stamp")));

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-set");
        var frame = Assert.IsType<VariableFrameState>(run.WorkflowState!.RootVariableFrame);
        Assert.Equal(Iso(Start), frame.Values["stamp"].InlineValue!.Value.GetString());
    }

    [Theory]
    [InlineData(1, "Early")]
    [InlineData(-1, "Late")]
    public async Task A_control_intrinsic_outcome_may_branch_on_a_clock_comparison(int deadlineOffsetMinutes, string expectedOutcome)
    {
        var clock = new FakeTimeProvider(Start);
        await using var harness = NewHarness(clock, configure: null, "actexec-sequence", "actexec-control");
        var root = Sequence("node-sequence", ControlOnDeadline("node-control", Start.AddMinutes(deadlineOffsetMinutes)));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root));

        run.AssertWorkflowCompleted();
        Assert.Equal(expectedOutcome, run.AssertCompleted("node-control").Completion!.OutcomeKey);
    }

    private static string Iso(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static (string Now, string Id) Split(string? text)
    {
        Assert.NotNull(text);
        var parts = text.Split('|');
        Assert.Equal(2, parts.Length);
        return (parts[0], parts[1]);
    }

    /// <summary>A lowercase UUID v7 whose 48-bit timestamp prefix is the evaluation instant's Unix milliseconds.</summary>
    private static void AssertV7At(string id, DateTimeOffset instant)
    {
        Assert.Matches(UuidV7, id);
        var milliseconds = long.Parse(id.Replace("-", string.Empty)[..12], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        Assert.Equal(instant.ToUnixTimeMilliseconds(), milliseconds);
    }

    private static RuntimeExpressionBinding JavaScript(string script, IReadOnlyDictionary<string, ExpressionParameterBinding> parameters) =>
        new("JavaScript", script, parameters: parameters);

    private static RuntimeInputBinding NowAndIdText() =>
        new(
            "text",
            StringType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Expression,
            expression: JavaScript(
                "args.now + '|' + args.id",
                new Dictionary<string, ExpressionParameterBinding>
                {
                    ["now"] = new ClockExpressionParameterBinding(),
                    ["id"] = new UniqueIdExpressionParameterBinding(),
                }));

    private static ExecutableNode WriteNowAndId(string nodeId) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(WriteLine).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "clr",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding> { ["text"] = NowAndIdText() },
            metadata: new Dictionary<string, string>());

    private static ExecutableNode SetFromClock(string nodeId, string variableKey) =>
        new(
            nodeId,
            $"authored-{nodeId}",
            "elsa.intrinsic.set",
            "1.0.0",
            "intrinsic",
            JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>
            {
                [WorkflowIntrinsicInputKeys.Value] = new(
                    WorkflowIntrinsicInputKeys.Value,
                    StringType,
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Expression,
                    expression: JavaScript(
                        "args.now",
                        new Dictionary<string, ExpressionParameterBinding> { ["now"] = new ClockExpressionParameterBinding() }))
            },
            new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.Set,
            intrinsicVariable: new RuntimeVariableReference(variableKey, VariableReference.WorkflowScopeId));

    /// <summary>ISO UTC strings compare lexicographically, so the comparison needs no <c>Date</c>.</summary>
    private static ExecutableNode ControlOnDeadline(string nodeId, DateTimeOffset deadline) =>
        new(
            nodeId,
            $"authored-{nodeId}",
            "elsa.intrinsic.control",
            "1.0.0",
            "intrinsic",
            JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>
            {
                [WorkflowIntrinsicInputKeys.Outcome] = new(
                    WorkflowIntrinsicInputKeys.Outcome,
                    StringType,
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Expression,
                    expression: JavaScript(
                        "args.now < args.deadline ? 'Early' : 'Late'",
                        new Dictionary<string, ExpressionParameterBinding>
                        {
                            ["now"] = new ClockExpressionParameterBinding(),
                            ["deadline"] = new LiteralExpressionParameterBinding(JsonSerializer.SerializeToElement(Iso(deadline))),
                        }))
            },
            new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.Control);

    private static RuntimeVariableDeclaration StringVariable(string key) =>
        new(key, key, StringType, ValueProtectionPolicy.InstanceInline);

    private static ExecutableNode Sequence(string nodeId, params ExecutableNode[] children) =>
        new(
            nodeId,
            $"authored-{nodeId}",
            typeof(Elsa.Activities.Sequence.Activities.Sequence).FullName!,
            "1.0.0",
            typeof(SequenceDescriptor).FullName!,
            JsonSerializer.SerializeToElement(new SequenceDescriptor()),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>(),
            [new ExecutableChildSlot(Elsa.Activities.Sequence.Activities.Sequence.ActivitiesSlotName, children)],
            new ExecutableActivityStructure(
                Elsa.Activities.Sequence.Activities.Sequence.StructureKind,
                Elsa.Activities.Sequence.Activities.Sequence.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = children.Select(child => child.ExecutableNodeId).ToArray() })));

    private static WorkflowExecutionHarness NewHarness(TimeProvider clock, Action<IServiceCollection>? configure, params string[] ids)
    {
        var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new Elsa.Activities.Sequence.ActivitiesSequenceFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddMemoryCache();
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                new EventsFeature().ConfigureServices(services);
                new SerializationFeature().ConfigureServices(services);
                new ExpressionsFeature().ConfigureServices(services);
                new JavaScriptFeature().ConfigureServices(services);
                new JintFeature().ConfigureServices(services);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(clock);
                configure?.Invoke(services);
            })
            .Build(ids);

        using var scope = harness.Services.CreateScope();
        foreach (var task in scope.ServiceProvider.GetServices<IStartupTask>())
            task.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();

        return harness;
    }

    private sealed record SequenceDescriptor;

    /// <summary>
    /// The <see cref="RetryFaultedActivityAlterationExecutionTests"/> shape: a structural parent keeps the leaf's fault
    /// waiting for intervention, and the operator retries the leaf through a submitted <c>RetryFaultedActivity</c> plan.
    /// </summary>
    private sealed class RetryFixture : IAsyncDisposable
    {
        private const string Operator = "operator-1";
        private static readonly WorkflowAlterationAuthorityScope Authority =
            new(WorkflowExecutionHarness.Partition.Value, "activity-behavioural-drive", "activity-behavioural-drive");

        private readonly WorkflowExecutionHarness _harness;

        private RetryFixture(WorkflowExecutionHarness harness, ClockTextLog texts, WorkflowExecutionRun run)
        {
            _harness = harness;
            Texts = texts;
            Run = run;
        }

        public ClockTextLog Texts { get; }
        public WorkflowExecutionRun Run { get; }

        public static async Task<RetryFixture> StartAsync(TimeProvider clock)
        {
            var texts = new ClockTextLog();
            var harness = NewHarness(
                clock,
                services =>
                {
                    services.AddSingleton(new InterventionDirective());
                    services.AddSingleton(new ChildEvaluationLog());
                    services.AddSingleton(new FailureBudget(1));
                    services.AddSingleton(texts);
                    services.TryAddScoped<IRuntimeResumptionService, RuntimeResumptionService>();
                },
                "actexec-parent", "actexec-f");
            var leaf = new ExecutableNode(
                executableNodeId: "node-f",
                authoredActivityId: "authored-node-f",
                activityType: typeof(ClockTextFaultingActivity).FullName!,
                activityTypeVersion: "1.0.0",
                descriptorType: "test/leaf",
                descriptorPayload: JsonSerializer.SerializeToElement(new { type = "leaf" }),
                inputBindings: new Dictionary<string, RuntimeInputBinding> { ["text"] = NowAndIdText() },
                metadata: new Dictionary<string, string>());
            var executable = StructuralExecutionTestSupport.NewExecutable(
                StructuralExecutionTestSupport.NewStructuralNode("node-parent", typeof(InterventionParentActivity), leaf));
            var reference = await harness.PublishAsync(executable, "retry-clock-source");
            await harness.StartPublishedAsync(reference, harness.ExecutionId, tenantId: WorkflowExecutionHarness.Partition.Value);
            return new RetryFixture(harness, texts, await harness.ReadRunAsync(harness.ExecutionId));
        }

        public async Task<WorkflowAlterationJobState> RetryAsync(string sourceActivityExecutionId, string idempotencyKey)
        {
            WorkflowAlterationPlanAdmissionResult admission;
            await using (var scope = _harness.Services.CreateAsyncScope())
            {
                admission = await scope.ServiceProvider.GetRequiredService<WorkflowAlterationPlanService>().SubmitAsync(
                    new WorkflowAlterationSubmission(
                        Authority,
                        WorkflowAlterationTargetSelector.ForExecutionIds([_harness.ExecutionId]),
                        [new WorkflowAlterationEnvelope(
                            RetryFaultedActivityAlterationHandler.Kind,
                            RetryFaultedActivityAlterationHandler.SchemaVersion,
                            JsonSerializer.SerializeToElement(new { sourceActivityExecutionId }))]),
                    new WorkflowAlterationOperatorProvenance(Operator, correlationId: null),
                    idempotencyKey);
            }

            var jobId = WorkflowAlterationIdentity.CreateJobId(admission.Plan.PlanId, _harness.ExecutionId);
            var options = new WorkflowAlterationOrchestrationOptions { WorkerId = "test-worker" };
            WorkflowAlterationJobState? job = null;
            for (var sweep = 0; sweep < 8; sweep++)
            {
                await using (var scope = _harness.Services.CreateAsyncScope())
                {
                    await scope.ServiceProvider.GetRequiredService<WorkflowAlterationOrchestrationSweep>()
                        .ExecuteAsync(options, "test-worker");
                    job = await scope.ServiceProvider.GetRequiredService<IWorkflowAlterationStore>().FindJobAsync(jobId);
                }

                if (job?.Status is WorkflowAlterationJobStatus.Succeeded or WorkflowAlterationJobStatus.Failed or WorkflowAlterationJobStatus.Cancelled)
                    break;
            }

            Assert.NotNull(job);
            await _harness.SweepUntilQuietAsync();
            return job!;
        }

        public Task<WorkflowExecutionRun> ReadRunAsync() => _harness.ReadRunAsync(_harness.ExecutionId);

        public ValueTask DisposeAsync() => _harness.DisposeAsync();
    }
}

/// <summary>Records the hydrated <c>text</c> input of every <see cref="ClockTextFaultingActivity"/> attempt, in order.</summary>
public sealed class ClockTextLog
{
    private readonly List<string> _values = [];
    public IReadOnlyList<string> Values => _values;
    public void Add(string value) => _values.Add(value);
}

/// <summary>
/// A leaf that records the text it was hydrated with, then faults while its <see cref="FailureBudget"/> lasts and
/// completes afterwards, as <see cref="OutcomeUnknownActivity"/> does.
/// </summary>
public sealed class ClockTextFaultingActivity(FailureBudget budget, ClockTextLog log) : Activity
{
    [ActivityInput(Key = "text")]
    public string Text { get; set; } = null!;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        log.Add(Text);
        return ValueTask.FromResult(budget.TakeFailure()
            ? ActivityTransition.Fault<ActivityUnit>(new ActivityFault(
                OutcomeUnknownActivity.Code, "The reservation response was lost.", category: OutcomeUnknownActivity.Category))
            : ActivityTransition.Complete(ActivityUnit.Value));
    }
}
