using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Persistence.Groundwork.Exceptions;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Models;
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

    [Theory]
    [InlineData(ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind)]
    [InlineData(ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind)]
    public void StableDescriptorAndLayoutKinds_Keep_The_CleanBreak_Floor_While_Advancing_To_Version_3(string kind)
    {
        Assert.Equal(3, ElsaRuntimeDocumentVersions.CurrentFor(kind));
        Assert.Equal(2, ElsaRuntimeDocumentVersions.MinimumReadableFor(kind));
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

    [Fact]
    public void Deserialize_Rejects_A_PreConsumerKey_WorkflowExecutable_At_The_CleanBreak_Floor()
    {
        var envelope = new DocumentEnvelope(
            ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind,
            "artifact-legacy",
            ElsaRuntimeDocumentVersions.LegacySchemaVersion,
            1,
            "{}",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

        var exception = Assert.Throws<GroundworkRuntimeDocumentVersionException>(
            () => Serializer.Deserialize<WorkflowExecutable>(envelope));

        Assert.Contains("clean-break", exception.Message, StringComparison.Ordinal);
        Assert.Contains("minimum readable version is 2", exception.Message, StringComparison.Ordinal);
        Assert.Contains("No compatibility upcaster is permitted", exception.Message, StringComparison.Ordinal);
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
        // Bookmark state remains at version 1, so any upcaster for it is a signal the current
        // version was not bumped alongside the step.
        Assert.Throws<GroundworkRuntimeDocumentVersionException>(() =>
            new GroundworkRuntimeDocumentUpcasterRegistry(
            [
                new RenameFieldUpcaster(Kind, fromVersion: 1, "a", "b")
            ]));
    }

    [Fact]
    public void Registry_Construction_Rejects_An_Upcaster_Below_A_CleanBreak_Floor()
    {
        var exception = Assert.Throws<GroundworkRuntimeDocumentVersionException>(() =>
            new GroundworkRuntimeDocumentUpcasterRegistry(
            [
                new RenameFieldUpcaster(ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind, fromVersion: 1, "descriptorType", "descriptor")
            ]));

        Assert.Contains("clean-break minimum readable version 2", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not permitted", exception.Message, StringComparison.Ordinal);
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
    public void WorkflowExecutableV2ToV3Upcaster_adds_input_identity_and_sensitivity_recursively()
    {
        var content = JsonNode.Parse("""
            {
              "executable": {
                "rootActivity": {
                  "executableNodeId": "root",
                  "inputBindings": {
                    "rootInput": { "inputName": "rootInput" }
                  },
                  "childSlots": [
                    {
                      "activities": [
                        {
                          "executableNodeId": "child",
                          "inputBindings": {
                            "childInput": { "inputName": "childInput" }
                          },
                          "childSlots": []
                        }
                      ]
                    }
                  ]
                }
              }
            }
            """)!.AsObject();

        var result = new WorkflowExecutableDocumentV2ToV3Upcaster().Upcast(content);

        var root = result["executable"]!["rootActivity"]!;
        var rootBinding = root["inputBindings"]!["rootInput"]!;
        Assert.Equal("rootInput", rootBinding["inputKey"]!.GetValue<string>());
        Assert.False(rootBinding["isSensitive"]!.GetValue<bool>());

        var childBinding = root["childSlots"]![0]!["activities"]![0]!["inputBindings"]!["childInput"]!;
        Assert.Equal("childInput", childBinding["inputKey"]!.GetValue<string>());
        Assert.False(childBinding["isSensitive"]!.GetValue<bool>());
    }

    [Fact]
    public void WorkflowExecutableSourceReferenceV2ToV3Upcaster_adds_empty_authored_inputs()
    {
        var content = JsonNode.Parse("""{"reference":{"sourceReferenceId":"reference-1"}}""")!.AsObject();

        var result = new WorkflowExecutableSourceReferenceDocumentV2ToV3Upcaster().Upcast(content);

        Assert.Empty(Assert.IsType<JsonArray>(result["reference"]!["authoredInputs"]));
    }

    private static GroundworkRuntimeDocumentUpcasterRegistry Registry(
        params IGroundworkRuntimeDocumentUpcaster[] additional) =>
        new(
        [
            new ExecutionScopeAttemptDocumentUpcaster(ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind),
            new ExecutionScopeAttemptDocumentUpcaster(ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind),
            new ExecutionScopeAttemptDocumentUpcaster(ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind),
            new WorkflowExecutableDocumentV2ToV3Upcaster(),
            new WorkflowExecutionStateDocumentV1ToV2Upcaster(),
            new WorkflowExecutionStateDocumentV2ToV3Upcaster(),
            new WorkflowExecutableSourceReferenceDocumentV2ToV3Upcaster(),
            new WorkflowTriggerBindingDocumentV1ToV2Upcaster(),
            new RecurringTriggerScheduleDocumentV1ToV2Upcaster(),
            .. additional
        ]);

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
