using Elsa.Workflows.Design.Api.Handlers;
using Elsa.Workflows.Design.Api.Requests;

namespace Elsa.Contracts.Generator;

/// <summary>
/// Merges the contract fragments of every contributing src assembly into <c>docs/contracts/</c>:
/// <c>fragments/&lt;Assembly&gt;.json</c>, <c>submit-schema.json</c> (produced by the *served* handler —
/// literally the same code path as <c>GET design/workflows/definitions/submit/schema</c>), and
/// <c>manifest.json</c> with per-fragment fingerprints. Fails loudly on any unreadable input: a silently
/// partial contract set is worse than none (spec 149 edge case).
/// </summary>
public sealed class ContractsMerge(Diagnostics diagnostics)
{
    public const string GeneratorId = "tools/contracts/Elsa.Contracts.Generator";

    public int Run(string repoRoot, string configuration, string outputDirectory)
    {
        var sourceRoot = Path.Combine(repoRoot, "src");

        // App outputs carry the full runtime dependency closure (NuGet assemblies libraries never copy
        // to their own bin). Registering them as probe directories lets feature types that reference
        // external packages (Groundwork, EF Core, Fluid, ...) load for projection.
        var appsRoot = Path.Combine(sourceRoot, "Apps");
        if (Directory.Exists(appsRoot))
        {
            foreach (var appProject in Directory.EnumerateFiles(appsRoot, "*.csproj", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var appAssembly = FindBuiltAssembly(appProject, Path.GetFileNameWithoutExtension(appProject), configuration);
                if (appAssembly is not null)
                    TargetAssembly.AddProbeDirectory(Path.GetDirectoryName(appAssembly)!);
            }
        }

        // Pass 1: locate every built assembly and register its bin directory for dependency probing.
        var assemblyPaths = new List<string>();
        foreach (var projectFile in EnumerateContractProjects(sourceRoot))
        {
            var assemblyName = Path.GetFileNameWithoutExtension(projectFile);
            var assemblyPath = FindBuiltAssembly(projectFile, assemblyName, configuration);
            if (assemblyPath is null)
            {
                diagnostics.Error(projectFile, "ELSACT001",
                    $"No built assembly found for '{assemblyName}' (configuration {configuration}). Build the solution first: dotnet build Elsa.Server.slnx -c {configuration}.");
                continue;
            }

            TargetAssembly.AddProbeDirectory(Path.GetDirectoryName(assemblyPath)!);
            assemblyPaths.Add(assemblyPath);
        }

        if (diagnostics.HasErrors)
            return 1;

        // Pass 2: index feature ids across all assemblies so DependsOn closures compose cross-assembly,
        // then project each assembly into its fragment.
        var featureIndex = FeatureIndex.Build(assemblyPaths, diagnostics);
        var projector = new FragmentProjector(diagnostics, featureIndex);
        var fragments = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var counts = (Features: 0, Activities: 0, Structures: 0, Intrinsics: 0);

        foreach (var assemblyPath in assemblyPaths)
        {
            var binDirectory = Path.GetDirectoryName(assemblyPath)!;

            ContractFragment fragment;
            try
            {
                var referencePaths = Directory.EnumerateFiles(binDirectory, "*.dll").ToArray();
                fragment = projector.Project(assemblyPath, referencePaths);
            }
            catch (Exception exception) when (exception is IOException or BadImageFormatException or TypeLoadException or System.Reflection.ReflectionTypeLoadException)
            {
                diagnostics.Error(assemblyPath, "ELSACT002",
                    $"Assembly could not be projected into a contract fragment: {exception.GetBaseException().Message}");
                continue;
            }

            if (!fragment.HasContributions)
                continue;

            fragments.Add(fragment.Assembly, DeterministicJson.SerializeToBytes(fragment));
            counts = (
                counts.Features + fragment.Features.Count,
                counts.Activities + fragment.Activities.Count,
                counts.Structures + fragment.Structures.Count,
                counts.Intrinsics + fragment.Intrinsics.Count);
        }

        if (diagnostics.HasErrors)
            return 1;

        // The submit schema joins as-is (RFC Part 1): produced by the same handler the endpoint dispatches.
        var submitSchemaView = new GetWorkflowDefinitionSubmitSchemaHandler()
            .Handle(new GetWorkflowDefinitionSubmitSchema(), CancellationToken.None)
            .GetAwaiter().GetResult();
        var submitSchemaBytes = DeterministicJson.SerializeToBytes(submitSchemaView);

        var manifest = new ContractsManifest(
            SchemaVersion: "1.0",
            Generator: GeneratorId,
            Fragments: fragments.Select(pair => new FragmentFingerprint(pair.Key, DeterministicJson.Fingerprint(pair.Value))).ToArray(),
            SubmitSchema: DeterministicJson.Fingerprint(submitSchemaBytes),
            Counts: new ContractsManifestCounts(
                fragments.Count, counts.Features, counts.Activities, counts.Structures, counts.Intrinsics));

        var fragmentsDirectory = Path.Combine(outputDirectory, "fragments");
        if (Directory.Exists(fragmentsDirectory))
            Directory.Delete(fragmentsDirectory, recursive: true);
        foreach (var (name, bytes) in fragments)
            DeterministicJson.WriteFile(Path.Combine(fragmentsDirectory, name + ".json"), bytes);
        DeterministicJson.WriteFile(Path.Combine(outputDirectory, "submit-schema.json"), submitSchemaBytes);
        DeterministicJson.WriteFile(Path.Combine(outputDirectory, "manifest.json"), DeterministicJson.SerializeToBytes(manifest));

        Console.WriteLine($"Merged {fragments.Count} contract fragments into {outputDirectory} " +
                          $"({counts.Features} features, {counts.Activities} activities, {counts.Structures} structures, {counts.Intrinsics} intrinsics).");
        return 0;
    }

    /// <summary>
    /// Every src project except hosts under <c>src/Apps</c> (application composition, not feature surface).
    /// Contribution detection happens by projecting — a project with nothing to declare yields no fragment,
    /// so absence stays meaningful without any opt-in flag to forget (spec FR-001 completeness rule).
    /// </summary>
    public static IEnumerable<string> EnumerateContractProjects(string sourceRoot) =>
        Directory.EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !RelativeTo(sourceRoot, path).StartsWith("Apps" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal);

    public static string? FindBuiltAssembly(string projectFile, string assemblyName, string configuration)
    {
        var binConfiguration = Path.Combine(Path.GetDirectoryName(projectFile)!, "bin", configuration);
        if (!Directory.Exists(binConfiguration))
            return null;

        return Directory.EnumerateFiles(binConfiguration, assemblyName + ".dll", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "ref" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static string RelativeTo(string root, string path) => Path.GetRelativePath(root, path);
}
