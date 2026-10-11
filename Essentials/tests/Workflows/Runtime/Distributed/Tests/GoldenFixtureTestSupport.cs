using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

/// <summary>
/// Shared plumbing for the leaf's golden-fixture drift tests: canonicalization (recursively key-sorted, indented
/// JSON), semantic comparison against a committed <c>Fixtures/v1</c> file, and the regen write-back used after a
/// deliberate, versioned wire change. Each fixture test class keeps its own canonical instance and failure message;
/// this class owns only the mechanical file/JSON handling so the two frozen kinds cannot drift apart in test rigor.
/// </summary>
internal static class GoldenFixtureTestSupport
{
    /// <summary>Whether ELSA_DISTRIBUTED_FIXTURE_REGEN=1 is set: fixture tests write instead of assert.</summary>
    public static bool Regenerate => Environment.GetEnvironmentVariable("ELSA_DISTRIBUTED_FIXTURE_REGEN") == "1";

    public static void AssertJsonSemanticallyEqual(string expectedJson, string actualJson, string frozenShapeDescription)
    {
        var expected = JsonNode.Parse(expectedJson);
        var actual = JsonNode.Parse(actualJson);

        if (JsonNode.DeepEquals(expected, actual))
            return;

        Assert.Fail(
            $"{frozenShapeDescription}\n\n" +
            "To evolve it you must bump the schema version, add an upcaster, add a new versioned fixture, and keep the " +
            "old one. Run with ELSA_DISTRIBUTED_FIXTURE_REGEN=1 only after a deliberate, versioned change.\n\n" +
            $"Expected (committed):\n{Canonicalize(expected)}\n\nActual (serialized today):\n{Canonicalize(actual)}");
    }

    public static string Canonicalize(JsonNode? node) =>
        SortRecursively(node)?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";

    public static string ReadCommittedFixture(string kind)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "v1", kind + ".json");
        Assert.True(File.Exists(path), $"Missing committed golden fixture at '{path}'. Run with ELSA_DISTRIBUTED_FIXTURE_REGEN=1 to generate it.");
        return File.ReadAllText(path);
    }

    public static void WriteFixtureToSource(string sourceDirectory, string kind, string canonicalJson)
    {
        var directory = Path.Combine(sourceDirectory, "Fixtures", "v1");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, kind + ".json"), canonicalJson);
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
                foreach (var element in array)
                    newArray.Add(SortRecursively(element?.DeepClone()));
                return newArray;
            default:
                return node?.DeepClone();
        }
    }
}
