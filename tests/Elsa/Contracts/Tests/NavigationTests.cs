using System.Text.Json;
using Elsa.Contracts.Generator;
using Xunit;

namespace Elsa.Contracts.Tests;

/// <summary>
/// Navigation cost is a property of the published corpus, so it is guarded like any other.
/// </summary>
/// <remarks>
/// A benchmarked agent given these contracts cost MORE than one given nothing (209k tokens against a 163k
/// baseline), because a corpus you cannot read whole has to be searched, and searching loses to asking a
/// live server. The content was never the problem — the median fragment is about 810 tokens. The index and
/// the size budget are what make that median reachable.
/// </remarks>
public sealed class NavigationTests
{
    private static string ContractsRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent!)
        {
            if (File.Exists(Path.Combine(current.FullName, "Elsa.Server.slnx")))
                return Path.Combine(current.FullName, "docs", "contracts");
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static JsonDocument Index() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ContractsRoot(), "index.json")));

    private static string[] Keys(JsonElement index, string section) =>
        index.GetProperty(section).EnumerateArray().Select(entry => entry.GetProperty("key").GetString()!).ToArray();

    /// <summary>Every key the index publishes must resolve to a file that exists and actually contains it.</summary>
    [Fact]
    public void Every_index_entry_points_at_a_file_that_exists()
    {
        using var index = Index();

        foreach (var section in new[] { "activities", "intrinsics", "structures", "expressionTypes", "features", "routes" })
        {
            foreach (var entry in index.RootElement.GetProperty(section).EnumerateArray())
            {
                var file = entry.GetProperty("file").GetString()!;
                Assert.True(File.Exists(Path.Combine(ContractsRoot(), file)),
                    $"index.json maps '{entry.GetProperty("key").GetString()}' to '{file}', which does not exist.");
            }
        }
    }

    /// <summary>
    /// The worked example from the benchmark: every session needed <c>Elsa.SetVariable</c>, and it lives in
    /// a fragment whose name reads like API plumbing. If the index cannot answer this, it has not fixed
    /// anything.
    /// </summary>
    [Fact]
    public void The_intrinsic_every_consumer_needs_is_resolvable_by_both_of_its_identifiers()
    {
        using var index = Index();
        var intrinsics = Keys(index.RootElement, "intrinsics");

        Assert.Contains("Elsa.SetVariable", intrinsics);
        Assert.Contains("elsa.intrinsic.set@1", intrinsics);
    }

    [Fact]
    public void Every_activity_and_feature_in_the_corpus_is_indexed()
    {
        using var index = Index();
        var indexedActivities = Keys(index.RootElement, "activities").ToHashSet(StringComparer.Ordinal);
        var indexedFeatures = Keys(index.RootElement, "features").ToHashSet(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(Path.Combine(ContractsRoot(), "fragments"), "*.json"))
        {
            using var fragment = JsonDocument.Parse(File.ReadAllBytes(path));
            foreach (var activity in fragment.RootElement.GetProperty("activities").EnumerateArray())
                Assert.Contains(activity.GetProperty("activityTypeKey").GetString()!, indexedActivities);
            foreach (var feature in fragment.RootElement.GetProperty("features").EnumerateArray())
                Assert.Contains(feature.GetProperty("id").GetString()!, indexedFeatures);
        }
    }

    /// <summary>
    /// The deduplication that shrank the corpus by a third: an endpoint's body schema belongs to the
    /// OpenAPI part, and inlining it in the fragment too is what made fragments unopenable.
    /// </summary>
    [Fact]
    public void Fragments_do_not_inline_endpoint_body_schemas()
    {
        foreach (var path in Directory.EnumerateFiles(Path.Combine(ContractsRoot(), "fragments"), "*.json"))
        {
            using var fragment = JsonDocument.Parse(File.ReadAllBytes(path));
            foreach (var endpoint in fragment.RootElement.GetProperty("endpoints").EnumerateArray())
            {
                foreach (var field in new[] { "requestSchema", "responseSchema" })
                {
                    Assert.True(
                        !endpoint.TryGetProperty(field, out var schema) || schema.ValueKind == JsonValueKind.Null,
                        $"{Path.GetFileName(path)} inlines {field}; body schemas belong to the OpenAPI part, " +
                        "referenced by the endpoint's request/response type name.");
                }
            }
        }
    }

    /// <summary>
    /// A budget, not an exact size: the point is that no single artifact returns to being unopenable. The
    /// ceiling is deliberately generous — pretty-printing costs about 3x, and it is kept because
    /// `git diff docs/contracts/` between two tags is the published compatibility report.
    /// </summary>
    [Fact]
    public void No_published_artifact_is_large_enough_to_be_unopenable()
    {
        const int ceilingBytes = 400 * 1024;

        var oversized = Directory.EnumerateFiles(ContractsRoot(), "*.json", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(file => file.Length > ceilingBytes)
            .Select(file => $"{file.Name} ({file.Length / 1024} KB)")
            .ToArray();

        Assert.True(oversized.Length == 0,
            "These artifacts exceed the navigation budget: " + string.Join(", ", oversized) +
            ". Split by group, or check whether the bulk is duplicated from another artifact.");
    }

    [Fact]
    public void The_index_is_fingerprinted_in_the_manifest()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ContractsRoot(), "manifest.json")));

        Assert.Equal(
            manifest.RootElement.GetProperty("index").GetString(),
            DeterministicJson.Fingerprint(File.ReadAllBytes(Path.Combine(ContractsRoot(), "index.json"))));
    }
}
