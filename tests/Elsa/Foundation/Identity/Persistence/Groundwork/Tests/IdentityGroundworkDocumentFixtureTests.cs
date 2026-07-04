using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Tests;

/// <summary>
/// Golden-fixture drift and backward-compatibility tests for the durable identity document kinds (MS-1).
/// The drift test freezes the serialized shape of every identity document kind against a committed
/// <c>Fixtures/v1</c> fixture and fails when a shape changes without a version bump. The compatibility test
/// proves every committed fixture still loads through the real read path under the legacy schema stamp.
/// </summary>
public sealed class IdentityGroundworkDocumentFixtureTests
{
    // Set GROUNDWORK_FIXTURE_REGEN=1 and run this project to (re)write the committed fixtures after an
    // intentional version bump. Off by default so a normal run only compares.
    private static readonly bool Regenerate =
        Environment.GetEnvironmentVariable("GROUNDWORK_FIXTURE_REGEN") == "1";

    public static TheoryData<string> Kinds() => new()
    {
        IdentityStorageManifest.UserDocumentKind,
        IdentityStorageManifest.RoleDocumentKind,
        IdentityStorageManifest.ExternalIdentityDocumentKind,
        IdentityStorageManifest.TenantMembershipDocumentKind,
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Fixture_Matches_What_The_Store_Writes_Today(string kind)
    {
        var (id, contentJson) = await CaptureAsync(kind);
        Assert.False(string.IsNullOrEmpty(id));

        if (Regenerate)
        {
            WriteFixtureToSource(kind, contentJson);
            return;
        }

        var expected = ReadCommittedFixture(kind);
        AssertJsonSemanticallyEqual(expected, contentJson, kind);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Committed_Fixture_Loads_Through_The_Store_Under_The_Legacy_Stamp(string kind)
    {
        if (Regenerate)
            return;

        var fixtureContent = ReadCommittedFixture(kind);

        // Discover the composite id the store assigns for the deterministic record, then re-seed the
        // committed fixture bytes under that id in a fresh document store and read it back through the
        // real store bridge.
        var (id, _) = await CaptureAsync(kind);

        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        await docStore.SaveAsync(new SaveDocumentRequest(kind, id, IdentityStorageManifest.SchemaVersion, fixtureContent));

        var spot = await ReadSpotCheckAsync(kind, docStore);

        Assert.Equal(ExpectedSpotValue(kind), spot);
    }

    // --- Capture / read-back helpers ---

    private static async Task<(string Id, string ContentJson)> CaptureAsync(string kind)
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        await SaveDeterministicAsync(kind, docStore);
        var envelope = docStore.Snapshot(kind).Single();
        return (envelope.Id, envelope.ContentJson);
    }

    private static async Task SaveDeterministicAsync(string kind, IDocumentStore docStore)
    {
        switch (kind)
        {
            case var _ when kind == IdentityStorageManifest.UserDocumentKind:
                await new GroundworkUserStore(docStore).SaveAsync(IdentityGroundworkFixtures.User());
                break;
            case var _ when kind == IdentityStorageManifest.RoleDocumentKind:
                await new GroundworkRoleStore(docStore).SaveAsync(IdentityGroundworkFixtures.Role());
                break;
            case var _ when kind == IdentityStorageManifest.ExternalIdentityDocumentKind:
                await new GroundworkExternalIdentityStore(docStore).SaveAsync(IdentityGroundworkFixtures.ExternalIdentity());
                break;
            case var _ when kind == IdentityStorageManifest.TenantMembershipDocumentKind:
                await new GroundworkTenantMembershipStore(docStore).SaveAsync(IdentityGroundworkFixtures.TenantMembership());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown identity document kind.");
        }
    }

    private static async Task<string?> ReadSpotCheckAsync(string kind, IDocumentStore docStore)
    {
        if (kind == IdentityStorageManifest.UserDocumentKind)
            return (await new GroundworkUserStore(docStore).FindAsync("tenant-1", "user-1"))?.UserName;
        if (kind == IdentityStorageManifest.RoleDocumentKind)
            return (await new GroundworkRoleStore(docStore).FindAsync("tenant-1", "role-1"))?.Name;
        if (kind == IdentityStorageManifest.ExternalIdentityDocumentKind)
            return (await new GroundworkExternalIdentityStore(docStore).FindBySubjectAsync("tenant-1", "google", "sub-123"))?.UserId;
        if (kind == IdentityStorageManifest.TenantMembershipDocumentKind)
            return (await new GroundworkTenantMembershipStore(docStore).FindAsync("tenant-1", "user-1"))?.Status.ToString();
        throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown identity document kind.");
    }

    private static string ExpectedSpotValue(string kind)
    {
        if (kind == IdentityStorageManifest.UserDocumentKind) return "alice";
        if (kind == IdentityStorageManifest.RoleDocumentKind) return "Administrators";
        if (kind == IdentityStorageManifest.ExternalIdentityDocumentKind) return "user-1";
        if (kind == IdentityStorageManifest.TenantMembershipDocumentKind) return "Active";
        throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown identity document kind.");
    }

    // --- Semantic JSON comparison ---

    private static void AssertJsonSemanticallyEqual(string expectedJson, string actualJson, string kind)
    {
        var expected = JsonNode.Parse(expectedJson);
        var actual = JsonNode.Parse(actualJson);

        if (JsonNode.DeepEquals(expected, actual))
            return;

        Assert.Fail(
            $"The serialized shape of identity document kind '{kind}' no longer matches its committed golden " +
            $"fixture (Fixtures/v1/{kind}.json).\n\n" +
            "An identity record shape changed. To evolve a persisted identity shape you must, in the same change:\n" +
            "  1. bump IdentityStorageManifest.SchemaVersion (and add an upcaster if you must read the old shape),\n" +
            "  2. regenerate the golden fixture (run with GROUNDWORK_FIXTURE_REGEN=1), and\n" +
            "  3. keep old fixtures/readers so historical documents still load.\n\n" +
            $"Expected (committed fixture, canonical):\n{Canonicalize(expected)}\n\n" +
            $"Actual (written by the store today, canonical):\n{Canonicalize(actual)}");
    }

    private static string Canonicalize(JsonNode? node) =>
        SortRecursively(node)?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";

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

    // --- Fixture file access (source-tree relative via CallerFilePath, so no output copy is required) ---

    private static string ReadCommittedFixture(string kind)
    {
        var path = Path.Combine(SourceDirectory(), "Fixtures", "v1", kind + ".json");
        Assert.True(
            File.Exists(path),
            $"Missing committed golden fixture for kind '{kind}' at '{path}'. " +
            "Run this project with GROUNDWORK_FIXTURE_REGEN=1 to generate it.");
        return File.ReadAllText(path);
    }

    private static void WriteFixtureToSource(string kind, string contentJson)
    {
        var directory = Path.Combine(SourceDirectory(), "Fixtures", "v1");
        Directory.CreateDirectory(directory);
        var canonical = Canonicalize(JsonNode.Parse(contentJson));
        File.WriteAllText(Path.Combine(directory, kind + ".json"), canonical);
    }

    private static string SourceDirectory([CallerFilePath] string? callerFilePath = null) =>
        Path.GetDirectoryName(callerFilePath)!;
}
