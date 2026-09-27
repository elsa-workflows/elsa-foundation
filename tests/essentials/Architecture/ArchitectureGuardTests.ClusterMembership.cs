using System.Xml.Linq;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed partial class ArchitectureGuardTests
{
    private const string ClusterMembershipContract = "Elsa.Cluster.Core";
    private const string InProcessClusterMembership = "Elsa.Cluster.InProcess";

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

    /// <summary>Every cluster project but the contract and the in-process default is a provider, as is any concrete
    /// persistence provider.</summary>
    private static bool IsMembershipProviderProject(string name, string relativePath) =>
        name.StartsWith("Elsa.Cluster.", StringComparison.Ordinal) && name is not ClusterMembershipContract and not InProcessClusterMembership ||
        PersistenceProviderNeutralityBoundary.IsConcreteProviderProject(name, relativePath);

    private static bool IsMembershipProviderPackage(string package) =>
        PersistenceProviderNeutralityBoundary.IsConcreteProviderPackage(package) ||
        AdditionalDatabaseDriverPrefixes.Concat(ActorFrameworkPackagePrefixes).Any(prefix => package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
