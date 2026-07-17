using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Golden-fixture drift and backward-compatibility tests for the runtime persistence bridge (W3).
/// </summary>
/// <remarks>
/// The drift test freezes the serialized shape of every runtime document kind: it compares the JSON the
/// real store bridge writes today against the fixture for that kind's current version and fails when a
/// shape changes without a version bump. The compatibility test proves every v1 fixture still loads
/// through the real read path under the legacy pre-versioning schema stamp and its production upcaster chain.
/// </remarks>
public sealed class GroundworkRuntimeDocumentFixtureTests
{
    [Fact]
    public void Workflow_execution_state_shape_is_explicitly_versioned_at_v4() =>
        Assert.Equal(
            4,
            Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.CurrentFor(
                ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind));

    [Fact]
    public void Workflow_executable_shape_is_explicitly_versioned_at_v6() =>
        Assert.Equal(
            6,
            Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.CurrentFor(
                ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind));

    [Fact]
    public void Activity_execution_state_shape_is_explicitly_versioned_at_v4() =>
        Assert.Equal(
            4,
            Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.CurrentFor(
                ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind));

    [Fact]
    public void Durable_timer_shape_is_explicitly_versioned_at_v2() =>
        Assert.Equal(
            2,
            Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.CurrentFor(
                ElsaRuntimeStorageManifest.DurableTimerDocumentKind));

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

        Assert.Equal(currentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), schemaVersion);

        if (Regenerate)
        {
            WriteFixtureToSource(kind, currentVersion, contentJson);
            return;
        }

        var expected = ReadCommittedFixture(kind, currentVersion);
        AssertJsonSemanticallyEqual(expected, contentJson, kind, currentVersion);
    }

    [Fact]
    public void Historical_workflow_executable_v4_fixture_upcasts_to_the_current_v5_fixture()
    {
        const string kind = ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind;
        var historical = JsonNode.Parse(ReadCommittedFixture(kind, 4))!.AsObject();

        var upcasted = GroundworkTestSerialization.UpcasterRegistry.Upcast(kind, 4, 5, historical);

        AssertJsonSemanticallyEqual(ReadCommittedFixture(kind, 5), upcasted.ToJsonString(), kind, 5);
    }

    [Fact]
    public async Task Current_worker_loads_v5_unknown_and_v6_explicit_input_nullability()
    {
        var legacy = await LoadWorkflowExecutableFixtureAsync(5, "workflowExecutable-input-contract.json");
        var current = await LoadWorkflowExecutableFixtureAsync(6, "workflowExecutable.json");

        Assert.NotNull(legacy.RootActivity.ActivityContract);
        Assert.NotNull(current.RootActivity.ChildSlots.Single().Activities.Single().ActivityContract);
        var legacyInput = Assert.Single(legacy.RootActivity.ActivityContract!.Inputs).Value;
        var currentInput = Assert.Single(current.RootActivity.ChildSlots.Single().Activities.Single().ActivityContract!.Inputs).Value;
        Assert.Null(legacyInput.IsNullable);
        Assert.False(currentInput.IsNullable);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Committed_Fixture_Loads_Through_The_Bridge_Under_The_Legacy_Stamp(string kind)
    {
        if (Regenerate)
            return;

        // Workflow executables carrying the removed generic value bindings are an intentional major-version
        // incompatibility. They must be translated by the Elsa 3 importer, not revived in canonical runtime
        // persistence through an upcaster or compatibility model.
        if (kind == ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind)
            return;

        var fixtureContent = ReadCommittedFixture(kind, 1);

        // Seed the committed fixture under the pre-versioning "1.0.0" stamp, exactly as a document written
        // before per-kind versioning would carry it, then read it back through the real store bridge.
        var store = await GroundworkRuntimeDocumentFixtureFactory.SeedLegacyFixtureAsync(kind, fixtureContent);

        var spot = await GroundworkRuntimeDocumentFixtureFactory.ReadSpotCheckAsync(kind, store);

        Assert.NotNull(spot);
        Assert.Equal(GroundworkRuntimeDocumentFixtureFactory.ExpectedSpotValue(kind), spot);
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
            "  2. register an IGroundworkRuntimeDocumentUpcaster for the previous version,\n" +
            "  3. add a new golden fixture for the new version (run with GROUNDWORK_FIXTURE_REGEN=1), and\n" +
            "  4. keep the old fixture so historical documents still load.\n\n" +
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
        return ReadCommittedFixtureFile(version, kind + ".json");
    }

    private static string ReadCommittedFixtureFile(int version, string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", $"v{version}", fileName);
        Assert.True(
            File.Exists(path),
            $"Missing committed golden fixture at '{path}'. " +
            "Run this project with GROUNDWORK_FIXTURE_REGEN=1 to generate it.");
        return File.ReadAllText(path);
    }

    private static async Task<WorkflowExecutable> LoadWorkflowExecutableFixtureAsync(int version, string fileName)
    {
        const string kind = ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind;
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        await store.SaveAsync(new SaveDocumentRequest(
            kind,
            "artifact-1",
            ElsaRuntimeDocumentVersions.Stamp(version),
            ReadCommittedFixtureFile(version, fileName)));

        return await new GroundworkWorkflowExecutableStore(store, GroundworkTestSerialization.Serializer)
                   .FindAsync("artifact-1")
               ?? throw new InvalidOperationException($"Workflow executable fixture v{version}/{fileName} did not load.");
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
