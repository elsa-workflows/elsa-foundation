using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Messaging;
using Xunit;
using SubmissionPolicy = Elsa.Workflows.Runtime.Messaging.MessageSubmissionPolicy;
using SubmissionRules = Elsa.Workflows.Runtime.Messaging.MessageSubmissionRules;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class MessageSubmissionContractTests
{
    private static readonly DateTimeOffset AcceptedAt = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_submitted_payload_survives_original_document_disposal_including_JSON_null()
    {
        MessageSubmission submission;
        using (var original = JsonDocument.Parse("{\"business\":{\"items\":[1,null]}}"))
            submission = new("Payment", "Order 123", original.RootElement);
        Assert.Equal(1, submission.Payload.GetProperty("business").GetProperty("items")[0].GetInt32());
        Assert.Equal(JsonValueKind.Null, submission.Payload.GetProperty("business").GetProperty("items")[1].ValueKind);
        using var nullPayload = JsonDocument.Parse("null");
        Assert.Equal(JsonValueKind.Null, new MessageSubmission("Payment", "Order 123", nullPayload.RootElement).Payload.ValueKind);
    }

    [Fact]
    public void Undefined_and_disposed_payloads_refuse_with_safe_validation_classification()
    {
        Validation(() => new MessageSubmission("private-name", "private-key", default));
        var document = JsonDocument.Parse("{\"private-payload\":1}");
        var disposed = document.RootElement;
        document.Dispose();
        var error = Validation(() => new MessageSubmission("private-name", "private-key", disposed));
        Assert.DoesNotContain("private-", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("key")]
    [InlineData("producer-id")]
    public void Invalid_structural_identity_text_cannot_escape_through_public_models(string field)
    {
        using var document = JsonDocument.Parse("null");
        foreach (var value in new[] { "", "\t\u2003", "private-canary-\ud800" })
        {
            var error = Validation(() => new MessageSubmission(field == "name" ? value : "Payment",
                field == "key" ? value : "Order 123", document.RootElement, field == "producer-id" ? value : null));
            Assert.DoesNotContain("private-canary", error.ToString());
        }
    }

    [Fact]
    public void Missing_required_identity_is_validation_while_an_omitted_producer_ID_is_allowed()
    {
        using var document = JsonDocument.Parse("null");
        Validation(() => new MessageSubmission(null!, "Order 123", document.RootElement));
        Validation(() => new MessageSubmission("Payment", null!, document.RootElement));
        Validation(() => new MessageReceipt(null!, AcceptedAt, MessageDeliveryMode.ImmediateOnly));
        _ = new MessageSubmission("Payment", "Order 123", document.RootElement, producerMessageId: null);
    }

    [Fact]
    public void Opaque_server_receipt_IDs_survive_original_duplicate_wrapping_without_replacement()
    {
        string[] ids = ["accepted-\ud800", "accepted-\ud801", "accepted-\udc00", "accepted-\ufffd", "accepted\0tail", "accepted"];
        var retained = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            var receipt = new MessageReceipt(id, AcceptedAt, MessageDeliveryMode.ImmediateOnly);
            var duplicate = new MessageSubmissionResult(receipt, true);
            Assert.Same(receipt, duplicate.Receipt);
            Assert.Equal(id, duplicate.Receipt.ReceiptId);
            retained.Add(duplicate.Receipt.ReceiptId);
        }
        Assert.Equal(ids.Length, retained.Count);
    }

    [Fact]
    public void Public_content_preserves_exact_intent_and_does_not_duplicate_deployment_limits()
    {
        using var document = JsonDocument.Parse("null");
        var submission = new MessageSubmission(" Payment ", "00123", document.RootElement, "ID", int.MaxValue);
        Assert.Equal(" Payment ", submission.Name);
        Assert.Equal("00123", submission.CorrelationKey);
        Assert.Equal(int.MaxValue, submission.LifetimeSeconds);
        var omitted = new MessageSubmission("Payment", "123", document.RootElement);
        var immediate = new MessageSubmission("Payment", "123", document.RootElement, lifetimeSeconds: 0);
        Assert.Null(omitted.LifetimeSeconds);
        Assert.Equal(0, immediate.LifetimeSeconds);
        Validation(() => new MessageSubmission("Payment", "123", document.RootElement, lifetimeSeconds: -1));
    }

    [Theory]
    [InlineData("unknown-mode")]
    [InlineData("missing-buffer")]
    [InlineData("immediate-buffer")]
    [InlineData("past-buffer")]
    [InlineData("equal-buffer")]
    [InlineData("past-dedup")]
    [InlineData("equal-dedup")]
    [InlineData("minimum-time")]
    [InlineData("maximum-time")]
    public void Receipt_shape_cannot_claim_contradictory_mode_or_deadlines(string invalid)
    {
        var mode = invalid == "unknown-mode" ? (MessageDeliveryMode)77 : invalid == "immediate-buffer"
            ? MessageDeliveryMode.ImmediateOnly : MessageDeliveryMode.Buffered;
        var accepted = invalid == "minimum-time" ? default : invalid == "maximum-time" ? DateTimeOffset.MaxValue : AcceptedAt;
        DateTimeOffset? buffer = invalid switch
        {
            "missing-buffer" => null, "past-buffer" => AcceptedAt.AddSeconds(-1), "equal-buffer" => AcceptedAt,
            _ => AcceptedAt.AddHours(1)
        };
        DateTimeOffset? dedup = invalid switch
        {
            "past-dedup" => AcceptedAt.AddSeconds(-1), "equal-dedup" => AcceptedAt, _ => null
        };
        Validation(() => new MessageReceipt("private-receipt", accepted, mode, buffer, dedup));
    }

    [Theory]
    [InlineData(MessageDeliveryMode.Buffered)]
    [InlineData(MessageDeliveryMode.ImmediateOnly)]
    public void Receipt_accepts_actual_finite_deadlines_at_the_representable_upper_boundary(MessageDeliveryMode mode)
    {
        using var payload = JsonDocument.Parse("null");
        var intent = SubmissionRules.CreateIntent("Payment", "Order 123", payload.RootElement,
            mode == MessageDeliveryMode.ImmediateOnly ? "id" : null, mode == MessageDeliveryMode.ImmediateOnly ? 0 : 1);
        var times = SubmissionRules.AtAcceptance(intent, DateTimeOffset.MaxValue.AddSeconds(-1), new SubmissionPolicy(1, 1));
        var receipt = new MessageReceipt("accepted", times.AcceptedAt, mode, times.BufferUntil, times.DeduplicateUntil);
        Assert.Equal(DateTimeOffset.MaxValue, mode == MessageDeliveryMode.Buffered ? receipt.BufferUntil : receipt.DeduplicateUntil);
    }

    [Fact]
    public void Independent_deadlines_and_original_receipt_survive_duplicate_response_wrapping()
    {
        var original = new MessageReceipt("accepted-original", AcceptedAt.ToOffset(TimeSpan.FromHours(2)),
            MessageDeliveryMode.Buffered, AcceptedAt.AddDays(30), AcceptedAt.AddSeconds(1));
        var duplicate = new MessageSubmissionResult(original, true);
        Assert.Same(original, duplicate.Receipt);
        Assert.True(duplicate.Duplicate);
        Assert.Equal(TimeSpan.Zero, duplicate.Receipt.AcceptedAt.Offset);
        Assert.Equal(AcceptedAt.AddDays(30), duplicate.Receipt.BufferUntil);
        Assert.Equal(AcceptedAt.AddSeconds(1), duplicate.Receipt.DeduplicateUntil);
        var immediate = new MessageReceipt("accepted-unmatched", AcceptedAt, MessageDeliveryMode.ImmediateOnly);
        Assert.Null(immediate.BufferUntil);
        Validation(() => new MessageSubmissionResult(null!, false));
        Validation(() => new MessageReceipt("\t", AcceptedAt, MessageDeliveryMode.ImmediateOnly));
    }

    [Fact]
    public void Ordinary_submission_receipt_and_result_formatting_does_not_disclose_business_content()
    {
        using var document = JsonDocument.Parse("{\"private-payload\":\"private-value\"}");
        var submission = new MessageSubmission("private-name", "private-key", document.RootElement, "private-producer-id");
        var receipt = new MessageReceipt("private-receipt", AcceptedAt, MessageDeliveryMode.ImmediateOnly);
        foreach (var value in new object[] { submission, receipt, new MessageSubmissionResult(receipt, false) })
        {
            Assert.DoesNotContain("private-", $"Result: {value}");
            Assert.DoesNotContain("private-", string.Format("{0}", value));
        }
    }

    [Theory]
    [InlineData(MessageSubmissionFailureKind.Validation, "message-validation")]
    [InlineData(MessageSubmissionFailureKind.Conflict, "message-conflict")]
    [InlineData(MessageSubmissionFailureKind.Quota, "message-quota")]
    [InlineData(MessageSubmissionFailureKind.Forbidden, "message-forbidden")]
    [InlineData(MessageSubmissionFailureKind.Unavailable, "message-unavailable")]
    [InlineData(MessageSubmissionFailureKind.Uncertain, "message-uncertain")]
    public void Public_failure_codes_distinguish_refusals_from_uncertain_commit(MessageSubmissionFailureKind kind, string code)
    {
        var error = new MessageSubmissionException(kind);
        Assert.Equal(code, error.ReasonCode);
        Assert.Equal(kind, error.Kind);
        Assert.Null(error.InnerException);
        if (kind == MessageSubmissionFailureKind.Uncertain)
        {
            Assert.Contains("uncertain", error.Message);
            Assert.DoesNotContain("not accepted", error.Message);
        }
        if (kind == MessageSubmissionFailureKind.Unavailable) Assert.Contains("not accepted", error.Message);
    }

    [Fact]
    public void Unknown_failure_classification_is_rejected_without_manufacturing_an_uncertain_outcome() =>
        Assert.Throws<ArgumentException>(() => new MessageSubmissionException((MessageSubmissionFailureKind)77));

    private static MessageSubmissionException Validation(Action action)
    {
        var error = Assert.Throws<MessageSubmissionException>(action);
        Assert.Equal(MessageSubmissionFailureKind.Validation, error.Kind);
        return error;
    }
}
