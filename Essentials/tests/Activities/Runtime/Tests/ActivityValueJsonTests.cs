using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Runtime.Core.Serialization;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// The completion projector and the input hydrator use the same JSON options, so a .NET object one activity
/// returns hydrates the next activity's input with every member set (#3799).
/// </summary>
public sealed partial class ActivityValueJsonTests
{
    private static readonly ValueTypeDescriptor OrderType = new("Shop.Order");

    [Fact]
    public void Projector_writes_result_members_in_camelCase()
    {
        var result = Project(new Order { CustomerName = "Ann", Total = 5m }).Completion.Result.InlineValue!.Value;

        Assert.Equal("Ann", result.GetProperty("customerName").GetString());
        Assert.Equal(5m, result.GetProperty("total").GetDecimal());
        Assert.False(result.TryGetProperty("CustomerName", out _));
    }

    [Fact]
    public void Hydrator_reads_camelCase_members_into_the_input_property()
    {
        var activity = Hydrate(JsonSerializer.SerializeToElement(new { customerName = "Ann", total = 5m }));

        Assert.Equal("Ann", activity.Order!.CustomerName);
        Assert.Equal(5m, activity.Order.Total);
    }

    [Fact]
    public void Hydrator_still_reads_PascalCase_members_written_with_default_options()
    {
        // Literal, variable and secret envelopes are written with System.Text.Json defaults, so they keep PascalCase.
        var activity = Hydrate(JsonSerializer.SerializeToElement(new Order { CustomerName = "Ann", Total = 5m }));

        Assert.Equal("Ann", activity.Order!.CustomerName);
        Assert.Equal(5m, activity.Order.Total);
    }

    [Fact]
    public void A_projected_result_hydrates_the_next_input_unchanged()
    {
        var result = Project(new Order { CustomerName = "Ann", Total = 5m }).Completion.Result.InlineValue!.Value;

        var activity = Hydrate(result);

        Assert.Equal("Ann", activity.Order!.CustomerName);
        Assert.Equal(5m, activity.Order.Total);
    }

    [Fact]
    public void Hydrator_still_refuses_a_number_written_as_a_string()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Hydrate(JsonSerializer.SerializeToElement(new { customerName = "Ann", total = "5" })));

        Assert.Contains("cannot be materialized", exception.Message);
    }

    [Fact]
    public void Public_profile_matches_actual_completion_bytes_and_does_not_mutate_cached_runtime_options()
    {
        var order = new Order { CustomerName = "Ann & Co", Total = 5m };
        var expected = JsonSerializer.Serialize(order, ActivityValueJsonProfile.CreateOptions());
        var separate = ActivityValueJsonProfile.CreateOptions();
        separate.PropertyNamingPolicy = null;

        var actual = Project(order).Completion.Result.InlineValue!.Value.GetRawText();

        Assert.Equal(expected, actual);
        Assert.Equal("Ann & Co", Hydrate(JsonSerializer.Deserialize<JsonElement>(actual)).Order!.CustomerName);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Order>("{\"total\":\"5\"}", ActivityValueJsonProfile.CreateOptions()));
    }

    private static ActivityCompletionProjection Project(Order order) =>
        new ActivityCompletionProjector().Project(
            "invocation-1",
            new ActivityAttempt("attempt-1", "invocation-1", 1, ActivityAttemptReason.Initial, DateTimeOffset.UnixEpoch),
            ProducerContract(),
            ActivityTransition.Complete(order),
            DateTimeOffset.UnixEpoch);

    private static OrderConsumer Hydrate(JsonElement order)
    {
        var contract = ConsumerContract();
        var activity = new OrderConsumer();
        new ActivityInputHydrator().Hydrate(activity, contract, new ActivityInputSnapshot(
            "invocation-2", contract.SchemaFingerprint, "bindings",
            new Dictionary<string, ValueEnvelope> { ["order"] = ValueEnvelope.Inline(OrderType, order, ValueProtectionPolicy.InstanceInline) },
            DateTimeOffset.UnixEpoch));
        return activity;
    }

    private static ActivityContract ProducerContract() => new(
        "Shop.CreateOrder", "1.0.0", "test", JsonSerializer.SerializeToElement(new { }),
        [],
        new ActivityResultContract(OrderType, true, ActivityValuePolicy.Default, []),
        ["Done"], new ActivityActivationRequirement("test", "Shop.CreateOrder"));

    private static ActivityContract ConsumerContract() => new(
        "Shop.ReceiveOrder", "1.0.0", "test", JsonSerializer.SerializeToElement(new { }),
        [new ActivityInputContract("order", "Order", OrderType, true, false, false, null, ActivityValuePolicy.Default)],
        new ActivityResultContract(new ValueTypeDescriptor("Unit"), false, ActivityValuePolicy.Default, []),
        ["Done"], new ActivityActivationRequirement("test", "Shop.ReceiveOrder"));

    private sealed class Order
    {
        public string? CustomerName { get; set; }
        public decimal Total { get; set; }
    }

    private sealed class OrderConsumer : StructuralActivity
    {
        [ActivityInput(Key = "order")]
        public Order? Order { get; set; }
    }
}
