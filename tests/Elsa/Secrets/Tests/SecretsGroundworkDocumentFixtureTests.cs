using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Secrets.Persistence.Groundwork.Stores;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Secrets.Tests;

/// <summary>
/// Golden-fixture drift and current-read tests for the persisted secret document kind (the W18
/// follow-up gate the Secrets domain was missing; Identity's MS-1 fixtures are the precedent).
/// The drift test freezes the serialized shape of the <c>secret</c> document kind against the committed
/// <c>Fixtures/v2/secret.json</c> fixture and fails when the shape changes without a version bump. The
/// read test proves that current committed shape loads through the real repository path. The fixture
/// secret is fully deterministic (fixed id and timestamps) and carries both payload wire variants — a
/// metadata-only encrypted-store shape with a literal fake protected value (never real protector
/// output, which is nonce-randomized) and a plain value shape.
/// </summary>
public sealed class SecretsGroundworkDocumentFixtureTests
{
    // Set GROUNDWORK_FIXTURE_REGEN=1 and run this project to (re)write the committed fixture after an
    // intentional version bump. Off by default so a normal run only compares.
    private static readonly bool Regenerate =
        Environment.GetEnvironmentVariable("GROUNDWORK_FIXTURE_REGEN") == "1";

    [Fact]
    public async Task Fixture_Matches_What_The_Repository_Writes_Today()
    {
        var (id, contentJson) = await CaptureAsync();
        Assert.Equal("8:tenant-1payments.api", id);

        if (Regenerate)
        {
            WriteFixtureToSource(contentJson);
            return;
        }

        var expected = ReadCommittedFixture();
        AssertJsonSemanticallyEqual(expected, contentJson);
    }

    [Fact]
    public async Task Committed_Fixture_Loads_Through_The_Repository_Under_The_Legacy_Stamp()
    {
        if (Regenerate)
            return;

        var fixtureContent = ReadCommittedFixture();

        var docStore = NewDocumentStore();
        await docStore.SaveAsync(new SaveDocumentRequest(
            SecretsStorageManifest.SecretDocumentKind,
            "8:tenant-1payments.api",
            SecretsStorageManifest.SchemaVersion,
            fixtureContent));

        var secret = await new GroundworkSecretRepository(docStore).FindAsync("tenant-1", "payments.api");

        Assert.NotNull(secret);
        Assert.Equal("payments.api", secret!.Name);
        Assert.Equal(SecretStatus.Active, secret.Status);
        Assert.Equal(2, secret.Versions.Count);

        // Encrypted-store shape: metadata-only payload with the protected-value entry.
        var encryptedVersion = secret.Versions.Single(x => x.Version == 1);
        Assert.Equal(SecretStatus.Revoked, encryptedVersion.Status);
        Assert.Null(encryptedVersion.Payload.Value);
        Assert.Equal("v2:key-1:AAAA:BBBB:CCCC", encryptedVersion.Payload.Metadata["protectedValue"]);

        // Plain shape: value carried directly (configuration-store style).
        Assert.Equal("fixture-plain-value", secret.LatestActiveVersion!.Payload.Value);
    }

    // --- Capture helpers ---

    private static async Task<(string Id, string ContentJson)> CaptureAsync()
    {
        var docStore = NewDocumentStore();
        await new GroundworkSecretRepository(docStore).SaveAsync(FixtureSecret());
        var envelope = docStore.Snapshot(SecretsStorageManifest.SecretDocumentKind).Single();
        return (envelope.Id, envelope.ContentJson);
    }

    private static InMemoryDocumentStore NewDocumentStore() => new(SecretsStorageManifest.Create());

    /// <summary>
    /// Deterministic secret pinning every serialized property: fixed id/timestamps, description, scope,
    /// tags, a non-default version status, an expiry, and both payload variants. The protected value is
    /// a literal placeholder in the key-ring wire format — never real ciphertext.
    /// </summary>
    private static Secret FixtureSecret() => new()
    {
        Id = "secret-fixture-0001",
        TenantId = "tenant-1",
        Name = "payments.api",
        DisplayName = "Payments API key",
        Description = "Golden-fixture secret pinning the v2 wire shape.",
        TypeName = SecretTypeNames.Text,
        StoreName = SecretStoreNames.Encrypted,
        Scope = "tenant-1",
        Tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "payments", "api" },
        Status = SecretStatus.Active,
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
        Versions =
        [
            new SecretVersion
            {
                Version = 1,
                Status = SecretStatus.Revoked,
                CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                ExpiresAt = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Payload = new SecretPayload
                {
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["protectedValue"] = "v2:key-1:AAAA:BBBB:CCCC"
                    }
                }
            },
            new SecretVersion
            {
                Version = 2,
                Status = SecretStatus.Active,
                CreatedAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                Payload = SecretPayload.FromValue("fixture-plain-value")
            }
        ]
    };

    // --- Semantic JSON comparison ---

    private static void AssertJsonSemanticallyEqual(string expectedJson, string actualJson)
    {
        var expected = JsonNode.Parse(expectedJson);
        var actual = JsonNode.Parse(actualJson);

        if (JsonNode.DeepEquals(expected, actual))
            return;

        Assert.Fail(
            "The serialized shape of the 'secret' document kind no longer matches its committed golden " +
            "fixture (Fixtures/v2/secret.json).\n\n" +
            "A persisted secret shape changed. To evolve it you must, in the same change:\n" +
            "  1. bump SecretsStorageManifest.SchemaVersion, and\n" +
            "  2. regenerate the golden fixture (run with GROUNDWORK_FIXTURE_REGEN=1).\n\n" +
            $"Expected (committed fixture, canonical):\n{Canonicalize(expected)}\n\n" +
            $"Actual (written by the repository today, canonical):\n{Canonicalize(actual)}");
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

    private static string ReadCommittedFixture()
    {
        var path = FixturePath();
        Assert.True(
            File.Exists(path),
            $"Missing committed golden fixture for kind '{SecretsStorageManifest.SecretDocumentKind}' at '{path}'. " +
            "Run this project with GROUNDWORK_FIXTURE_REGEN=1 to generate it.");
        return File.ReadAllText(path);
    }

    private static void WriteFixtureToSource(string contentJson)
    {
        var path = FixturePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Canonicalize(JsonNode.Parse(contentJson)));
    }

    private static string FixturePath()
    {
        var fileName = Path.GetFileName(SecretsStorageManifest.SecretDocumentKind + ".json");
        return Path.Combine(SourceDirectory(), "Fixtures", "v2", fileName);
    }

    private static string SourceDirectory([CallerFilePath] string? callerFilePath = null) =>
        Path.GetDirectoryName(callerFilePath)!;
}
