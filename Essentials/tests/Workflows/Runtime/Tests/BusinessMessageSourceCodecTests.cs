using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Messaging;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Canonical data/initial planning only; no producer, recipient, reservation or settlement authority.</summary>
public sealed class BusinessMessageSourceCodecTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Actual_factories_match_the_independent_ASCII_wire_vector_used_by_EF_adapter_tests()
    {
        var context = new RuntimeDeliveryDiscoveryPartition(new("store"), null, WorkflowRunKind.PublishedRun, new("system", "root"));
        var submission = new MessageSubmission("payment", "001", Payload());
        var intent = MessageSubmissionRules.CreateIntent("payment", "001", submission.Payload);
        var header = BusinessMessageAcceptedHeader.Create("message", "receipt", context, "producer", "config", submission, intent.Fingerprint,
            new(), DateTimeOffset.UnixEpoch, 1, [MessageDefinitionIdentity.ForWorkflowDefinition(new(context.Scope), "accounting")]);
        var source = BusinessMessageSourceState.InitialBuffered(header, BusinessMessagePayloadSlot.Present(Payload()));
        var work = Assert.Single(BusinessMessageDefinitionWork.Initial(source));
        Assert.Equal("elsa.business-message-header/v1:09a1954565d6a077fde9e696ce8b3d60985cf7669776e2f9ba38b3999438a5d6", BusinessMessageAcceptedHeaderCodec.Fingerprint(header));
        Assert.Equal("elsa.business-message-source/v1:9e77fecbdcde485610ec5a94561e6aa582c811e2fccdbbdf2613ff2e75e0fe05", BusinessMessageSourceCodec.Fingerprint(source));
        Assert.Equal("elsa.business-message-work-address/v1:9cb1a856fbe7dab18069a83e97debf1e493df2708decfa76627363e4020653d5", work.WorkId); Assert.Equal("elsa.business-message-work/v1:748baeae8ab08aa5c515729aafc4d02bad4771f157bfe752ec9d5464a8f9ada4", work.Fingerprint());
    }

    [Fact]
    public void Buffered_initial_work_is_complete_detached_stable_and_scoped_to_the_frozen_logical_cohort()
    {
        var source = Initial(); var work = BusinessMessageDefinitionWork.Initial(source);
        Assert.Equal(2, work.Count); Assert.Equal(source.Header.Definitions, work.Select(item => item.Definition));
        Assert.Equal(2, work.Select(item => item.WorkId).Distinct().Count());
        foreach (var item in work)
        {
            var restored = BusinessMessageDefinitionWork.Decode(item.Encode());
            Assert.Equal(item.WorkId, restored.WorkId); Assert.Equal(item.Fingerprint(), restored.Fingerprint());
            Assert.Equal(At, restored.NotBefore); restored.ValidatePlanningBinding(source);
        }
        Assert.Throws<NotSupportedException>(() => ((IList<BusinessMessageDefinitionWork>)work).Clear());
        var roundTrip = BusinessMessageSourceCodec.Decode(BusinessMessageSourceCodec.Encode(source));
        Assert.Equal(work.Select(item => item.WorkId), BusinessMessageDefinitionWork.Initial(roundTrip).Select(item => item.WorkId));
    }

    [Fact]
    public void Immediate_only_requires_the_complete_unmatched_cohort_and_never_schedules_later_discovery()
    {
        var header = Header(lifetime: 0);
        var source = BusinessMessageSourceState.InitialImmediateUnmatched(header, BusinessMessagePayloadSlot.Present(Payload()), header.Definitions);
        Assert.True(source.IsImmediateUnmatched); Assert.Equal(BusinessMessageSourceState.Terminal, source.Status);
        Assert.Empty(BusinessMessageDefinitionWork.Initial(source));
        Assert.Throws<InvalidDataException>(() => BusinessMessageSourceState.InitialImmediateUnmatched(header,
            BusinessMessagePayloadSlot.Present(Payload()), header.Definitions.Take(1).ToArray()));
        Assert.Throws<NotSupportedException>(() => BusinessMessageSourceState.InitialBuffered(header, BusinessMessagePayloadSlot.Present(Payload())));
        var empty = Header(lifetime: 0, names: []);
        var noDefinitions = BusinessMessageSourceState.InitialImmediateUnmatched(empty, BusinessMessagePayloadSlot.Present(Payload()), []);
        Assert.True(noDefinitions.IsImmediateUnmatched); Assert.Empty(BusinessMessageDefinitionWork.Initial(noDefinitions));
    }

    [Fact]
    public void Empty_buffered_cohort_has_no_owed_work_without_claiming_business_delivery_or_adding_future_definitions()
    {
        var source = Initial(names: []);
        Assert.Empty(source.Progress); Assert.False(source.HasOwedWork); Assert.False(source.IsImmediateUnmatched);
        Assert.Empty(BusinessMessageDefinitionWork.Initial(source));
        Assert.Empty(BusinessMessageSourceCodec.Decode(BusinessMessageSourceCodec.Encode(source)).Header.Definitions);
    }

    [Fact]
    public void Unknown_source_or_work_profiles_and_fictional_dispatch_kind_refuse()
    {
        var source = Initial(); var node = Node(source); node["version"] = 2;
        Assert.Throws<NotSupportedException>(() => BusinessMessageSourceCodec.Decode(node.ToJsonString()));
        var work = JsonNode.Parse(BusinessMessageDefinitionWork.Initial(source)[0].Encode())!; work["version"] = 2;
        Assert.Throws<NotSupportedException>(() => BusinessMessageDefinitionWork.Decode(work.ToJsonString()));
        work["version"] = 1; work["kind"] = "business-message.dispatch-selected/1";
        Assert.Throws<NotSupportedException>(() => BusinessMessageDefinitionWork.Decode(work.ToJsonString()));
    }

    [Fact]
    public void Selected_immediate_metadata_is_readable_but_the_initial_builder_refuses_fictional_dispatch_work()
    {
        var header = Header(lifetime: 0, names: ["accounting"]);
        var node = Node(BusinessMessageSourceState.InitialImmediateUnmatched(header, BusinessMessagePayloadSlot.Present(Payload()), header.Definitions));
        SetSelected(Row(node), At);
        var metadata = BusinessMessageSourceCodec.Decode(node.ToJsonString());
        Assert.Equal(BusinessMessageDefinitionStatus.Selected, Assert.Single(metadata.Progress).Status);
        Assert.Throws<NotSupportedException>(() => BusinessMessageDefinitionWork.Initial(metadata));
    }

    [Theory]
    [InlineData("missing")] [InlineData("duplicate")] [InlineData("foreign")]
    [InlineData("immediate-pending")] [InlineData("buffered-unmatched")]
    public void Incomplete_foreign_or_mode_inconsistent_progress_refuses_without_clipping(string defect)
    {
        var node = Node(Initial()); var rows = node["progress"]!.AsArray();
        if (defect == "missing") rows.RemoveAt(1);
        else if (defect == "duplicate") rows[1] = rows[0]!.DeepClone();
        else if (defect == "foreign") rows[0]!["id"] = BusinessMessageWire.O("brand-new-definition");
        else if (defect == "buffered-unmatched") { rows[0]!.AsObject().Remove("discovery"); rows[0]!["status"] = 4; rows[0]!["resolvedAtUtcTicks"] = At.UtcTicks; }
        else
        {
            var immediate = Header(lifetime: 0);
            node["header"] = JsonNode.Parse(BusinessMessageAcceptedHeaderCodec.Encode(immediate));
        }
        Assert.Throws<InvalidDataException>(() => BusinessMessageSourceCodec.Decode(node.ToJsonString()));
    }

    [Theory]
    [InlineData("unknown-root")] [InlineData("duplicate-root")] [InlineData("unknown-progress")]
    [InlineData("null-selection")] [InlineData("missing-payload-slot")]
    public void Strict_owned_wrapper_rejects_unknown_duplicate_null_and_missing_members_with_safe_errors(string defect)
    {
        var node = Node(Initial());
        if (defect == "unknown-root") node["raw-secret-canary"] = "raw-secret-canary";
        else if (defect == "unknown-progress") Row(node)["raw-secret-canary"] = true;
        else if (defect == "null-selection") Row(node)["selection"] = null;
        else if (defect == "missing-payload-slot") node.AsObject().Remove("payloadSlot");
        var json = node.ToJsonString(); if (defect == "duplicate-root") json = "{\"version\":1," + json[1..];
        var error = Assert.Throws<InvalidDataException>(() => BusinessMessageSourceCodec.Decode(json));
        Assert.DoesNotContain("raw-secret-canary", error.ToString());
    }

    [Fact]
    public void Semantic_source_fingerprint_ignores_order_but_preserves_payload_number_spelling_and_immutable_header_hash()
    {
        var first = Initial(payload: "{\"z\":1,\"a\":null}"); var node = Node(first);
        var rows = node["progress"]!.AsArray(); var original = rows[0]!.DeepClone(); rows[0] = rows[1]!.DeepClone(); rows[1] = original;
        node["payloadSlot"]!["payload"] = JsonNode.Parse("{\"a\":null,\"z\":1}");
        var equivalent = BusinessMessageSourceCodec.Decode(node.ToJsonString());
        Assert.Equal(BusinessMessageSourceCodec.Fingerprint(first), BusinessMessageSourceCodec.Fingerprint(equivalent));
        Assert.NotEqual(BusinessMessageSourceCodec.Fingerprint(first), BusinessMessageSourceCodec.Fingerprint(Initial(payload: "{\"a\":null,\"z\":1.0}")));
        var expired = Expired(first);
        Assert.Equal(BusinessMessageAcceptedHeaderCodec.Fingerprint(first.Header), BusinessMessageAcceptedHeaderCodec.Fingerprint(expired.Header));
        Assert.NotEqual(BusinessMessageSourceCodec.Fingerprint(first), BusinessMessageSourceCodec.Fingerprint(expired));
    }

    [Fact]
    public void Partial_terminal_outcomes_keep_each_definition_and_allow_selected_consumption_after_buffer_expiry()
    {
        var node = Node(Initial()); var first = Row(node); SetSelected(first, At.AddSeconds(1));
        SetTerminal(first, BusinessMessageDefinitionStatus.Delivered, At.AddHours(1), BusinessMessageEvidenceKind.Consumption);
        var second = node["progress"]!.AsArray()[1]!.AsObject(); SetExpired(second, At.AddSeconds(60));
        var source = BusinessMessageSourceCodec.Decode(node.ToJsonString());
        Assert.Equal(new[] { BusinessMessageDefinitionStatus.Delivered, BusinessMessageDefinitionStatus.Expired }, source.Progress.Select(item => item.Status));
        Assert.Equal(BusinessMessageSourceState.Terminal, source.Status);
        Assert.Equal(At.AddHours(1), source.Progress[0].ResolvedAt);
    }

    [Theory]
    [InlineData("expired-before-deadline")] [InlineData("delivered-no-selection")] [InlineData("delivered-wrong-evidence")]
    [InlineData("failed-no-close")] [InlineData("failed-no-code")] [InlineData("zero-epoch")] [InlineData("wrong-reservation-owner")]
    public void Terminal_shapes_require_correct_selection_epochs_closure_consumption_and_clock_fields(string defect)
    {
        var node = Node(Initial()); var row = Row(node);
        if (defect == "expired-before-deadline") SetExpired(row, At.AddSeconds(59));
        else
        {
            SetSelected(row, At.AddSeconds(1));
            if (defect == "zero-epoch") row["selection"]!["capabilityEpoch"] = 0;
            else if (defect is "failed-no-close" or "failed-no-code")
            {
                SetTerminal(row, BusinessMessageDefinitionStatus.Failed, At.AddSeconds(2), BusinessMessageEvidenceKind.ClosedUnconsumed);
                if (defect == "failed-no-close") row.Remove("evidence"); else row.Remove("failure");
            }
            else
            {
                SetTerminal(row, BusinessMessageDefinitionStatus.Delivered, At.AddSeconds(2), BusinessMessageEvidenceKind.Consumption);
                if (defect == "delivered-no-selection") row.Remove("selection");
                else if (defect == "delivered-wrong-evidence") row["evidence"]!["kind"] = (int)BusinessMessageEvidenceKind.ClosedUnconsumed;
                else row["evidence"]!["selectionId"] = BusinessMessageWire.O("other-owner");
            }
        }
        Assert.Throws<InvalidDataException>(() => BusinessMessageSourceCodec.Decode(node.ToJsonString()));
    }

    [Theory]
    [InlineData("failed-selected")] [InlineData("abandoned-selected")] [InlineData("abandoned-pending")]
    public void Closed_failure_and_abandonment_metadata_have_exact_bound_evidence_shapes_without_granting_a_transition(string variant)
    {
        var prior = Initial(); var node = Node(prior); var row = Row(node);
        if (variant == "abandoned-pending")
        {
            row.Remove("discovery"); row["status"] = 6; row["resolvedAtUtcTicks"] = At.AddSeconds(1).UtcTicks;
            row["evidence"] = new JsonObject { ["kind"] = 2, ["id"] = BusinessMessageWire.O("audit-proof") };
        }
        else
        {
            SetSelected(row, At.AddSeconds(1));
            SetTerminal(row, variant == "failed-selected" ? BusinessMessageDefinitionStatus.Failed : BusinessMessageDefinitionStatus.Abandoned,
                At.AddSeconds(2), BusinessMessageEvidenceKind.ClosedUnconsumed);
        }
        var metadata = BusinessMessageSourceCodec.Decode(node.ToJsonString());
        Assert.True(metadata.Progress[0].IsTerminal); Assert.Equal(BusinessMessageDefinitionStatus.Pending, metadata.Progress[1].Status);
        Assert.Throws<NotSupportedException>(() => BusinessMessageSourceState.ValidateSuccessorShape(prior, metadata));
    }

    [Fact]
    public void Readable_proof_reference_strings_cannot_authorize_a_delivery_transition_and_terminal_winners_never_change()
    {
        var node = Node(Initial()); SetSelected(Row(node), At.AddSeconds(1)); var selected = BusinessMessageSourceCodec.Decode(node.ToJsonString());
        SetTerminal(Row(node), BusinessMessageDefinitionStatus.Delivered, At.AddSeconds(2), BusinessMessageEvidenceKind.Consumption);
        var delivered = BusinessMessageSourceCodec.Decode(node.ToJsonString());
        Assert.Throws<NotSupportedException>(() => BusinessMessageSourceState.ValidateSuccessorShape(selected, delivered));
        var changed = Node(delivered); changed["progress"]![0]!["selection"]!["id"] = BusinessMessageWire.O("different-winner");
        changed["progress"]![0]!["evidence"]!["selectionId"] = BusinessMessageWire.O("different-winner");
        var another = BusinessMessageSourceCodec.Decode(changed.ToJsonString());
        Assert.Throws<InvalidDataException>(() => BusinessMessageSourceState.ValidateSuccessorShape(delivered, another));
        Assert.Throws<InvalidDataException>(() => BusinessMessageSourceState.ValidateSuccessorShape(delivered, Initial()));
    }

    [Fact]
    public void Retired_payload_remains_distinct_from_JSON_null_and_metadata_never_reopens_owed_planning()
    {
        var pending = Initial(); var expired = Expired(pending); var node = Node(expired);
        node["payloadSlot"] = JsonNode.Parse(BusinessMessageAcceptedHeaderCodec.EncodePayloadSlot(BusinessMessagePayloadSlot.Retired("retire-op", "retire-proof")));
        var retired = BusinessMessageSourceCodec.Decode(node.ToJsonString());
        Assert.Equal(BusinessMessagePayloadState.Retired, retired.PayloadSlot.State);
        Assert.Throws<InvalidOperationException>(() => retired.PayloadSlot.Payload);
        Assert.Equal(expired.Header.SubmittedFingerprint, retired.Header.SubmittedFingerprint);
        Assert.NotEqual(BusinessMessageSourceCodec.Fingerprint(expired), BusinessMessageSourceCodec.Fingerprint(retired));
        Assert.Throws<InvalidDataException>(() => BusinessMessageSourceState.ValidateSuccessorShape(retired, expired));
        Assert.Throws<NotSupportedException>(() => BusinessMessageSourceState.ValidateSuccessorShape(expired, retired));
        var owed = Node(pending); owed["payloadSlot"] = node["payloadSlot"]!.DeepClone();
        Assert.Throws<InvalidDataException>(() => BusinessMessageSourceCodec.Decode(owed.ToJsonString()));
        Assert.Equal(JsonValueKind.Null, pending.PayloadSlot.Payload.ValueKind);
    }

    [Fact]
    public void Planning_rechecks_actual_present_payload_and_current_generation_instead_of_trusting_header_or_work_projections()
    {
        var source = Initial(); var work = BusinessMessageDefinitionWork.Initial(source)[0]; var node = Node(source);
        node["payloadSlot"]!["payload"] = JsonNode.Parse("{\"changed\":true}");
        Assert.Throws<InvalidDataException>(() => BusinessMessageSourceCodec.Decode(node.ToJsonString()));
        node = Node(source); Row(node)["discovery"]!["generation"] = 2; Row(node)["discovery"]!["notBeforeUtcTicks"] = At.AddSeconds(1).UtcTicks;
        var successor = BusinessMessageSourceCodec.Decode(node.ToJsonString());
        BusinessMessageSourceState.ValidateSuccessorShape(source, successor);
        Assert.Throws<InvalidDataException>(() => work.ValidatePlanningBinding(successor));
        var foreign = Initial(messageId: "different-source");
        Assert.Throws<InvalidDataException>(() => work.ValidatePlanningBinding(foreign));
    }

    [Theory]
    [InlineData("same-generation-time")] [InlineData("skip-generation")]
    [InlineData("backwards-generation")] [InlineData("backwards-time")]
    public void Retry_generation_and_NotBefore_cannot_rewrite_an_existing_obligation_or_skip_its_successor(string defect)
    {
        var before = Node(Initial()); var after = Node(Initial());
        if (defect == "same-generation-time") Row(after)["discovery"]!["notBeforeUtcTicks"] = At.AddSeconds(1).UtcTicks;
        else if (defect == "skip-generation")
        { Row(after)["discovery"]!["generation"] = 3; Row(after)["discovery"]!["notBeforeUtcTicks"] = At.AddSeconds(1).UtcTicks; }
        else
        {
            Row(before)["discovery"]!["generation"] = 2; Row(before)["discovery"]!["notBeforeUtcTicks"] = At.AddSeconds(2).UtcTicks;
            Row(after)["discovery"]!["generation"] = defect == "backwards-generation" ? 1 : 3;
            Row(after)["discovery"]!["notBeforeUtcTicks"] = defect == "backwards-generation" ? At.AddSeconds(3).UtcTicks : At.AddSeconds(1).UtcTicks;
        }
        // Both are independently valid read snapshots. The retained retry contract rejects their relationship.
        var prior = BusinessMessageSourceCodec.Decode(before.ToJsonString());
        var next = BusinessMessageSourceCodec.Decode(after.ToJsonString());
        Assert.Throws<InvalidDataException>(() => BusinessMessageSourceState.ValidateSuccessorShape(prior, next));
    }

    [Theory]
    [InlineData("work-id")] [InlineData("generation")] [InlineData("not-before")] [InlineData("definition")]
    public void Changed_work_content_cannot_keep_the_original_derived_work_identity(string change)
    {
        var work = BusinessMessageDefinitionWork.Initial(Initial())[0]; var node = JsonNode.Parse(work.Encode())!.AsObject();
        if (change == "work-id") node["workId"] = "self-asserted-work";
        else if (change == "generation") node["generation"] = 2;
        else if (change == "not-before") node["notBeforeUtcTicks"] = At.AddSeconds(1).UtcTicks;
        else node["definitionId"] = BusinessMessageWire.O("brand-new-definition");
        Assert.Throws<InvalidDataException>(() => BusinessMessageDefinitionWork.Decode(node.ToJsonString()));
    }

    [Fact]
    public void Opaque_ID_frames_and_safe_formatting_preserve_surrogates_NUL_and_component_boundaries()
    {
        var first = Initial(messageId: "message\0\ud800", names: ["definition\udc00"]);
        var original = Assert.Single(BusinessMessageDefinitionWork.Initial(first));
        var roundTrip = BusinessMessageDefinitionWork.Decode(original.Encode());
        Assert.Equal("message\0\ud800", roundTrip.Source.SourceId); Assert.Equal("definition\udc00", roundTrip.Definition.DefinitionId);
        Assert.NotEqual(original.WorkId, Assert.Single(BusinessMessageDefinitionWork.Initial(Initial(messageId: "message\0\ud801", names: ["definition\udc00"]))).WorkId);
        Assert.NotEqual(Assert.Single(BusinessMessageDefinitionWork.Initial(Initial(messageId: "a\0b", names: ["c"]))).WorkId,
            Assert.Single(BusinessMessageDefinitionWork.Initial(Initial(messageId: "a", names: ["b\0c"]))).WorkId);
        Assert.DoesNotContain("message\0\ud800", original.ToString());
        Assert.DoesNotContain("definition\udc00", first.ToString());
    }

    private static BusinessMessageAcceptedHeader Header(int? lifetime = 60, string[]? names = null, string payload = "null", string messageId = "message")
    {
        var context = new RuntimeDeliveryDiscoveryPartition(new("store"), null, WorkflowRunKind.PublishedRun, new("system", "root"));
        var scope = new MessageScope(context.Scope); var submission = new MessageSubmission("payment", "001", Payload(payload), lifetimeSeconds: lifetime);
        var intent = MessageSubmissionRules.CreateIntent(submission.Name, submission.CorrelationKey, submission.Payload, null, lifetime);
        return BusinessMessageAcceptedHeader.Create(messageId, "receipt", context, "producer", "config", submission, intent.Fingerprint,
            new(), At, 1, (names ?? ["accounting", "fulfillment"]).Select(name => MessageDefinitionIdentity.ForWorkflowDefinition(scope, name)).ToArray());
    }
    private static BusinessMessageSourceState Initial(string payload = "null", string messageId = "message", string[]? names = null)
    { var header = Header(payload: payload, messageId: messageId, names: names); return BusinessMessageSourceState.InitialBuffered(header, BusinessMessagePayloadSlot.Present(Payload(payload))); }
    private static JsonElement Payload(string json = "null") { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    private static JsonNode Node(BusinessMessageSourceState source) => JsonNode.Parse(BusinessMessageSourceCodec.Encode(source))!;
    private static JsonObject Row(JsonNode source) => source["progress"]!.AsArray()[0]!.AsObject();
    private static BusinessMessageSourceState Expired(BusinessMessageSourceState source)
    {
        var node = Node(source); foreach (var row in node["progress"]!.AsArray()) SetExpired(row!.AsObject(), source.Header.Receipt.BufferUntil!.Value);
        return BusinessMessageSourceCodec.Decode(node.ToJsonString());
    }
    private static void SetExpired(JsonObject row, DateTimeOffset resolved)
    { row.Remove("discovery"); row["status"] = 3; row["resolvedAtUtcTicks"] = resolved.UtcTicks; }
    private static void SetSelected(JsonObject row, DateTimeOffset selectedAt)
    {
        row.Remove("discovery"); row.Remove("resolvedAtUtcTicks"); row["status"] = 1;
        row["selection"] = new JsonObject { ["id"] = BusinessMessageWire.O("selection"), ["capabilityEpoch"] = 1, ["selectedAtUtcTicks"] = selectedAt.UtcTicks };
    }
    private static void SetTerminal(JsonObject row, BusinessMessageDefinitionStatus status, DateTimeOffset resolvedAt, BusinessMessageEvidenceKind evidence)
    {
        row["status"] = (int)status; row["resolvedAtUtcTicks"] = resolvedAt.UtcTicks;
        row["evidence"] = new JsonObject { ["kind"] = (int)evidence, ["id"] = BusinessMessageWire.O("proof"), ["selectionId"] = BusinessMessageWire.O("selection"), ["capabilityEpoch"] = 1 };
        if (status == BusinessMessageDefinitionStatus.Failed) row["failure"] = 0;
    }
}
