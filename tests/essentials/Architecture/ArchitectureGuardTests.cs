using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed partial class ArchitectureGuardTests
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
        // Elsa.Workbench keeps a narrow exception for the host-only module-management registry builder
        // (ModuleManagementRegistryBuilder), exercised by ModuleManagementRegistryBuilderTests.
        ("Elsa.Workbench", "Elsa.Modularity.Tests"),
        // The shared-persistence resolver stays internal to the EF policy assembly. Focused unit
        // and migration metadata tests inspect detached plans without widening its production API.
        ("Elsa.Persistence.EntityFramework", "Elsa.Persistence.EntityFramework.Tests"),
        ("Elsa.Persistence.EntityFramework", "Elsa.Persistence.EntityFrameworkCore.Migrations.Tests")
    ];

    private static readonly Regex AssemblyInternalsVisibleToPattern = new(@"assembly\s*:\s*InternalsVisibleTo", RegexOptions.Compiled);

    // Speculative public contracts removed because they had zero in-repo consumers. Match
    // public type declarations only so OpenIddict's IApplicationManager (constitution R3
    // exception) and longer identifiers such as IApplicationManagerOptions stay legal.
    private static readonly string[] PrunedPublicContractNames =
    [
        "IExpressionFactory",
        "IHttpContextValueSelector",
        "IRuntimeInputBindingValidator",
        "IWorkflowDesignContextFactory",
        "IWorkflowDesignContext",
        "WorkflowDesignContext",
        "IApplicationManager",
        "ICredentialManager",
        "IProviderManager",
        "IClaimMappingManager",
    ];

    private static readonly Regex PrunedPublicContractDeclarationPattern = new(
        $@"\bpublic\s+(?:(?:partial|sealed|abstract|static|readonly)\s+)*(?:interface|class|record(?:\s+(?:struct|class))?|struct|enum)\s+(?<name>{string.Join("|", PrunedPublicContractNames)})\b",
        RegexOptions.Compiled);

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

    /// <summary>
    /// Every guard below is built on <see cref="ProjectFiles"/>, so if that enumeration ever stops seeing a
    /// module root, fifteen guards go quietly vacuous instead of red. That is not hypothetical: moving Elsa 3
    /// to <c>src/extensions/</c> dropped seven projects out of all of them while the suite stayed green, because
    /// the enumeration was pinned to <c>src/</c> and <c>tests/</c> (#1815).
    /// </summary>
    /// <remarks>
    /// The expected roots are discovered from the working tree, deliberately NOT read from
    /// <see cref="ModuleRoots.All"/>. Checking the enumeration against the same list it is built from is
    /// circular and passes even when a whole root has been dropped, which is precisely the bug being guarded
    /// against. The excluded roots hold projects that are not first-party modules and are outside every
    /// guard's remit: <c>samples/</c>, <c>tools/</c>, and <c>docs/</c>, whose
    /// <c>reports/repros/</c> tree carries standalone reproduction projects attached to written reports.
    /// </remarks>
    [Fact]
    public void Project_enumeration_covers_every_module_root()
    {
        string[] notModuleRoots = ["samples", "tools", "docs"];

        var rootsHoldingProjects = Directory.EnumerateDirectories(RepoRoot)
            .Select(directory => Path.GetFileName(directory)!)
            .Where(name => !name.StartsWith('.'))
            .Where(name => !notModuleRoots.Contains(name, StringComparer.Ordinal))
            .Where(name => Directory.EnumerateFiles(Path.Join(RepoRoot, name), "*.csproj", SearchOption.AllDirectories).Any())
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(rootsHoldingProjects);

        var projects = ProjectFiles().ToArray();
        var ignored = rootsHoldingProjects
            .Where(root => !projects.Any(project => project.RelativePath.StartsWith(root + "/", StringComparison.Ordinal)))
            .ToArray();

        Assert.True(
            ignored.Length == 0,
            "These roots hold projects but contribute none to ProjectFiles(), so every guard built on it is "
            + "silently ignoring them. Add them to ModuleRoots.All: " + string.Join(", ", ignored));
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
    public void Active_project_and_solution_references_resolve_to_existing_projects()
    {
        var missing = new List<string>();

        // ProjectFiles intentionally covers the source/test domain tree used by the architecture
        // conventions. The benchmark project files this guard also walked were deleted when
        // performance measurement was retired (#1668).
        foreach (var project in ProjectFiles())
        {
            var document = XDocument.Load(project.FullPath);
            foreach (var include in document.Descendants("ProjectReference")
                         .Select(reference => reference.Attribute("Include")?.Value)
                         .OfType<string>())
            {
                var path = Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(project.FullPath)!,
                    include.Replace('\\', Path.DirectorySeparatorChar)));
                if (!File.Exists(path))
                    missing.Add($"{project.RelativePath} -> {Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/')}");
            }
        }

        foreach (var solutionProject in SolutionProjects())
        {
            var path = Path.Combine(RepoRoot, solutionProject.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                missing.Add($"Elsa.Server.slnx -> {solutionProject.Path}");
        }

        Assert.True(
            missing.Count == 0,
            "Every active ProjectReference and solution project must resolve to a checked-in project. " +
            "Missing entries would otherwise be downgraded to MSB9008 warnings:" +
            Environment.NewLine + string.Join(Environment.NewLine, missing));
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

        var violations = PersistenceProviderNeutralityBoundary.ProjectNames
            .Select(projectName => projectsByName[projectName])
            .SelectMany(project => ReachableProjects(project)
                .Where(reached => PersistenceProviderNeutralityBoundary.IsConcreteProviderProject(reached.Name, reached.RelativePath))
                .Select(reached => $"{project.RelativePath} reaches concrete provider project {reached.RelativePath}")
                .Concat(ReachableProjects(project).Prepend(project)
                    .SelectMany(PackageReferences)
                    .Where(PersistenceProviderNeutralityBoundary.IsConcreteProviderPackage)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(package => $"{project.RelativePath} reaches concrete provider package {package}")))
            .ToList();

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

    [Fact]
    public void Docker_reference_shell_enables_graph_authoring_with_activity_design()
    {
        var path = Path.Combine(RepoRoot, "docker", "compose", "elsa-workbench.shells.json");
        var features = ReadDefaultShellFeatures(path);

        Assert.True(features.ContainsKey("ActivitiesDesignApi"));
        Assert.True(features.ContainsKey("ActivitiesGraphDesign"));
    }

    [Fact]
    public void Docker_reference_shell_inlines_the_entity_framework_modules()
    {
        var path = Path.Combine(RepoRoot, "docker", "compose", "elsa-workbench.shells.json");
        var features = ReadDefaultShellFeatures(path);

        foreach (var feature in new[]
                 {
                     "WorkflowsRuntimeEntityFrameworkCore",
                     "ActivitiesDesignEntityFrameworkCore",
                     "WorkflowsDesignEntityFrameworkCore",
                     "WorkflowsRuntimeDistributedEntityFrameworkCorePersistence",
                     "WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence",
                     "WorkflowsPublishingEntityFrameworkCore",
                     "DiagnosticsOpenTelemetryEntityFrameworkCore",
                     "DiagnosticsStructuredLogsEntityFrameworkCore",
                     "SecretsEntityFrameworkCore",
                     "WorkflowsDashboardEntityFrameworkCore"
                 })
        {
            Assert.True(features.ContainsKey(feature), $"Docker shell must explicitly enable {feature}.");
        }

        // No separate provider feature supplies the connection: each module selects its provider
        // and resolves ConnectionStrings:Elsa, which docker-compose.yml supplies.
        Assert.Equal(
            "PostgreSql",
            Assert.IsType<JsonObject>(features["WorkflowsRuntimeEntityFrameworkCore"])["Provider"]?.GetValue<string>());
    }

    [Fact]
    public void Server_Dockerfile_restores_ReadyToRun_packages_before_no_restore_publish()
    {
        var dockerfile = File.ReadAllText(Path.Combine(RepoRoot, "src", "apps", "Elsa.Workbench", "Dockerfile"));
        var restoreStart = dockerfile.IndexOf("    && dotnet restore ", StringComparison.Ordinal);
        var publishStart = dockerfile.IndexOf("    && dotnet publish ", StringComparison.Ordinal);

        Assert.True(restoreStart >= 0, "The server Dockerfile must contain a dotnet restore command.");
        Assert.True(publishStart > restoreStart, "The server Dockerfile must restore before publishing.");

        var restoreCommand = dockerfile[restoreStart..publishStart];
        var publishCommand = dockerfile[publishStart..];
        Assert.Contains("-p:PublishReadyToRun=true", restoreCommand, StringComparison.Ordinal);
        Assert.Contains("--no-restore", publishCommand, StringComparison.Ordinal);
        Assert.Contains("-p:PublishReadyToRun=true", publishCommand, StringComparison.Ordinal);
    }

    [Fact]
    public void Docker_reference_shell_enables_the_publish_engine_alongside_its_transport()
    {
        var features = ReadDefaultShellFeatures(Path.Combine(RepoRoot, "docker", "compose", "elsa-workbench.shells.json"));

        Assert.True(features.ContainsKey("WorkflowsPublishingApi"));
        Assert.True(features.ContainsKey("WorkflowsPublishing"));
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
        var runtimeCoreDirectory = Path.Combine(RepoRoot, "src", "essentials", "Workflows", "Runtime", "Core");
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

    [Fact]
    public void Pruned_unused_public_contracts_do_not_reappear_in_production_source()
    {
        var violations = ModuleSourceFiles()
            .Where(file => !IsGeneratedScratchFile(file) && !IsBuildArtifactFile(file))
            .SelectMany(file => FindPrunedPublicContractNames(File.ReadAllText(file))
                .Select(name => $"{Path.GetRelativePath(RepoRoot, file).Replace(Path.DirectorySeparatorChar, '/')}: {name}"))
            .Distinct()
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Pruned_contract_guard_matches_public_declarations_not_external_or_longer_names()
    {
        Assert.Equal(
            ["IApplicationManager"],
            FindPrunedPublicContractNames(
                """
                public interface IApplicationManager
                {
                    void Register();
                }
                """));
        Assert.Equal(
            ["WorkflowDesignContext"],
            FindPrunedPublicContractNames("public sealed class WorkflowDesignContext { }"));
        Assert.Equal(
            ["IWorkflowDesignContextFactory"],
            FindPrunedPublicContractNames("public interface IWorkflowDesignContextFactory { }"));

        Assert.Empty(FindPrunedPublicContractNames(
            """
            using OpenIddict.Abstractions;

            public sealed class OpenIddictTokenService(IApplicationManager applications)
            {
                public IApplicationManagerOptions Options { get; } = new();
            }

            public sealed class IApplicationManagerOptions;
            """));
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

        var violations = ModuleSourceFiles()
            .Where(file => !IsBuildArtifactFile(file))
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
            "src/essentials/Activities/Runtime/Services/ActivityFactory.cs",
            "src/essentials/Activities/Runtime/Services/ActivityConstructorRegistry.cs",
            "src/essentials/Activities/Runtime/Core/Contracts/IActivityFactory.cs",
            "src/essentials/Activities/Runtime/Core/Contracts/IActivityConstructor.cs",
            "src/essentials/Activities/Runtime/Core/Contracts/IActivityConstructorRegistry.cs"
        ];
        var violations = removedPaths.Where(relativePath =>
            File.Exists(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Empty(violations);
    }

    [Fact] // framework §2.6.6 (constitution v4.0.0) — event types are named for the fact, never `On`-prefixed.
    public void No_event_type_is_named_with_an_On_prefix()
    {
        // `On` is the handling-side idiom (`OnModelCreating` raises/reacts); on the event type it
        // names the consumer instead of the fact. Scanning source rather than loaded assemblies
        // catches a declaration in any package without this project referencing all of them.
        // Only files that mention IEvent are considered, so `OnChildCompletedAsync`-style handler
        // methods elsewhere are untouched.
        var onPrefixedDeclaration = new Regex(@"\b(?:class|record|struct)\s+(On[A-Z]\w*)", RegexOptions.Compiled);

        var violations = ModuleSourceFiles()
            .Where(file => !IsBuildArtifactFile(file))
            .SelectMany(file =>
            {
                var code = File.ReadAllText(file);
                if (!code.Contains("IEvent", StringComparison.Ordinal))
                    return [];

                return onPrefixedDeclaration.Matches(StripCommentsAndStringLiterals(code))
                    .Select(match => $"{Path.GetRelativePath(RepoRoot, file).Replace(Path.DirectorySeparatorChar, '/')}: {match.Groups[1].Value}");
            })
            .Distinct()
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static bool IsDesignReference(ProjectInfo reference) =>
        reference.Name.StartsWith("Elsa.", StringComparison.Ordinal) &&
        reference.Name.Contains(".Design", StringComparison.Ordinal);

    // ExtensionBuilder wrote runtime-generated scratch projects under this path. The feature
    // is retired, but the prune scan still skips those files if they reappear so generated
    // output cannot look like a reintroduced public contract.
    private static bool IsGeneratedScratchFile(string filePath) =>
        filePath.Replace(Path.DirectorySeparatorChar, '/').Contains("/extension-builder/projects/", StringComparison.Ordinal);

    // Build output under src/**/obj and src/**/bin (AssemblyInfo, GlobalUsings.g.cs, EF/source-generator
    // scaffolds) is not source; scanning it would make a token sweep depend on build state.
    private static bool IsBuildArtifactFile(string filePath)
    {
        var normalized = filePath.Replace(Path.DirectorySeparatorChar, '/');
        return normalized.Contains("/obj/", StringComparison.Ordinal) || normalized.Contains("/bin/", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> FindPrunedPublicContractNames(string source) =>
        PrunedPublicContractDeclarationPattern.Matches(StripCommentsAndStringLiterals(source))
            .Select(match => match.Groups["name"].Value)
            .ToArray();

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

    /// <summary>
    /// Every repository root that holds first-party code. Required modules live under <c>src/</c> and
    /// optional ones under <c>src/extensions/</c> (#1815), each extension carrying its own <c>tests/</c>.
    /// </summary>
    /// <remarks>
    /// Sweeps built on this must enumerate all of them. A sweep pinned to <c>src/</c> keeps compiling and
    /// keeps passing once a module moves out; it simply stops looking at that module, which is a silent
    /// loss of coverage rather than a failure. Moving Elsa 3 dropped seven projects out of the fifteen
    /// guards below before this was widened.
    /// </remarks>
    private static IEnumerable<ProjectInfo> ProjectFiles() =>
        ModuleRoots.Resolve(RepoRoot, ModuleRoots.All)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories))
            .Select(file => ProjectInfo.From(RepoRoot, file));

    /// <summary>Production source files across every module root, excluding test and build output.</summary>
    private static IEnumerable<string> ModuleSourceFiles() => ModuleRoots.ProductionSourceFiles(RepoRoot);

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

    private static IEnumerable<ProjectInfo> ReachableProjects(ProjectInfo root)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { root.FullPath };
        var pending = new Stack<ProjectInfo>(ProjectReferences(root));
        while (pending.TryPop(out var project))
        {
            if (!visited.Add(project.FullPath))
                continue;

            yield return project;
            foreach (var reference in ProjectReferences(project))
                pending.Push(reference);
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

    /// <summary>
    /// Project-name prefixes that belong to an optional module, and the <c>src/extensions/</c> bucket owning
    /// each (#1815). Order matters only if one prefix is a prefix of another, which none are today.
    /// </summary>
    private static readonly (string Prefix, string Bucket)[] ExtensionBuckets =
    [
        ("Elsa3.", "Elsa3"),
        ("Elsa.Agent.", "Agent"),
    ];

    private static string ExpectedProjectPath(ProjectInfo project)
    {
        if (project.Name == "Elsa.Workbench")
            return "src/apps/Elsa.Workbench/Elsa.Workbench.csproj";

        // Deployable host app, like Elsa.Workbench: it lives under src/apps/, not the src/essentials/<domain>/
        // tree, so its name (which collides with the Elsa.Foundation identity domain) does not dictate its
        // path.
        if (project.Name == "Elsa.Foundation.Host")
            return "src/apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj";

        if (project.Name == "Elsa.Architecture.Tests")
            return "tests/essentials/Architecture/Elsa.Architecture.Tests.csproj";

        if (project.Name == "Elsa.Primitives")
            return "src/essentials/Primitives/Primitives/Elsa.Primitives.csproj";

        // Optional modules live under src/extensions/<Bucket>/<src|tests>/ rather than in the src/essentials tree
        // (#1815). The name-to-path derivation is the same one the src/ rules below use; only the root
        // differs, and the src|tests split sits inside the extension rather than at the top of the tree.
        // Moving a module is therefore one row in ExtensionBuckets, not another branch here.
        if (ExtensionBuckets.FirstOrDefault(bucket => project.Name.StartsWith(bucket.Prefix, StringComparison.Ordinal)) is { Bucket.Length: > 0 } owner)
        {
            var subPath = string.Join('/', project.Name[owner.Prefix.Length..].Split('.'));
            var root = project.RelativePath.Contains("/tests/", StringComparison.Ordinal) ? "tests" : "src";
            return $"src/extensions/{owner.Bucket}/{root}/{subPath}/{project.Name}.csproj";
        }

        // Matched against src/essentials and tests/essentials specifically, not a bare src/ prefix: every extension
        // also lives under src/ now, so a loose prefix would quietly hand an unregistered bucket the core
        // expectation instead of reaching the refusal below.
        if (project.Name.StartsWith("Elsa.", StringComparison.Ordinal) && project.RelativePath.StartsWith("src/essentials/", StringComparison.Ordinal))
            return $"src/essentials/{string.Join('/', project.Name.Split('.')[1..])}/{project.Name}.csproj";

        if (project.Name.StartsWith("Elsa.", StringComparison.Ordinal) && project.RelativePath.StartsWith("tests/essentials/", StringComparison.Ordinal))
            return $"tests/essentials/{string.Join('/', project.Name.Split('.')[1..])}/{project.Name}.csproj";

        // An Elsa.* project outside every root this method knows has no expected path to compare against,
        // and returning its own path would compare it with itself — the guard would pass while checking
        // nothing. Most likely it is a module moved to src/extensions without a row in ExtensionBuckets,
        // so refuse and say so rather than go quiet.
        if (project.Name.StartsWith("Elsa.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{project.Name} lives at {project.RelativePath}, which is neither src/essentials, tests/essentials, nor a " +
                "registered extension bucket. Add its bucket to ExtensionBuckets, or teach ExpectedProjectPath " +
                "where that root puts a module.");
        }

        return project.RelativePath;
    }

    private static string ExpectedSolutionFolder(ProjectInfo project, HashSet<string> projectDirectories)
    {
        if (project.Name == "Elsa.Workbench")
            return "/src/apps/";

        // Deployable host app under src/apps/ (see ExpectedProjectPath): grouped directly beneath the Apps
        // solution folder, alongside Elsa.Workbench.
        if (project.Name == "Elsa.Foundation.Host")
            return "/src/apps/";

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

// Provider-neutral persistence contract projects must never gain a declared or transitive edge to a
// concrete provider (EF Core or a database driver). Reviewed with spec 094; the resolved
// project.assets.json check that used to accompany it went away with the EF surface ratchet.
internal static class PersistenceProviderNeutralityBoundary
{
    public static IReadOnlyList<string> ProjectNames { get; } =
    [
        "Elsa.Workflows.Runtime.Core",
        "Elsa.Foundation.Identity.Core",
        "Elsa.Foundation.Identity",
        "Elsa.Secrets.Core",
        "Elsa.Workflows.Runtime.Distributed"
    ];

    public static bool IsConcreteProviderPackage(string packageName) =>
        packageName.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) ||
        IsPackageFamily(packageName, "Microsoft.Data.Sqlite") ||
        IsPackageFamily(packageName, "SQLitePCLRaw") ||
        IsPackageFamily(packageName, "Microsoft.Data.SqlClient") ||
        IsPackageFamily(packageName, "System.Data.SqlClient") ||
        IsPackageFamily(packageName, "Npgsql");

    public static bool IsConcreteProviderProject(string projectName, string relativePath) =>
        HasProviderMarker(projectName) || HasProviderMarker(relativePath);

    private static bool IsPackageFamily(string packageName, string family) =>
        packageName.Equals(family, StringComparison.OrdinalIgnoreCase) ||
        packageName.StartsWith(family + ".", StringComparison.OrdinalIgnoreCase);

    private static bool HasProviderMarker(string value) =>
        value.Contains(".EFCore", StringComparison.OrdinalIgnoreCase) ||
        value.Contains(".EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("/EFCore/", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("/EntityFrameworkCore/", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("/Sqlite/", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("/SqlServer/", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("/PostgreSql/", StringComparison.OrdinalIgnoreCase);
}
