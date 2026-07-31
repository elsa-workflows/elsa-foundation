using Elsa.Maps.Generator;

// Replaces the paired PowerShell/bash generators under tools/maps. The scripts remain as thin shims so
// every documented invocation in AGENTS.md, docs/maps/README.md and the spec tasks.md files keeps working.
//
// Usage: dotnet run --project tools/maps/Elsa.Maps.Generator -- <layer> [<layer> ...]
//   domain | extension-points | architecture-reference | feature-dependency | maps | all

var layers = args.Length > 0 ? args : ["all"];

try
{
    var repo = RepoContext.Discover();
    var projects = ProjectGraph.Read(repo);
    var written = new List<string>();

    foreach (var layer in Expand(layers))
    {
        written.AddRange(layer switch
        {
            "domain" => DomainMapGenerator.Generate(repo, projects),
            "extension-points" => ExtensionPointMapGenerator.Generate(repo, projects),
            "architecture-reference" => ArchitectureReferenceMapGenerator.Generate(repo, projects),
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
        ? ["domain", "extension-points", "architecture-reference"]
        : requested;
