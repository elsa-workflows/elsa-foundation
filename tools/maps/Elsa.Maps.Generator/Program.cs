using Elsa.Maps.Generator;

// Replaces the paired PowerShell/bash generators under tools/maps. The scripts remain as thin shims so
// every documented invocation in AGENTS.md, docs/maps/README.md and the spec tasks.md files keeps working.
//
// Usage: dotnet run --project tools/maps/Elsa.Maps.Generator -- <layer> [<layer> ...]
//   domain | extension-points | architecture-reference | feature-dependency | maps | all | check

var layers = args.Length > 0 ? args : ["all"];

try
{
    var repo = RepoContext.Discover();

    // Freshness check: recompute the fingerprint and compare it with the committed manifest. Writes
    // nothing, so it is safe to run in CI while generation itself stays manually initiated.
    if (layers is ["check"])
        return MapFreshness.Check(repo);

    var projects = ProjectGraph.Read(repo);
    var written = new List<string>();

    foreach (var layer in Expand(layers))
    {
        written.AddRange(layer switch
        {
            "domain" => DomainMapGenerator.Generate(repo, projects),
            "extension-points" => ExtensionPointMapGenerator.Generate(repo, projects),
            "architecture-reference" => ArchitectureReferenceMapGenerator.Generate(repo, projects),
            "feature-dependency" => FeatureDependencyMapGenerator.Generate(repo, projects),
            "maps" => CoreMapsGenerator.Generate(repo),
            _ => throw new ArgumentException($"Unknown map layer '{layer}'.")
        });
    }

    Console.WriteLine("Generated maps:");
    foreach (var path in written)
        Console.WriteLine($" - {path}");

    return 0;
}
catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

static IEnumerable<string> Expand(IReadOnlyList<string> requested) =>
    requested.Contains("all", StringComparer.Ordinal)
        ? ["maps", "domain", "extension-points", "architecture-reference", "feature-dependency"]
        : requested;
