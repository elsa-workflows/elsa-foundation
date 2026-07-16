using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Persistence.Groundwork.Exceptions;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkRuntimeDocumentSerializerTests
{
    private const string Kind = ElsaRuntimeStorageManifest.BookmarkStateDocumentKind;

    private static readonly IGroundworkRuntimeDocumentSerializer Serializer = GroundworkTestSerialization.Serializer;

    // --- Write path: version stamping ---

    [Fact]
    public void Serialize_Stamps_The_Current_Version_Of_The_Kind()
    {
        var (schemaVersion, contentJson) = Serializer.Serialize(Kind, Bookmark());

        Assert.Equal("1", schemaVersion);
        Assert.Contains("\"bookmarkId\"", contentJson);
    }

    [Fact]
    public void Serialize_Throws_For_An_Unknown_Kind()
    {
        Assert.Throws<ArgumentException>(() => Serializer.Serialize("not-a-real-kind", Bookmark()));
    }

    // --- Read path: current version round-trips ---

    [Fact]
    public void Deserialize_RoundTrips_A_Current_Version_Document()
    {
        var state = Bookmark();
        var (schemaVersion, contentJson) = Serializer.Serialize(Kind, state);

        var deserialized = Serializer.Deserialize<BookmarkState>(Envelope(schemaVersion, contentJson));

        Assert.Equal(state.BookmarkId, deserialized.BookmarkId);
        Assert.Equal(state.WorkflowExecutionId, deserialized.WorkflowExecutionId);
        Assert.Equal(state.StimulusType, deserialized.StimulusType);
    }

    [Fact]
    public void Deserialize_Accepts_The_Legacy_Stamp_As_Version_1()
    {
        var (_, contentJson) = Serializer.Serialize(Kind, Bookmark());

        // Every document written before per-kind versioning carries the manifest-wide "1.0.0" stamp.
        var deserialized = Serializer.Deserialize<BookmarkState>(Envelope(ElsaRuntimeDocumentVersions.LegacySchemaVersion, contentJson));

        Assert.Equal("bm-1", deserialized.BookmarkId);
    }

    // --- Read path: version enforcement fails loudly ---

    [Fact]
    public void Deserialize_Throws_On_A_Future_Version_Naming_Kind_Found_And_Supported()
    {
        var (_, contentJson) = Serializer.Serialize(Kind, Bookmark());

        var exception = Assert.Throws<GroundworkRuntimeDocumentVersionException>(
            () => Serializer.Deserialize<BookmarkState>(Envelope("2", contentJson)));

        Assert.Contains(Kind, exception.Message);
        Assert.Contains("2", exception.Message);
        Assert.Contains("1", exception.Message);
    }

    [Theory]
    [InlineData("not-a-version")]
    [InlineData("1.5")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.0")]
    public void Deserialize_Throws_On_An_Unparsable_Version(string schemaVersion)
    {
        var (_, contentJson) = Serializer.Serialize(Kind, Bookmark());

        Assert.Throws<GroundworkRuntimeDocumentVersionException>(
            () => Serializer.Deserialize<BookmarkState>(Envelope(schemaVersion, contentJson)));
    }

    [Fact]
    public void IsCurrentVersion_Is_True_For_The_Current_And_Legacy_Stamps_And_False_For_Others()
    {
        var (_, contentJson) = Serializer.Serialize(Kind, Bookmark());

        Assert.True(Serializer.IsCurrentVersion(Envelope("1", contentJson)));
        Assert.True(Serializer.IsCurrentVersion(Envelope(ElsaRuntimeDocumentVersions.LegacySchemaVersion, contentJson)));
        Assert.False(Serializer.IsCurrentVersion(Envelope("2", contentJson)));
    }

    // --- Upcaster registry: chained upcast across historical versions ---

    [Fact]
    public void Registry_Applies_A_v1_To_v3_Chain_In_Order()
    {
        var registry = Registry(
        [
            new RenameFieldUpcaster("test-thing", fromVersion: 1, "a", "b"),
            new RenameFieldUpcaster("test-thing", fromVersion: 2, "b", "c")
        ]);

        var content = new JsonObject { ["a"] = "value" };
        var upcasted = registry.Upcast("test-thing", fromVersion: 1, toVersion: 3, content);

        Assert.False(upcasted.ContainsKey("a"));
        Assert.False(upcasted.ContainsKey("b"));
        Assert.Equal("value", upcasted["c"]!.GetValue<string>());
    }

    [Fact]
    public void Registry_Leaves_Content_Untouched_When_From_Equals_To()
    {
        var registry = Registry();

        var content = new JsonObject { ["a"] = "value" };
        var result = registry.Upcast("test-thing", fromVersion: 2, toVersion: 2, content);

        Assert.Equal("value", result["a"]!.GetValue<string>());
    }

    // --- Upcaster registry: eager validation at construction, not first use ---

    [Fact]
    public void Registry_Construction_Throws_On_A_Duplicate_FromVersion()
    {
        var exception = Assert.Throws<GroundworkRuntimeDocumentVersionException>(() =>
            new GroundworkRuntimeDocumentUpcasterRegistry(
            [
                new RenameFieldUpcaster("test-thing", fromVersion: 1, "a", "b"),
                new RenameFieldUpcaster("test-thing", fromVersion: 1, "a", "c")
            ]));

        Assert.Contains("test-thing", exception.Message);
    }

    [Fact]
    public void Registry_Construction_Throws_On_A_Chain_Gap()
    {
        // Steps at 1 and 3 with no step at 2: the gap must fail at construction, not on first read.
        var exception = Assert.Throws<GroundworkRuntimeDocumentVersionException>(() =>
            new GroundworkRuntimeDocumentUpcasterRegistry(
            [
                new RenameFieldUpcaster("test-thing", fromVersion: 1, "a", "b"),
                new RenameFieldUpcaster("test-thing", fromVersion: 3, "c", "d")
            ]));

        Assert.Contains("gap", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registry_Construction_Throws_On_A_Step_At_Or_Beyond_A_Known_Kinds_Current_Version()
    {
        // Every real kind is at version 1 today, so any upcaster for one is a signal the current
        // version was not bumped alongside the step.
        Assert.Throws<GroundworkRuntimeDocumentVersionException>(() =>
            new GroundworkRuntimeDocumentUpcasterRegistry(
            [
                new RenameFieldUpcaster(Kind, fromVersion: 1, "a", "b")
            ]));
    }

    [Fact]
    public void Registry_Construction_Throws_On_A_Non_Positive_FromVersion()
    {
        Assert.Throws<GroundworkRuntimeDocumentVersionException>(() =>
            new GroundworkRuntimeDocumentUpcasterRegistry(
            [
                new RenameFieldUpcaster("test-thing", fromVersion: 0, "a", "b")
            ]));
    }

    [Fact]
    public void Registry_With_Only_Production_Upcasters_Passes_Unrelated_Content_Through()
    {
        var registry = Registry();

        var content = new JsonObject { ["a"] = "value" };
        var result = registry.Upcast("test-thing", fromVersion: 1, toVersion: 1, content);

        Assert.Same(content, result);
    }

    [Fact]
    public void PublicationProjectionUpcasters_AreNeutralAndPreserveLegacyTriggerSemantics()
    {
        var registry = Registry();
        var sourceReference = registry.Upcast(
            ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind,
            1,
            2,
            new JsonObject { ["reference"] = new JsonObject() });
        var eventBinding = registry.Upcast(
            ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind,
            1,
            2,
            new JsonObject { ["stimulusType"] = "Event" });
        var httpBinding = registry.Upcast(
            ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind,
            1,
            2,
            new JsonObject { ["stimulusType"] = "HttpEndpoint" });
        var schedule = registry.Upcast(
            ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind,
            1,
            2,
            new JsonObject { ["schedule"] = new JsonObject() });

        var reference = Assert.IsType<JsonObject>(sourceReference["reference"]);
        Assert.True(reference.ContainsKey("publicationId"));
        Assert.Null(reference["publicationId"]);
        Assert.Null(reference["slotId"]);
        Assert.Equal((int)TriggerCardinality.FanOut, eventBinding["cardinality"]!.GetValue<int>());
        Assert.Equal((int)TriggerCardinality.Exclusive, httpBinding["cardinality"]!.GetValue<int>());
        Assert.False(eventBinding["isActive"]!.GetValue<bool>());
        Assert.False(httpBinding["isActive"]!.GetValue<bool>());
        var scheduleState = Assert.IsType<JsonObject>(schedule["schedule"]);
        Assert.Null(scheduleState["publicationId"]);
        Assert.Null(scheduleState["slotId"]);
        Assert.False(scheduleState["isActive"]!.GetValue<bool>());
    }

    [Fact]
    public void WorkflowExecutionStateV1ToV2Upcaster_AddsNeutralClassificationAndSourceProvenance()
    {
        var content = JsonNode.Parse("""{"collection":"workflowExecutionState","state":{"workflowExecutionId":"wf-1"}}""")!.AsObject();

        var result = new WorkflowExecutionStateDocumentV1ToV2Upcaster().Upcast(content);

        var state = Assert.IsType<JsonObject>(result["state"]);
        Assert.Equal(0, state["runKind"]!.GetValue<int>());
        Assert.Null(state["pinnedSource"]);
    }

    [Fact]
    public void WorkflowExecutionStateV2ToV3Upcaster_adds_the_effective_history_timestamp()
    {
        var content = JsonNode.Parse("""{"collection":"workflowExecutionState","state":{"createdAt":"2026-07-13T09:00:00+00:00","startedAt":"2026-07-13T10:00:00+00:00","completedAt":"2026-07-13T11:00:00+00:00","updatedAt":"2026-07-13T12:00:00+00:00"}}""")!.AsObject();

        var result = new WorkflowExecutionStateDocumentV2ToV3Upcaster().Upcast(content);

        Assert.Equal(new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero).UtcTicks, result["historySortTicks"]!.GetValue<long>());
    }

    [Fact]
    public void WorkflowExecutionStateV3ToV4Upcaster_adds_a_neutral_root_variable_frame()
    {
        var content = JsonNode.Parse("""{"state":{"workflowExecutionId":"wf-1"}}""")!.AsObject();

        var result = new WorkflowExecutionStateDocumentV3ToV4Upcaster().Upcast(content);

        var state = Assert.IsType<JsonObject>(result["state"]);
        Assert.True(state.ContainsKey("rootVariableFrame"));
        Assert.Null(state["rootVariableFrame"]);
    }

    [Fact]
    public void WorkflowExecutableV3ToV4Upcaster_adds_neutral_value_flow_fields_recursively()
    {
        var content = JsonNode.Parse("""
            {
              "executable": {
                "rootActivity": {
                  "childSlots": [
                    { "activities": [{ "childSlots": [] }] }
                  ]
                }
              }
            }
            """)!.AsObject();

        var result = new WorkflowExecutableDocumentV3ToV4Upcaster().Upcast(content);

        var root = Assert.IsType<JsonObject>(result["executable"]!["rootActivity"]);
        var child = Assert.IsType<JsonObject>(root["childSlots"]![0]!["activities"]![0]);
        AssertNeutralExecutableNode(root);
        AssertNeutralExecutableNode(child);
    }

    [Fact]
    public void ActivityExecutionStateV2ToV3Upcaster_adds_a_neutral_variable_frame()
    {
        var content = JsonNode.Parse("""{"state":{"invocationId":"ae-1"}}""")!.AsObject();

        var result = new ActivityExecutionStateDocumentV2ToV3Upcaster().Upcast(content);

        var state = Assert.IsType<JsonObject>(result["state"]);
        Assert.True(state.ContainsKey("variableFrame"));
        Assert.Null(state["variableFrame"]);
    }

    [Fact]
    public void ActivityExecutionStateV3ToV4Upcaster_adds_neutral_iteration_frame_fields()
    {
        var content = JsonNode.Parse("""{"state":{"invocationId":"ae-1"}}""")!.AsObject();

        var result = new ActivityExecutionStateDocumentV3ToV4Upcaster().Upcast(content);

        var state = Assert.IsType<JsonObject>(result["state"]);
        Assert.True(state.ContainsKey("iterationVariableFrame"));
        Assert.Null(state["iterationVariableFrame"]);
        Assert.True(state.ContainsKey("iterationFrameRequest"));
        Assert.Null(state["iterationFrameRequest"]);
    }

    [Fact]
    public void ActivityExecutionStateV3ToV4Upcaster_marks_legacy_inflight_iteration_metadata_incompatible()
    {
        var content = JsonNode.Parse("""
            {
              "state": {
                "invocationId": "ae-loop-body",
                "provenance": {
                  "metadata": {
                    "runtime.loop.iterationOwnerNodeId": "node-loop",
                    "runtime.loop.iterationItemName": "currentItem",
                    "runtime.loop.iterationItemValue": "\"alpha\""
                  }
                }
              }
            }
            """)!.AsObject();

        var result = new ActivityExecutionStateDocumentV3ToV4Upcaster().Upcast(content);

        var compatibility = Assert.IsType<JsonObject>(result["state"]!["valueFlowCompatibility"]);
        Assert.False(compatibility["isCompatible"]!.GetValue<bool>());
        Assert.Equal("VF-ACT-009", compatibility["incompatibilityCode"]!.GetValue<string>());
        Assert.Contains("typed iteration frame", compatibility["reason"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Activity_iteration_frame_roundtrips_with_populated_protected_values()
    {
        var policy = new ValueProtectionPolicy(
            DurableValueLifecycle.Instance,
            DurableValueStorage.Inline,
            isSensitive: true,
            requiresEncryption: true,
            redactionMode: "mask");
        var values = new Dictionary<string, ValueEnvelope>
        {
            ["currentItem"] = ValueEnvelope.Inline(
                new ValueTypeDescriptor("String"),
                JsonSerializer.SerializeToElement("secret"),
                policy)
        };
        var parent = new VariableFrameFactory().CreateRoot("wf-1", "workflow", new Dictionary<string, ValueEnvelope>());
        var frame = new VariableFrameFactory().CreateIteration("node-loop", "ae-body", "iteration-1", parent, values);
        var document = new ActivityStateDocument("wf-1", ActivityState("ae-body") with { IterationVariableFrame = frame });

        var (version, content) = Serializer.Serialize(ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind, document);
        var restored = Serializer.Deserialize<ActivityStateDocument>(new DocumentEnvelope(
            ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind,
            "wf-1:ae-body",
            version,
            1,
            content,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch));

        var item = restored.State.IterationVariableFrame!.Values["currentItem"];
        Assert.Equal("secret", item.InlineValue!.Value.GetString());
        Assert.Equal(policy.Lifecycle, item.Policy.Lifecycle);
        Assert.Equal(policy.Storage, item.Policy.Storage);
        Assert.Equal(policy.IsSensitive, item.Policy.IsSensitive);
        Assert.Equal(policy.RequiresEncryption, item.Policy.RequiresEncryption);
        Assert.Equal(policy.RedactionMode, item.Policy.RedactionMode);
        Assert.Equal(frame.ParentFrameId, restored.State.IterationVariableFrame.ParentFrameId);
    }

    [Fact]
    public void Activity_iteration_frame_request_roundtrips_with_populated_values()
    {
        var request = new LoopIterationScopeRequest(
            "node-loop",
            "iteration-1",
            new Dictionary<string, ValueEnvelope>
            {
                ["currentIndex"] = ValueEnvelope.Inline(
                    new ValueTypeDescriptor("Int32"),
                    JsonSerializer.SerializeToElement(1),
                    ValueProtectionPolicy.InstanceInline)
            });
        var document = new ActivityStateDocument("wf-1", ActivityState("ae-pending") with { IterationFrameRequest = request });

        var (version, content) = Serializer.Serialize(ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind, document);
        var restored = Serializer.Deserialize<ActivityStateDocument>(new DocumentEnvelope(
            ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind,
            "wf-1:ae-pending",
            version,
            1,
            content,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch));

        Assert.Equal("node-loop", restored.State.IterationFrameRequest!.OwnerNodeId);
        Assert.Equal(1, restored.State.IterationFrameRequest.Values["currentIndex"].InlineValue!.Value.GetInt32());
    }

    [Fact]
    public void DurableTimerV1ToV2Upcaster_adds_neutral_typed_payload_metadata()
    {
        var content = JsonNode.Parse("""{"timer":{"timerId":"timer-1"}}""")!.AsObject();

        var result = new DurableTimerDocumentV1ToV2Upcaster().Upcast(content);

        var timer = Assert.IsType<JsonObject>(result["timer"]);
        Assert.True(timer.ContainsKey("payloadType"));
        Assert.Null(timer["payloadType"]);
        Assert.True(timer.ContainsKey("providerId"));
        Assert.Null(timer["providerId"]);
    }

    private static GroundworkRuntimeDocumentUpcasterRegistry Registry(
        params IGroundworkRuntimeDocumentUpcaster[] additional) =>
        new(
        [
            new WorkflowExecutableDocumentV1ToV2Upcaster(),
            new WorkflowExecutableDocumentV2ToV3Upcaster(),
            new WorkflowExecutableDocumentV3ToV4Upcaster(),
            new ActivityExecutionStateDocumentV1ToV2Upcaster(),
            new ActivityExecutionStateDocumentV2ToV3Upcaster(),
            new ActivityExecutionStateDocumentV3ToV4Upcaster(),
            new WorkflowExecutionStateDocumentV1ToV2Upcaster(),
            new WorkflowExecutionStateDocumentV2ToV3Upcaster(),
            new WorkflowExecutionStateDocumentV3ToV4Upcaster(),
            new DurableTimerDocumentV1ToV2Upcaster(),
            new WorkflowExecutableSourceReferenceDocumentV1ToV2Upcaster(),
            new WorkflowTriggerBindingDocumentV1ToV2Upcaster(),
            new RecurringTriggerScheduleDocumentV1ToV2Upcaster(),
            .. additional
        ]);

    private static void AssertNeutralExecutableNode(JsonObject node)
    {
        Assert.True(node.ContainsKey("activityContract"));
        Assert.Null(node["activityContract"]);
        Assert.True(node.ContainsKey("intrinsicKind"));
        Assert.Null(node["intrinsicKind"]);
        Assert.True(node.ContainsKey("intrinsicVariable"));
        Assert.Null(node["intrinsicVariable"]);
    }

    private static BookmarkState Bookmark() => new(
        BookmarkId: "bm-1",
        WorkflowExecutionId: "wf-1",
        ActivityExecutionId: "ae-1",
        ExecutableNodeId: "node-1",
        ResumeTargetId: "resume-1",
        StimulusType: "Http",
        StimulusHash: "hash-1",
        Payload: JsonSerializer.SerializeToElement(new { url = "/orders" }),
        Metadata: new Dictionary<string, string> { ["tag"] = "v1" },
        CreatedAt: DateTimeOffset.UnixEpoch,
        ExpiresAt: null);

    private static ActivityExecutionState ActivityState(string activityExecutionId) => new(
        new ActivityExecution(activityExecutionId, "wf-1", "node-body", "authored-body", "test/body", "1.0.0"),
        ActivityExecutionStatus.Scheduled,
        null,
        DateTimeOffset.UnixEpoch,
        null,
        null,
        "ae-loop",
        "ae-loop",
        null,
        "iteration-1",
        0,
        [],
        [],
        0,
        0,
        new Dictionary<string, string>());

    private sealed record ActivityStateDocument(string WorkflowExecutionId, ActivityExecutionState State);

    private static DocumentEnvelope Envelope(string schemaVersion, string contentJson) =>
        new(Kind, "bm-1", schemaVersion, 1, contentJson, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    // Synthetic upcaster: renames a top-level field, standing in for a real schema change so the
    // registry's chaining and validation can be exercised without a production version bump.
    private sealed class RenameFieldUpcaster(string documentKind, int fromVersion, string oldName, string newName)
        : IGroundworkRuntimeDocumentUpcaster
    {
        public string DocumentKind { get; } = documentKind;
        public int FromVersion { get; } = fromVersion;

        public JsonObject Upcast(JsonObject content)
        {
            if (content.TryGetPropertyValue(oldName, out var value))
            {
                content.Remove(oldName);
                content[newName] = value?.DeepClone();
            }

            return content;
        }
    }
}
