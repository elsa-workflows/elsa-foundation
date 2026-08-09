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
        var hostAssemblyPaths = new List<string>();
        if (Directory.Exists(appsRoot))
        {
            foreach (var appProject in Directory.EnumerateFiles(appsRoot, "*.csproj", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var appAssembly = FindBuiltAssembly(appProject, Path.GetFileNameWithoutExtension(appProject), configuration);
                if (appAssembly is null)
                {
                    diagnostics.Error(appProject, "ELSACT001",
                        $"No built assembly found for host '{Path.GetFileNameWithoutExtension(appProject)}' (configuration {configuration}). Build the solution first: dotnet build Elsa.Server.slnx -c {configuration}.");
                    continue;
                }

                TargetAssembly.AddProbeDirectory(Path.GetDirectoryName(appAssembly)!);
                hostAssemblyPaths.Add(appAssembly);
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
        var projectedFragments = new SortedDictionary<string, ContractFragment>(StringComparer.Ordinal);
        var featureIdsByFragment = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var expressionTypesByFragment = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var apiEndpoints = new List<OpenApiEndpoint>();
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

            // Serialize WITHOUT the endpoint body schemas. They are published once, in openapi.json's
            // components, and embedding them here too was 93% of the largest fragment (451 KB, of which
            // intrinsics were 5 KB). The endpoint entry keeps its request/response TYPE names, which are
            // exactly the component keys, so the schema is one hop away instead of duplicated 95 times.
            fragments.Add(fragment.Assembly, DeterministicJson.SerializeToBytes(WithoutEndpointSchemas(fragment)));
            projectedFragments.Add(fragment.Assembly, fragment);
            featureIdsByFragment[fragment.Assembly] = fragment.Features.Select(feature => feature.Id).ToArray();
            expressionTypesByFragment[fragment.Assembly] = fragment.Expressions?.Descriptors
                .Select(descriptor => descriptor.Type).ToArray() ?? [];
            apiEndpoints.AddRange(fragment.Endpoints.Select(endpoint => new OpenApiEndpoint(
                endpoint.Verb,
                endpoint.Route,
                fragment.Assembly,
                endpoint.FeatureId,
                endpoint.SuccessStatuses,
                endpoint.SuccessStatusCondition,
                endpoint.AllowsAnonymous,
                endpoint.Permissions,
                endpoint.RequestSchema,
                endpoint.RequestType,
                endpoint.ResponseSchema,
                endpoint.ResponseType,
                endpoint.ConfigurationDependent)));
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

        var hostsIndex = BuildHostsIndex(hostAssemblyPaths, featureIdsByFragment, expressionTypesByFragment);
        var hostsBytes = DeterministicJson.SerializeToBytes(hostsIndex);

        var openApiParts = OpenApiDocumentBuilder.BuildParts(apiEndpoints, ContractFragment.CurrentSchemaVersion);
        var openApiRoot = OpenApiDocumentBuilder.BuildRoot(openApiParts, ContractFragment.CurrentSchemaVersion);
        var apiBytes = DeterministicJson.SerializeToBytes(openApiRoot);
        var openApiPartBytes = openApiParts.ToDictionary(
            part => part.Key,
            part => DeterministicJson.SerializeToBytes(part.Value),
            StringComparer.Ordinal);

        var indexBytes = DeterministicJson.SerializeToBytes(ContractsIndexBuilder.Build(
            projectedFragments,
            apiEndpoints.ToDictionary(
                endpoint => $"{endpoint.Verb} {endpoint.Route}",
                endpoint => $"openapi/{OpenApiDocumentBuilder.GroupOf(endpoint)}.json",
                StringComparer.Ordinal)));

        var vocabularyBytes = DeterministicJson.SerializeToBytes(VocabularyProjector.Project());

        var manifest = new ContractsManifest(
            SchemaVersion: "1.0",
            Generator: GeneratorId,
            Fragments: fragments.Select(pair => new FragmentFingerprint(pair.Key, DeterministicJson.Fingerprint(pair.Value))).ToArray(),
            SubmitSchema: DeterministicJson.Fingerprint(submitSchemaBytes),
            Hosts: DeterministicJson.Fingerprint(hostsBytes),
            OpenApi: DeterministicJson.Fingerprint(apiBytes),
            Vocabularies: DeterministicJson.Fingerprint(vocabularyBytes),
            Counts: new ContractsManifestCounts(
                fragments.Count, counts.Features, counts.Activities, counts.Structures, counts.Intrinsics),
            Index: DeterministicJson.Fingerprint(indexBytes));

        var fragmentsDirectory = Path.Combine(outputDirectory, "fragments");
        if (Directory.Exists(fragmentsDirectory))
            Directory.Delete(fragmentsDirectory, recursive: true);
        foreach (var (name, bytes) in fragments)
            DeterministicJson.WriteFile(Path.Combine(fragmentsDirectory, name + ".json"), bytes);
        DeterministicJson.WriteFile(Path.Combine(outputDirectory, "submit-schema.json"), submitSchemaBytes);
        DeterministicJson.WriteFile(Path.Combine(outputDirectory, "hosts.json"), hostsBytes);
        var openApiDirectory = Path.Combine(outputDirectory, "openapi");
        if (Directory.Exists(openApiDirectory))
            Directory.Delete(openApiDirectory, recursive: true);
        foreach (var (group, bytes) in openApiPartBytes.OrderBy(part => part.Key, StringComparer.Ordinal))
            DeterministicJson.WriteFile(Path.Combine(openApiDirectory, group + ".json"), bytes);
        DeterministicJson.WriteFile(Path.Combine(outputDirectory, "openapi.json"), apiBytes);
        DeterministicJson.WriteFile(Path.Combine(outputDirectory, "index.json"), indexBytes);
        DeterministicJson.WriteFile(Path.Combine(outputDirectory, "vocabularies.json"), vocabularyBytes);
        DeterministicJson.WriteFile(Path.Combine(outputDirectory, "manifest.json"), DeterministicJson.SerializeToBytes(manifest));

        Console.WriteLine($"Merged {fragments.Count} contract fragments into {outputDirectory} " +
                          $"({counts.Features} features, {counts.Activities} activities, {counts.Structures} structures, {counts.Intrinsics} intrinsics).");
        return 0;
    }

    /// <summary>
    /// Which contract fragments each shipped host actually contains — the third term of consumer
    /// availability (spec 149 / RFC #1191). A fragment describes what an assembly contributes *if the
    /// assembly is present*; it is not a claim that any given host ships it. Enabling a feature whose
    /// assembly the host does not carry is a silent no-op ("requested N feature(s) that are not available
    /// in the runtime feature catalog"), so availability is
    /// <c>fragments ∩ shells.json ∩ hosts.json[host]</c>, not the two-way intersection the RFC first stated.
    /// Read from the host's <c>.deps.json</c> rather than its bin contents: deps.json is regenerated per
    /// build and cannot be polluted by assemblies left behind from an earlier branch.
    /// Note that a runtime-kind attribute could not express this — Elsa.Activities.Scripting and
    /// Elsa.Activities.Http both declare <c>elsa.server</c>, yet only one ships in Elsa.Workbench.
    /// </summary>
    private HostsIndex BuildHostsIndex(
        IReadOnlyList<string> hostAssemblyPaths,
        IReadOnlyDictionary<string, IReadOnlyList<string>> featureIdsByFragment,
        IReadOnlyDictionary<string, IReadOnlyList<string>> expressionTypesByFragment)
    {
        var known = featureIdsByFragment.Keys.ToHashSet(StringComparer.Ordinal);
        var hosts = new List<HostContractSet>();

        foreach (var hostAssemblyPath in hostAssemblyPaths.Order(StringComparer.Ordinal))
        {
            var hostName = Path.GetFileNameWithoutExtension(hostAssemblyPath);
            var depsPath = Path.Combine(Path.GetDirectoryName(hostAssemblyPath)!, hostName + ".deps.json");
            if (!File.Exists(depsPath))
            {
                diagnostics.Error(hostAssemblyPath, "ELSACT011",
                    $"Host '{hostName}' has no '{hostName}.deps.json'; its available-fragment set cannot be published.");
                continue;
            }

            IReadOnlyCollection<string> shipped;
            try
            {
                shipped = ReadDependencyLibraryNames(depsPath);
            }
            catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
            {
                diagnostics.Error(depsPath, "ELSACT011",
                    $"Host '{hostName}' dependency file could not be read: {exception.GetBaseException().Message}");
                continue;
            }

            var shippedFragments = shipped.Where(known.Contains).Order(StringComparer.Ordinal).ToArray();
            hosts.Add(new HostContractSet(
                hostName,
                shippedFragments.SelectMany(fragment => featureIdsByFragment[fragment]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                shippedFragments.SelectMany(fragment => expressionTypesByFragment[fragment]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                shippedFragments));
        }

        return new HostsIndex("1.0", hosts.OrderBy(host => host.Host, StringComparer.Ordinal).ToArray());
    }

    private static IReadOnlyCollection<string> ReadDependencyLibraryNames(string depsPath)
    {
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(depsPath));
        if (!document.RootElement.TryGetProperty("libraries", out var libraries) ||
            libraries.ValueKind != System.Text.Json.JsonValueKind.Object)
            return [];

        // deps.json library keys are "<name>/<version>"; the name alone identifies the assembly.
        return libraries.EnumerateObject()
            .Select(library => library.Name.Split('/')[0])
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Every src project except hosts under <c>src/Apps</c> (application composition, not feature surface).
    /// Contribution detection happens by projecting — a project with nothing to declare yields no fragment,
    /// so absence stays meaningful without any opt-in flag to forget (spec FR-001 completeness rule).
    /// </summary>
    /// <summary>
    /// Drops the inline endpoint body schemas before a fragment is written.
    /// </summary>
    /// <remarks>
    /// Navigation cost, not content: a benchmarked contracts-equipped agent cost MORE than one with no
    /// contracts at all, because it had to search a corpus it could not read. The median fragment is 3 KB;
    /// the outliers were 450 KB, and in every case the bulk was endpoint request/response schemas that
    /// <c>openapi.json</c> already publishes in <c>components/schemas</c>. Removing the duplicate keeps the
    /// fragment about the assembly's authoring surface, which is what a consumer opens it for.
    /// </remarks>
    private static ContractFragment WithoutEndpointSchemas(ContractFragment fragment) => fragment with
    {
        Endpoints = fragment.Endpoints
            .Select(endpoint => endpoint with { RequestSchema = null, ResponseSchema = null })
            .ToArray()
    };

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
