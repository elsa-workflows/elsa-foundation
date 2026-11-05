using System.Globalization;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Messaging;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Pure rules only; these tests do not establish acceptance, provider durability or delivery.</summary>
public sealed class MessageSubmissionRulesTests
{
    private static readonly DateTimeOffset AcceptedAt = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reordered_objects_and_whitespace_preserve_content_identity_at_every_depth()
    {
        var first = Intent("""{ "b": [ { "z": 1, "a": null }, true ], "a": "text" }""");
        var second = Intent("""{"a":"text","b":[{"a":null,"z":1},true]}""");
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.StartsWith(MessageSubmissionRules.FingerprintVersion + ":", first.Fingerprint);
    }

    [Theory]
    [InlineData("{\"value\":1}", "{\"value\":1.0}")]
    [InlineData("{\"value\":1}", "{\"value\":1e0}")]
    [InlineData("{\"Value\":1}", "{\"value\":1}")]
    [InlineData("\"Text\"", "\"text\"")]
    [InlineData("[1,2]", "[2,1]")]
    [InlineData("null", "{}")]
    [InlineData("\"é\"", "\"é\"")]
    public void Semantically_significant_submitted_details_do_not_collapse(string left, string right) =>
        Assert.NotEqual(Intent(left).Fingerprint, Intent(right).Fingerprint);

    [Fact]
    public void Optional_producer_id_and_exact_envelope_fields_participate_in_source_equality()
    {
        var original = Intent("null");
        Assert.NotEqual(original.Fingerprint, Intent("null", name: "payment").Fingerprint);
        Assert.NotEqual(original.Fingerprint, Intent("null", key: "order ").Fingerprint);
        Assert.NotEqual(original.Fingerprint, Intent("null", id: "id-a").Fingerprint);
        Assert.NotEqual(Intent("null", id: "id-a").Fingerprint, Intent("null", id: "id-b").Fingerprint);
        Assert.NotEqual(Intent("null", name: "ab", key: "c").Fingerprint, Intent("null", name: "a", key: "bc").Fingerprint);
    }

    [Fact]
    public void Omitted_lifetime_stays_distinct_from_explicit_default_and_immediate_intent()
    {
        var omitted = Intent("null");
        Assert.NotEqual(omitted.Fingerprint, Intent("null", seconds: 86400).Fingerprint);
        Assert.NotEqual(omitted.Fingerprint, Intent("null", seconds: 0).Fingerprint);
        var old = MessageSubmissionRules.AtAcceptance(omitted, AcceptedAt);
        var changed = MessageSubmissionRules.AtAcceptance(omitted, AcceptedAt, new MessageSubmissionPolicy(60, 120));
        Assert.Equal(AcceptedAt.AddDays(1), old.BufferUntil);
        Assert.Equal(AcceptedAt.AddMinutes(1), changed.BufferUntil);
        Assert.Equal(omitted.Fingerprint, Intent("null").Fingerprint);
    }

    [Fact]
    public void Snapshot_outlives_and_detaches_from_the_original_document()
    {
        MessageSubmissionIntent intent;
        using (var document = JsonDocument.Parse("{\"data\":[1,null]}"))
            intent = MessageSubmissionRules.CreateIntent("Payment", "order", document.RootElement);
        Assert.Equal(1, intent.Payload.GetProperty("data")[0].GetInt32());
        Assert.Equal(Intent("{\"data\":[1,null]}").Fingerprint, intent.Fingerprint);
        Assert.Equal(JsonValueKind.Null, Intent("null").Payload.ValueKind);
    }

    [Fact]
    public void Undefined_and_disposed_DOMs_are_refused_with_safe_errors()
    {
        Invalid("invalid-payload", () => MessageSubmissionRules.CreateIntent("Payment", "order", default));
        var document = JsonDocument.Parse("null");
        var disposed = document.RootElement;
        document.Dispose();
        Assert.Throws<MessageSubmissionValidationException>(() => MessageSubmissionRules.CreateIntent("Payment", "order", disposed));
    }

    [Theory]
    [InlineData("{\"a\":1,\"a\":2}")]
    [InlineData("{\"outer\":[{\"a\":1,\"a\":2}]}")]
    [InlineData("{\"a\":1,\"\\u0061\":2}")]
    public void Duplicate_members_are_rejected_including_nested_and_escaped_names(string payload) =>
        Invalid("duplicate-payload-member", () => Intent(payload));

    [Fact]
    public void Case_distinct_members_and_equivalent_string_escapes_remain_valid()
    {
        Assert.Equal(Intent("{\"A\":1,\"a\":2,\"text\":\"a\"}").Fingerprint,
            Intent("{\"text\":\"\\u0061\",\"a\":2,\"A\":1}").Fingerprint);
    }

    [Fact]
    public void Payload_depth_has_a_real_container_boundary()
    {
        static string Nested(int count) => new string('[', count) + "null" + new string(']', count);
        using var allowed = JsonDocument.Parse(Nested(64), new JsonDocumentOptions { MaxDepth = 128 });
        using var refused = JsonDocument.Parse(Nested(65), new JsonDocumentOptions { MaxDepth = 128 });
        _ = MessageSubmissionRules.CreateIntent("Payment", "order", allowed.RootElement);
        Invalid("payload-too-deep", () => MessageSubmissionRules.CreateIntent("Payment", "order", refused.RootElement));
    }

    [Fact]
    public void Raw_payload_size_guard_is_separate_from_the_shared_envelope_budget()
    {
        var limit = MessageSubmissionRules.MaximumPayloadBytes;
        _ = Intent(BoundaryPayload());
        Invalid("payload-too-large", () => Intent(JsonSerializer.Serialize(new string('a', limit - 1))));
        Invalid("payload-too-large", () => Intent("\"" + new string('é', limit / 2) + "\""));
    }

    [Fact]
    public void A_payload_filling_the_entire_budget_leaves_no_room_for_required_envelope_fields()
    {
        var payload = JsonSerializer.Serialize(new string('a', MessageSubmissionRules.MaximumPayloadBytes - 2));
        Assert.Equal(MessageSubmissionRules.MaximumPayloadBytes, Encoding.UTF8.GetByteCount(payload));
        Invalid("submission-too-large", () => Intent(payload));
    }

    [Fact]
    public void Complete_normalized_envelope_accepts_the_exact_shared_boundary_but_not_one_more_byte()
    {
        var payload = BoundaryPayload();
        Assert.Equal(MessageSubmissionRules.MaximumPayloadBytes, EnvelopeBytes(payload));
        _ = Intent(payload);
        var excessive = JsonSerializer.Serialize(new string('a', payload.Length - 1));
        Assert.Equal(MessageSubmissionRules.MaximumPayloadBytes + 1, EnvelopeBytes(excessive));
        Invalid("submission-too-large", () => Intent(excessive));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("key")]
    [InlineData("escaping")]
    [InlineData("lifetime")]
    public void Optional_fields_and_identity_escaping_share_the_complete_envelope_budget(string extra)
    {
        var payload = BoundaryPayload();
        var name = extra == "escaping" ? "Paymenté" : "Payment";
        var key = extra == "key" ? new string('k', 1024) : "order";
        var id = extra == "id" ? new string('i', 256) : null;
        int? seconds = extra == "lifetime" ? 2592000 : null;
        Assert.True(EnvelopeBytes(payload, name, key, id, seconds) > MessageSubmissionRules.MaximumPayloadBytes);
        Invalid("submission-too-large", () => Intent(payload, name, key, id, seconds));
    }

    [Fact]
    public void Payload_string_escaping_counts_even_when_its_original_UTF8_fits()
    {
        var text = new string('é', MessageSubmissionRules.MaximumPayloadBytes / 6);
        var original = "\"" + text + "\"";
        Assert.True(Encoding.UTF8.GetByteCount(original) < MessageSubmissionRules.MaximumPayloadBytes);
        Assert.True(EnvelopeBytes(JsonSerializer.Serialize(text)) > MessageSubmissionRules.MaximumPayloadBytes);
        Invalid("submission-too-large", () => Intent(original));
    }

    [Theory]
    [InlineData("name", 256)]
    [InlineData("key", 1024)]
    [InlineData("id", 256)]
    public void Identity_limits_count_multibyte_scalar_UTF8_exactly(string field, int limit)
    {
        var boundary = string.Concat(Enumerable.Repeat("😀", limit / 4));
        _ = WithField(field, boundary);
        Invalid("invalid-" + (field == "id" ? "message-id" : field), () => WithField(field, boundary + "a"));
    }

    [Theory]
    [InlineData("name", "")]
    [InlineData("name", "\t\u2003")]
    [InlineData("key", " ")]
    [InlineData("id", "")]
    [InlineData("id", "\n")]
    public void Empty_or_Unicode_whitespace_identities_are_refused(string field, string value) =>
        Assert.Throws<MessageSubmissionValidationException>(() => WithField(field, value));

    [Theory]
    [InlineData("name")]
    [InlineData("key")]
    [InlineData("id")]
    public void Unpaired_surrogates_are_refused_without_echoing_business_content(string field)
    {
        var error = Assert.Throws<MessageSubmissionValidationException>(() => WithField(field, "private-canary-\ud800"));
        Assert.DoesNotContain("private-canary", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void Exact_nonblank_spacing_is_preserved_and_length_prefix_collisions_do_not_alias()
    {
        Assert.NotEqual(Intent("null", key: " order").Fingerprint, Intent("null", key: "order").Fingerprint);
        Assert.Equal(" order ", Intent("null", key: " order ").CorrelationKey);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2592001)]
    [InlineData(int.MaxValue)]
    public void Out_of_range_sender_lifetimes_refuse(int seconds) =>
        Invalid("invalid-lifetime", () => Intent("null", seconds: seconds));

    [Fact]
    public void Fixed_independent_deadlines_do_not_depend_on_buffer_or_run_completion()
    {
        var withId = Intent("null", id: "id", seconds: 1);
        var times = MessageSubmissionRules.AtAcceptance(withId, AcceptedAt);
        Assert.Equal(AcceptedAt.AddSeconds(1), times.BufferUntil);
        Assert.Equal(AcceptedAt.AddDays(7), times.DeduplicateUntil);
        var reverse = MessageSubmissionRules.AtAcceptance(Intent("null", id: "id", seconds: 2592000), AcceptedAt,
            new MessageSubmissionPolicy(1, 1));
        Assert.Equal(AcceptedAt.AddDays(30), reverse.BufferUntil);
        Assert.Equal(AcceptedAt.AddSeconds(1), reverse.DeduplicateUntil);
        Assert.Null(MessageSubmissionRules.AtAcceptance(Intent("null"), AcceptedAt).DeduplicateUntil);
    }

    [Fact]
    public void Immediate_only_has_no_open_buffer_but_keeps_ID_recognition()
    {
        var times = MessageSubmissionRules.AtAcceptance(Intent("null", id: "id", seconds: 0), AcceptedAt);
        Assert.Null(times.BufferUntil);
        Assert.Equal(0, times.BufferLifetimeSeconds);
        Assert.Equal(AcceptedAt.AddDays(7), times.DeduplicateUntil);
    }

    [Theory]
    [InlineData(0, 1, "invalid-buffer-policy")]
    [InlineData(2592001, 1, "invalid-buffer-policy")]
    [InlineData(1, 0, "invalid-deduplication-policy")]
    [InlineData(1, 7776001, "invalid-deduplication-policy")]
    public void Invalid_deployment_policy_cannot_create_deadlines(int buffer, int dedup, string reason) =>
        Invalid(reason, () => new MessageSubmissionPolicy(buffer, dedup));

    [Fact]
    public void Deployment_maximum_rejects_excessive_override_without_clamping_or_disabling_immediate_mode()
    {
        var policy = new MessageSubmissionPolicy(3600, 604800, maximumBufferLifetimeSeconds: 3600);
        Assert.Equal(AcceptedAt.AddHours(1), MessageSubmissionRules.AtAcceptance(Intent("null", seconds: 3600), AcceptedAt, policy).BufferUntil);
        Assert.Null(MessageSubmissionRules.AtAcceptance(Intent("null", seconds: 0), AcceptedAt, policy).BufferUntil);
        Invalid("lifetime-exceeds-policy", () => MessageSubmissionRules.AtAcceptance(Intent("null", seconds: 3601), AcceptedAt, policy));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2592001)]
    public void Invalid_deployment_maximum_refuses_before_snapshot(int maximum) =>
        Invalid("invalid-buffer-policy", () => new MessageSubmissionPolicy(1, 1, maximum));

    [Fact]
    public void Deployment_default_cannot_exceed_its_configured_maximum() =>
        Invalid("invalid-buffer-policy", () => new MessageSubmissionPolicy(3601, 604800, 3600));

    [Fact]
    public void Trusted_time_is_snapshotted_in_UTC_and_overflow_never_wraps_or_leaks_input()
    {
        var offset = AcceptedAt.ToOffset(TimeSpan.FromHours(2));
        var times = MessageSubmissionRules.AtAcceptance(Intent("null", id: "id"), offset);
        Assert.Equal(TimeSpan.Zero, times.AcceptedAt.Offset);
        Assert.Equal(AcceptedAt, times.AcceptedAt);
        Invalid("invalid-acceptance-time", () => MessageSubmissionRules.AtAcceptance(Intent("null"), default));
        Invalid("acceptance-deadline-overflow", () => MessageSubmissionRules.AtAcceptance(Intent("null", id: "id"), DateTimeOffset.MaxValue.AddSeconds(-1)));
        var maximum = MessageSubmissionRules.AtAcceptance(Intent("null", id: "id"), AcceptedAt, new MessageSubmissionPolicy(2592000, 7776000));
        Assert.Equal(AcceptedAt.AddDays(90), maximum.DeduplicateUntil);
    }

    private static MessageSubmissionIntent WithField(string field, string value) => field switch
    {
        "name" => Intent("null", name: value), "key" => Intent("null", key: value), "id" => Intent("null", id: value),
        _ => throw new ArgumentException("Unknown test field.")
    };

    private static string BoundaryPayload() => JsonSerializer.Serialize(new string('a',
        MessageSubmissionRules.MaximumPayloadBytes - EnvelopeBytes("\"\"")));

    private static int EnvelopeBytes(string payload, string name = "Payment", string key = "order", string? id = null, int? seconds = null) =>
        Encoding.UTF8.GetByteCount("{\"name\":" + JsonSerializer.Serialize(name) + ",\"correlationKey\":" + JsonSerializer.Serialize(key) +
            (id is null ? "" : ",\"messageId\":" + JsonSerializer.Serialize(id)) +
            (seconds is null ? "" : ",\"lifetimeSeconds\":" + seconds.Value.ToString(CultureInfo.InvariantCulture)) + ",\"payload\":" + payload + "}");

    private static MessageSubmissionIntent Intent(string json, string name = "Payment", string key = "order",
        string? id = null, int? seconds = null)
    {
        using var document = JsonDocument.Parse(json);
        return MessageSubmissionRules.CreateIntent(name, key, document.RootElement, id, seconds);
    }

    private static void Invalid(string reason, Action action) =>
        Assert.Equal(reason, Assert.Throws<MessageSubmissionValidationException>(action).ReasonCode);
}
