using System.Xml.Linq;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>Owns the distributed Runtime EF production-boundary and subtraction guard.</summary>
public sealed class RuntimeExecutionPlacementPersistenceArchitectureTests
{
    private static readonly string ProductionRoot = Path.Join(
        RepoRoot, "src", "essentials", "Workflows", "Runtime", "Distributed", "Persistence", "EntityFrameworkCore");

    [Fact]
    public void Production_adapter_is_provider_neutral_and_has_no_migrations()
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
        Assert.Empty(Directory.EnumerateFiles(ProductionRoot, "*Migration*", SearchOption.AllDirectories));
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
}
