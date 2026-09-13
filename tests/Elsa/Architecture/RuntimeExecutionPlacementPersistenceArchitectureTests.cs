using System.Xml.Linq;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>Owns the distributed Runtime EF production-boundary and subtraction guard.</summary>
public sealed class RuntimeExecutionPlacementPersistenceArchitectureTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string ProductionRoot = Path.Join(
        RepoRoot, "src", "Elsa", "Workflows", "Runtime", "Distributed", "Persistence", "EntityFrameworkCore");

    [Fact]
    public void Production_adapter_is_provider_neutral_and_has_no_groundwork_or_migrations()
    {
        var project = XDocument.Load(Path.Join(ProductionRoot, "Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.csproj"));
        var packages = project.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .ToArray();
        Assert.DoesNotContain(packages, package => package.Contains("Sqlite", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(packages, package => package.Contains("SqlServer", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(packages, package => package.Contains("Postgre", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(packages, package => package.Contains("MySql", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(packages, package => package.StartsWith("Groundwork.", StringComparison.Ordinal));
        Assert.Empty(Directory.EnumerateFiles(ProductionRoot, "*Migration*", SearchOption.AllDirectories));

        var source = Directory.EnumerateFiles(ProductionRoot, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();
        Assert.DoesNotContain(source, text => text.Contains("using Groundwork.", StringComparison.Ordinal));
        Assert.DoesNotContain(source, text => text.Contains("IQueryable", StringComparison.Ordinal));
        Assert.DoesNotContain(source, text => text.Contains("FromSql", StringComparison.Ordinal));
    }

    [Fact]
    public void D01_surface_contains_one_context_entity_store_registration_and_feature()
    {
        var paths = Directory.EnumerateFiles(ProductionRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetFileName(path))
            .ToArray();
        Assert.Contains("ExecutionPlacementDbContext.cs", paths);
        Assert.Contains("ExecutionPlacementLeaseEntity.cs", paths);
        Assert.Contains("EfExecutionPlacementStore.cs", paths);
        Assert.Contains("DistributedRuntimeExecutionPlacementEntityFrameworkCoreRegistration.cs", paths);
        Assert.Contains("DistributedRuntimeExecutionPlacementEntityFrameworkCoreFeature.cs", paths);
    }

    [Fact]
    public void D02_D03_surface_contains_the_shared_context_entities_mappings_registration_and_feature()
    {
        var paths = Directory.EnumerateFiles(ProductionRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetFileName(path))
            .ToArray();

        Assert.Contains("ExecutionCommandTransportDbContext.cs", paths);
        Assert.Contains("ExecutionCommandStreamHeadEntity.cs", paths);
        Assert.Contains("ExecutionCommandTransportItemEntity.cs", paths);
        Assert.Contains("ExecutionCommandStreamHeadEntityConfiguration.cs", paths);
        Assert.Contains("ExecutionCommandTransportItemEntityConfiguration.cs", paths);
        Assert.Contains("ExecutionCommandTransportEfModule.cs", paths);
        Assert.Contains("ExecutionCommandTransportEntityFrameworkPersistenceException.cs", paths);
        Assert.Contains("DistributedRuntimeExecutionCommandTransportEntityFrameworkCoreRegistration.cs", paths);
        Assert.Contains("DistributedRuntimeExecutionCommandTransportEntityFrameworkCoreFeature.cs", paths);
        Assert.Contains("EfExecutionCommandTransport.cs", paths);
    }

    [Fact]
    public void D02_D03_production_source_does_not_advertise_runtime_lease_fencing_or_provider_engines()
    {
        var source = Directory.EnumerateFiles(ProductionRoot, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.DoesNotContain(source, text => text.Contains("IWorkflowExecutionLeaseFencingCapability", StringComparison.Ordinal));
        Assert.DoesNotContain(source, text => text.Contains("Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal));
        Assert.DoesNotContain(source, text => text.Contains("Microsoft.EntityFrameworkCore.SqlServer", StringComparison.Ordinal));
        Assert.DoesNotContain(source, text => text.Contains("Npgsql.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(source, text => text.Contains("MySql.EntityFrameworkCore", StringComparison.Ordinal));
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
