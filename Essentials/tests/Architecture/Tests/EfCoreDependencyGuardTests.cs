using System.Text.Json;
using System.Xml.Linq;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// EF Core is the destination persistence family, but each implementation still enters
/// through an explicitly reviewed program issue. This ratchet therefore keeps the currently admitted
/// surface at the vendor-owned OpenIddict host boundary plus the repository implementations explicitly
/// admitted by the reviewed records below. It reads
/// each source project's evaluated Release and Debug restore graphs, so imported, conditional, transitive,
/// and provider-only EF edges anywhere else under <c>src/</c> fail and name the offender. Each host exception also
/// validates its exact resolved EF package set. Each replacement changes this guard deliberately with its own
/// architecture evidence; the destination decision alone is not a repository-wide exemption.
/// </summary>
public sealed class EfCoreDependencyGuardTests
{
    private static readonly string[] RestoreConfigurations = ["Release", "Debug"];
    private static readonly string[] AllowedEfConsumers = ["Elsa.Foundation.Host", "Elsa.Workbench"];

    /// <summary>
    /// <c>Elsa.Foundation.Host</c> carries EF Core (#2151, #2191) for its opt-in
    /// cluster membership provider and its opt-in Data Protection key store and nothing else, so its closure is theirs - EF
    /// Core and Relational - plus the four engines their tables can live in. No design-time package, no in-memory provider
    /// and no vendor EF store: every feature's EF module still arrives from a feed.
    /// </summary>
    private static readonly string[] AllowedFoundationHostEfPackages =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Abstractions",
        "Microsoft.EntityFrameworkCore.Analyzers",
        "Microsoft.EntityFrameworkCore.Relational",
        "Microsoft.EntityFrameworkCore.Sqlite",
        "Microsoft.EntityFrameworkCore.Sqlite.Core",
        "Microsoft.EntityFrameworkCore.SqlServer",
        "MySql.EntityFrameworkCore",
        "Npgsql.EntityFrameworkCore.PostgreSQL"
    ];

    private static readonly string[] AllowedWorkbenchEfPackages =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Abstractions",
        "Microsoft.EntityFrameworkCore.Analyzers",
        "Microsoft.EntityFrameworkCore.Design",
        "Microsoft.EntityFrameworkCore.InMemory",
        "Microsoft.EntityFrameworkCore.Relational",
        "Microsoft.EntityFrameworkCore.Sqlite",
        "Microsoft.EntityFrameworkCore.Sqlite.Core",
        // The supported host composes every first-party module on EF, so it carries all four provider
        // engines and lets an operator select one per module.
        "Microsoft.EntityFrameworkCore.SqlServer",
        "MySql.EntityFrameworkCore",
        "Npgsql.EntityFrameworkCore.PostgreSQL",
        "OpenIddict.EntityFrameworkCore",
        "OpenIddict.EntityFrameworkCore.Models"
    ];
    private const string EfPackageToken = "EntityFrameworkCore";

    [Fact]
    public void Only_admitted_consumers_and_pilot_projects_resolve_ef_core_packages()
    {
        var projects = LoadModuleProjects();

        Assert.All(AllowedEfConsumers, name => Assert.Contains(name, projects.Keys));

        var offenders = projects.Values
            .Where(project => !project.IsAdmittedRepository && !AllowedEfConsumers.Contains(project.Name, StringComparer.Ordinal))
            .SelectMany(project => project.EfPackagesByConfiguration
                .Where(configuration => configuration.Value.Length > 0)
                .Select(configuration => $"{project.Name} ({configuration.Key})"))
            .Order()
            .ToArray();

        Assert.True(offenders.Length == 0, Report("resolve EF Core outside the admitted consumers and pilot projects", offenders));
    }

    /// <summary>Every admitted host, each held to the EF closure it was reviewed with.</summary>
    public static TheoryData<string> AdmittedHosts => [.. AllowedEfConsumers];

    [Theory]
    [MemberData(nameof(AdmittedHosts))]
    public void An_admitted_host_resolves_only_its_reviewed_EF_packages(string host)
    {
        var reviewed = ReviewedEfPackages(host);
        var offenders = LoadModuleProjects()[host].EfPackagesByConfiguration
            .SelectMany(configuration =>
                FindUnexpectedEfPackages(configuration.Value, reviewed)
                    .Select(package => $"{package} ({configuration.Key}) is not reviewed")
                    .Concat(FindUnexpectedEfPackages(reviewed, configuration.Value)
                        .Select(package => $"{package} ({configuration.Key}) is missing from the reviewed closure")))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(offenders.Length == 0, Report($"are unreviewed EF packages resolved by {host}", offenders));
    }

    private static string[] ReviewedEfPackages(string host) => host switch
    {
        "Elsa.Foundation.Host" => AllowedFoundationHostEfPackages,
        "Elsa.Workbench" => AllowedWorkbenchEfPackages,
        _ => throw new ArgumentOutOfRangeException(nameof(host), host, "An admitted EF consumer needs a reviewed EF closure of its own.")
    };

    [Fact]
    public void Every_admitted_ETL_project_resolves_only_its_reviewed_EF_closure()
    {
        var offenders = Adr0087FiniteEtlEf.ExpectedEfPackagesByProject
            .SelectMany(project => RestoreConfigurations.SelectMany(configuration =>
            {
                var resolved = ReadProjectEfPackages(project.Key, configuration);
                return FindUnexpectedEfPackages(resolved, project.Value)
                    .Select(package => $"{project.Key} ({configuration}) unexpectedly resolves {package}")
                    .Concat(FindUnexpectedEfPackages(project.Value, resolved)
                        .Select(package => $"{project.Key} ({configuration}) no longer resolves reviewed package {package}"));
            })).ToArray();
        Assert.True(offenders.Length == 0, Report("drift from the reviewed finite ETL EF closure", offenders));
    }

    [Fact]
    public void Every_admitted_Secrets_pilot_project_resolves_only_its_reviewed_EF_closure()
    {
        var offenders = Adr0072SecretsEfPilot.ExpectedEfPackagesByProject
            .SelectMany(project => RestoreConfigurations.SelectMany(configuration =>
            {
                var resolved = ReadProjectEfPackages(project.Key, configuration);
                var unexpected = FindUnexpectedEfPackages(resolved, project.Value)
                    .Select(package => $"{project.Key} ({configuration}) unexpectedly resolves {package}");
                var missing = FindUnexpectedEfPackages(project.Value, resolved)
                    .Select(package => $"{project.Key} ({configuration}) no longer resolves reviewed package {package}");
                return unexpected.Concat(missing);
            }))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(offenders.Length == 0, Report("drift from the reviewed Secrets EF pilot closure", offenders));
    }

    [Fact]
    public void Every_admitted_Studio_Preferences_project_resolves_only_its_reviewed_EF_closure()
    {
        var offenders = Adr0073RepositoryFirstEf.ExpectedEfPackagesByProject
            .SelectMany(project => RestoreConfigurations.SelectMany(configuration =>
            {
                var resolved = ReadProjectEfPackages(project.Key, configuration);
                var unexpected = FindUnexpectedEfPackages(resolved, project.Value)
                    .Select(package => $"{project.Key} ({configuration}) unexpectedly resolves {package}");
                var missing = FindUnexpectedEfPackages(project.Value, resolved)
                    .Select(package => $"{project.Key} ({configuration}) no longer resolves reviewed package {package}");
                return unexpected.Concat(missing);
            }))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(offenders.Length == 0, Report("drift from the reviewed Studio Preferences EF closure", offenders));
    }

    [Fact]
    public void Every_admitted_Runtime_placement_project_resolves_only_its_reviewed_EF_closure()
    {
        var offenders = Adr0073RuntimePlacementEf.ExpectedEfPackagesByProject
            .SelectMany(project => RestoreConfigurations.SelectMany(configuration =>
            {
                var resolved = ReadProjectEfPackages(project.Key, configuration);
                var unexpected = FindUnexpectedEfPackages(resolved, project.Value)
                    .Select(package => $"{project.Key} ({configuration}) unexpectedly resolves {package}");
                var missing = FindUnexpectedEfPackages(project.Value, resolved)
                    .Select(package => $"{project.Key} ({configuration}) no longer resolves reviewed package {package}");
                return unexpected.Concat(missing);
            }))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(offenders.Length == 0, Report("drift from the reviewed Runtime placement EF closure", offenders));
    }

    [Fact]
    public void Every_admitted_Activities_Design_project_resolves_only_its_reviewed_EF_closure()
    {
        var offenders = Adr0073ActivitiesDesignEf.ExpectedEfPackagesByProject
            .SelectMany(project => RestoreConfigurations.SelectMany(configuration =>
            {
                var resolved = ReadProjectEfPackages(project.Key, configuration);
                var unexpected = FindUnexpectedEfPackages(resolved, project.Value)
                    .Select(package => $"{project.Key} ({configuration}) unexpectedly resolves {package}");
                var missing = FindUnexpectedEfPackages(project.Value, resolved)
                    .Select(package => $"{project.Key} ({configuration}) no longer resolves reviewed package {package}");
                return unexpected.Concat(missing);
            }))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(offenders.Length == 0, Report("drift from the reviewed Activities Design EF closure", offenders));
    }

    [Fact]
    public void Every_admitted_Runtime_bookmarks_project_resolves_only_its_reviewed_EF_closure()
    {
        var offenders = Adr0073RuntimeBookmarksEf.ExpectedEfPackagesByProject
            .SelectMany(project => RestoreConfigurations.SelectMany(configuration =>
            {
                var resolved = ReadProjectEfPackages(project.Key, configuration);
                var unexpected = FindUnexpectedEfPackages(resolved, project.Value)
                    .Select(package => $"{project.Key} ({configuration}) unexpectedly resolves {package}");
                var missing = FindUnexpectedEfPackages(project.Value, resolved)
                    .Select(package => $"{project.Key} ({configuration}) no longer resolves reviewed package {package}");
                return unexpected.Concat(missing);
            }))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(offenders.Length == 0, Report("drift from the reviewed Runtime bookmarks EF closure", offenders));
    }

    [Fact]
    public void Every_admitted_Dashboard_run_health_project_resolves_only_its_reviewed_EF_closure()
    {
        var offenders = Adr0073DashboardRunHealthEf.ExpectedEfPackagesByProject
            .SelectMany(project => RestoreConfigurations.SelectMany(configuration =>
            {
                var resolved = ReadProjectEfPackages(project.Key, configuration);
                return FindUnexpectedEfPackages(resolved, project.Value)
                    .Select(package => $"{project.Key} ({configuration}) unexpectedly resolves {package}")
                    .Concat(FindUnexpectedEfPackages(project.Value, resolved)
                        .Select(package => $"{project.Key} ({configuration}) no longer resolves reviewed package {package}"));
            }))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(offenders.Length == 0, Report("drift from the reviewed Dashboard run-health EF closure", offenders));
    }

    [Fact]
    public void Every_admitted_Publishing_snapshot_review_project_resolves_only_its_reviewed_EF_closure()
    {
        var offenders = Adr0073PublishingSnapshotReviewEf.ExpectedEfPackagesByProject
            .SelectMany(project => RestoreConfigurations.SelectMany(configuration =>
            {
                var resolved = ReadProjectEfPackages(project.Key, configuration);
                return FindUnexpectedEfPackages(resolved, project.Value)
                    .Select(package => $"{project.Key} ({configuration}) unexpectedly resolves {package}")
                    .Concat(FindUnexpectedEfPackages(project.Value, resolved)
                        .Select(package => $"{project.Key} ({configuration}) no longer resolves reviewed package {package}"));
            }))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(offenders.Length == 0, Report("drift from the reviewed Publishing snapshot-review EF closure", offenders));
    }

    [Fact]
    public void Every_admitted_Structured_Logs_project_resolves_only_its_reviewed_EF_closure()
    {
        var offenders = Adr0073StructuredLogsEf.ExpectedEfPackagesByProject
            .SelectMany(project => RestoreConfigurations.SelectMany(configuration =>
            {
                var resolved = ReadProjectEfPackages(project.Key, configuration);
                var unexpected = FindUnexpectedEfPackages(resolved, project.Value)
                    .Select(package => $"{project.Key} ({configuration}) unexpectedly resolves {package}");
                var missing = FindUnexpectedEfPackages(project.Value, resolved)
                    .Select(package => $"{project.Key} ({configuration}) no longer resolves reviewed package {package}");
                return unexpected.Concat(missing);
            }))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(offenders.Length == 0, Report("drift from the reviewed Structured Logs EF closure", offenders));
    }

    [Fact]
    public void Every_admitted_OpenTelemetry_project_resolves_only_its_reviewed_EF_closure()
    {
        var offenders = Adr0073OpenTelemetryEf.ExpectedEfPackagesByProject
            .SelectMany(project => RestoreConfigurations.SelectMany(configuration =>
            {
                var resolved = ReadProjectEfPackages(project.Key, configuration);
                var unexpected = FindUnexpectedEfPackages(resolved, project.Value)
                    .Select(package => $"{project.Key} ({configuration}) unexpectedly resolves {package}");
                var missing = FindUnexpectedEfPackages(project.Value, resolved)
                    .Select(package => $"{project.Key} ({configuration}) no longer resolves reviewed package {package}");
                return unexpected.Concat(missing);
            }))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(offenders.Length == 0, Report("drift from the reviewed OpenTelemetry EF closure", offenders));
    }

    [Fact]
    public void Every_admitted_Identity_EF_project_resolves_only_its_reviewed_closure()
    {
        var offenders = Adr0073IdentityEf.ExpectedEfPackagesByProject
            .SelectMany(project => RestoreConfigurations.SelectMany(configuration =>
            {
                var resolved = ReadProjectEfPackages(project.Key, configuration);
                var unexpected = FindUnexpectedEfPackages(resolved, project.Value)
                    .Select(package => $"{project.Key} ({configuration}) unexpectedly resolves {package}");
                var missing = FindUnexpectedEfPackages(project.Value, resolved)
                    .Select(package => $"{project.Key} ({configuration}) no longer resolves reviewed package {package}");
                return unexpected.Concat(missing);
            }))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(offenders.Length == 0, Report("drift from the reviewed Identity EF closure", offenders));
    }

    [Fact]
    public void Every_admitted_Workflows_Design_EF_project_resolves_only_its_reviewed_closure()
    {
        var offenders = Adr0073WorkflowsDesignEf.ExpectedEfPackagesByProject
            .SelectMany(project => RestoreConfigurations.SelectMany(configuration =>
            {
                var resolved = ReadProjectEfPackages(project.Key, configuration);
                var unexpected = FindUnexpectedEfPackages(resolved, project.Value)
                    .Select(package => $"{project.Key} ({configuration}) unexpectedly resolves {package}");
                var missing = FindUnexpectedEfPackages(project.Value, resolved)
                    .Select(package => $"{project.Key} ({configuration}) no longer resolves reviewed package {package}");
                return unexpected.Concat(missing);
            }))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(offenders.Length == 0, Report("drift from the reviewed Workflows Design EF closure", offenders));
    }

    [Fact]
    public void Every_admitted_Elsa3_import_EF_project_resolves_only_its_reviewed_closure()
    {
        var offenders = Adr0073Elsa3ImportEf.ExpectedEfPackagesByProject
            .SelectMany(project => RestoreConfigurations.SelectMany(configuration =>
            {
                var resolved = ReadProjectEfPackages(project.Key, configuration);
                var unexpected = FindUnexpectedEfPackages(resolved, project.Value)
                    .Select(package => $"{project.Key} ({configuration}) unexpectedly resolves {package}");
                var missing = FindUnexpectedEfPackages(project.Value, resolved)
                    .Select(package => $"{project.Key} ({configuration}) no longer resolves reviewed package {package}");
                return unexpected.Concat(missing);
            }))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(offenders.Length == 0, Report("drift from the reviewed Elsa 3 import EF closure", offenders));
    }

    [Fact]
    public void Every_admitted_activation_guard_project_resolves_only_its_reviewed_EF_closure()
    {
        var offenders = Adr0076ActivationGuardEf.ExpectedEfPackagesByProject
            .SelectMany(project => RestoreConfigurations.SelectMany(configuration =>
            {
                var resolved = ReadProjectEfPackages(project.Key, configuration);
                var unexpected = FindUnexpectedEfPackages(resolved, project.Value)
                    .Select(package => $"{project.Key} ({configuration}) unexpectedly resolves {package}");
                var missing = FindUnexpectedEfPackages(project.Value, resolved)
                    .Select(package => $"{project.Key} ({configuration}) no longer resolves reviewed package {package}");
                return unexpected.Concat(missing);
            }))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(offenders.Length == 0, Report("drift from the reviewed activation-guard EF closure", offenders));
    }

    [Fact]
    public void Every_admitted_readability_project_resolves_only_its_reviewed_EF_closure()
    {
        var offenders = Spec183ReadabilityEf.ExpectedEfPackagesByProject
            .SelectMany(project => RestoreConfigurations.SelectMany(configuration =>
            {
                var resolved = ReadProjectEfPackages(project.Key, configuration);
                var unexpected = FindUnexpectedEfPackages(resolved, project.Value)
                    .Select(package => $"{project.Key} ({configuration}) unexpectedly resolves {package}");
                var missing = FindUnexpectedEfPackages(project.Value, resolved)
                    .Select(package => $"{project.Key} ({configuration}) no longer resolves reviewed package {package}");
                return unexpected.Concat(missing);
            }))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(offenders.Length == 0, Report("drift from the reviewed readability-source EF closure", offenders));
    }

    [Fact]
    public void Every_admitted_cluster_membership_project_resolves_only_its_reviewed_EF_closure()
    {
        var offenders = DriftFromReviewedClosures(Adr0078ClusterMembershipEf.ExpectedEfPackagesByProject);

        Assert.True(offenders.Length == 0, Report("drift from the reviewed cluster membership EF closure", offenders));
    }

    [Fact]
    public void Every_admitted_data_protection_project_resolves_only_its_reviewed_EF_closure()
    {
        var offenders = DriftFromReviewedClosures(Adr0076DataProtectionKeyStoreEf.ExpectedEfPackagesByProject);

        Assert.True(offenders.Length == 0, Report("drift from the reviewed Data Protection key store EF closure", offenders));
    }

    /// <summary>Every EF package each project resolves that its reviewed closure lacks, and every reviewed one it no longer resolves.</summary>
    private static string[] DriftFromReviewedClosures(IReadOnlyDictionary<string, string[]> expectedByProject) =>
        expectedByProject
            .SelectMany(project => RestoreConfigurations.SelectMany(configuration =>
            {
                var resolved = ReadProjectEfPackages(project.Key, configuration);
                return FindUnexpectedEfPackages(resolved, project.Value)
                    .Select(package => $"{project.Key} ({configuration}) unexpectedly resolves {package}")
                    .Concat(FindUnexpectedEfPackages(project.Value, resolved)
                        .Select(package => $"{project.Key} ({configuration}) no longer resolves reviewed package {package}"));
            }))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// The adapter's reviewed closure is exact, so a provider engine appearing in it is flagged rather than
    /// absorbed — proved here against the record itself, because a passing guard cannot show what it would
    /// refuse.
    /// </summary>
    [Fact]
    public void The_activation_guard_closure_rejects_a_provider_engine()
    {
        var reviewed = Adr0076ActivationGuardEf.ExpectedEfPackagesByProject[Adr0076ActivationGuardEf.AdapterProject];

        Assert.Equal(
            ["Npgsql.EntityFrameworkCore.PostgreSQL"],
            FindUnexpectedEfPackages([.. reviewed, "Npgsql.EntityFrameworkCore.PostgreSQL"], reviewed));
    }

    [Fact]
    public void Workbench_package_allowlist_rejects_an_unreviewed_transitive_wrapper()
    {
        const string assets = """
            {
              "libraries": {
                "Contoso.Persistence/1.0.0": { "type": "package" },
                "Microsoft.EntityFrameworkCore/10.0.0": { "type": "package" }
              },
              "targets": {
                "net10.0": {
                  "Contoso.Persistence/1.0.0": {
                    "type": "package",
                    "dependencies": { "Microsoft.EntityFrameworkCore": "10.0.0" }
                  },
                  "Microsoft.EntityFrameworkCore/10.0.0": { "type": "package" }
                }
              }
            }
            """;

        Assert.Equal(["Contoso.Persistence"], FindUnexpectedEfPackages(ReadEfDependencyPackages(assets), AllowedWorkbenchEfPackages));
    }

    [Fact]
    public void Evaluated_assets_detect_an_imported_provider_only_dependency()
    {
        const string assets = """
            {
              "libraries": {
                "Npgsql.EntityFrameworkCore.PostgreSQL/10.0.0": { "type": "package" },
                "Microsoft.EntityFrameworkCore/10.0.0": { "type": "package" }
              },
              "targets": {
                "net10.0": {
                  "Npgsql.EntityFrameworkCore.PostgreSQL/10.0.0": {
                    "type": "package",
                    "dependencies": { "Microsoft.EntityFrameworkCore": "10.0.0" }
                  },
                  "Microsoft.EntityFrameworkCore/10.0.0": { "type": "package" }
                }
              }
            }
            """;

        Assert.True(ResolvesEfCore(assets));
        // The allowed set is spelled out here rather than borrowed from a project's reviewed closure, so this
        // stays a test of the detector even as a project's closure legitimately grows.
        Assert.Equal(
            ["Npgsql.EntityFrameworkCore.PostgreSQL"],
            FindUnexpectedEfPackages(ReadEfDependencyPackages(assets), ["Microsoft.EntityFrameworkCore"]));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"libraries\": []}")]
    [InlineData("{\"libraries\": {}}")]
    [InlineData("{\"libraries\": {}, \"targets\": []}")]
    public void Evaluated_assets_reject_missing_or_non_object_dependency_graphs(string assets)
    {
        Assert.Throws<InvalidOperationException>(() => ResolvesEfCore(assets));
    }

    [Fact]
    public void Evaluated_assets_reject_a_target_node_missing_from_libraries()
    {
        const string assets = """
            {
              "libraries": {},
              "targets": {
                "net10.0": {
                  "Contoso.Persistence/1.0.0": { "type": "package" }
                }
              }
            }
            """;

        Assert.Throws<InvalidOperationException>(() => ReadEfDependencyPackages(assets));
    }

    [Fact]
    public void Evaluated_assets_reject_a_dependency_node_missing_from_libraries()
    {
        const string assets = """
            {
              "libraries": {
                "Contoso.Persistence/1.0.0": { "type": "package" }
              },
              "targets": {
                "net10.0": {
                  "Contoso.Persistence/1.0.0": {
                    "type": "package",
                    "dependencies": { "Microsoft.EntityFrameworkCore": "10.0.0" }
                  }
                }
              }
            }
            """;

        Assert.Throws<InvalidOperationException>(() => ReadEfDependencyPackages(assets));
    }

    [Fact]
    public void Evaluated_assets_reject_a_library_without_a_known_node_type()
    {
        const string assets = """
            {
              "libraries": {
                "Contoso.Persistence/1.0.0": {}
              },
              "targets": {
                "net10.0": {
                  "Contoso.Persistence/1.0.0": {}
                }
              }
            }
            """;

        Assert.Throws<InvalidOperationException>(() => ReadEfDependencyPackages(assets));
    }

    [Fact]
    public void Evaluated_assets_reject_a_package_library_missing_from_every_target()
    {
        const string assets = """
            {
              "libraries": {
                "Contoso.Persistence/1.0.0": { "type": "package" },
                "Microsoft.EntityFrameworkCore/10.0.0": { "type": "package" }
              },
              "targets": {
                "net10.0": {
                  "Microsoft.EntityFrameworkCore/10.0.0": { "type": "package" }
                }
              }
            }
            """;

        Assert.Throws<InvalidOperationException>(() => ReadEfDependencyPackages(assets));
    }

    [Fact]
    public void Evaluated_assets_reject_a_package_target_with_a_different_version_than_its_library_node()
    {
        const string assets = """
            {
              "libraries": {
                "Contoso.Persistence/1.0.0": { "type": "package" }
              },
              "targets": {
                "net10.0": {
                  "Contoso.Persistence/2.0.0": { "type": "package" }
                }
              }
            }
            """;

        Assert.Throws<InvalidOperationException>(() => ReadEfDependencyPackages(assets));
    }

    [Fact]
    public void Evaluated_assets_reject_a_target_type_that_differs_from_its_library_node()
    {
        const string assets = """
            {
              "libraries": {
                "Microsoft.EntityFrameworkCore/10.0.0": { "type": "project" }
              },
              "targets": {
                "net10.0": {
                  "Microsoft.EntityFrameworkCore/10.0.0": { "type": "package" }
                }
              }
            }
            """;

        Assert.Throws<InvalidOperationException>(() => ReadEfDependencyPackages(assets));
    }

    [Fact]
    public void First_party_dependency_edges_cannot_be_conditionally_hidden_from_the_reviewed_restore_graphs()
    {
        var offenders = Directory.EnumerateFiles(RepoRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path) && IsMsBuildFile(path))
            .SelectMany(path => FindConditionalDependencyElements(XDocument.Load(path))
                .Where(element => !IsAdmittedConditionalDependency(Path.GetRelativePath(RepoRoot, path), element))
                .Select(element => $"{Path.GetRelativePath(RepoRoot, path)}: {element.Name.LocalName}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            Report("conditionally declare dependency/import edges that are not covered by the Release and Debug restore graphs", offenders));
    }

    /// <summary>
    /// The declared exceptions, both in the root Directory.Build.props: the PublicApiAnalyzers reference a project opts
    /// into with ElsaTrackPublicApi, and the naming analyzer every production project compiles with. Neither condition
    /// reads $(Configuration): one reads a property the project file sets unconditionally, the other the project's
    /// location. The Release and Debug restore graphs this guard reads therefore resolve both identically, and
    /// neither can hide a package or project from one of them. An exception matches on path, element, Include and the
    /// exact condition text, so another reference placed under the same condition is still an offender.
    /// </summary>
    private static readonly (string RelativePath, string ElementName, string Include, string Condition)[] AdmittedConditionalDependencies =
    [
        ("Directory.Build.props", "PackageReference", "Microsoft.CodeAnalysis.PublicApiAnalyzers", "'$(ElsaTrackPublicApi)' == 'true'"),
        ("Directory.Build.props", "ProjectReference", "$(_ElsaNamingAnalyzerProject)", "'$(ElsaIsProductionProject)' == 'true'"),
    ];

    private static bool IsAdmittedConditionalDependency(string relativePath, XElement element) =>
        AdmittedConditionalDependencies.Any(admitted =>
            relativePath.Replace('\\', '/') == admitted.RelativePath &&
            element.Name.LocalName == admitted.ElementName &&
            element.Attribute("Include")?.Value == admitted.Include &&
            element.AncestorsAndSelf()
                .SelectMany(ancestor => ancestor.Attributes())
                .Any(attribute => attribute.Name.LocalName == "Condition" && attribute.Value == admitted.Condition));

    [Theory]
    [InlineData("<Project><ItemGroup Condition=\"'$(Configuration)' == 'Debug'\"><PackageReference Include=\"Contoso.Persistence\" /></ItemGroup></Project>")]
    [InlineData("<Project><Choose><When Condition=\"'$(UseContoso)' == 'true'\"><ItemGroup><ProjectReference Include=\"Contoso.csproj\" /></ItemGroup></When></Choose></Project>")]
    [InlineData("<Project><Import Project=\"Contoso.props\" Condition=\"'$(Configuration)' == 'Release'\" /></Project>")]
    public void Conditional_dependency_declarations_are_detected(string xml)
    {
        Assert.NotEmpty(FindConditionalDependencyElements(XDocument.Parse(xml)));
    }

    /// <summary>
    /// The allowlist admits exactly the two declared references - same path, element, Include and condition text - and
    /// nothing else: a different path, element, Include or condition still counts as an offender.
    /// </summary>
    [Fact]
    public void Only_the_declared_analyzer_references_are_admitted_as_conditional_dependencies()
    {
        const string trackingCondition = "'$(ElsaTrackPublicApi)' == 'true'";
        const string productionCondition = "'$(ElsaIsProductionProject)' == 'true'";
        const string analyzerProject = "$(_ElsaNamingAnalyzerProject)";

        Assert.True(IsAdmittedConditionalDependency("Directory.Build.props", Reference("PackageReference", "Microsoft.CodeAnalysis.PublicApiAnalyzers", trackingCondition)));
        Assert.True(IsAdmittedConditionalDependency("Directory.Build.props", Reference("ProjectReference", analyzerProject, productionCondition)));

        Assert.False(IsAdmittedConditionalDependency("VersionLines.props", Reference("PackageReference", "Microsoft.CodeAnalysis.PublicApiAnalyzers", trackingCondition)));
        Assert.False(IsAdmittedConditionalDependency("Directory.Build.props", Reference("ProjectReference", "Contoso.csproj", trackingCondition)));
        Assert.False(IsAdmittedConditionalDependency("Directory.Build.props", Reference("PackageReference", "Contoso.Persistence", trackingCondition)));
        Assert.False(IsAdmittedConditionalDependency("Directory.Build.props", Reference("ProjectReference", "Contoso.csproj", productionCondition)));
        Assert.False(IsAdmittedConditionalDependency("Directory.Build.props", Reference("PackageReference", "Microsoft.CodeAnalysis.PublicApiAnalyzers", "'$(Configuration)' == 'Debug'")));
        Assert.False(IsAdmittedConditionalDependency("Directory.Build.props", Reference("PackageReference", "Microsoft.CodeAnalysis.PublicApiAnalyzers", "'$(ElsaVersionLine)' == 'A'")));

        static XElement Reference(string element, string include, string condition) =>
            XDocument.Parse($"<Project><ItemGroup Condition=\"{condition}\"><{element} Include=\"{include}\" /></ItemGroup></Project>")
                .Descendants(element).Single();
    }

    private static Dictionary<string, Project> LoadModuleProjects()
    {
        var projects = new Dictionary<string, Project>(StringComparer.Ordinal);
        var files = ModuleRoots.Resolve(RepoRoot, ModuleRoots.Production)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories));
        foreach (var file in files.Where(f => !IsBuildOutput(f) && ModuleRoots.IsNotTestFile(f)))
        {
            var relativePath = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');
            projects.Add(
                Path.GetFileNameWithoutExtension(file),
                new Project(
                    Path.GetFileNameWithoutExtension(file),
                    RestoreConfigurations.ToDictionary(
                        configuration => configuration,
                        configuration => ReadProjectEfPackages(relativePath, configuration),
                        StringComparer.Ordinal),
                    Adr0072SecretsEfPilot.IsProjectPath(relativePath) ||
                    Adr0073RepositoryFirstEf.IsProjectPath(relativePath) ||
                    Adr0073StructuredLogsEf.IsProjectPath(relativePath) ||
                    Adr0073OpenTelemetryEf.IsProjectPath(relativePath) ||
                    Adr0073IdentityEf.IsProjectPath(relativePath) ||
                    Adr0073RuntimePlacementEf.IsProjectPath(relativePath) ||
                    Adr0073RuntimeBookmarksEf.IsProjectPath(relativePath) ||
                    Adr0073DashboardRunHealthEf.IsProjectPath(relativePath) ||
                    Adr0073PublishingSnapshotReviewEf.IsProjectPath(relativePath) ||
                    Adr0073WorkflowsDesignEf.IsProjectPath(relativePath) ||
                    Adr0073ActivitiesDesignEf.IsProjectPath(relativePath) ||
                    Adr0073Elsa3ImportEf.IsProjectPath(relativePath) ||
                    Adr0087FiniteEtlEf.IsProjectPath(relativePath) ||
                    Adr0076ActivationGuardEf.IsProjectPath(relativePath) ||
                    Spec183ReadabilityEf.IsProjectPath(relativePath) ||
                    Adr0078ClusterMembershipEf.IsProjectPath(relativePath) ||
                    Adr0076DataProtectionKeyStoreEf.IsProjectPath(relativePath)));
        }
        return projects;
    }

    private static string[] ReadProjectEfPackages(string relativeProjectPath, string configuration)
    {
        var projectDirectory = Path.GetDirectoryName(Path.Join(RepoRoot, relativeProjectPath))!;
        var assetsPath = configuration == "Release"
            ? Path.Join(projectDirectory, "obj", "project.assets.json")
            : Path.Join(projectDirectory, "obj", "ef-guard", configuration, "project.assets.json");
        if (!File.Exists(assetsPath))
            throw new InvalidOperationException(
                $"Evaluated {configuration} restore assets are required for '{relativeProjectPath}'. Restore Elsa.Server.slnx for both Release and Debug before running the EF dependency guard.");

        return ReadEfDependencyPackages(File.ReadAllText(assetsPath));
    }

    private static bool ResolvesEfCore(string assetsJson) => ReadEfDependencyPackages(assetsJson).Length > 0;

    private static string[] ReadEfDependencyPackages(string assetsJson)
    {
        using var document = JsonDocument.Parse(assetsJson);
        if (!document.RootElement.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Evaluated restore assets must contain an object-valued 'libraries' graph.");

        if (!document.RootElement.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Evaluated restore assets must contain an object-valued 'targets' graph.");

        var libraryNodes = libraries.EnumerateObject()
            .ToDictionary(
                library => library.Name,
                library => new LibraryNode(library.Name.Split('/', 2)[0], ReadLibraryType(library)),
                StringComparer.OrdinalIgnoreCase);
        var libraryNames = libraryNodes.Values.Select(library => library.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var packageNames = libraryNodes.Values
            .Where(library => string.Equals(library.Type, "package", StringComparison.Ordinal))
            .Select(library => library.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dependencies = packageNames.ToDictionary(name => name, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var targetNodeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in targets.EnumerateObject())
        {
            if (target.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"Evaluated restore target '{target.Name}' must be an object.");

            foreach (var library in target.Value.EnumerateObject())
            {
                if (!libraryNodes.TryGetValue(library.Name, out var libraryNode))
                    throw new InvalidOperationException($"Evaluated restore target node '{library.Name}' is missing from 'libraries'.");

                if (library.Value.ValueKind != JsonValueKind.Object ||
                    !library.Value.TryGetProperty("type", out var targetType) ||
                    targetType.ValueKind != JsonValueKind.String ||
                    !string.Equals(targetType.GetString(), libraryNode.Type, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Evaluated restore target node '{library.Name}' must be an object declaring type '{libraryNode.Type}' to match 'libraries'.");
                }

                var name = libraryNode.Name;
                targetNodeKeys.Add(library.Name);
                if (!library.Value.TryGetProperty("dependencies", out var libraryDependencies))
                    continue;
                if (libraryDependencies.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException($"Evaluated dependencies for '{name}' must be an object.");

                var dependencyNames = libraryDependencies.EnumerateObject().Select(dependency => dependency.Name).ToArray();
                var missingDependencies = dependencyNames.Where(dependency => !libraryNames.Contains(dependency)).ToArray();
                if (missingDependencies.Length > 0)
                    throw new InvalidOperationException(
                        $"Evaluated dependencies for '{name}' contain nodes missing from 'libraries': {string.Join(", ", missingDependencies)}.");

                if (packageNames.Contains(name))
                    dependencies[name].UnionWith(dependencyNames.Where(packageNames.Contains));
            }
        }

        var packagesMissingFromTargets = libraryNodes
            .Where(library => library.Value.Type == "package" && !targetNodeKeys.Contains(library.Key))
            .Select(library => library.Key)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (packagesMissingFromTargets.Length > 0)
            throw new InvalidOperationException(
                $"Evaluated restore package libraries are missing from every target: {string.Join(", ", packagesMissingFromTargets)}.");

        var efDependencyPackages = packageNames
            .Where(name => name.Contains(EfPackageToken, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var newlyDiscovered = dependencies
                .Where(package => package.Value.Overlaps(efDependencyPackages) && !efDependencyPackages.Contains(package.Key))
                .Select(package => package.Key)
                .ToArray();
            if (newlyDiscovered.Length == 0)
                break;

            efDependencyPackages.UnionWith(newlyDiscovered);
        }

        return efDependencyPackages.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ReadLibraryType(JsonProperty library)
    {
        if (!library.Value.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"Evaluated restore library '{library.Name}' must declare type 'package' or 'project'.");

        var libraryType = type.GetString()!;
        if (libraryType is not "package" and not "project")
            throw new InvalidOperationException($"Evaluated restore library '{library.Name}' must declare type 'package' or 'project'.");

        return libraryType;
    }

    private sealed record LibraryNode(string Name, string Type);

    private static string[] FindUnexpectedEfPackages(IEnumerable<string> resolved, IEnumerable<string> allowed) =>
        resolved.Except(allowed, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    private static IEnumerable<XElement> FindConditionalDependencyElements(XDocument document) =>
        document.Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference" or "Import")
            .Where(element => element.AncestorsAndSelf()
                .SelectMany(ancestor => ancestor.Attributes())
                .Any(attribute => attribute.Name.LocalName == "Condition"));

    private static bool IsMsBuildFile(string path) =>
        path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase);

    private static string Report(string what, IEnumerable<string> offenders) =>
        $"These src entries {what}:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}";

    private sealed record Project(string Name, IReadOnlyDictionary<string, string[]> EfPackagesByConfiguration, bool IsAdmittedRepository);

    /// <summary>
    /// The Secrets EF pilot surface. The pilot's bounded policy is superseded, but its implementation stays
    /// admitted as the reviewed starting surface while later Program #1665 replacements remain evidence-gated.
    /// <see cref="SecretsEfPersistencePilotArchitectureTests"/> owns the exact package and source inventory.
    /// </summary>
    internal static class Adr0072SecretsEfPilot
    {
        public static readonly string[] SurfacePathPrefixes =
        [
            "Essentials/src/Persistence/EntityFramework/",
            "Essentials/src/Secrets/Persistence/EntityFrameworkCore/",
            "Essentials/tests/Persistence/EntityFramework/",
            "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/"
        ];

        public static readonly IReadOnlyDictionary<string, string[]> ExpectedEfPackagesByProject =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Essentials/src/Persistence/EntityFramework/Elsa.Persistence.EntityFramework.csproj"] = CorePackages(),
                ["Essentials/src/Secrets/Persistence/EntityFrameworkCore/Elsa.Secrets.Persistence.EntityFrameworkCore.csproj"] = CorePackages(),
                // Binds all four engines at the pinned versions so a provider upgrade that moved a Use* extension
                // fails CI instead of a host; nothing here opens a connection.
                ["Essentials/tests/Persistence/EntityFramework/BindingDriftTests/Elsa.Persistence.EntityFramework.BindingDriftTests.csproj"] =
                [
                    .. CorePackages(),
                    "Microsoft.EntityFrameworkCore.SqlServer",
                    "Microsoft.EntityFrameworkCore.Sqlite",
                    "Microsoft.EntityFrameworkCore.Sqlite.Core",
                    "MySql.EntityFrameworkCore",
                    "Npgsql.EntityFrameworkCore.PostgreSQL"
                ],
                ["Essentials/tests/Persistence/EntityFramework/Tests/Elsa.Persistence.EntityFramework.Tests.csproj"] =
                [.. CorePackages(), "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite.Core"],
                ["Essentials/tests/Secrets/Persistence/EntityFrameworkCore/MySql/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.MySql.Tests.csproj"] =
                [.. CorePackages(), "MySql.EntityFrameworkCore"],
                ["Essentials/tests/Secrets/Persistence/EntityFrameworkCore/PostgreSql/PackageFeedProbe/Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.PackageFeedProbe.csproj"] =
                [.. CorePackages(), "Npgsql.EntityFrameworkCore.PostgreSQL"],
                ["Essentials/tests/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests.csproj"] =
                [.. CorePackages(), "Npgsql.EntityFrameworkCore.PostgreSQL"],
                ["Essentials/tests/Secrets/Persistence/EntityFrameworkCore/SqlServer/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.SqlServer.Tests.csproj"] =
                [.. CorePackages(), "Microsoft.EntityFrameworkCore.SqlServer"],
                // One engine, its own: the SqlServer and Npgsql this resolved before #1878 came in through
                // the retired Secrets Tooling project, never from anything this suite loads.
                ["Essentials/tests/Secrets/Persistence/EntityFrameworkCore/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.csproj"] =
                [.. CorePackages(), "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite.Core"]
            };

        public static IEnumerable<string> ProjectPaths => ExpectedEfPackagesByProject.Keys;

        public static bool IsProjectPath(string relativePath) =>
            ProjectPaths.Contains(relativePath, StringComparer.Ordinal);

        private static string[] CorePackages() =>
        [
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Analyzers",
            "Microsoft.EntityFrameworkCore.Relational"
        ];
    }

    /// <summary>
    /// The first repository-first admission beyond the Secrets pilot. Provider engines remain
    /// confined to the focused test project; the production adapter owns only EF Core relational APIs.
    /// </summary>
    internal static class Adr0073RepositoryFirstEf
    {
        public static readonly IReadOnlyDictionary<string, string[]> ExpectedEfPackagesByProject =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Essentials/src/Studio/Preferences/Persistence/EntityFrameworkCore/Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.csproj"] = CorePackages(),
                ["Essentials/tests/Studio/Preferences/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.ProviderTests.csproj"] =
                [
                    .. CorePackages(),
                    "Microsoft.EntityFrameworkCore.SqlServer",
                    "MySql.EntityFrameworkCore",
                    "Npgsql.EntityFrameworkCore.PostgreSQL"
                ],
                ["Essentials/tests/Studio/Preferences/Persistence/EntityFrameworkCore/Tests/Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Tests.csproj"] =
                [
                    .. CorePackages(),
                    "Microsoft.EntityFrameworkCore.Sqlite",
                    "Microsoft.EntityFrameworkCore.Sqlite.Core"
                ]
            };

        public static IEnumerable<string> ProjectPaths => ExpectedEfPackagesByProject.Keys;

        public static bool IsProjectPath(string relativePath) =>
            ProjectPaths.Contains(relativePath, StringComparer.Ordinal);

        private static string[] CorePackages() =>
        [
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Analyzers",
            "Microsoft.EntityFrameworkCore.Relational"
        ];
    }

    /// <summary>
    /// Activities Design EF replacement, admitted by its audited implementation slice: the provider-neutral
    /// adapter plus its SQLite and provider test projects.
    /// </summary>
    internal static class Adr0073ActivitiesDesignEf
    {
        public static readonly IReadOnlyDictionary<string, string[]> ExpectedEfPackagesByProject =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Essentials/src/Activities/Design/Persistence/EntityFrameworkCore/Elsa.Activities.Design.Persistence.EntityFrameworkCore.csproj"] = CorePackages(),
                ["Essentials/tests/Activities/Design/Persistence/EntityFrameworkCore/Tests/Elsa.Activities.Design.Persistence.EntityFrameworkCore.Tests.csproj"] =
                [.. CorePackages(), "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite.Core", "Microsoft.EntityFrameworkCore.SqlServer"],
                ["Essentials/tests/Activities/Design/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Activities.Design.Persistence.EntityFrameworkCore.ProviderTests.csproj"] =
                [.. CorePackages(), "Microsoft.EntityFrameworkCore.SqlServer", "MySql.EntityFrameworkCore", "Npgsql.EntityFrameworkCore.PostgreSQL"]
            };

        public static IEnumerable<string> ProjectPaths => ExpectedEfPackagesByProject.Keys;
        public static bool IsProjectPath(string relativePath) => ProjectPaths.Contains(relativePath, StringComparer.Ordinal);
        private static string[] CorePackages() => ["Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Abstractions", "Microsoft.EntityFrameworkCore.Analyzers", "Microsoft.EntityFrameworkCore.Relational"];
    }

    /// <summary>
    /// The Runtime repository-first admission for issue #1717. The production
    /// adapter is provider-neutral; provider engines remain in focused test projects.
    /// </summary>
    internal static class Adr0073RuntimePlacementEf
    {
        public static readonly IReadOnlyDictionary<string, string[]> ExpectedEfPackagesByProject =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Essentials/src/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.csproj"] = CorePackages(),
                ["Essentials/tests/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Tests.csproj"] =
                [
                    .. CorePackages(),
                    "Microsoft.EntityFrameworkCore.Sqlite",
                    "Microsoft.EntityFrameworkCore.Sqlite.Core"
                ],
                ["Essentials/tests/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.ProviderTests.csproj"] =
                [
                    .. CorePackages(),
                    "Microsoft.EntityFrameworkCore.Sqlite",
                    "Microsoft.EntityFrameworkCore.Sqlite.Core",
                    "Microsoft.EntityFrameworkCore.SqlServer",
                    "MySql.EntityFrameworkCore",
                    "Npgsql.EntityFrameworkCore.PostgreSQL"
                ]
            };

        public static IEnumerable<string> ProjectPaths => ExpectedEfPackagesByProject.Keys;

        public static bool IsProjectPath(string relativePath) =>
            ProjectPaths.Contains(relativePath, StringComparer.Ordinal);

        private static string[] CorePackages() =>
        [
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Analyzers",
            "Microsoft.EntityFrameworkCore.Relational"
        ];
    }

    /// <summary>
    /// The Runtime bookmark-state admission for issue #1723. The production
    /// adapter is provider-neutral; provider engines remain in focused test projects.
    /// </summary>
    internal static class Adr0073RuntimeBookmarksEf
    {
        public static readonly IReadOnlyDictionary<string, string[]> ExpectedEfPackagesByProject =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.csproj"] = CorePackages(),
                ["Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests.csproj"] =
                [
                    .. CorePackages(),
                    "Microsoft.EntityFrameworkCore.SqlServer",
                    "Microsoft.EntityFrameworkCore.Sqlite",
                    "Microsoft.EntityFrameworkCore.Sqlite.Core",
                    "MySql.EntityFrameworkCore",
                    "Npgsql.EntityFrameworkCore.PostgreSQL"
                ],
                ["Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj"] =
                [.. CorePackages(), "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite.Core"]
            };

        public static IEnumerable<string> ProjectPaths => ExpectedEfPackagesByProject.Keys;

        public static bool IsProjectPath(string relativePath) =>
            ProjectPaths.Contains(relativePath, StringComparer.Ordinal);

        private static string[] CorePackages() =>
        [
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Analyzers",
            "Microsoft.EntityFrameworkCore.Relational"
        ];
    }

    /// <summary>The opt-in Dashboard run-health reader, backed by the admitted Runtime EF projection.</summary>
    internal static class Adr0073DashboardRunHealthEf
    {
        private const string Prefix = "Essentials/src/Workflows/Dashboard/Persistence/EntityFrameworkCore/";
        public static readonly IReadOnlyDictionary<string, string[]> ExpectedEfPackagesByProject =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [Prefix + "Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.csproj"] =
                [
                    "Microsoft.EntityFrameworkCore",
                    "Microsoft.EntityFrameworkCore.Abstractions",
                    "Microsoft.EntityFrameworkCore.Analyzers",
                    "Microsoft.EntityFrameworkCore.Relational"
                ]
            };

        public static bool IsProjectPath(string relativePath) => ExpectedEfPackagesByProject.ContainsKey(relativePath);
    }

    /// <summary>
    /// The opt-in Publishing ledger (issues #1748, #1751 and #1737). Its ordered activity-publication
    /// commands reach the Activities Design and Runtime EF projects, whose closures are the same core packages.
    /// </summary>
    internal static class Adr0073PublishingSnapshotReviewEf
    {
        private const string Source = "Essentials/src/Workflows/Publishing/Persistence/EntityFrameworkCore/";
        private const string Tests = "Essentials/tests/Workflows/Publishing/Persistence/EntityFrameworkCore/";
        private static string[] CorePackages() =>
        [
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Analyzers",
            "Microsoft.EntityFrameworkCore.Relational"
        ];

        public static readonly IReadOnlyDictionary<string, string[]> ExpectedEfPackagesByProject =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [Source + "Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.csproj"] = CorePackages(),
                [Tests + "Tests/Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests.csproj"] =
                [
                    .. CorePackages(),
                    "Microsoft.EntityFrameworkCore.SqlServer",
                    "Microsoft.EntityFrameworkCore.Sqlite",
                    "Microsoft.EntityFrameworkCore.Sqlite.Core",
                    "MySql.EntityFrameworkCore",
                    "Npgsql.EntityFrameworkCore.PostgreSQL"
                ]
            };

        public static bool IsProjectPath(string relativePath) => ExpectedEfPackagesByProject.ContainsKey(relativePath);
    }

    /// <summary>
    /// The Diagnostics Structured Logs repository-first admission for issue #1695. The
    /// production adapter is provider-neutral; SQLite belongs to the behavioral test project and
    /// SQL Server, PostgreSQL, and MySQL packages belong only to the live-provider test project.
    /// </summary>
    internal static class Adr0073StructuredLogsEf
    {
        public static readonly IReadOnlyDictionary<string, string[]> ExpectedEfPackagesByProject =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Essentials/src/Diagnostics/StructuredLogs/Persistence/EntityFrameworkCore/Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.csproj"] = CorePackages(),
                ["Essentials/tests/Diagnostics/StructuredLogs/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.ProviderTests.csproj"] =
                [
                    .. CorePackages(),
                    "Microsoft.EntityFrameworkCore.SqlServer",
                    "MySql.EntityFrameworkCore",
                    "Npgsql.EntityFrameworkCore.PostgreSQL"
                ],
                ["Essentials/tests/Diagnostics/StructuredLogs/Persistence/EntityFrameworkCore/Tests/Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Tests.csproj"] =
                [.. CorePackages(), "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite.Core"]
            };

        public static IEnumerable<string> ProjectPaths => ExpectedEfPackagesByProject.Keys;

        public static bool IsProjectPath(string relativePath) =>
            ProjectPaths.Contains(relativePath, StringComparer.Ordinal);

        private static string[] CorePackages() =>
        [
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Analyzers",
            "Microsoft.EntityFrameworkCore.Relational"
        ];
    }

    /// <summary>
    /// The Diagnostics OpenTelemetry repository-first admission for issue #1697. The
    /// production adapter is provider-neutral; SQLite belongs to the behavioral test project and
    /// SQL Server, PostgreSQL, and MySQL packages belong only to the live-provider test project.
    /// </summary>
    internal static class Adr0073OpenTelemetryEf
    {
        public static readonly IReadOnlyDictionary<string, string[]> ExpectedEfPackagesByProject =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Essentials/src/Diagnostics/OpenTelemetry/Persistence/EntityFrameworkCore/Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.csproj"] = CorePackages(),
                ["Essentials/tests/Diagnostics/OpenTelemetry/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.ProviderTests.csproj"] =
                [
                    .. CorePackages(),
                    "Microsoft.EntityFrameworkCore.Sqlite",
                    "Microsoft.EntityFrameworkCore.Sqlite.Core",
                    "Microsoft.EntityFrameworkCore.SqlServer",
                    "MySql.EntityFrameworkCore",
                    "Npgsql.EntityFrameworkCore.PostgreSQL"
                ],
                ["Essentials/tests/Diagnostics/OpenTelemetry/Persistence/EntityFrameworkCore/Tests/Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Tests.csproj"] =
                [.. CorePackages(), "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite.Core"]
            };

        public static IEnumerable<string> ProjectPaths => ExpectedEfPackagesByProject.Keys;

        public static bool IsProjectPath(string relativePath) =>
            ProjectPaths.Contains(relativePath, StringComparer.Ordinal);

        private static string[] CorePackages() =>
        [
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Analyzers",
            "Microsoft.EntityFrameworkCore.Relational"
        ];
    }

    /// <summary>
    /// The repository-first Identity EF admissions for issues #1708, #1710, and #1712.
    /// The production repository and ASP.NET Identity adapters stay provider-neutral; engines remain
    /// in focused test projects.
    /// </summary>
    internal static class Adr0073IdentityEf
    {
        public static readonly IReadOnlyDictionary<string, string[]> ExpectedEfPackagesByProject =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Essentials/src/Foundation/Identity/AspNetCoreIdentity/EntityFrameworkCore/Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.csproj"] = CorePackages(),
                ["Essentials/src/Foundation/Identity/Persistence/EntityFrameworkCore/Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.csproj"] = CorePackages(),
                ["Essentials/tests/Foundation/Identity/AspNetCoreIdentity/EntityFrameworkCore/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Tests.csproj"] =
                [.. CorePackages(), "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite.Core"],
                ["Essentials/tests/Foundation/Identity/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.ProviderTests.csproj"] =
                [
                    .. CorePackages(),
                    "Microsoft.EntityFrameworkCore.SqlServer",
                    "MySql.EntityFrameworkCore",
                    "Npgsql.EntityFrameworkCore.PostgreSQL"
                ],
                ["Essentials/tests/Foundation/Identity/Persistence/EntityFrameworkCore/Tests/Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Tests.csproj"] =
                [.. CorePackages(), "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite.Core"]
            };

        public static IEnumerable<string> ProjectPaths => ExpectedEfPackagesByProject.Keys;

        public static bool IsProjectPath(string relativePath) =>
            ProjectPaths.Contains(relativePath, StringComparer.Ordinal);

        private static string[] CorePackages() =>
        [
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Analyzers",
            "Microsoft.EntityFrameworkCore.Relational"
        ];
    }

    /// <summary>The scoped ETL design module admission for #3109 and ADR 0087.</summary>
    internal static class Adr0087FiniteEtlEf
    {
        public static readonly IReadOnlyDictionary<string, string[]> ExpectedEfPackagesByProject =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Extensions/Etl/src/Persistence/EntityFrameworkCore/Elsa.Etl.Persistence.EntityFrameworkCore.csproj"] =
                ["Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Abstractions", "Microsoft.EntityFrameworkCore.Analyzers", "Microsoft.EntityFrameworkCore.Relational"],
                ["Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj"] =
                ["Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Abstractions", "Microsoft.EntityFrameworkCore.Analyzers", "Microsoft.EntityFrameworkCore.Relational", "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite.Core"]
            };

        public static bool IsProjectPath(string relativePath) => ExpectedEfPackagesByProject.ContainsKey(relativePath);
    }

    /// <summary>
    /// The Elsa 3 import ledger admission for issue #1738. The production adapter is
    /// provider-neutral; SQLite and the SQL Server provider-mismatch binding belong to the behavioral test
    /// project, and SQL Server, PostgreSQL, and MySQL engines only to the live-provider smoke.
    /// </summary>
    internal static class Adr0073Elsa3ImportEf
    {
        public static readonly IReadOnlyDictionary<string, string[]> ExpectedEfPackagesByProject =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Extensions/Elsa3/src/Activities/Design/Import/Persistence/EntityFrameworkCore/Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.csproj"] = CorePackages(),
                ["Extensions/Elsa3/tests/Activities/Design/Import/Persistence/EntityFrameworkCore/Tests/Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests.csproj"] =
                [.. CorePackages(), "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite.Core", "Microsoft.EntityFrameworkCore.SqlServer"],
                ["Extensions/Elsa3/tests/Activities/Design/Import/Persistence/EntityFrameworkCore/ProviderTests/Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.ProviderTests.csproj"] =
                [.. CorePackages(), "Microsoft.EntityFrameworkCore.SqlServer", "MySql.EntityFrameworkCore", "Npgsql.EntityFrameworkCore.PostgreSQL"]
            };

        public static IEnumerable<string> ProjectPaths => ExpectedEfPackagesByProject.Keys;

        public static bool IsProjectPath(string relativePath) =>
            ProjectPaths.Contains(relativePath, StringComparer.Ordinal);

        private static string[] CorePackages() =>
        [
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Analyzers",
            "Microsoft.EntityFrameworkCore.Relational"
        ];
    }

    /// <summary>
    /// The Workflows Design repository-first admission for issue #1727. The
    /// production adapter is provider-neutral; SQLite belongs only to its focused behavioral test project.
    /// </summary>
    internal static class Adr0073WorkflowsDesignEf
    {
        public static readonly IReadOnlyDictionary<string, string[]> ExpectedEfPackagesByProject =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Essentials/src/Workflows/Design/Persistence/EntityFrameworkCore/Elsa.Workflows.Design.Persistence.EntityFrameworkCore.csproj"] = CorePackages(),
                ["Essentials/tests/Workflows/Design/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests.csproj"] =
                [.. CorePackages(), "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite.Core"],
                ["Essentials/tests/Workflows/Design/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Workflows.Design.Persistence.EntityFrameworkCore.ProviderTests.csproj"] =
                [.. CorePackages(), "Microsoft.EntityFrameworkCore.SqlServer", "MySql.EntityFrameworkCore", "Npgsql.EntityFrameworkCore.PostgreSQL"]
            };

        public static IEnumerable<string> ProjectPaths => ExpectedEfPackagesByProject.Keys;

        public static bool IsProjectPath(string relativePath) =>
            ProjectPaths.Contains(relativePath, StringComparer.Ordinal);

        private static string[] CorePackages() =>
        [
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Analyzers",
            "Microsoft.EntityFrameworkCore.Relational"
        ];
    }

    /// <summary>
    /// The activation-guard adapter: the one project that bridges Modularity to EF, so that
    /// Elsa.Modularity.Core and Elsa.Modularity.Nuplane keep no EF edge of their own. Admitted through its
    /// own record rather than through <c>AllowedEfConsumers</c>, which licenses a host to carry provider
    /// engines — the opposite of what this adapter may have.
    /// </summary>
    internal static class Adr0076ActivationGuardEf
    {
        public const string AdapterProject = "Essentials/src/Modularity/EntityFramework/Elsa.Modularity.EntityFramework.csproj";

        public static readonly IReadOnlyDictionary<string, string[]> ExpectedEfPackagesByProject =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                // EF Core and Relational only: the guard reads a module's migrations history through EF's
                // own abstractions, and binds the engine reflectively like every other Elsa package that
                // never references one.
                [AdapterProject] = CorePackages(),
                // Its tests carry exactly one engine, which is also what makes their unbindable-provider
                // case real rather than simulated.
                ["Essentials/tests/Modularity/EntityFramework/Tests/Elsa.Modularity.EntityFramework.Tests.csproj"] =
                [.. CorePackages(), "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite.Core"]
            };

        public static IEnumerable<string> ProjectPaths => ExpectedEfPackagesByProject.Keys;

        public static bool IsProjectPath(string relativePath) =>
            ProjectPaths.Contains(relativePath, StringComparer.Ordinal);

        private static string[] CorePackages() =>
        [
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Analyzers",
            "Microsoft.EntityFrameworkCore.Relational"
        ];
    }

    /// <summary>
    /// The readability source (#2099) reads schema-family declarations and reports them through the membership
    /// contract. Since #2143 it reads them through <c>Elsa.Persistence.Schema</c>, which references no EF Core, so the
    /// source itself is no longer admitted here: it resolves no EF package, and
    /// <see cref="Only_admitted_consumers_and_pilot_projects_resolve_ef_core_packages"/> keeps it that way, which is what
    /// lets a host compose it without EF; <c>Elsa.Foundation.Host</c> carries EF only for its opt-in durable
    /// membership provider and its opt-in Data Protection key store (#2151, #2191). Its tests read the declarations of
    /// two first-party module assemblies, which resolve EF Core and Relational and never a provider engine.
    /// </summary>
    internal static class Spec183ReadabilityEf
    {
        public static readonly IReadOnlyDictionary<string, string[]> ExpectedEfPackagesByProject =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Essentials/tests/Cluster/Readability/Tests/Elsa.Cluster.Readability.Tests.csproj"] = CorePackages()
            };

        public static bool IsProjectPath(string relativePath) =>
            ExpectedEfPackagesByProject.ContainsKey(relativePath);

        private static string[] CorePackages() =>
        [
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Analyzers",
            "Microsoft.EntityFrameworkCore.Relational"
        ];
    }

    /// <summary>
    /// The closure of a provider-neutral module composed on a host's own container: EF Core and Relational with what they
    /// bring, and no engine. Cluster membership and the Data Protection key store carry exactly this; each test project adds
    /// the engines it runs on.
    /// </summary>
    private static string[] HostComposedModulePackages() =>
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Abstractions",
        "Microsoft.EntityFrameworkCore.Analyzers",
        "Microsoft.EntityFrameworkCore.Relational"
    ];

    /// <summary>
    /// The opt-in EF Core cluster membership provider (#2098). The module is provider-neutral:
    /// a host composes it once on its own container and brings the engine; provider engines stay in its focused tests,
    /// SQLite in the fast suite and the three container engines in its provider legs.
    /// </summary>
    internal static class Adr0078ClusterMembershipEf
    {
        public static readonly IReadOnlyDictionary<string, string[]> ExpectedEfPackagesByProject =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Essentials/src/Cluster/EntityFrameworkCore/Elsa.Cluster.EntityFrameworkCore.csproj"] = HostComposedModulePackages(),
                // The shared conformance fixture and scenarios: engine-neutral, each test project supplies the engine.
                ["Essentials/tests/Cluster/EntityFrameworkCore/Testing/Elsa.Cluster.EntityFrameworkCore.Testing.csproj"] = HostComposedModulePackages(),
                ["Essentials/tests/Cluster/EntityFrameworkCore/Tests/Elsa.Cluster.EntityFrameworkCore.Tests.csproj"] =
                    [.. HostComposedModulePackages(), "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite.Core"],
                ["Essentials/tests/Cluster/EntityFrameworkCore/ProviderTests/Elsa.Cluster.EntityFrameworkCore.ProviderTests.csproj"] =
                    [.. HostComposedModulePackages(), "Microsoft.EntityFrameworkCore.SqlServer", "MySql.EntityFrameworkCore", "Npgsql.EntityFrameworkCore.PostgreSQL"]
            };

        public static IEnumerable<string> ProjectPaths => ExpectedEfPackagesByProject.Keys;

        public static bool IsProjectPath(string relativePath) =>
            ProjectPaths.Contains(relativePath, StringComparer.Ordinal);
    }

    /// <summary>
    /// The host's Data Protection composition and its opt-in EF key store (Elsa.Foundation.Host
    /// may also carry it). Like cluster membership, a host composes it once on its own container and brings the engine;
    /// provider engines stay in its focused tests, SQLite in the fast suite and the three container engines in its provider
    /// legs.
    /// </summary>
    internal static class Adr0076DataProtectionKeyStoreEf
    {
        public static readonly IReadOnlyDictionary<string, string[]> ExpectedEfPackagesByProject =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Essentials/src/Foundation/DataProtection/EntityFrameworkCore/Elsa.Foundation.DataProtection.EntityFrameworkCore.csproj"] =
                    HostComposedModulePackages(),
                ["Essentials/tests/Foundation/DataProtection/EntityFrameworkCore/Tests/Elsa.Foundation.DataProtection.EntityFrameworkCore.Tests.csproj"] =
                    [.. HostComposedModulePackages(), "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite.Core"],
                ["Essentials/tests/Foundation/DataProtection/EntityFrameworkCore/ProviderTests/Elsa.Foundation.DataProtection.EntityFrameworkCore.ProviderTests.csproj"] =
                    [.. HostComposedModulePackages(), "Microsoft.EntityFrameworkCore.SqlServer", "MySql.EntityFrameworkCore", "Npgsql.EntityFrameworkCore.PostgreSQL"]
            };

        public static IEnumerable<string> ProjectPaths => ExpectedEfPackagesByProject.Keys;

        public static bool IsProjectPath(string relativePath) =>
            ProjectPaths.Contains(relativePath, StringComparer.Ordinal);
    }
}
