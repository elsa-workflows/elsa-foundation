using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Serialization;
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
        Assert.Contains("\"stimulusLookupKey\"", contentJson);
        Assert.Contains("\"stimulusTypeLookupKey\"", contentJson);
    }

    [Fact]
    public void Serialize_Throws_For_An_Unknown_Kind()
    {
        var exception = Assert.Throws<DocumentSchemaVersionException>(() =>
            Serializer.Serialize("not-a-real-kind", Bookmark()));

        Assert.Equal(DocumentSchemaVersionFailure.UnknownDocumentKind, exception.Failure);
        Assert.Equal("not-a-real-kind", exception.DocumentKind);
    }

    [Theory]
    [InlineData(ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind)]
    [InlineData(ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind)]
    public void Version_4_Runtime_Kinds_Use_A_Clean_Baseline(string kind)
    {
        Assert.Equal(4, ElsaRuntimeDocumentVersions.CurrentFor(kind));
        Assert.Equal(4, ElsaRuntimeDocumentVersions.MinimumReadableFor(kind));
    }

    [Fact]
    public void WorkflowExecutable_Uses_A_Clean_V9_Baseline()
    {
        // v9 adds the exact operator-scheduling capability compiled for runtime alterations (#1016).
        Assert.Equal(9, ElsaRuntimeDocumentVersions.CurrentFor(ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind));
        Assert.Equal(9, ElsaRuntimeDocumentVersions.MinimumReadableFor(ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind));
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

    // --- Read path: version enforcement fails loudly ---

    [Fact]
    public void Deserialize_Throws_On_A_Future_Version_Naming_Kind_Found_And_Supported()
    {
        var (_, contentJson) = Serializer.Serialize(Kind, Bookmark());

        var exception = Assert.Throws<DocumentSchemaVersionException>(
            () => Serializer.Deserialize<BookmarkState>(Envelope("2", contentJson)));

        Assert.Equal(DocumentSchemaVersionFailure.Future, exception.Failure);
        Assert.Equal(Kind, exception.DocumentKind);
        Assert.Equal(2, exception.ParsedVersion);
        Assert.Equal(1, exception.CurrentVersion);
    }

    public static TheoryData<string, string> UnsupportedHistoricalVersions()
    {
        var data = new TheoryData<string, string>();
        foreach (var documentKind in ElsaRuntimeDocumentVersions.All.Keys)
        {
            var minimumReadableVersion = ElsaRuntimeDocumentVersions.MinimumReadableFor(documentKind);
            if (minimumReadableVersion == 1)
                continue;

            for (var version = 1; version < minimumReadableVersion; version++)
                data.Add(documentKind, ElsaRuntimeDocumentVersions.Stamp(version));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(UnsupportedHistoricalVersions))]
    public void Deserialize_Rejects_Versions_Below_A_Kinds_Minimum_Readable_Boundary(string documentKind, string schemaVersion)
    {
        var exception = Assert.Throws<DocumentSchemaVersionException>(
            () => Serializer.Deserialize<JsonObject>(Envelope(documentKind, schemaVersion, "not-json")));

        Assert.Equal(DocumentSchemaVersionFailure.TooOld, exception.Failure);
        Assert.Equal(documentKind, exception.DocumentKind);
        Assert.Equal(ElsaRuntimeDocumentVersions.MinimumReadableFor(documentKind), exception.MinimumReadableVersion);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData("not-a-version")]
    [InlineData("1.5")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.0")]
    [InlineData("1.0.0")]
    public void Deserialize_Throws_On_An_Unparsable_Version(string schemaVersion)
    {
        var (_, contentJson) = Serializer.Serialize(Kind, Bookmark());

        var exception = Assert.Throws<DocumentSchemaVersionException>(
            () => Serializer.Deserialize<BookmarkState>(Envelope(schemaVersion, contentJson)));

        Assert.Equal(DocumentSchemaVersionFailure.MalformedStamp, exception.Failure);
        Assert.Equal(schemaVersion, exception.SchemaVersion);
    }

    [Fact]
    public void IsCurrentVersion_Is_True_For_The_Current_Stamp_And_False_For_Other_Recognized_Versions()
    {
        var (_, contentJson) = Serializer.Serialize(Kind, Bookmark());

        Assert.True(Serializer.IsCurrentVersion(Envelope("1", contentJson)));
        Assert.False(Serializer.IsCurrentVersion(Envelope(
            ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
            "1",
            contentJson)));
        Assert.False(Serializer.IsCurrentVersion(Envelope("2", contentJson)));
    }

    [Fact]
    public void ExecutableActivityTemplateV1ToV2Upcaster_rewrites_nested_descriptors_recursively()
    {
        var content = JsonNode.Parse("""
            {
              "template": {
                "root": {
                  "descriptor": {
                    "consumerKey": "root-consumer",
                    "schemaVersion": "1",
                    "payload": { "kind": "root" }
                  },
                  "childSlots": [
                    {
                      "activities": [
                        {
                          "descriptor": {
                            "consumerKey": "child-consumer",
                            "schemaVersion": "2",
                            "payload": { "kind": "child" }
                          }
                        }
                      ]
                    }
                  ]
                }
              }
            }
            """)!.AsObject();

        var result = new ExecutableActivityTemplateDocumentV1ToV2Upcaster().Upcast(content);
        var root = Assert.IsType<JsonObject>(result["template"]!["root"]);
        var child = Assert.IsType<JsonObject>(root["childSlots"]![0]!["activities"]![0]);

        Assert.Null(root["descriptor"]);
        Assert.Equal("root-consumer", root["descriptorType"]!.GetValue<string>());
        Assert.Equal("1", root["descriptorSchemaVersion"]!.GetValue<string>());
        Assert.Equal("root", root["descriptorPayload"]!["kind"]!.GetValue<string>());
        Assert.Null(child["descriptor"]);
        Assert.Equal("child-consumer", child["descriptorType"]!.GetValue<string>());
        Assert.Equal("2", child["descriptorSchemaVersion"]!.GetValue<string>());
        Assert.Equal("child", child["descriptorPayload"]!["kind"]!.GetValue<string>());
    }

    [Fact]
    public void Frozen_v5_codec_rejects_a_v6_workflow_executable_before_deserialization()
    {
        const string kind = ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind;
        var codec = new VersionedJsonDocumentCodec(
            [new DocumentSchemaVersionPolicy(kind, 5, 5)],
            [],
            new DocumentSchemaVersionFormat(
                (documentKind, schemaVersion) => ElsaRuntimeDocumentVersions.Parse(documentKind, schemaVersion),
                (_, version) => ElsaRuntimeDocumentVersions.Stamp(version)));

        var exception = Assert.Throws<DocumentSchemaVersionException>(
            () => codec.Deserialize<JsonObject>(Envelope(kind, "6", "not-json")));

        Assert.Equal(DocumentSchemaVersionFailure.Future, exception.Failure);
        Assert.Equal(kind, exception.DocumentKind);
        Assert.Equal(6, exception.ParsedVersion);
        Assert.Equal(5, exception.CurrentVersion);
        Assert.Null(exception.InnerException);
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

    private static DocumentEnvelope Envelope(string schemaVersion, string contentJson) =>
        Envelope(Kind, schemaVersion, contentJson);

    private static DocumentEnvelope Envelope(string documentKind, string schemaVersion, string contentJson) =>
        new(documentKind, "document-1", schemaVersion, 1, contentJson, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
}
