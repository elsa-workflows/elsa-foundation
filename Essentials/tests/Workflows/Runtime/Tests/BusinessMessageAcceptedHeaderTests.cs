using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Messaging;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Pure data/codec controls; no acceptance transaction, producer, checkpoint capability or delivery is exercised.</summary>
public sealed class BusinessMessageAcceptedHeaderTests
{
    private static readonly DateTimeOffset AcceptedAt = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Accepted_header_round_trips_full_opaque_context_and_receipt_without_a_workflow_identity()
    {
        var partition = new WorkflowExecutionPartition("part\0\udc00");
        var context = new RuntimeDeliveryDiscoveryPartition(new("scope\0\ud800"), "tenant\ud801", WorkflowRunKind.TestRun,
            new("system\ud802", "root\udc01", new Dictionary<string, string> { ["key\0\ud803"] = "value\udc02" }), partition,
            new("test\0\ud804", AcceptedAt.AddHours(1), "tenant\ud801", partition));
        var header = Header(context, messageId: "message\0\ud805", receiptId: "receipt\udc03");
        var restored = RoundTrip(header);
        Assert.Equal("message\0\ud805", restored.Address.SourceId); Assert.Equal("receipt\udc03", restored.Receipt.ReceiptId);
        Assert.True(context.Matches(restored.Context)); Assert.Equal(header.CohortDigest, restored.CohortDigest);
        Assert.Equal(header.SubmittedFingerprint, restored.BindPayload(Payload()).Fingerprint);
        Assert.Equal("definition\0\ud806", Assert.Single(restored.Definitions).DefinitionId);
    }

    [Fact]
    public void Canonical_authority_and_complete_cohort_are_detached_ordinal_and_order_independent()
    {
        var metadata = new Dictionary<string, string> { ["z"] = "last", ["a"] = "first" };
        var context = Context(metadata: metadata); var scope = Scope(context);
        var definitions = new List<MessageDefinitionIdentity> { new(scope, "WorkflowDefinition", "receive-only"), new(scope, "WorkflowDefinition", "start") };
        var original = Header(context, definitions: definitions);
        metadata.Clear(); definitions.Clear();
        var reordered = Header(Context(metadata: new Dictionary<string, string> { ["a"] = "first", ["z"] = "last" }),
            definitions: [new(scope, "WorkflowDefinition", "start"), new(scope, "WorkflowDefinition", "receive-only")]);
        Assert.Equal(2, original.Definitions.Count); Assert.Equal("receive-only", original.Definitions[0].DefinitionId);
        Assert.Equal(BusinessMessageAcceptedHeaderCodec.Fingerprint(original), BusinessMessageAcceptedHeaderCodec.Fingerprint(reordered));
        Assert.Throws<NotSupportedException>(() => ((IList<MessageDefinitionIdentity>)original.Definitions).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)original.Context.Authority.Metadata).Clear());
    }

    [Fact]
    public void Logical_membership_needs_neither_start_flags_nor_open_bookmarks_and_empty_is_representable()
    {
        var context = Context(); var scope = Scope(context);
        var header = Header(context, definitions: [MessageDefinitionIdentity.ForWorkflowDefinition(scope, "receive-only")]);
        Assert.Equal("receive-only", Assert.Single(RoundTrip(header).Definitions).DefinitionId);
        Assert.Empty(RoundTrip(Header(context, definitions: [])).Definitions);
        Assert.NotEqual(header.CohortDigest, Header(context, definitions: []).CohortDigest);
        // Completeness is proved only by the acceptance transaction's full coherent interest read, not this list.
    }

    [Fact]
    public void Cohort_scope_duplicates_and_excess_refuse_without_clipping_or_protocol_splitting()
    {
        var context = Context(); var scope = Scope(context); var definition = MessageDefinitionIdentity.ForWorkflowDefinition(scope, "same-authored-id");
        Assert.Throws<ArgumentException>(() => Header(context, definitions: [definition, definition]));
        Assert.Throws<ArgumentException>(() => Header(context, definitions: [new(new(new("foreign")), "WorkflowDefinition", "same-authored-id")]));
        Assert.Throws<ArgumentException>(() => Header(context, definitions: Enumerable.Range(0, 257)
            .Select(index => MessageDefinitionIdentity.ForWorkflowDefinition(scope, index.ToString())).ToArray()));
        Assert.Equal(256, Header(context, definitions: Enumerable.Range(0, 256)
            .Select(index => MessageDefinitionIdentity.ForWorkflowDefinition(scope, index.ToString())).ToArray()).Definitions.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(86400)]
    [InlineData(0)]
    public void Lifetime_omission_explicit_default_and_zero_remain_distinct_fixed_intents(int? lifetime)
    {
        var header = Header(lifetime: lifetime, producerId: "producer-id"); var restored = RoundTrip(header);
        Assert.Equal(lifetime, restored.LifetimeSeconds);
        Assert.Equal(AcceptedAt.AddDays(7), restored.Receipt.DeduplicateUntil);
        Assert.Equal(lifetime == 0 ? MessageDeliveryMode.ImmediateOnly : MessageDeliveryMode.Buffered, restored.Receipt.Mode);
        DateTimeOffset? expectedBuffer = lifetime == 0 ? null : AcceptedAt.AddDays(1);
        Assert.Equal(expectedBuffer, restored.Receipt.BufferUntil);
        using var json = JsonDocument.Parse(BusinessMessageAcceptedHeaderCodec.Encode(header));
        Assert.Equal(lifetime is not null, json.RootElement.GetProperty("submission").TryGetProperty("lifetimeSeconds", out _));
        if (lifetime is null) Assert.NotEqual(header.SubmittedFingerprint, Header(lifetime: 86400, producerId: "producer-id").SubmittedFingerprint);
    }

    [Fact]
    public void Stored_policy_and_clock_round_trip_without_resolving_new_defaults_or_sliding_dedup()
    {
        var header = Header(policy: new(60, 120)); var restored = RoundTrip(header);
        Assert.Equal(AcceptedAt.AddSeconds(60), restored.Receipt.BufferUntil); Assert.Null(restored.Receipt.DeduplicateUntil);
        Assert.NotEqual(header.Receipt.BufferUntil, Header(policy: new(180, 240)).Receipt.BufferUntil);
        Assert.Equal(header.SubmittedFingerprint, Header(policy: new(180, 240)).SubmittedFingerprint);
        Assert.NotEqual(BusinessMessageAcceptedHeaderCodec.Fingerprint(header), BusinessMessageAcceptedHeaderCodec.Fingerprint(Header(policy: new(180, 240))));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("explicit-null-lifetime")]
    [InlineData("explicit-null-producer")]
    [InlineData("missing-scope")]
    [InlineData("duplicate-root")]
    [InlineData("duplicate-submission")]
    [InlineData("cohort-digest")]
    [InlineData("clock")]
    [InlineData("invalid-receipt")]
    public void Strict_decode_rejects_unknown_missing_duplicate_null_and_changed_canonical_values(string change)
    {
        var header = Header(); var json = BusinessMessageAcceptedHeaderCodec.Encode(header);
        var node = JsonNode.Parse(json)!.AsObject();
        switch (change)
        {
            case "unknown": node["Delivered"] = true; break;
            case "explicit-null-lifetime": node["submission"]!["lifetimeSeconds"] = null; break;
            case "explicit-null-producer": node["submission"]!["messageId"] = null; break;
            case "missing-scope": node["context"]!.AsObject().Remove("scope"); break;
            case "cohort-digest": node["cohortDigest"] = "unverified"; break;
            case "clock": node["acceptedAtUtcTicks"] = AcceptedAt.AddSeconds(1).UtcTicks; break;
            case "invalid-receipt": node["receiptId"] = ""; break;
        }
        if (change == "duplicate-root") json = "{\"version\":1," + json[1..];
        else if (change == "duplicate-submission") json = json.Replace("\"name\":\"Payment\"", "\"name\":\"Payment\",\"name\":\"Other\"");
        else json = node.ToJsonString();
        Assert.Throws<InvalidDataException>(() => BusinessMessageAcceptedHeaderCodec.Decode(json, BusinessMessageAcceptedHeaderCodec.Fingerprint(header)));
    }

    [Fact]
    public void Unknown_versions_refuse_before_materialization_and_foreign_fingerprints_do_not_validate()
    {
        var header = Header(); var node = JsonNode.Parse(BusinessMessageAcceptedHeaderCodec.Encode(header))!;
        node["version"] = 2;
        Assert.Throws<NotSupportedException>(() => BusinessMessageAcceptedHeaderCodec.Decode(node.ToJsonString(), BusinessMessageAcceptedHeaderCodec.Fingerprint(header)));
        Assert.Throws<InvalidDataException>(() => BusinessMessageAcceptedHeaderCodec.Decode(BusinessMessageAcceptedHeaderCodec.Encode(header), "foreign"));
    }

    [Fact]
    public void Payload_commitment_uses_current_canonical_rules_and_full_authored_fingerprint_is_revalidated()
    {
        var header = Header(); var reordered = JsonSerializer.Deserialize<JsonElement>("{\"a\":null,\"z\":1}");
        Assert.Equal(header.SubmittedFingerprint, header.BindPayload(reordered).Fingerprint);
        Assert.Throws<InvalidDataException>(() => header.BindPayload(JsonSerializer.Deserialize<JsonElement>("{\"a\":null,\"z\":1.0}")));
        Assert.Throws<MessageSubmissionValidationException>(() => header.BindPayload(JsonSerializer.Deserialize<JsonElement>("{\"z\":1,\"z\":2}")));
        Assert.Throws<ArgumentException>(() => BusinessMessageAcceptedHeader.Create("message", "receipt", Context(), "producer", "config", Submission(),
            Header(lifetime: 0).SubmittedFingerprint, new(), AcceptedAt, 1, []));
    }

    [Fact]
    public void Present_JSON_null_is_data_while_retired_payload_keeps_header_metadata_and_refuses_owed_planning()
    {
        var submission = Submission(payload: "null"); var header = Header(submission: submission);
        var present = BusinessMessagePayloadSlot.Present(submission.Payload);
        var decoded = BusinessMessageAcceptedHeaderCodec.DecodePayloadSlot(BusinessMessageAcceptedHeaderCodec.EncodePayloadSlot(present));
        Assert.Equal(JsonValueKind.Null, decoded.Payload.ValueKind); Assert.Equal(header.SubmittedFingerprint, decoded.BindForPlanning(header).Fingerprint);
        var retired = BusinessMessageAcceptedHeaderCodec.DecodePayloadSlot(BusinessMessageAcceptedHeaderCodec.EncodePayloadSlot(
            BusinessMessagePayloadSlot.Retired("operation\ud800", "proof\0\udc00")));
        Assert.Equal("operation\ud800", retired.RetirementOperationId); Assert.Equal("proof\0\udc00", retired.RetirementOperationFingerprint);
        Assert.Equal(header.Receipt.ReceiptId, RoundTrip(header).Receipt.ReceiptId);
        Assert.Throws<InvalidDataException>(() => retired.BindForPlanning(header));
        Assert.Throws<InvalidOperationException>(() => _ = retired.Payload);
    }

    [Fact]
    public void The_present_slot_detaches_payload_and_metadata_decode_never_substitutes_for_content_binding()
    {
        BusinessMessagePayloadSlot slot;
        using (var document = JsonDocument.Parse("{\"z\":1,\"a\":null}")) slot = BusinessMessagePayloadSlot.Present(document.RootElement);
        var header = RoundTrip(Header());
        Assert.Equal(header.SubmittedFingerprint, slot.BindForPlanning(header).Fingerprint);
        Assert.Throws<InvalidDataException>(() => BusinessMessagePayloadSlot.Present(JsonSerializer.SerializeToElement("changed")).BindForPlanning(header));
        // Core's authored DTO is structural; the accepted-header create path still rechecks actual T004 policy bounds.
        var beyondBound = Submission(lifetime: 31 * 24 * 60 * 60);
        Assert.Throws<MessageSubmissionValidationException>(() => Header(submission: beyondBound));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"state\":0}")]
    [InlineData("{\"state\":1}")]
    [InlineData("{\"state\":2,\"payload\":null}")]
    [InlineData("{\"state\":0,\"payload\":null,\"Delivered\":true}")]
    [InlineData("{\"state\":0,\"state\":1,\"payload\":null}")]
    public void Missing_or_corrupt_payload_slot_is_never_treated_as_null_or_retirement(string json) =>
        Assert.Throws<InvalidDataException>(() => BusinessMessageAcceptedHeaderCodec.DecodePayloadSlot(json));

    [Fact]
    public void Redacted_formatting_and_source_fingerprint_preserve_distinct_opaque_code_units()
    {
        var left = Header(messageId: "private\ud800"); var right = Header(messageId: "private\ud801");
        Assert.NotEqual(BusinessMessageAcceptedHeaderCodec.Fingerprint(left), BusinessMessageAcceptedHeaderCodec.Fingerprint(right));
        Assert.DoesNotContain("private", left.ToString()); Assert.DoesNotContain("private", BusinessMessagePayloadSlot.Retired("private", "private").ToString());
    }

    private static BusinessMessageAcceptedHeader RoundTrip(BusinessMessageAcceptedHeader header)
    {
        var json = BusinessMessageAcceptedHeaderCodec.Encode(header);
        var metadata = BusinessMessageAcceptedHeaderCodec.Decode(json);
        Assert.Equal(BusinessMessageAcceptedHeaderCodec.Fingerprint(header), BusinessMessageAcceptedHeaderCodec.Fingerprint(metadata));
        return BusinessMessageAcceptedHeaderCodec.Decode(json, BusinessMessageAcceptedHeaderCodec.Fingerprint(header));
    }
    private static JsonElement Payload() => JsonSerializer.Deserialize<JsonElement>("{\"z\":1,\"a\":null}");
    private static MessageSubmission Submission(string payload = "{\"z\":1,\"a\":null}", int? lifetime = null, string? producerId = null) =>
        new("Payment", "order", JsonSerializer.Deserialize<JsonElement>(payload), producerId, lifetime);
    private static RuntimeDeliveryDiscoveryPartition Context(string? tenant = "tenant", IReadOnlyDictionary<string, string>? metadata = null) =>
        new(new("scope"), tenant, WorkflowRunKind.PublishedRun, new("system", "root", metadata), new("execution"));
    private static MessageScope Scope(RuntimeDeliveryDiscoveryPartition context) => new(context.Scope, context.TenantId, context.TestScope?.ScopeId);
    private static BusinessMessageAcceptedHeader Header(RuntimeDeliveryDiscoveryPartition? context = null, string messageId = "message", string receiptId = "receipt",
        int? lifetime = null, string? producerId = null, MessageSubmissionPolicy? policy = null,
        IReadOnlyCollection<MessageDefinitionIdentity>? definitions = null, MessageSubmission? submission = null)
    {
        context ??= Context(); submission ??= Submission(lifetime: lifetime, producerId: producerId);
        var intent = MessageSubmissionRules.CreateIntent(submission.Name, submission.CorrelationKey, submission.Payload, submission.ProducerMessageId, submission.LifetimeSeconds);
        return BusinessMessageAcceptedHeader.Create(messageId, receiptId, context, "trusted-producer", "configuration", submission,
            intent.Fingerprint, policy ?? new(), AcceptedAt, 1,
            definitions ?? [MessageDefinitionIdentity.ForWorkflowDefinition(Scope(context), "definition\0\ud806")]);
    }
}
