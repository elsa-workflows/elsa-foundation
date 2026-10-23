using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
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
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Spec 247 (ADR 0045 amendment for #2717): a JavaScript input that declares a <c>WorkflowInstance</c> parameter reads
/// the running instance's correlation ID and instance ID from committed state, through the real agent, scheduler and
/// Jint evaluator. The start value is seeded; a later <c>SetCorrelationId</c> is seen by activities that run after it.
/// </summary>
// Runs WriteLine activities, which write to the real Console.Out that ConsoleCapture redirects process-wide.
[Collection("ConsoleCapture")]
public sealed class WorkflowInstanceParameterRunTests
{
    private static readonly ValueTypeDescriptor StringType = new("String");

    [Fact]
    public async Task A_later_activity_sees_the_correlation_id_a_correlate_assigned()
    {
        await using var harness = NewHarness("actexec-sequence", "actexec-before", "actexec-correlate", "actexec-after");
        var root = Sequence(
            "node-sequence",
            WriteCorrelationId("node-before"),
            Correlate("node-correlate", "c-2"),
            WriteCorrelationId("node-after"));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root), allowPendingWorkOnTerminalCompletion: false, correlationId: "c-1");

        run.AssertWorkflowCompleted();
        Assert.Equal("c-1", Text(run.AssertCompleted("node-before")));
        Assert.Equal("c-2", Text(run.AssertCompleted("node-after")));
    }

    [Fact]
    public async Task An_instance_without_a_correlation_id_reads_null()
    {
        await using var harness = NewHarness("actexec-write");

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(WriteCorrelationId("node-write")));

        run.AssertWorkflowCompleted();
        Assert.Equal("null", Text(run.AssertCompleted("node-write")));
    }

    [Fact]
    public async Task The_instance_id_is_the_workflow_execution_id()
    {
        await using var harness = NewHarness("actexec-write");

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            WriteLine("node-write", WorkflowInstanceExpressionParameterBinding.Members.InstanceId)));

        run.AssertWorkflowCompleted();
        Assert.Equal(run.WorkflowState!.WorkflowExecutionId, Text(run.AssertCompleted("node-write")));
    }

    private static string? Text(ActivityExecutionState state) =>
        state.InputSnapshot!.Values["text"].InlineValue!.Value.GetString();

    private static ExecutableNode WriteCorrelationId(string nodeId) =>
        WriteLine(nodeId, WorkflowInstanceExpressionParameterBinding.Members.CorrelationId);

    private static ExecutableNode WriteLine(string nodeId, string member)
    {
        var binding = new RuntimeInputBinding(
            "text",
            StringType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding(
                "JavaScript",
                "String(args.instance)",
                parameters: new Dictionary<string, ExpressionParameterBinding>
                {
                    ["instance"] = new WorkflowInstanceExpressionParameterBinding(member),
                }));
        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(WriteLine).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "clr",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding> { ["text"] = binding },
            metadata: new Dictionary<string, string>());
    }

    private static ExecutableNode Correlate(string nodeId, string correlationId) =>
        new(
            nodeId,
            $"authored-{nodeId}",
            $"elsa.intrinsic.{WorkflowIntrinsicKind.SetCorrelationId.ToString().ToLowerInvariant()}",
            "1.0.0",
            "intrinsic",
            JsonSerializer.SerializeToElement(new { kind = WorkflowIntrinsicKind.SetCorrelationId.ToString(), schemaVersion = "1.0.0" }),
            new Dictionary<string, RuntimeInputBinding>
            {
                [WorkflowIntrinsicInputKeys.Value] = new(
                    WorkflowIntrinsicInputKeys.Value,
                    StringType,
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement(correlationId), ValueProtectionPolicy.InstanceInline))
            },
            new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.SetCorrelationId);

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

    private static WorkflowExecutionHarness NewHarness(params string[] ids)
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
            })
            .Build(ids);

        using var scope = harness.Services.CreateScope();
        foreach (var task in scope.ServiceProvider.GetServices<IStartupTask>())
            task.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();

        return harness;
    }

    private sealed record SequenceDescriptor;
}
