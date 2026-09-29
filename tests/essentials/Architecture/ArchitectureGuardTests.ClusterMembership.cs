using System.Xml.Linq;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

public sealed partial class ArchitectureGuardTests
{
    private const string ClusterMembershipContract = "Elsa.Cluster.Core";
    private const string InProcessClusterMembership = "Elsa.Cluster.InProcess";
    private const string SharedSchemaSeam = "Elsa.Persistence.Schema";
    private const string ReadabilitySource = "Elsa.Cluster.Readability";

    /// <summary>Package-id prefixes of the actor frameworks ADR 0078 admits only as membership providers.</summary>
    private static readonly string[] ActorFrameworkPackagePrefixes = ["Microsoft.Orleans", "Orleans.", "Proto.Actor", "Proto.Cluster", "Proto.Remote", "Akka"];

    /// <summary>Database drivers <see cref="PersistenceProviderNeutralityBoundary"/> does not already recognize.</summary>
    private static readonly string[] AdditionalDatabaseDriverPrefixes = ["MySqlConnector", "MySql.Data"];

    /// <summary>
    /// The membership contract takes no provider dependency, declared or transitive: no EF Core, database engine, actor
    /// framework or membership provider project (spec 183, FR-002; #2097, Acceptance). Swapping the provider must
    /// never require touching the contract or its consumers.
    /// </summary>
    [Fact]
    public void Cluster_membership_contract_takes_no_provider_dependency() =>
        AssertNoMembershipProviderDependency(ClusterMembershipContract);

    /// <summary>
    /// A host that is not clustered gets the in-process default, so that default's closure is what non-clustered
    /// hosting pays for membership (FR-018; #2097). It reaches no provider, and no hosting or task library, so it cannot
    /// register a hosted service, a recurring task or a store.
    /// </summary>
    [Fact]
    public void Non_clustered_hosting_takes_no_hard_dependency_on_a_membership_provider()
    {
        AssertNoMembershipProviderDependency(InProcessClusterMembership);

        var project = ProjectFiles().Single(candidate => candidate.Name == InProcessClusterMembership);
        var closure = ReachableProjects(project).Prepend(project).ToArray();
        var hostingDependencies = closure
            .Where(reached => reached.Name.StartsWith("Elsa.Tasks", StringComparison.Ordinal))
            .Select(reached => $"{project.Name} reaches {reached.Name}")
            .Concat(closure
                .SelectMany(PackageReferences)
                .Where(package => package.StartsWith("Microsoft.Extensions.Hosting", StringComparison.OrdinalIgnoreCase))
                .Select(package => $"{project.Name} reaches package {package}"))
            .Concat(closure
                .SelectMany(reached => XDocument.Load(reached.FullPath).Descendants("FrameworkReference"))
                .Select(reference => $"{project.Name} reaches framework {reference.Attribute("Include")?.Value}"))
            .ToArray();

        Assert.True(hostingDependencies.Length == 0,
            "The in-process membership default must have no way to run anything in the background:" + Environment.NewLine +
            string.Join(Environment.NewLine, hostingDependencies));
    }

    /// <summary>
    /// The EF-free half of the schema-family surface, which every host shares with every package it loads (#2143; ADR
    /// 0067, amended 2026-09-29): no EF Core, database engine, actor framework or provider project, no Elsa project outside
    /// Line A, and no package but the <c>Microsoft.Extensions</c> abstractions. Anything more would be pinned by every host
    /// and loaded by every package, and EF Core in it would put EF into every host, even one that composes only the
    /// in-process default (ADR 0076; #2151 admits EF into Elsa.Foundation.Host for its opt-in membership provider only).
    /// </summary>
    [Fact]
    public void The_shared_schema_seam_takes_no_provider_and_only_abstractions()
    {
        AssertNoMembershipProviderDependency(SharedSchemaSeam);

        var project = ProjectFiles().Single(candidate => candidate.Name == SharedSchemaSeam);
        var lineA = VersionLines.LineAMembers(RepoRoot).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var violations = ReachableProjects(project)
            .Where(reached => !lineA.Contains(reached.Name))
            .Select(reached => $"{project.Name} reaches {reached.Name}, which is not on Line A")
            .Concat(PackageReferences(project)
                .Where(package => !package.StartsWith("Microsoft.Extensions.", StringComparison.OrdinalIgnoreCase))
                .Select(package => $"{project.Name} references package {package}"))
            .ToArray();

        Assert.True(violations.Length == 0,
            $"{SharedSchemaSeam} is shared by every host, so it may reach only Line A and Microsoft.Extensions abstractions:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// What a host composes to reach its membership: the readability report, the fleet and the dormancy source (#2143). A
    /// host composes it whether or not it clusters, so it reaches no EF Core and no database engine: a cluster of one
    /// needs no EF at all, and <c>Elsa.Foundation.Host</c> carries EF only for the opt-in durable provider (ADR 0076,
    /// amended 2026-09-29; #2151). It is still classed as a provider project by name below, which keeps the contract and
    /// the in-process default from reaching it.
    /// </summary>
    [Fact]
    public void The_readability_source_a_host_composes_takes_no_ef_dependency()
    {
        var project = ProjectFiles().Single(candidate => candidate.Name == ReadabilitySource);
        var violations = ReachableProjects(project)
            .Prepend(project)
            .SelectMany(PackageReferences)
            .Where(PersistenceProviderNeutralityBoundary.IsConcreteProviderPackage)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Concat(ReachableProjects(project)
                .Where(reached => PersistenceProviderNeutralityBoundary.IsConcreteProviderProject(reached.Name, reached.RelativePath) ||
                                  reached.Name == "Elsa.Persistence.EntityFramework")
                .Select(reached => reached.Name))
            .ToArray();

        Assert.True(violations.Length == 0,
            $"{ReadabilitySource} must reach no EF Core, engine or EF persistence project, so a host without EF can compose it:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore", true)]
    [InlineData("Pomelo.EntityFrameworkCore.MySql", true)]
    [InlineData("Npgsql", true)]
    [InlineData("Microsoft.Data.SqlClient", true)]
    [InlineData("Microsoft.Data.Sqlite", true)]
    [InlineData("MySqlConnector", true)]
    [InlineData("Microsoft.Orleans.Server", true)]
    [InlineData("Proto.Cluster", true)]
    [InlineData("Akka.Cluster", true)]
    [InlineData("Microsoft.Extensions.Options", false)]
    [InlineData("Microsoft.Extensions.DependencyInjection.Abstractions", false)]
    public void Membership_provider_packages_are_recognized(string package, bool isProvider) =>
        Assert.Equal(isProvider, IsMembershipProviderPackage(package));

    [Theory]
    [InlineData("Elsa.Cluster.EntityFrameworkCore", "src/essentials/Cluster/EntityFrameworkCore/Elsa.Cluster.EntityFrameworkCore.csproj", true)]
    [InlineData("Elsa.Cluster.Orleans", "src/extensions/Orleans/src/Cluster/Elsa.Cluster.Orleans.csproj", true)]
    [InlineData("Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore", "src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.csproj", true)]
    [InlineData("Elsa.Cluster.Readability", "src/essentials/Cluster/Readability/Elsa.Cluster.Readability.csproj", true)]
    [InlineData(InProcessClusterMembership, "src/essentials/Cluster/InProcess/Elsa.Cluster.InProcess.csproj", false)]
    [InlineData("Elsa.Primitives", "src/essentials/Primitives/Primitives/Elsa.Primitives.csproj", false)]
    public void Membership_provider_projects_are_recognized(string name, string path, bool isProvider) =>
        Assert.Equal(isProvider, IsMembershipProviderProject(name, path));

    private static void AssertNoMembershipProviderDependency(string projectName)
    {
        var project = ProjectFiles().Single(candidate => candidate.Name == projectName);
        var reached = ReachableProjects(project).ToArray();
        var violations = reached
            .Where(reference => IsMembershipProviderProject(reference.Name, reference.RelativePath))
            .Select(reference => $"{project.Name} reaches provider project {reference.RelativePath}")
            .Concat(reached
                .Prepend(project)
                .SelectMany(PackageReferences)
                .Where(IsMembershipProviderPackage)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(package => $"{project.Name} reaches provider package {package}"))
            .ToArray();

        Assert.True(violations.Length == 0,
            $"{projectName} must take no membership provider dependency (ADR 0078; spec 183, FR-002):" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    /// <summary>Every cluster project but the contract and the in-process default is a provider, or bridges membership to
    /// one as the readability source bridges it to the EF persistence's schema families, and so is any concrete
    /// persistence provider.</summary>
    private static bool IsMembershipProviderProject(string name, string relativePath) =>
        name.StartsWith("Elsa.Cluster.", StringComparison.Ordinal) && name is not ClusterMembershipContract and not InProcessClusterMembership ||
        PersistenceProviderNeutralityBoundary.IsConcreteProviderProject(name, relativePath);

    private static bool IsMembershipProviderPackage(string package) =>
        PersistenceProviderNeutralityBoundary.IsConcreteProviderPackage(package) ||
        AdditionalDatabaseDriverPrefixes.Concat(ActorFrameworkPackagePrefixes).Any(prefix => package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
