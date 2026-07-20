using System.Reflection;
using Elsa.Events.Core.Contracts;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

public class DesignContractSuiteShapeTests
{
    [Fact]
    public void Shared_assembly_and_fixture_contracts_are_provider_neutral()
    {
        Assert.True(typeof(WorkflowDesignContractSuite).IsAbstract);
        Assert.True(typeof(ActivityDesignContractSuite).IsAbstract);

        var referencedAssemblies = Assembly.GetExecutingAssembly().GetReferencedAssemblies();
        Assert.DoesNotContain(referencedAssemblies, assembly => ForbiddenProviderAssembly(assembly.Name));

        var fixtureTypes = new[]
        {
            typeof(IDesignPersistenceContractFixture),
            typeof(IDesignPersistenceContractFixtureFactory)
        };
        var signatureTypes = fixtureTypes
            .SelectMany(type => type.GetMethods())
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
            .Concat(fixtureTypes.SelectMany(type => type.GetProperties()).Select(property => property.PropertyType))
            .SelectMany(ExpandType)
            .Distinct()
            .ToArray();

        Assert.DoesNotContain(signatureTypes, type => ForbiddenProviderAssembly(type.Assembly.GetName().Name));
    }

    [Fact]
    public void Fixture_exposes_staging_and_actual_event_observation_but_no_reconciliation_shortcut()
    {
        var fixtureType = typeof(IDesignPersistenceContractFixture);

        Assert.NotNull(fixtureType.GetMethod(nameof(IDesignPersistenceContractFixture.StageActivityReconciliationCandidatesAsync)));
        Assert.Null(fixtureType.GetMethod("ReconcileActivityVersionsAsync"));

        var observation = fixtureType.GetMethod(nameof(IDesignPersistenceContractFixture.ReadObservedEventsAsync));
        Assert.NotNull(observation);
        Assert.Equal(typeof(Task<IReadOnlyList<IEvent>>), observation!.ReturnType);
    }

    private static IEnumerable<Type> ExpandType(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is { } elementType)
        {
            foreach (var nested in ExpandType(elementType))
                yield return nested;
        }

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var nested in ExpandType(argument))
                yield return nested;
        }
    }

    private static bool ForbiddenProviderAssembly(string? assemblyName) =>
        assemblyName?.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) == true ||
        assemblyName?.Contains(".EFCore", StringComparison.OrdinalIgnoreCase) == true ||
        assemblyName?.StartsWith("Groundwork.", StringComparison.OrdinalIgnoreCase) == true ||
        assemblyName?.StartsWith("MongoDB.", StringComparison.OrdinalIgnoreCase) == true ||
        assemblyName?.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase) == true ||
        assemblyName?.Contains("MongoDb", StringComparison.OrdinalIgnoreCase) == true ||
        assemblyName?.Contains("PostgreSql", StringComparison.OrdinalIgnoreCase) == true ||
        assemblyName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true ||
        assemblyName?.Contains("SqlClient", StringComparison.OrdinalIgnoreCase) == true ||
        assemblyName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
}
