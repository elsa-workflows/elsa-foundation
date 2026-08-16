using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

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
        IdentityStorageManifest.IdentityUserDocumentKind,
        IdentityStorageManifest.IdentityRoleDocumentKind,
        IdentityStorageManifest.IdentityApplicationDocumentKind,
        IdentityStorageManifest.IdentityCredentialDocumentKind,
        IdentityStorageManifest.IdentityClaimMappingDocumentKind,
        IdentityStorageManifest.IdentityProviderConfigurationDocumentKind,
        IdentityStorageManifest.IdentityGlobalProviderConfigurationDocumentKind,
        IdentityStorageManifest.ExternalLoginDocumentKind,
        IdentityStorageManifest.IdentityTenantMembershipDocumentKind,
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
    public async Task Committed_Fixture_Loads_Through_The_V2_Row_Store(string kind)
    {
        if (Regenerate)
            return;

        var fixtureContent = ReadCommittedFixture(kind);

        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        SeedFixture(kind, fixtureContent, docStore);

        var spot = await ReadSpotCheckAsync(kind, docStore);

        Assert.Equal(ExpectedSpotValue(kind), spot);
    }

    // --- Capture / read-back helpers ---

    private static async Task<(string Id, string ContentJson)> CaptureAsync(string kind)
    {
        var docStore = IdentityGroundworkFixtures.NewDocumentStore();
        await SaveDeterministicAsync(kind, docStore);
        var id = DeterministicId(kind);
        var row = Rows(kind, docStore).Read(kind, id)
                  ?? throw new InvalidOperationException($"The deterministic Identity row '{kind}/{id}' was not written.");
        return (row.Id, row.CanonicalJson);
    }

    private static void SeedFixture(string kind, string contentJson, IdentityTestPersistence persistence)
    {
        var projected = kind switch
        {
            var value when value == IdentityStorageManifest.IdentityUserDocumentKind => UserProjections(contentJson),
            var value when value == IdentityStorageManifest.IdentityRoleDocumentKind => RoleProjections(contentJson),
            var value when value == IdentityStorageManifest.IdentityClaimMappingDocumentKind => ClaimMappingProjections(contentJson),
            var value when value == IdentityStorageManifest.ExternalLoginDocumentKind => ExternalLoginProjections(contentJson),
            _ => new Dictionary<string, object?>()
        };
        var result = Rows(kind, persistence).Save(new GroundworkIdentityRowWrite(
            kind,
            DeterministicId(kind),
            contentJson,
            projected,
            GroundworkIdentityRowWriteCondition.CreateOnly));
        Assert.True(result.Succeeded, result.Message);
    }

    private static GroundworkIdentityRowStore Rows(string kind, IdentityTestPersistence persistence) =>
        persistence.Rows(kind == IdentityStorageManifest.IdentityGlobalProviderConfigurationDocumentKind
            ? IdentityGroundworkFixtures.GlobalAccessor()
            : IdentityGroundworkFixtures.Accessor());

    private static IReadOnlyDictionary<string, object?> UserProjections(string json)
    {
        var value = JsonSerializer.Deserialize<IdentityUserDocument>(json, IdentityGroundworkJson.Options)!;
        return new Dictionary<string, object?>
        {
            [IdentityStorageManifest.NormalizedUserNameKeyField] = value.NormalizedUserNameKey,
            [IdentityStorageManifest.NormalizedEmailKeyField] = value.NormalizedEmailKey
        };
    }

    private static IReadOnlyDictionary<string, object?> RoleProjections(string json)
    {
        var value = JsonSerializer.Deserialize<IdentityRoleDocument>(json, IdentityGroundworkJson.Options)!;
        return new Dictionary<string, object?>
        {
            [IdentityStorageManifest.NormalizedRoleNameKeyField] = value.NormalizedRoleNameKey,
            [IdentityStorageManifest.TenantIdField] = value.TenantId
        };
    }

    private static IReadOnlyDictionary<string, object?> ClaimMappingProjections(string json)
    {
        var value = JsonSerializer.Deserialize<IdentityClaimMappingDocument>(json, IdentityGroundworkJson.Options)!;
        return new Dictionary<string, object?>
        {
            [IdentityStorageManifest.ProviderLookupKeyField] = value.ProviderLookupKey
        };
    }

    private static IReadOnlyDictionary<string, object?> ExternalLoginProjections(string json)
    {
        var value = JsonSerializer.Deserialize<IdentityExternalLoginDocument>(json, IdentityGroundworkJson.Options)!;
        return new Dictionary<string, object?>
        {
            [IdentityStorageManifest.UserLookupKeyField] = value.UserLookupKey
        };
    }

    private static string DeterministicId(string kind) => kind switch
    {
        var value when value == IdentityStorageManifest.IdentityUserDocumentKind => IdentityCompositeDocumentId.From("tenant-1", "user-1"),
        var value when value == IdentityStorageManifest.IdentityRoleDocumentKind => IdentityCompositeDocumentId.From("tenant-1", "role-1"),
        var value when value == IdentityStorageManifest.IdentityApplicationDocumentKind => IdentityCompositeDocumentId.From("tenant-1", "app-1"),
        var value when value == IdentityStorageManifest.IdentityCredentialDocumentKind => IdentityCompositeDocumentId.From("tenant-1", "credential-1"),
        var value when value == IdentityStorageManifest.IdentityClaimMappingDocumentKind => IdentityCompositeDocumentId.From("tenant-1", "google", "claim-map-1"),
        var value when value == IdentityStorageManifest.IdentityProviderConfigurationDocumentKind => IdentityCompositeDocumentId.From("tenant-1", "google"),
        var value when value == IdentityStorageManifest.IdentityGlobalProviderConfigurationDocumentKind => IdentityCompositeDocumentId.Normalize("google"),
        var value when value == IdentityStorageManifest.ExternalLoginDocumentKind => IdentityCompositeDocumentId.From("tenant-1", "google", "sub-123"),
        var value when value == IdentityStorageManifest.IdentityTenantMembershipDocumentKind => IdentityCompositeDocumentId.From("tenant-1", "user-1"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown identity row kind.")
    };

    private static async Task SaveDeterministicAsync(string kind, IdentityTestPersistence docStore)
    {
        switch (kind)
        {
            case var _ when kind == IdentityStorageManifest.IdentityUserDocumentKind:
                await IdentityGroundworkFixtures.UserStore(docStore).SaveAsync(IdentityGroundworkFixtures.User());
                break;
            case var _ when kind == IdentityStorageManifest.IdentityRoleDocumentKind:
                await IdentityGroundworkFixtures.RoleStore(docStore).SaveAsync(IdentityGroundworkFixtures.Role());
                break;
            case var _ when kind == IdentityStorageManifest.IdentityApplicationDocumentKind:
                await IdentityGroundworkFixtures.ApplicationStore(docStore).SaveAsync(IdentityGroundworkFixtures.Application());
                break;
            case var _ when kind == IdentityStorageManifest.IdentityCredentialDocumentKind:
                await IdentityGroundworkFixtures.CredentialStore(docStore).SaveAsync(IdentityGroundworkFixtures.Credential());
                break;
            case var _ when kind == IdentityStorageManifest.IdentityClaimMappingDocumentKind:
                await IdentityGroundworkFixtures.ClaimMappingStore(docStore).SaveAsync(IdentityGroundworkFixtures.ClaimMappingRule());
                break;
            case var _ when kind == IdentityStorageManifest.IdentityProviderConfigurationDocumentKind:
                await IdentityGroundworkFixtures.ProviderConfigurationStore(docStore).SaveAsync(IdentityGroundworkFixtures.TenantProviderConfiguration());
                break;
            case var _ when kind == IdentityStorageManifest.IdentityGlobalProviderConfigurationDocumentKind:
                await IdentityGroundworkFixtures.GlobalProviderConfigurationStore(docStore).SaveAsync(IdentityGroundworkFixtures.GlobalProviderConfiguration());
                break;
            case var _ when kind == IdentityStorageManifest.ExternalLoginDocumentKind:
                await IdentityGroundworkFixtures.UserStore(docStore).SaveAsync(IdentityGroundworkFixtures.User());
                await IdentityGroundworkFixtures.ExternalIdentityStore(docStore).SaveAsync(IdentityGroundworkFixtures.ExternalIdentity());
                break;
            case var _ when kind == IdentityStorageManifest.IdentityTenantMembershipDocumentKind:
                await IdentityGroundworkFixtures.UserStore(docStore).SaveAsync(IdentityGroundworkFixtures.User());
                await IdentityGroundworkFixtures.TenantMembershipStore(docStore).SaveAsync(IdentityGroundworkFixtures.TenantMembership());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown identity document kind.");
        }
    }

    private static async Task<string?> ReadSpotCheckAsync(string kind, IdentityTestPersistence docStore)
    {
        if (kind == IdentityStorageManifest.IdentityUserDocumentKind)
            return (await IdentityGroundworkFixtures.UserStore(docStore).FindAsync("tenant-1", "user-1"))?.UserName;
        if (kind == IdentityStorageManifest.IdentityRoleDocumentKind)
            return (await IdentityGroundworkFixtures.RoleStore(docStore).FindAsync("tenant-1", "role-1"))?.Name;
        if (kind == IdentityStorageManifest.IdentityApplicationDocumentKind)
            return (await IdentityGroundworkFixtures.ApplicationStore(docStore).FindAsync("tenant-1", "app-1"))?.ClientId;
        if (kind == IdentityStorageManifest.IdentityCredentialDocumentKind)
            return (await IdentityGroundworkFixtures.CredentialStore(docStore).FindAsync("tenant-1", "credential-1"))?.SubjectId;
        if (kind == IdentityStorageManifest.IdentityClaimMappingDocumentKind)
            return (await IdentityGroundworkFixtures.ClaimMappingStore(docStore).ListForProviderAsync("tenant-1", "google")).Single().MatchClaimType;
        if (kind == IdentityStorageManifest.IdentityProviderConfigurationDocumentKind)
            return (await IdentityGroundworkFixtures.ProviderConfigurationStore(docStore).FindForTenantAsync("tenant-1", "google"))?.Kind;
        if (kind == IdentityStorageManifest.IdentityGlobalProviderConfigurationDocumentKind)
            return (await IdentityGroundworkFixtures.GlobalProviderConfigurationStore(docStore).FindGlobalAsync("google"))?.Kind;
        if (kind == IdentityStorageManifest.ExternalLoginDocumentKind)
            return (await IdentityGroundworkFixtures.ExternalIdentityStore(docStore).FindBySubjectAsync("tenant-1", "google", "sub-123"))?.UserId;
        if (kind == IdentityStorageManifest.IdentityTenantMembershipDocumentKind)
            return (await IdentityGroundworkFixtures.TenantMembershipStore(docStore).FindAsync("tenant-1", "user-1"))?.Status.ToString();
        throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown identity document kind.");
    }

    private static string ExpectedSpotValue(string kind)
    {
        if (kind == IdentityStorageManifest.IdentityUserDocumentKind) return "alice";
        if (kind == IdentityStorageManifest.IdentityRoleDocumentKind) return "Administrators";
        if (kind == IdentityStorageManifest.IdentityApplicationDocumentKind) return "client-1";
        if (kind == IdentityStorageManifest.IdentityCredentialDocumentKind) return "app-1";
        if (kind == IdentityStorageManifest.IdentityClaimMappingDocumentKind) return "groups";
        if (kind == IdentityStorageManifest.IdentityProviderConfigurationDocumentKind) return "external-oidc";
        if (kind == IdentityStorageManifest.IdentityGlobalProviderConfigurationDocumentKind) return "external-oidc";
        if (kind == IdentityStorageManifest.ExternalLoginDocumentKind) return "user-1";
        if (kind == IdentityStorageManifest.IdentityTenantMembershipDocumentKind) return "Active";
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
