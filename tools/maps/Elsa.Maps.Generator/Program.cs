using Elsa.Maps.Generator;

// Replaces the paired PowerShell/bash generators under tools/maps. The scripts remain as thin shims so
// every documented invocation in AGENTS.md, docs/maps/README.md and the spec tasks.md files keeps working.
//
// Usage: dotnet run --project tools/maps/Elsa.Maps.Generator -- <layer> [<layer> ...]
//   dependency-map | maps | domain | extension-points | architecture-reference | feature-dependency | all | check
//   solution-filters | solution-filters-check | solution-filters-self-test
//   solution-filter-roots <filter-path>
//   ef-suites-select <event> [<base-sha> <head-sha>] | ef-suites-self-test
//   project-facts-self-test

// "dependency-map" first: it is the dataset every project-graph map is a projection of (spec 149). Then "maps":
// the v2 findings report reads summary lines out of the v1 maps, so they must exist first.
string[] knownLayers = ["dependency-map", "maps", "domain", "extension-points", "architecture-reference", "feature-dependency"];
var layers = args.Length > 0 ? args : ["all"];

try
{
    var repo = RepoContext.Discover();

    if (layers.SequenceEqual(["solution-filters"], StringComparer.Ordinal))
    {
        var filterPaths = SolutionFilterGenerator.Generate(repo);
        Console.WriteLine("Generated solution filters:");
        foreach (var path in filterPaths)
            Console.WriteLine($" - {path}");
        return 0;
    }

    if (layers.SequenceEqual(["solution-filters-check"], StringComparer.Ordinal))
        return SolutionFilterGenerator.Check(repo);

    if (layers.SequenceEqual(["solution-filters-self-test"], StringComparer.Ordinal))
    {
        SolutionFilterGeneratorContractTests.Run();
        return 0;
    }

    if (layers.Length == 2 && string.Equals(layers[0], "solution-filter-roots", StringComparison.Ordinal))
    {
        foreach (var path in SolutionFilterGenerator.GetRoots(repo, layers[1]))
            Console.WriteLine(path);
        return 0;
    }

    if (layers.SequenceEqual(["ef-suites-self-test"], StringComparer.Ordinal))
    {
        EfSuiteSelectorContractTests.Run();
        return 0;
    }

    if (layers.SequenceEqual(["project-facts-self-test"], StringComparer.Ordinal))
    {
        ProjectFactsContractTests.Run();
        return 0;
    }

    if ((layers.Length is 2 or 4) && string.Equals(layers[0], "ef-suites-select", StringComparison.Ordinal))
    {
        Console.WriteLine(EfSuiteSelector.Select(repo, layers[1],
            layers.Length == 4 ? layers[2] : null,
            layers.Length == 4 ? layers[3] : null).ToJson());
        return 0;
    }

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
        throw new ArgumentException($"Unknown generator command '{unknown}'. Known map layers: {string.Join(", ", knownLayers)}, all, check, solution-filters, solution-filters-check, solution-filters-self-test, solution-filter-roots <filter-path>, ef-suites-select <event> [<base-sha> <head-sha>], ef-suites-self-test, project-facts-self-test.");

    var projects = ProjectGraph.Read(repo);
    var written = new List<string>();

    foreach (var layer in requested)
    {
        written.AddRange(layer switch
        {
            "dependency-map" => DependencyMap.Generate(repo, projects),
            "domain" => DomainMapGenerator.Generate(repo, projects),
            "extension-points" => ExtensionPointMapGenerator.Generate(repo, projects),
            "architecture-reference" => ArchitectureReferenceMapGenerator.Generate(repo, projects),
            "feature-dependency" => FeatureDependencyMapGenerator.Generate(repo, projects),
            "maps" => CoreMapsGenerator.Generate(repo, projects),
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
