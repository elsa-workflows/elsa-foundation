using Elsa.Maps.Generator;

// Replaces the paired PowerShell/bash generators under tools/maps. The scripts remain as thin shims so
// every documented invocation in AGENTS.md, docs/maps/README.md and the spec tasks.md files keeps working.
//
// Usage: dotnet run --project tools/maps/Elsa.Maps.Generator -- <layer> [<layer> ...]
//   domain | extension-points | architecture-reference | feature-dependency | maps | all | check

// "maps" first: the v2 findings report reads summary lines out of the v1 maps, so they must exist first.
string[] knownLayers = ["maps", "domain", "extension-points", "architecture-reference", "feature-dependency"];
var layers = args.Length > 0 ? args : ["all"];

try
{
    var repo = RepoContext.Discover();

    // Freshness check: regenerates into a scratch directory and compares the bytes with what is
    // committed. Writes nothing into the repository, so it is safe to run in CI while generation
    // itself stays manually initiated.
    if (layers.Contains("check", StringComparer.Ordinal))
    {
        if (layers.Length > 1)
            throw new ArgumentException("'check' cannot be combined with other layers.");

        return MapFreshness.Check(repo);
    }

    // Validate every requested layer before generating, so an unknown one fails cleanly instead of
    // writing part of the maps and then throwing.
    var requested = layers.Contains("all", StringComparer.Ordinal) ? knownLayers : layers;
    if (requested.FirstOrDefault(layer => !knownLayers.Contains(layer, StringComparer.Ordinal)) is { } unknown)
        throw new ArgumentException($"Unknown map layer '{unknown}'. Known layers: {string.Join(", ", knownLayers)}, all, check.");

    var projects = ProjectGraph.Read(repo);
    var written = new List<string>();

    foreach (var layer in requested)
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
