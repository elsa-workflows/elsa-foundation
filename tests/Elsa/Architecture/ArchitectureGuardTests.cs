using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class ArchitectureGuardTests
{
    private static readonly string[] AllowedCorePackageReferences =
    [
        "Microsoft.Extensions.Primitives",
        "Microsoft.Extensions.Options"
    ];

    private static readonly HashSet<(string Project, string Reference)> DeferredRuntimeDesignReferences =
    [
        ("Elsa.Workflows.Runtime.JavaScript", "Elsa.Workflows.Design.Core")
    ];

    // Documented exceptions to the §2.23.3 no-InternalsVisibleTo rule. Each entry needs a reason
    // recorded at the declaration site (csproj comment) and here. Additions require architect review.
    private static readonly HashSet<(string Project, string Target)> AllowedInternalsVisibleTo =
    [
        // The ExtensionBuilder subsystem is a host-private surface of ~80 interlocking internal types;
        // publicizing it to satisfy §2.23.3 would promote host-only contracts into public API. It now
        // lives in its own module (Elsa.Modularity.ExtensionBuilder) which exposes those internals to
        // the shared modularity test project (MD-3, Elsa 4 architecture review 2026-07).
        ("Elsa.Modularity.ExtensionBuilder", "Elsa.Modularity.Tests"),
        // Elsa.Server keeps a narrow exception for the host-only module-management registry builder
        // (ModuleManagementRegistryBuilder), exercised by ModuleManagementRegistryBuilderTests.
        ("Elsa.Server", "Elsa.Modularity.Tests")
    ];

    private static readonly Regex AssemblyInternalsVisibleToPattern = new(@"assembly\s*:\s*InternalsVisibleTo", RegexOptions.Compiled);

    [Fact]
    public void Solution_has_no_global_layer_marker_folders()
    {
        var solution = XDocument.Load(Path.Combine(RepoRoot, "Elsa.Server.slnx"));
        var folders = solution.Descendants("Folder")
            .Select(x => x.Attribute("Name")?.Value)
            .Where(x => x is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("/core/", folders);
        Assert.DoesNotContain("/modules/", folders);
    }

    [Fact]
    public void Project_paths_match_domain_tree_convention()
    {
        var mismatches = ProjectFiles()
            .Select(project => (Project: project, Expected: ExpectedProjectPath(project)))
            .Where(x => x.Project.RelativePath != x.Expected)
            .Select(x => $"{x.Project.Name}: expected {x.Expected}, actual {x.Project.RelativePath}")
            .ToList();

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public void Solution_folders_collapse_leaf_project_segments()
    {
        var projectDirectories = ProjectFiles()
            .Select(project => Path.GetDirectoryName(project.RelativePath)!)
            .ToHashSet(StringComparer.Ordinal);
        var expectedFolders = ProjectFiles()
            .ToDictionary(project => project.RelativePath, project => ExpectedSolutionFolder(project, projectDirectories), StringComparer.OrdinalIgnoreCase);
        var actualFolders = SolutionProjects()
            .ToDictionary(project => project.Path, project => project.Folder, StringComparer.OrdinalIgnoreCase);
        var mismatches = expectedFolders
            .Where(expected => !actualFolders.TryGetValue(expected.Key, out var actual) || actual != expected.Value)
            .Select(expected =>
            {
                actualFolders.TryGetValue(expected.Key, out var actual);
                return $"{expected.Key}: expected {expected.Value}, actual {actual ?? "<missing>"}";
            })
            .ToList();

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public void Core_projects_do_not_reference_implementation_projects()
    {
        var violations = ProjectFiles()
            .Where(project => project.Name.EndsWith(".Core", StringComparison.Ordinal))
            .SelectMany(project => ProjectReferences(project)
                .Where(reference => !IsCoreSafeReference(reference.Name))
                .Select(reference => $"{project.Name} -> {reference.Name}"))
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Core_projects_do_not_reference_heavy_packages()
    {
        var violations = ProjectFiles()
            .Where(project => project.Name.EndsWith(".Core", StringComparison.Ordinal))
            .SelectMany(project => PackageReferences(project)
                .Where(package => !IsCoreSafePackage(package))
                .Select(package => $"{project.Name} -> {package}"))
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void In_scope_persistence_contract_projects_remain_free_of_concrete_provider_dependencies()
    {
        var projects = ProjectFiles().ToArray();
        var projectsByName = projects.ToDictionary(project => project.Name, StringComparer.Ordinal);
        var missing = PersistenceProviderNeutralityBoundary.ProjectNames
            .Where(projectName => !projectsByName.ContainsKey(projectName))
            .ToArray();
        Assert.True(missing.Length == 0, "Missing provider-neutral persistence projects: " + string.Join(", ", missing));

        var violations = new EfCoreSurfaceScanner(RepoRoot).FindProtectedProviderNeutralityViolations();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Elsa_primitives_has_no_external_package_references()
    {
        var primitives = ProjectFiles().Single(x => x.Name == "Elsa.Primitives");

        Assert.Empty(PackageReferences(primitives));
    }

    [Fact]
    public void Runtime_projects_do_not_add_design_references()
    {
        var violations = ProjectFiles()
            .Where(IsRuntimeProject)
            .SelectMany(project => ProjectReferences(project)
                .Where(reference =>
                    reference.Name.StartsWith("Elsa.Workflows.Design.", StringComparison.Ordinal) ||
                    reference.Name.StartsWith("Elsa.Activities.Design.", StringComparison.Ordinal))
                .Where(reference => !DeferredRuntimeDesignReferences.Contains((project.Name, reference.Name)))
                .Select(reference => $"{project.Name} -> {reference.Name}"))
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Runtime_projects_do_not_reference_elsa3_compatibility_projects()
    {
        var violations = ProjectFiles()
            .Where(IsRuntimeProject)
            .SelectMany(project => ProjectReferences(project)
                .Where(reference => reference.Name.StartsWith("Elsa3.", StringComparison.Ordinal))
                .Select(reference => $"{project.Name} -> {reference.Name}"))
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("shells.json")]
    [InlineData("shells.baseline.json")]
    public void Server_default_shell_enables_flowchart_runtime_feature(string fileName)
    {
        var features = ReadDefaultShellFeatures(ServerConfigurationPath(fileName));

        Assert.True(
            features.ContainsKey("ActivitiesFlowchart"),
            $"{fileName} must enable ActivitiesFlowchart so Flowchart root activities can resolve runtime services.");
    }

    [Theory]
    [InlineData("shells.json")]
    [InlineData("shells.baseline.json")]
    public void Server_default_shell_enables_graph_authoring_when_activity_design_is_enabled(string fileName)
    {
        var features = ReadDefaultShellFeatures(ServerConfigurationPath(fileName));

        Assert.True(features.ContainsKey("ActivitiesDesignApi"));
        Assert.True(
            features.ContainsKey("ActivitiesGraphDesign"),
            $"{fileName} must enable ActivitiesGraphDesign so Activity Design advertises the graph authoring provider.");
    }

    [Fact]
    public void Docker_reference_shell_enables_graph_authoring_with_activity_design()
    {
        var path = Path.Combine(RepoRoot, "docker", "compose", "elsa-server.shells.json");
        var features = ReadDefaultShellFeatures(path);

        Assert.True(features.ContainsKey("ActivitiesDesignApi"));
        Assert.True(features.ContainsKey("ActivitiesGraphDesign"));
    }

    [Theory]
    [InlineData("shells.json")]
    [InlineData("shells.baseline.json")]
    public void Server_default_shell_enables_http_endpoint_activity_feature(string fileName)
    {
        var features = ReadDefaultShellFeatures(ServerConfigurationPath(fileName));

        Assert.True(
            features.ContainsKey("ActivitiesHttp"),
            $"{fileName} must enable ActivitiesHttp so a clean server checkout can publish and serve HTTP-triggered workflows.");
    }

    [Theory]
    [InlineData("shells.json")]
    [InlineData("shells.baseline.json")]
    public void Server_default_shell_enables_coalesced_checkpoint_persistence(string fileName)
    {
        var features = ReadDefaultShellFeatures(ServerConfigurationPath(fileName));
        var settings = Assert.IsType<JsonObject>(features["WorkflowsRuntimeCheckpointPersistence"]);

        Assert.Equal("Coalesced", settings["Mode"]?.GetValue<string>());
        Assert.Equal(50, settings["MaxSegmentCheckpoints"]?.GetValue<int>());
    }

    [Theory]
    [InlineData("shells.json")]
    [InlineData("shells.baseline.json")]
    public void Server_default_shell_selects_one_unified_Groundwork_persistence_leaf(string fileName)
    {
        var features = ReadDefaultShellFeatures(ServerConfigurationPath(fileName));

        Assert.True(features.ContainsKey("GroundworkUnifiedPersistenceSqlite"),
            $"{fileName} must select one Groundwork SQLite target for the six provider-level persistence families.");
        Assert.False(features.ContainsKey("GroundworkRuntimePersistenceSqlite"),
            $"{fileName} must not compose a second Groundwork provider leaf.");
        Assert.False(features.ContainsKey("GroundworkPublishingPersistenceSqlite"),
            $"{fileName} must not select the retired standalone Publishing lane.");
        Assert.False(features.ContainsKey("WorkflowsDesignPersistenceEFCoreSqlite"),
            $"{fileName} must not override unified workflow-design persistence with EF Core.");
        Assert.False(features.ContainsKey("ActivitiesDesignPersistenceEFCoreSqlite"),
            $"{fileName} must not override unified activity-design persistence with EF Core.");
    }

    [Fact]
    public void Groundwork_production_reads_use_only_admitted_bounded_query_APIs()
    {
        var productionTargets = XDocument.Load(Path.Combine(RepoRoot, "src", "Elsa", "Directory.Build.targets"));
        var warningsAsErrors = productionTargets.Descendants("WarningsAsErrors").Single().Value;
        Assert.Contains("GW0004", warningsAsErrors.Split(';', StringSplitOptions.RemoveEmptyEntries));

        const string unitOfWorkAdapterPath = "src/Elsa/Persistence/Groundwork/Stores/GroundworkDocumentUnitOfWorkStore.cs";
        var unitOfWorkSource = File.ReadAllText(Path.Combine(RepoRoot, unitOfWorkAdapterPath));
        Assert.Single(Regex.Matches(unitOfWorkSource, @"\bDocumentStoreQuery\b").Cast<Match>());
        Assert.Equal(3, Regex.Matches(unitOfWorkSource, @"\bPortableDocumentQuery\b").Count);
        Assert.Single(Regex.Matches(unitOfWorkSource, "Groundwork document unit-of-work adapter does not query documents.").Cast<Match>());

        const string scopedAdapterPath = "src/Elsa/Persistence/Groundwork/Stores/GroundworkScopedDocumentStore.cs";
        var scopedAdapterSource = File.ReadAllText(Path.Combine(RepoRoot, scopedAdapterPath));
        Assert.Single(Regex.Matches(scopedAdapterSource, @"\bDocumentStoreQuery\b").Cast<Match>());
        Assert.Equal(3, Regex.Matches(scopedAdapterSource, @"\bPortableDocumentQuery\b").Count);
        Assert.Equal(7, Regex.Matches(scopedAdapterSource, @"WithDocumentsAsync\(store => store\.").Count);

        var forbiddenTypes = new[] { "DocumentStoreQuery", "PortableDocumentQuery" };
        var violations = Directory.EnumerateFiles(Path.Combine(RepoRoot, "src", "Elsa"), "*.cs", SearchOption.AllDirectories)
            .Select(file => new
            {
                File = file,
                RelativePath = Path.GetRelativePath(RepoRoot, file).Replace(Path.DirectorySeparatorChar, '/')
            })
            .Where(candidate => candidate.RelativePath.Contains("/Groundwork/", StringComparison.Ordinal))
            .Where(candidate =>
                !StringComparer.Ordinal.Equals(candidate.RelativePath, unitOfWorkAdapterPath) &&
                !StringComparer.Ordinal.Equals(candidate.RelativePath, scopedAdapterPath))
            .SelectMany(candidate =>
            {
                var source = StripCommentsAndStringLiterals(File.ReadAllText(candidate.File));
                return forbiddenTypes
                    .Where(type => Regex.IsMatch(source, $@"\b{type}\b"))
                    .Select(type => $"{candidate.RelativePath}: {type}");
            })
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Server_catalogs_http_endpoint_feature_and_its_runtime_dependency()
    {
        var server = ProjectFiles().Single(project => project.Name == "Elsa.Server");
        var references = ProjectReferences(server).Select(reference => reference.Name).ToHashSet(StringComparer.Ordinal);
        var program = File.ReadAllText(Path.Combine(RepoRoot, "src", "Apps", "Elsa.Server", "Program.cs"));

        Assert.Contains("Elsa.Activities.Http", references);
        Assert.Contains("Elsa.Workflows.Runtime.Http", references);
        Assert.Contains(".WithHostAssemblies()", program, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("shells.json")]
    [InlineData("shells.baseline.json")]
    public void Server_catalogs_and_enables_dashboard_dependencies_in_the_default_shell(string fileName)
    {
        var server = ProjectFiles().Single(project => project.Name == "Elsa.Server");
        var references = ProjectReferences(server).Select(reference => reference.Name).ToHashSet(StringComparer.Ordinal);
        var program = File.ReadAllText(Path.Combine(RepoRoot, "src", "Apps", "Elsa.Server", "Program.cs"));
        var features = ReadDefaultShellFeatures(ServerConfigurationPath(fileName));

        Assert.Contains("Elsa.Workflows.Design.Validations", references);
        Assert.Contains("Elsa.Workflows.Runtime.Resumption", references);
        Assert.Contains("typeof(Elsa.Workflows.Design.Validations.WorkflowDesignValidationsFeature).Assembly", program, StringComparison.Ordinal);
        Assert.Contains("typeof(WorkflowsRuntimeResumptionFeature).Assembly", program, StringComparison.Ordinal);
        Assert.Contains("WorkflowDesignValidations", features);
        Assert.Contains("WorkflowsRuntimeResumption", features);
    }

    [Fact]
    public void Server_catalogs_graph_design_separately_from_graph_runtime()
    {
        var server = ProjectFiles().Single(project => project.Name == "Elsa.Server");
        var references = ProjectReferences(server).Select(reference => reference.Name).ToHashSet(StringComparer.Ordinal);
        var program = File.ReadAllText(Path.Combine(RepoRoot, "src", "Apps", "Elsa.Server", "Program.cs"));

        Assert.Contains("Elsa.Activities.Graph.Design", references);
        Assert.Contains("Elsa.Activities.Graph.Runtime", references);
        Assert.Contains("typeof(GraphActivitiesDesignFeature).Assembly", program, StringComparison.Ordinal);
        Assert.Contains("typeof(GraphActivitiesRuntimeFeature).Assembly", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Server_catalogs_workflow_design_validations_required_by_dashboard()
    {
        var server = ProjectFiles().Single(project => project.Name == "Elsa.Server");
        var references = ProjectReferences(server).Select(reference => reference.Name).ToHashSet(StringComparer.Ordinal);
        var program = File.ReadAllText(Path.Combine(RepoRoot, "src", "Apps", "Elsa.Server", "Program.cs"));

        Assert.Contains("Elsa.Workflows.Design.Validations", references);
        Assert.Contains(
            "typeof(Elsa.Workflows.Design.Validations.WorkflowDesignValidationsFeature).Assembly",
            program,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Workflows_runtime_core_does_not_use_authored_workflow_models()
    {
        string[] forbiddenPatterns =
        [
            "Elsa.Workflows.Design",
            "WorkflowDefinitionState",
            "ActivityNode"
        ];
        var runtimeCoreDirectory = Path.Combine(RepoRoot, "src", "Elsa", "Workflows", "Runtime", "Core");
        var violations = Directory.EnumerateFiles(runtimeCoreDirectory, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file =>
            {
                var text = StripCommentsAndStringLiterals(File.ReadAllText(file));
                return forbiddenPatterns
                    .Where(pattern => text.Contains(pattern, StringComparison.Ordinal))
                    .Select(pattern => $"{Path.GetRelativePath(RepoRoot, file).Replace(Path.DirectorySeparatorChar, '/')}: {pattern}");
            })
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact] // spec 006 T050 (SC-001) — no project in the activity-construction runtime path references any Design project.
    public void Activity_construction_runtime_path_has_no_design_reference() =>
        AssertNoForbiddenProjectReferences(
            [
                "Elsa.Activities.Runtime",
                "Elsa.Activities.Runtime.Core",
                "Elsa.Activities.Primitives",
                "Elsa.Activities.Graph.Runtime",
                "Elsa.Activities.DispatchWorkflow.Runtime",
            ],
            IsDesignReference);

    [Fact]
    public void Dispatch_workflow_modules_preserve_runtime_design_and_transport_boundaries()
    {
        var projects = ProjectFiles().ToDictionary(project => project.Name, StringComparer.Ordinal);
        var runtime = projects["Elsa.Activities.DispatchWorkflow.Runtime"];
        var design = projects["Elsa.Activities.DispatchWorkflow.Design"];
        var runtimeReferences = ProjectReferences(runtime).Select(reference => reference.Name).ToHashSet(StringComparer.Ordinal);
        var designReferences = ProjectReferences(design).Select(reference => reference.Name).ToHashSet(StringComparer.Ordinal);
        var forbiddenReferences = new[]
        {
            "Elsa.Activities.Composition.Runtime",
            "Elsa.Workflows.Design.Core",
            "Elsa.Studio"
        };

        Assert.Contains("Elsa.Workflows.Runtime.Core", runtimeReferences);
        Assert.DoesNotContain("Elsa.Workflows.Runtime", runtimeReferences);
        Assert.DoesNotContain("Elsa.Workflows.Runtime.Resumption", runtimeReferences);
        Assert.Contains(
            "WorkflowsRuntimeResumption",
            File.ReadAllText(Path.Join(Path.GetDirectoryName(runtime.FullPath)!, "DispatchWorkflowRuntimeFeature.cs")),
            StringComparison.Ordinal);
        Assert.DoesNotContain(forbiddenReferences, runtimeReferences.Contains);
        Assert.DoesNotContain("Elsa.Activities.Composition.Runtime", designReferences);
        Assert.DoesNotContain(PackageReferences(runtime), package => package.Contains("MassTransit", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(PackageReferences(design), package => package.Contains("MassTransit", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(PackageReferences(runtime), package => package.Contains("Broker", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(PackageReferences(design), package => package.Contains("Broker", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(PackageReferences(runtime), package => package.Contains("ServiceBus", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(PackageReferences(design), package => package.Contains("ServiceBus", StringComparison.OrdinalIgnoreCase));

        var sourceFiles = new[] { runtime, design }
            .SelectMany(project => Directory.EnumerateFiles(Path.GetDirectoryName(project.FullPath)!, "*.cs", SearchOption.AllDirectories));
        var workflowDefinitionActivityReferences = sourceFiles
            .Where(file => StripCommentsAndStringLiterals(File.ReadAllText(file)).Contains("WorkflowDefinitionActivity", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(RepoRoot, file))
            .ToArray();
        Assert.Empty(workflowDefinitionActivityReferences);

        var forbiddenContractTerms = new[]
        {
            "MassTransit",
            "ServiceBus",
            "RoutingChannel",
            "TransportSelection",
            "Priority",
            "Affinity"
        };
        var transportContractReferences = sourceFiles
            .SelectMany(file =>
            {
                var text = StripCommentsAndStringLiterals(File.ReadAllText(file));
                return forbiddenContractTerms
                    .Where(term => text.Contains(term, StringComparison.Ordinal))
                    .Select(term => $"{Path.GetRelativePath(RepoRoot, file).Replace(Path.DirectorySeparatorChar, '/')}: {term}");
            })
            .ToArray();
        Assert.Empty(transportContractReferences);
    }

    [Fact] // spec 006 T053 (SC-006) — the seam's feature projects do not reference one another (G4).
    public void Activity_construction_feature_projects_do_not_reference_each_other()
    {
        string[] featureProjects =
        [
            "Elsa.Activities.Primitives",
            "Elsa.Activities.Graph.Runtime",
            "Elsa.Activities.Graph.Design",
            "Elsa.Activities.Design.Reconciliation.Clr",
        ];
        var featureSet = featureProjects.ToHashSet(StringComparer.Ordinal);

        // Scoped to cross-references *among* the seam features (SC-006). A new seam feature must be added
        // to this list to be covered — the check under-covers silently as the seam grows.
        AssertNoForbiddenProjectReferences(featureProjects, reference => featureSet.Contains(reference.Name));
    }

    // Shared skeleton for the two SC-001/SC-006 project-reference facts: verify every named project exists,
    // then assert none of them declares a <ProjectReference> the predicate forbids. Only DIRECT edges are
    // checked — transitive pulls are the reference graph's own concern (and, for the runtime side, are also
    // covered by Runtime_projects_do_not_add_design_references, which spans every runtime project).
    private static void AssertNoForbiddenProjectReferences(string[] projectNames, Func<ProjectInfo, bool> isForbidden)
    {
        var nameSet = projectNames.ToHashSet(StringComparer.Ordinal);
        var found = ProjectFiles().Where(p => nameSet.Contains(p.Name)).ToList();

        var missing = nameSet.Except(found.Select(p => p.Name)).ToList();
        Assert.True(missing.Count == 0, "Missing expected projects: " + string.Join(", ", missing));

        var violations = found
            .SelectMany(project => ProjectReferences(project)
                .Where(isForbidden)
                .Select(reference => $"{project.Name} -> {reference.Name}"))
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void InternalsVisibleTo_occurrences_are_limited_to_documented_exceptions()
    {
        var csprojViolations = ProjectFiles()
            .SelectMany(project => XDocument.Load(project.FullPath)
                .Descendants("InternalsVisibleTo")
                .Select(x => x.Attribute("Include")?.Value)
                .OfType<string>()
                .Where(target => !AllowedInternalsVisibleTo.Contains((project.Name, target)))
                .Select(target => $"{project.Name} -> {target} ({project.RelativePath})"));

        var attributeViolations = ProjectFiles()
            .SelectMany(project => Directory.EnumerateFiles(Path.GetDirectoryName(project.FullPath)!, "*.cs", SearchOption.AllDirectories)
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                               !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Where(file => AssemblyInternalsVisibleToPattern.IsMatch(StripCommentsAndStringLiterals(File.ReadAllText(file))))
                .Select(file => $"{project.Name} -> [assembly: InternalsVisibleTo] in {Path.GetRelativePath(RepoRoot, file).Replace(Path.DirectorySeparatorChar, '/')}"));

        var violations = csprojViolations.Concat(attributeViolations).ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Projects_do_not_declare_duplicate_project_references()
    {
        var violations = ProjectFiles()
            .SelectMany(project => XDocument.Load(project.FullPath)
                .Descendants("ProjectReference")
                .Select(x => x.Attribute("Include")?.Value)
                .OfType<string>()
                .Select(include => include.Replace('\\', '/'))
                .GroupBy(include => include, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => $"{project.Name}: duplicate ProjectReference {group.Key}"))
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact] // spec 006 T052 (SC-002) — the deleted 005 implementation-descriptor family is gone from production code.
    public void No_production_code_references_deleted_implementation_descriptor_types()
    {
        // Each token is a distinct deleted identifier; "ImplementationDescriptor" / "ActivityImplementationResolver"
        // subsume the Clr*/Workflow*/registry/source/resolver variants via substring match.
        string[] forbiddenTokens =
        [
            "IImplementationDescriptor",
            "ImplementationDescriptor",
            "IImplementationDescriptorSource",
            "ImplementationDescriptorRegistry",
            "OnImplementationDescriptorsInitializing",
            "IActivityImplementationResolver",
            "ActivityImplementationResolver",
        ];

        var sourceRoot = Path.Combine(RepoRoot, "src");
        var violations = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsGeneratedScratchFile(file) && !IsBuildArtifactFile(file))
            .SelectMany(file =>
            {
                var code = StripCommentsAndStringLiterals(File.ReadAllText(file));
                return forbiddenTokens
                    .Where(token => code.Contains(token, StringComparison.Ordinal))
                    .Select(token => $"{Path.GetRelativePath(RepoRoot, file).Replace(Path.DirectorySeparatorChar, '/')}: {token}");
            })
            .Distinct()
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Legacy_activity_factory_and_constructor_registry_are_absent()
    {
        string[] removedPaths =
        [
            "src/Elsa/Activities/Runtime/Services/ActivityFactory.cs",
            "src/Elsa/Activities/Runtime/Services/ActivityConstructorRegistry.cs",
            "src/Elsa/Activities/Runtime/Core/Contracts/IActivityFactory.cs",
            "src/Elsa/Activities/Runtime/Core/Contracts/IActivityConstructor.cs",
            "src/Elsa/Activities/Runtime/Core/Contracts/IActivityConstructorRegistry.cs"
        ];
        var violations = removedPaths.Where(relativePath =>
            File.Exists(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Empty(violations);
    }

    private static bool IsDesignReference(ProjectInfo reference) =>
        reference.Name.StartsWith("Elsa.", StringComparison.Ordinal) &&
        reference.Name.Contains(".Design", StringComparison.Ordinal);

    private static bool IsGeneratedScratchFile(string filePath) =>
        filePath.Replace(Path.DirectorySeparatorChar, '/').Contains("/extension-builder/projects/", StringComparison.Ordinal);

    // Build output under src/**/obj and src/**/bin (AssemblyInfo, GlobalUsings.g.cs, EF/source-generator
    // scaffolds) is not source; scanning it would make a token sweep depend on build state.
    private static bool IsBuildArtifactFile(string filePath)
    {
        var normalized = filePath.Replace(Path.DirectorySeparatorChar, '/');
        return normalized.Contains("/obj/", StringComparison.Ordinal) || normalized.Contains("/bin/", StringComparison.Ordinal);
    }

    private static bool IsRuntimeProject(ProjectInfo project) =>
        project.Name == "Elsa.Workflows.Runtime"
        || project.Name.StartsWith("Elsa.Workflows.Runtime.", StringComparison.Ordinal)
        || project.Name == "Elsa.Activities.Runtime"
        || project.Name.StartsWith("Elsa.Activities.Runtime.", StringComparison.Ordinal)
        || project.Name == "Elsa.Activities.Graph.Runtime";

    [Fact]
    public void Source_scan_strips_interpolated_string_text_but_preserves_interpolation_code()
    {
        const string text = "var message = $\"ActivityNode literal {typeof(ActivityNode).Name}\";";
        var sanitized = StripCommentsAndStringLiterals(text);

        Assert.DoesNotContain("ActivityNode literal", sanitized, StringComparison.Ordinal);
        Assert.Contains("typeof(ActivityNode)", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_scan_strips_interpolated_raw_string_text_but_preserves_interpolation_code()
    {
        const string text = "var message = $\"\"\"ActivityNode literal {typeof(ActivityNode).Name}\"\"\";";
        var sanitized = StripCommentsAndStringLiterals(text);

        Assert.DoesNotContain("ActivityNode literal", sanitized, StringComparison.Ordinal);
        Assert.Contains("typeof(ActivityNode)", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_scan_preserves_multi_dollar_raw_interpolation_code_with_nested_braces()
    {
        const string text = "var message = $$\"\"\"ActivityNode literal {{ new { Name = typeof(ActivityNode).Name } }}\"\"\";";
        var sanitized = StripCommentsAndStringLiterals(text);

        Assert.DoesNotContain("ActivityNode literal", sanitized, StringComparison.Ordinal);
        Assert.Contains("typeof(ActivityNode)", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_scan_strips_raw_string_text()
    {
        const string text = "\"\"\"ActivityNode literal\"\"\"";
        var sanitized = StripCommentsAndStringLiterals(text);

        Assert.DoesNotContain("ActivityNode", sanitized, StringComparison.Ordinal);
    }

    private static bool IsCoreSafeReference(string referenceName) =>
        referenceName.EndsWith(".Core", StringComparison.Ordinal) ||
        referenceName == "Elsa.Primitives" ||
        referenceName.EndsWith(".Primitives", StringComparison.Ordinal);

    private static bool IsCoreSafePackage(string packageName) =>
        AllowedCorePackageReferences.Contains(packageName) ||
        packageName.EndsWith(".Abstractions", StringComparison.Ordinal);

    private static IEnumerable<ProjectInfo> ProjectFiles()
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            var project = ProjectInfo.From(RepoRoot, file);
            if (IsGeneratedScratchProject(project))
                continue;

            yield return project;
        }

        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, "tests"), "*.csproj", SearchOption.AllDirectories))
            yield return ProjectInfo.From(RepoRoot, file);
    }

    // The extension-builder feature writes runtime-generated scratch projects under guid-named
    // project/snapshot folders (gitignored, never part of the solution). Exclude them from the
    // domain-tree convention checks so generated artifacts are not enshrined in the slnx.
    private static bool IsGeneratedScratchProject(ProjectInfo project) =>
        project.RelativePath.Contains("/extension-builder/projects/", StringComparison.Ordinal);

    private static IEnumerable<SolutionProjectInfo> SolutionProjects()
    {
        var solution = XDocument.Load(Path.Combine(RepoRoot, "Elsa.Server.slnx"));
        foreach (var folder in solution.Descendants("Folder"))
        {
            var folderName = folder.Attribute("Name")?.Value;
            if (folderName is null)
                continue;

            foreach (var project in folder.Elements("Project"))
            {
                var path = project.Attribute("Path")?.Value;
                if (path is not null)
                    yield return new SolutionProjectInfo(folderName, path.Replace('\\', '/'));
            }
        }
    }

    private static IEnumerable<ProjectInfo> ProjectReferences(ProjectInfo project)
    {
        var document = XDocument.Load(project.FullPath);
        foreach (var include in document.Descendants("ProjectReference").Select(x => x.Attribute("Include")?.Value).OfType<string>())
        {
            var normalizedInclude = include.Replace('\\', Path.DirectorySeparatorChar);
            var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project.FullPath)!, normalizedInclude));
            yield return ProjectInfo.From(RepoRoot, path);
        }
    }

    private static IEnumerable<string> PackageReferences(ProjectInfo project)
    {
        var document = XDocument.Load(project.FullPath);
        return document.Descendants("PackageReference")
            .Where(x => !IsBuildTimeOnlyReference(x))
            .Select(x => x.Attribute("Include")?.Value)
            .OfType<string>();
    }

    private static bool IsBuildTimeOnlyReference(XElement reference)
    {
        var attribute = reference.Attribute("PrivateAssets")?.Value;
        if (string.Equals(attribute, "all", StringComparison.OrdinalIgnoreCase))
            return true;

        var child = reference.Elements("PrivateAssets").FirstOrDefault()?.Value;
        return string.Equals(child, "all", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonObject ReadDefaultShellFeatures(string path)
    {
        var document = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidOperationException($"{Path.GetFileName(path)} is not a JSON object.");

        return document["CShells"]?["Shells"]?["default"]?["Features"] as JsonObject
            ?? throw new InvalidOperationException($"{Path.GetFileName(path)} must contain CShells.Shells.default.Features.");
    }

    private static string ServerConfigurationPath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (Path.IsPathRooted(fileName) || !StringComparer.Ordinal.Equals(Path.GetFileName(fileName), fileName))
            throw new ArgumentException("The server configuration name must be a relative file name.", nameof(fileName));

        return Path.Join(RepoRoot, "src", "Apps", "Elsa.Server", fileName);
    }

    private static string StripCommentsAndStringLiterals(string text)
    {
        var sanitized = new char[text.Length];
        var state = SourceScanState.Code;
        var interpolationReturnState = SourceScanState.Code;
        var interpolationCloseBraceCount = 1;
        var interpolationDepth = 0;
        var rawStringDollarCount = 0;
        var rawStringQuoteCount = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var current = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            switch (state)
            {
                case SourceScanState.Code when current == '/' && next == '/':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    state = SourceScanState.LineComment;
                    break;
                case SourceScanState.Code when current == '/' && next == '*':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    state = SourceScanState.BlockComment;
                    break;
                case SourceScanState.Code when TryReadRawStringStart(text, i, out var rawStringPrefixLength, out rawStringQuoteCount, out var detectedRawStringDollarCount):
                    rawStringDollarCount = detectedRawStringDollarCount;
                    for (var j = 0; j < rawStringPrefixLength; j++)
                        sanitized[i + j] = ' ';
                    i += rawStringPrefixLength - 1;
                    state = rawStringDollarCount == 0 ? SourceScanState.RawString : SourceScanState.InterpolatedRawString;
                    break;
                case SourceScanState.Code when current == '$' && next == '"':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    state = SourceScanState.InterpolatedString;
                    break;
                case SourceScanState.Code when current == '$' && next == '@' && i + 2 < text.Length && text[i + 2] == '"':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    sanitized[++i] = ' ';
                    state = SourceScanState.InterpolatedVerbatimString;
                    break;
                case SourceScanState.Code when current == '@' && next == '$' && i + 2 < text.Length && text[i + 2] == '"':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    sanitized[++i] = ' ';
                    state = SourceScanState.InterpolatedVerbatimString;
                    break;
                case SourceScanState.Code when current == '@' && next == '"':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    state = SourceScanState.VerbatimString;
                    break;
                case SourceScanState.Code when current == '"':
                    sanitized[i] = ' ';
                    state = SourceScanState.String;
                    break;
                case SourceScanState.Code when current == '\'':
                    sanitized[i] = ' ';
                    state = SourceScanState.Character;
                    break;
                case SourceScanState.Code:
                    sanitized[i] = current;
                    break;
                case SourceScanState.LineComment when current is '\r' or '\n':
                    sanitized[i] = current;
                    state = SourceScanState.Code;
                    break;
                case SourceScanState.BlockComment when current == '*' && next == '/':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    state = SourceScanState.Code;
                    break;
                case SourceScanState.String when current == '\\' && next != '\0':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    break;
                case SourceScanState.String when current == '"':
                    sanitized[i] = ' ';
                    state = SourceScanState.Code;
                    break;
                case SourceScanState.VerbatimString when current == '"' && next == '"':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    break;
                case SourceScanState.VerbatimString when current == '"':
                    sanitized[i] = ' ';
                    state = SourceScanState.Code;
                    break;
                case SourceScanState.RawString when HasRun(text, i, '"', rawStringQuoteCount):
                    for (var j = 0; j < rawStringQuoteCount; j++)
                        sanitized[i + j] = ' ';
                    i += rawStringQuoteCount - 1;
                    rawStringQuoteCount = 0;
                    state = SourceScanState.Code;
                    break;
                case SourceScanState.InterpolatedRawString when HasRun(text, i, '"', rawStringQuoteCount):
                    for (var j = 0; j < rawStringQuoteCount; j++)
                        sanitized[i + j] = ' ';
                    i += rawStringQuoteCount - 1;
                    rawStringDollarCount = 0;
                    rawStringQuoteCount = 0;
                    state = SourceScanState.Code;
                    break;
                case SourceScanState.InterpolatedRawString when HasRun(text, i, '{', rawStringDollarCount):
                    for (var j = 0; j < rawStringDollarCount; j++)
                        sanitized[i + j] = '{';
                    i += rawStringDollarCount - 1;
                    interpolationDepth = 1;
                    interpolationCloseBraceCount = rawStringDollarCount;
                    interpolationReturnState = state;
                    state = SourceScanState.InterpolationExpression;
                    break;
                case SourceScanState.InterpolatedRawString:
                    sanitized[i] = current is '\r' or '\n' ? current : ' ';
                    break;
                case SourceScanState.InterpolatedString when current == '\\' && next != '\0':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    break;
                case SourceScanState.InterpolatedString when current == '{' && next == '{':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    break;
                case SourceScanState.InterpolatedString when current == '{':
                    sanitized[i] = current;
                    interpolationDepth = 1;
                    interpolationCloseBraceCount = 1;
                    interpolationReturnState = state;
                    state = SourceScanState.InterpolationExpression;
                    break;
                case SourceScanState.InterpolatedString when current == '}':
                    sanitized[i] = next == '}' ? ' ' : current;
                    if (next == '}')
                        sanitized[++i] = ' ';
                    break;
                case SourceScanState.InterpolatedString when current == '"':
                    sanitized[i] = ' ';
                    state = SourceScanState.Code;
                    break;
                case SourceScanState.InterpolatedVerbatimString when current == '"' && next == '"':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    break;
                case SourceScanState.InterpolatedVerbatimString when current == '{' && next == '{':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    break;
                case SourceScanState.InterpolatedVerbatimString when current == '{':
                    sanitized[i] = current;
                    interpolationDepth = 1;
                    interpolationCloseBraceCount = 1;
                    interpolationReturnState = state;
                    state = SourceScanState.InterpolationExpression;
                    break;
                case SourceScanState.InterpolatedVerbatimString when current == '}':
                    sanitized[i] = next == '}' ? ' ' : current;
                    if (next == '}')
                        sanitized[++i] = ' ';
                    break;
                case SourceScanState.InterpolatedVerbatimString when current == '"':
                    sanitized[i] = ' ';
                    state = SourceScanState.Code;
                    break;
                case SourceScanState.InterpolationExpression when current == '{':
                    sanitized[i] = current;
                    interpolationDepth++;
                    break;
                case SourceScanState.InterpolationExpression when current == '}' && interpolationDepth > 1:
                    sanitized[i] = current;
                    interpolationDepth--;
                    break;
                case SourceScanState.InterpolationExpression when HasRun(text, i, '}', interpolationCloseBraceCount):
                    for (var j = 0; j < interpolationCloseBraceCount; j++)
                        sanitized[i + j] = '}';
                    i += interpolationCloseBraceCount - 1;
                    interpolationDepth--;
                    if (interpolationDepth == 0)
                    {
                        interpolationCloseBraceCount = 1;
                        state = interpolationReturnState;
                    }
                    break;
                case SourceScanState.InterpolationExpression when current == '}':
                    sanitized[i] = current;
                    interpolationDepth--;
                    if (interpolationDepth == 0)
                        state = interpolationReturnState;
                    break;
                case SourceScanState.InterpolationExpression:
                    sanitized[i] = current;
                    break;
                case SourceScanState.Character when current == '\\' && next != '\0':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    break;
                case SourceScanState.Character when current == '\'':
                    sanitized[i] = ' ';
                    state = SourceScanState.Code;
                    break;
                default:
                    sanitized[i] = current is '\r' or '\n' ? current : ' ';
                    break;
            }
        }

        return new string(sanitized);
    }

    private static bool TryReadRawStringStart(string text, int index, out int prefixLength, out int quoteCount, out int dollarCount)
    {
        prefixLength = 0;
        quoteCount = 0;
        dollarCount = 0;

        var quoteIndex = index;
        while (quoteIndex < text.Length && text[quoteIndex] == '$')
            quoteIndex++;
        dollarCount = quoteIndex - index;

        if (quoteIndex == index && text[index] != '"')
            return false;

        quoteCount = CountRun(text, quoteIndex, '"');
        if (quoteCount < 3)
        {
            quoteCount = 0;
            return false;
        }

        prefixLength = quoteIndex - index + quoteCount;
        return true;
    }

    private static bool HasRun(string text, int index, char value, int count) => CountRun(text, index, value) >= count;

    private static int CountRun(string text, int index, char value)
    {
        var count = 0;
        while (index + count < text.Length && text[index + count] == value)
            count++;

        return count;
    }

    private static string ExpectedProjectPath(ProjectInfo project)
    {
        if (project.Name == "Elsa.Server")
            return "src/Apps/Elsa.Server/Elsa.Server.csproj";

        if (project.Name == "Elsa.Architecture.Tests")
            return "tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj";

        if (project.Name == "Elsa.Primitives")
            return "src/Elsa/Primitives/Primitives/Elsa.Primitives.csproj";

        if (project.Name.StartsWith("Elsa3.", StringComparison.Ordinal) && project.RelativePath.StartsWith("tests/", StringComparison.Ordinal))
            return $"tests/Elsa3/{string.Join('/', project.Name.Split('.')[1..])}/{project.Name}.csproj";

        if (project.Name.StartsWith("Elsa3.", StringComparison.Ordinal))
            return $"src/Elsa3/{string.Join('/', project.Name.Split('.')[1..])}/{project.Name}.csproj";

        if (project.Name.StartsWith("Elsa.", StringComparison.Ordinal) && project.RelativePath.StartsWith("src/", StringComparison.Ordinal))
            return $"src/Elsa/{string.Join('/', project.Name.Split('.')[1..])}/{project.Name}.csproj";

        if (project.Name.StartsWith("Elsa.", StringComparison.Ordinal) && project.RelativePath.StartsWith("tests/", StringComparison.Ordinal))
            return $"tests/Elsa/{string.Join('/', project.Name.Split('.')[1..])}/{project.Name}.csproj";

        return project.RelativePath;
    }

    private static string ExpectedSolutionFolder(ProjectInfo project, HashSet<string> projectDirectories)
    {
        if (project.Name == "Elsa.Server")
            return "/src/Apps/";

        var directory = Path.GetDirectoryName(project.RelativePath)!.Replace('\\', '/');
        var lastProjectSegment = project.Name.Split('.')[^1];
        var lastDirectorySegment = directory.Split('/')[^1];
        var hasChildProject = projectDirectories.Any(other =>
            other.Length > directory.Length &&
            other.StartsWith(directory + "/", StringComparison.Ordinal));
        var keepLeafFolder = project.Name is "Elsa.Primitives" or "Elsa.Primitives.Hosting";

        if (!keepLeafFolder && lastDirectorySegment == lastProjectSegment && !hasChildProject)
            directory = Path.GetDirectoryName(directory)!.Replace('\\', '/');

        return $"/{directory}/";
    }

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                    return directory.FullName;

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find repository root.");
        }
    }

    private sealed record ProjectInfo(string Name, string FullPath, string RelativePath)
    {
        public static ProjectInfo From(string repoRoot, string fullPath)
        {
            var normalizedFullPath = Path.GetFullPath(fullPath);
            return new ProjectInfo(
                Path.GetFileNameWithoutExtension(normalizedFullPath),
                normalizedFullPath,
                Path.GetRelativePath(repoRoot, normalizedFullPath).Replace(Path.DirectorySeparatorChar, '/'));
        }
    }

    private enum SourceScanState
    {
        Code,
        LineComment,
        BlockComment,
        String,
        VerbatimString,
        RawString,
        InterpolatedString,
        InterpolatedVerbatimString,
        InterpolatedRawString,
        InterpolationExpression,
        Character
    }

    private sealed record SolutionProjectInfo(string Folder, string Path);
}
