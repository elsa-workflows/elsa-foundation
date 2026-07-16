using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Persistence.Groundwork.Exceptions;
using Elsa.Persistence.Groundwork.Serialization;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Golden-fixture drift and backward-compatibility tests for the runtime persistence bridge (W3).
/// </summary>
/// <remarks>
/// The drift test freezes the serialized shape of every runtime document kind: it compares the JSON the
/// real store bridge writes today against the fixture for that kind's current version and fails when a
/// shape changes without a version bump. Historical fixtures either load through the real read path or,
/// for an explicit clean break, are retained as evidence that the retired wire format is rejected.
/// </remarks>
public sealed class GroundworkRuntimeDocumentFixtureTests
{
    [Fact]
    public void Workflow_execution_state_shape_is_explicitly_versioned_at_v3() =>
        Assert.Equal(
            3,
            Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.CurrentFor(
                ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind));

    [Theory]
    [InlineData(ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind)]
    [InlineData(ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind)]
    public void Input_evidence_shapes_are_explicitly_versioned_at_v3(string documentKind) =>
        Assert.Equal(3, Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.CurrentFor(documentKind));

    // Set GROUNDWORK_FIXTURE_REGEN=1 and run this project to (re)write the committed fixtures into the
    // source tree after an intentional version bump. Off by default so a normal run only compares.
    private static readonly bool Regenerate =
        Environment.GetEnvironmentVariable("GROUNDWORK_FIXTURE_REGEN") == "1";

    public static TheoryData<string> Kinds()
    {
        var data = new TheoryData<string>();
        foreach (var kind in GroundworkRuntimeDocumentFixtureFactory.AllKinds)
            data.Add(kind);
        return data;
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Fixture_Matches_What_The_Bridge_Writes_Today(string kind)
    {
        var (schemaVersion, contentJson) = await GroundworkRuntimeDocumentFixtureFactory.CaptureAsync(kind);
        var currentVersion = Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.CurrentFor(kind);

        Assert.Equal(ElsaRuntimeDocumentVersions.Stamp(currentVersion), schemaVersion);

        if (Regenerate)
        {
            WriteFixtureToSource(kind, currentVersion, contentJson);
            return;
        }

        var expected = ReadCommittedFixture(kind, currentVersion);
        AssertJsonSemanticallyEqual(expected, contentJson, kind, currentVersion);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Historical_V1_Fixture_Is_Loaded_Or_Explicitly_Rejected_At_A_CleanBreak(string kind)
    {
        if (Regenerate)
            return;

        var fixtureContent = ReadCommittedFixture(kind, 1);

        // Seed the committed fixture under the pre-versioning "1.0.0" stamp, exactly as a document written
        // before per-kind versioning would carry it, then read it back through the real store bridge.
        var store = await GroundworkRuntimeDocumentFixtureFactory.SeedLegacyFixtureAsync(kind, fixtureContent);

        if (ElsaRuntimeDocumentVersions.MinimumReadableFor(kind) == 1)
        {
            var spot = await GroundworkRuntimeDocumentFixtureFactory.ReadSpotCheckAsync(kind, store);
            Assert.NotNull(spot);
            Assert.Equal(GroundworkRuntimeDocumentFixtureFactory.ExpectedSpotValue(kind), spot);
            return;
        }

        var exception = await Assert.ThrowsAsync<GroundworkRuntimeDocumentVersionException>(async () =>
            await GroundworkRuntimeDocumentFixtureFactory.ReadSpotCheckAsync(kind, store));
        Assert.Contains("clean-break", exception.Message, StringComparison.Ordinal);
        Assert.Contains("version 1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("minimum readable version is 2", exception.Message, StringComparison.Ordinal);
    }

    // --- Semantic JSON comparison ---

    // Golden fixtures are compared semantically, not byte-for-byte: incidental formatting or property
    // ordering differences must not fail, but any field added, renamed, removed, or changed in value must.
    // Both sides are normalized to a canonical form (object properties recursively sorted by name, then
    // re-serialized) and compared as strings, which yields a readable diff on mismatch.
    private static void AssertJsonSemanticallyEqual(string expectedJson, string actualJson, string kind, int version)
    {
        var expected = JsonNode.Parse(expectedJson);
        var actual = JsonNode.Parse(actualJson);

        if (JsonNode.DeepEquals(expected, actual))
            return;

        var expectedCanonical = Canonicalize(expected);
        var actualCanonical = Canonicalize(actual);

        Assert.Fail(
            $"The serialized shape of runtime document kind '{kind}' no longer matches its committed golden fixture " +
            $"(Fixtures/v{version}/{kind}.json).\n\n" +
            "A state record shape changed. To evolve a runtime document shape you must, in the same change:\n" +
            "  1. bump that kind's version in ElsaRuntimeDocumentVersions,\n" +
            "  2. register an IGroundworkRuntimeDocumentUpcaster for each supported historical step, or explicitly advance the clean-break floor,\n" +
            "  3. add a golden fixture for the new version (run with GROUNDWORK_FIXTURE_REGEN=1), and\n" +
            "  4. retain historical fixtures as load or rejection evidence.\n\n" +
            $"Expected (committed fixture, canonical):\n{expectedCanonical}\n\n" +
            $"Actual (written by the bridge today, canonical):\n{actualCanonical}");
    }

    private static string Canonicalize(JsonNode? node)
    {
        var sorted = SortRecursively(node);
        return sorted?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
    }

    private static JsonNode? SortRecursively(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var result = new JsonObject();
                foreach (var property in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                    result[property.Key] = SortRecursively(property.Value?.DeepClone());
                return result;
            case JsonArray array:
                var newArray = new JsonArray();
                foreach (var item in array)
                    newArray.Add(SortRecursively(item?.DeepClone()));
                return newArray;
            default:
                return node?.DeepClone();
        }
    }

    // --- Fixture file access ---

    private static string ReadCommittedFixture(string kind, int version)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", $"v{version}", kind + ".json");
        Assert.True(
            File.Exists(path),
            $"Missing committed golden fixture for kind '{kind}' at '{path}'. " +
            "Run this project with GROUNDWORK_FIXTURE_REGEN=1 to generate it.");
        return File.ReadAllText(path);
    }

    private static void WriteFixtureToSource(string kind, int version, string contentJson)
    {
        var directory = Path.Combine(SourceDirectory(), "Fixtures", $"v{version}");
        Directory.CreateDirectory(directory);
        var canonical = Canonicalize(JsonNode.Parse(contentJson));
        File.WriteAllText(Path.Combine(directory, kind + ".json"), canonical);
    }

    private static string SourceDirectory([CallerFilePath] string? callerFilePath = null) =>
        Path.GetDirectoryName(callerFilePath)!;
}
