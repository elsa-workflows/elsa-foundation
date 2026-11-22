using System.Text.Json;
using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Behavioral;

/// <summary>
/// A .NET object returned by one activity arrives intact on the next activity's input of the same type (#3799):
/// the result is written and the input is read with the same JSON options, so no member is silently left unset.
/// </summary>
public sealed class ActivityResultRoundTripTests
{
    [Fact]
    public async Task A_returned_object_bound_to_the_next_input_keeps_its_members()
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .Build("actexec-seq", "actexec-producer", "actexec-consumer");
        var producer = Nodes.Leaf("node-producer", typeof(CreateOrder));
        var consumer = Node("node-consumer", typeof(ReceiveOrder), new RuntimeInputBinding(
            nameof(ReceiveOrder.Order), new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference("node-producer", nameof(CreateOrderResult.Order), "scope:root")));
        var root = Nodes.Structural(
            "node-sequence",
            typeof(SequenceActivity),
            SequenceActivity.StructureKind,
            SequenceActivity.StructureSchemaVersion,
            new { activities = new[] { producer.ExecutableNodeId, consumer.ExecutableNodeId } },
            [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, [producer, consumer])]);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root));

        run.AssertWorkflowCompleted();
        run.AssertOutcomes("node-consumer", "Done");
        Assert.Equal(new Order("Ann", 5m), ReceiveOrder.Received(run));
    }

    private static ExecutableNode Node(string nodeId, Type activityType, RuntimeInputBinding input) => new(
        executableNodeId: nodeId,
        authoredActivityId: $"authored-{nodeId}",
        activityType: activityType.FullName!,
        activityTypeVersion: "1.0.0",
        descriptorType: "test",
        descriptorPayload: JsonSerializer.SerializeToElement(new { }),
        inputBindings: new Dictionary<string, RuntimeInputBinding> { [input.InputName] = input },
        metadata: new Dictionary<string, string>());

    public sealed class Order
    {
        public Order() { }

        public Order(string? customerName, decimal total)
        {
            CustomerName = customerName;
            Total = total;
        }

        public string? CustomerName { get; set; }
        public decimal Total { get; set; }

        public override bool Equals(object? obj) => obj is Order other && CustomerName == other.CustomerName && Total == other.Total;
        public override int GetHashCode() => HashCode.Combine(CustomerName, Total);
        public override string ToString() => $"Order {{ CustomerName = {CustomerName ?? "null"}, Total = {Total} }}";
    }

    public sealed record CreateOrderResult([property: Output(Key = "Order")] Order Order);

    public sealed class CreateOrder : Activity<CreateOrderResult>
    {
        protected override ValueTask<ActivityTransition<CreateOrderResult>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new CreateOrderResult(new Order("Ann", 5m))));
    }

    public sealed record ReceiveOrderResult([property: Output(Key = "Order")] Order Order);

    /// <summary>Echoes its hydrated <see cref="Order"/> input as its result, so the test reads what the activity saw.</summary>
    public sealed class ReceiveOrder : Activity<ReceiveOrderResult>
    {
        [ActivityInput] public Order Order { get; set; } = default!;

        protected override ValueTask<ActivityTransition<ReceiveOrderResult>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new ReceiveOrderResult(Order)));

        public static Order? Received(WorkflowExecutionRun run) =>
            run.State("node-consumer").Completion!.Result.InlineValue!.Value.GetProperty("order").Deserialize<Order>(JsonSerializerOptions.Web);
    }
}
