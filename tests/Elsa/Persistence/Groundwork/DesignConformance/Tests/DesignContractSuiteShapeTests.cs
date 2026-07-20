using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

public class DesignContractSuiteShapeTests
{
    [Fact]
    public void Shared_suites_are_abstract_and_provider_sdk_free()
    {
        Assert.True(typeof(WorkflowDesignContractSuite).IsAbstract);
        Assert.True(typeof(ActivityDesignContractSuite).IsAbstract);

        var references = typeof(WorkflowDesignContractSuite).Assembly.GetReferencedAssemblies()
            .Select(x => x.Name)
            .Where(x => x is not null)
            .ToArray();

        Assert.DoesNotContain(references, name => name!.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, name => name!.Contains("Groundwork", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, name => name!.Contains("Sqlite", StringComparison.OrdinalIgnoreCase));
    }
}
