using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Messaging;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class ReceiveMessageContractTests
{
    [Fact]
    public void Authoring_contract_is_hidden_and_declares_exact_required_inputs_and_literal_start_default()
    {
        Assert.False(typeof(ReceiveMessage).GetCustomAttribute<ActivityAttribute>()!.IsDiscoverable);
        foreach (var key in new[] { nameof(ReceiveMessage.MessageName), nameof(ReceiveMessage.BusinessKey) })
        {
            var property = typeof(ReceiveMessage).GetProperty(key)!;
            Assert.Equal(key, property.GetCustomAttribute<ActivityInputAttribute>()!.Key);
            Assert.NotNull(property.GetCustomAttribute<RequiredAttribute>());
        }
        var start = typeof(ReceiveMessage).GetProperty(nameof(ReceiveMessage.CanStartWorkflow))!.GetCustomAttribute<ActivityInputAttribute>()!;
        Assert.Equal("false", start.DefaultValue); Assert.Equal("Literal", start.DefaultSyntax);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Unsupported_start_or_direct_invocation_refuses_before_interpreting_payload_or_returning_wait_or_result(bool targeted)
    {
        IActivity activity = new ReceiveMessage { MessageName = "order", BusinessKey = "001", CanStartWorkflow = true };
        // Deliberately not a Message envelope: a targeted caller cannot acquire authority through the activity DTO.
        var context = new ActivityExecutionContext("workflow", "invocation", "attempt", "receive", CancellationToken.None,
            JsonSerializer.SerializeToElement(42), targeted ? "receive" : null);
        var failure = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => activity.ExecuteAsync(context).AsTask());
        Assert.Contains("cannot participate", failure.Message);
    }

    [Fact]
    public async Task Supplied_typed_resume_data_cannot_claim_accepted_or_consumed_authority()
    {
        IStatefulActivity<MessageReceived, ReceiveMessageWaitState, MessageReceived> activity = new ReceiveMessage();
        var context = new ActivityExecutionContext("workflow", "invocation", "attempt", "receive", CancellationToken.None);
        var data = new MessageReceived("message", "order", "001", JsonSerializer.SerializeToElement(new { approved = true }));
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => activity.ResumeAsync(
            new(context, new("order", "001"), data, "registration", "delivery")).AsTask());
    }

    [Fact]
    public async Task Requested_cancellation_propagates_before_unavailability()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        IActivity activity = new ReceiveMessage();
        var context = new ActivityExecutionContext("workflow", "invocation", "attempt", "receive", cancellation.Token);
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => activity.ExecuteAsync(context).AsTask());
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Fact]
    public void Envelope_and_wait_round_trip_preserve_exact_text_cloned_payload_and_read_only_declarations()
    {
        MessageReceived data;
        using (var document = JsonDocument.Parse("{\"nested\":{\"value\":7}}")) data = new("private-message", " Order ", "001", document.RootElement);
        var names = new[] { "other", " Order " }; var wait = new ReceiveMessageWaitState(" Order ", "001", names); names[0] = "changed";
        var restored = JsonSerializer.SerializeToElement(data).Deserialize<MessageReceived>()!;
        var restoredWait = JsonSerializer.SerializeToElement(wait).Deserialize<ReceiveMessageWaitState>()!;
        Assert.Equal(" Order ", restored.MessageName); Assert.Equal("001", restored.BusinessKey);
        Assert.Equal(7, restored.Payload.GetProperty("nested").GetProperty("value").GetInt32());
        Assert.Equal(wait.DeclaredMessageNames, restoredWait.DeclaredMessageNames); Assert.Contains("other", wait.DeclaredMessageNames);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)wait.DeclaredMessageNames)[0] = "changed");
        Assert.DoesNotContain("private-message", data.ToString()); Assert.DoesNotContain("001", wait.ToString());
    }

    [Fact]
    public void Resolved_wait_name_must_match_its_finite_declaration_exactly_and_SDK_text_rules_apply()
    {
        Assert.Throws<ArgumentException>(() => new ReceiveMessageWaitState("Order", "001", ["order"]));
        Assert.Throws<ArgumentException>(() => new ReceiveMessageWaitState("order", "001", []));
        Assert.Throws<MessageSubmissionException>(() => new MessageReceived("message", "order", "\ud800", JsonSerializer.SerializeToElement<object?>(null)));
        Assert.Throws<MessageSubmissionException>(() => new MessageReceived("message", "order", "001", default));
    }
}
