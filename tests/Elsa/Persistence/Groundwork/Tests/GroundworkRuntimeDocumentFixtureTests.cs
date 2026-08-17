using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Golden-fixture drift and supported-compatibility tests for the runtime persistence bridge (W3).
/// </summary>
/// <remarks>
/// The drift test freezes the serialized shape of every runtime document kind: it compares the JSON the
/// real store bridge writes today against the fixture for that kind's current version and fails when a
/// shape changes without a version bump. The read-path test loads every supported generation; every kind
/// except explicitly retained rolling windows supports only its clean current baseline. Executable activity
/// templates retain v1-to-v2 and post-commit outbox documents retain v3-to-v4.
///
/// Spec 151 / T036 closed the Source Reference v4-to-v5 window. T033 renamed the persisted activation
/// identity (<c>publicationId</c> -&gt; <c>activationId</c>), so a v4 document names a field this build no
/// longer reads and would have deserialized the activation identity as null. The clean break advances the
/// minimum-readable baseline to the current version, drops the upcaster, and deletes the v4 fixture.
/// </remarks>
public sealed class GroundworkRuntimeDocumentFixtureTests
{
    [Fact]
    public void Every_document_kind_uses_its_declared_readable_baseline()
    {
        Assert.All(
            Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.All,
            pair =>
            {
                var minimumReadable = Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.MinimumReadableFor(pair.Key);
                var expected = pair.Key switch
                {
                    ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind => 1,
                    ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind => 3,
                    _ => pair.Value
                };
                Assert.Equal(expected, minimumReadable);
            });
    }

    [Fact]
    public void Dispatch_dependency_shapes_are_explicitly_versioned()
    {
        Assert.Equal(
            4,
            Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.CurrentFor(
                ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind));
        Assert.Equal(
            9,
            Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.CurrentFor(
                ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind));
        Assert.Equal(
            5,
            Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.CurrentFor(
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind));
    }

    [Fact]
    public void Workflow_execution_state_uses_a_clean_v4_contract()
    {
        Assert.Equal(4, Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.CurrentFor(
            ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind));
        Assert.Equal(4, Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.MinimumReadableFor(
            ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind));
    }

    [Fact]
    public void Source_reference_declares_a_clean_v5_baseline()
    {
        // Spec 151 / T036: the v4-to-v5 read window closed when T033 renamed the persisted activation
        // identity. v4 documents are rejected loudly rather than read with a null ActivationId.
        Assert.Equal(5, Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.CurrentFor(
            ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind));
        Assert.Equal(5, Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.MinimumReadableFor(
            ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind));
    }

    [Fact]
    public void Workflow_executable_declares_a_clean_v9_baseline()
    {
        // v9 adds the exact operator-scheduling capability compiled for runtime alterations (#1016).
        Assert.Equal(
            9,
            Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.CurrentFor(
                ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind));
        Assert.Equal(
            9,
            Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.MinimumReadableFor(
                ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind));
    }

    [Fact]
    public void Incident_state_declares_a_clean_v2_baseline()
    {
        Assert.Equal(
            2,
            Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.CurrentFor(
                ElsaRuntimeStorageManifest.IncidentStateDocumentKind));
        Assert.Equal(
            2,
            Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.MinimumReadableFor(
                ElsaRuntimeStorageManifest.IncidentStateDocumentKind));
    }

    [Fact]
    public void Post_commit_outbox_shape_is_explicitly_versioned_at_v4() =>
        Assert.Equal(
            4,
            Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.CurrentFor(
                ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind));

    [Fact]
    public void Scheduler_work_claim_shape_is_explicitly_versioned_at_v3() =>
        Assert.Equal(
            3,
            Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.CurrentFor(
                ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind));

    [Fact]
    public void Durable_timer_claim_shape_is_explicitly_versioned_at_v2() =>
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

    public static TheoryData<string, int> SupportedFixtureVersions()
    {
        var data = new TheoryData<string, int>();
        foreach (var kind in GroundworkRuntimeDocumentFixtureFactory.AllKinds)
        {
            var minimum = Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.MinimumReadableFor(kind);
            var current = Elsa.Persistence.Groundwork.Serialization.ElsaRuntimeDocumentVersions.CurrentFor(kind);
            for (var version = minimum; version <= current; version++)
                data.Add(kind, version);
        }

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
    [MemberData(nameof(SupportedFixtureVersions))]
    public async Task Every_Supported_Fixture_Version_Loads_Through_The_Bridge(string kind, int version)
    {
        if (Regenerate)
            return;

        var fixtureContent = ReadCommittedFixture(kind, version);
        var store = await GroundworkRuntimeDocumentFixtureFactory.SeedFixtureAsync(kind, version, fixtureContent);

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
            "A state record shape changed. After GA, compatible evolution requires, in the same change:\n" +
            "  1. bump that kind's version in ElsaRuntimeDocumentVersions,\n" +
            "  2. register a Groundwork IDocumentJsonUpcaster for the previous version,\n" +
            "  3. add a new golden fixture for the new version (run with GROUNDWORK_FIXTURE_REGEN=1), and\n" +
            "  4. keep every supported historical fixture.\n" +
            "For a clean break, replace the current fixture, advance minimum-readable, and document the " +
            "required datastore reset. For a supported rolling window, use Groundwork upcasters and retain " +
            "every readable fixture; do not add Elsa-specific compatibility machinery.\n\n" +
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
        => ReadCommittedFixtureFile(kind, version);

    private static string ReadCommittedFixtureFile(string fixtureName, int version)
    {
        var fileName = fixtureName + ".json";
        if (Path.IsPathRooted(fileName) || !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
            throw new ArgumentException("Fixture names must be file names.", nameof(fixtureName));
        var fixturesDirectory = Path.Join(AppContext.BaseDirectory, "Fixtures", $"v{version}");
        var path = Path.Join(fixturesDirectory, fileName);
        Assert.True(
            File.Exists(path),
            $"Missing committed golden fixture '{fixtureName}' at '{path}'. " +
            "Run this project with GROUNDWORK_FIXTURE_REGEN=1 to generate it.");
        return File.ReadAllText(path);
    }

    private static void WriteFixtureToSource(string kind, int version, string contentJson)
    {
        var directory = Path.Join(SourceDirectory(), "Fixtures", $"v{version}");
        Directory.CreateDirectory(directory);
        var canonical = Canonicalize(JsonNode.Parse(contentJson));
        File.WriteAllText(Path.Join(directory, kind + ".json"), canonical);
    }

    private static string SourceDirectory([CallerFilePath] string? callerFilePath = null) =>
        Path.GetDirectoryName(callerFilePath)!;
}
