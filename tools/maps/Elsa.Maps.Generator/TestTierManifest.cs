using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Maps.Generator;

/// <summary>
/// The test tier manifest, <c>tools/ci/test-tiers.json</c> (spec 263): every test project with its tier, the class
/// splits, the shard grouping and the quarantine list. <see cref="TestTierCheck"/> validates it against the tree.
/// </summary>
public sealed class TestTierManifest
{
    public const string Path = "tools/ci/test-tiers.json";
    public const string PrTier = "pr";
    public const string IntegrationTier = "integration";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public int Schema { get; init; }
    public bool IntegrationLeavesPrTier { get; init; }
    public double ShardTargetMinutes { get; init; }
    public IReadOnlyDictionary<string, string> OwnJobs { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<TestTierExemption> HostLaunchExemptions { get; init; } = [];
    public IReadOnlyList<TestTierProject> Projects { get; init; } = [];
    public IReadOnlyDictionary<string, TestTierSplit> Splits { get; init; } = new Dictionary<string, TestTierSplit>();
    public TestTierShards Shards { get; init; } = new();
    public IReadOnlyList<EfSuite> ContainerSuites { get; init; } = [];
    public TestTierSecretsSuite? SecretsCompositionSuite { get; init; }
    public IReadOnlyList<TestTierQuarantine> Quarantine { get; init; } = [];

    /// <summary>Reads the manifest, naming the JSON path of the first part it cannot use.</summary>
    /// <exception cref="InvalidOperationException">The file is missing, unreadable or not schema 1.</exception>
    public static TestTierManifest Read(RepoContext repo)
    {
        var absolute = repo.Absolute(Path);
        if (!File.Exists(absolute))
            throw new InvalidOperationException($"{Path} is missing.");
        return Parse(File.ReadAllText(absolute));
    }

    /// <summary>Parses manifest text; separate from <see cref="Read"/> so the contract tests can feed fixtures.</summary>
    public static TestTierManifest Parse(string json)
    {
        TestTierManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<TestTierManifest>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{Path}: invalid JSON at {exception.Path ?? "$"}: {exception.Message}");
        }

        if (manifest is null || manifest.Schema != 1)
            throw new InvalidOperationException($"{Path}: $.schema must be 1.");
        return manifest;
    }
}

public sealed class TestTierProject
{
    public string Project { get; init; } = "";
    public string Tier { get; init; } = "";
    public string? Trait { get; init; }
    public double? MedianMinutes { get; init; }
}

public sealed class TestTierExemption
{
    public string Project { get; init; } = "";
    public string Reason { get; init; } = "";
}

public sealed class TestTierSplit
{
    public IReadOnlyList<TestTierGroup> Groups { get; init; } = [];
}

public sealed class TestTierGroup
{
    public string Name { get; init; } = "";
    public IReadOnlyList<string> Classes { get; init; } = [];
    public bool Rest { get; init; }
}

public sealed class TestTierShards
{
    public IReadOnlyList<TestTierShard> Pr { get; init; } = [];
    public IReadOnlyList<TestTierShard> Integration { get; init; } = [];
    public IReadOnlyList<TestTierContainerShard> Containers { get; init; } = [];
}

public sealed class TestTierShard
{
    public string Name { get; init; } = "";
    public IReadOnlyList<string> Units { get; init; } = [];
}

public sealed class TestTierContainerShard
{
    public string Name { get; init; } = "";
    public IReadOnlyList<string> Suites { get; init; } = [];
}

public sealed class TestTierSecretsSuite
{
    public string Suite { get; init; } = "";
    public string Project { get; init; } = "";
}

public sealed class TestTierQuarantine
{
    public string Test { get; init; } = "";
    public int Issue { get; init; }

    [JsonPropertyName("since")]
    public string? Since { get; init; }
}
