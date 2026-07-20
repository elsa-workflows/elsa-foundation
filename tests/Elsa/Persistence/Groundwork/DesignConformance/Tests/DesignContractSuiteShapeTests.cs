using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Elsa.Events.Core.Contracts;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

public class DesignContractSuiteShapeTests
{
    private static readonly string[] ReusableSourceFiles =
    [
        "WorkflowDesignContractSuite.cs",
        "ActivityDesignContractSuite.cs",
        "DesignPersistenceContractFixture.cs",
        "DesignPersistenceFixtureData.cs"
    ];

    private static readonly Regex[] ForbiddenProviderReferences =
    [
        new(@"\b(?:global::)?Microsoft\.EntityFrameworkCore\b", RegexOptions.CultureInvariant),
        new(@"\b(?:global::)?Groundwork\.(?!DesignConformance\b)", RegexOptions.CultureInvariant),
        new(@"\b(?:global::)?MongoDB\.Driver\b", RegexOptions.CultureInvariant),
        new(@"\b(?:global::)?Npgsql\b", RegexOptions.CultureInvariant),
        new(@"\b(?:global::)?Microsoft\.Data\.Sql(?:Client|ite)\b", RegexOptions.CultureInvariant)
    ];

    [Fact]
    public void Shared_suites_are_abstract_and_only_the_reusable_source_boundary_is_provider_guarded()
    {
        Assert.True(typeof(WorkflowDesignContractSuite).IsAbstract);
        Assert.True(typeof(ActivityDesignContractSuite).IsAbstract);

        foreach (var file in ReusableSourceFiles)
        {
            var source = File.ReadAllText(Path.Combine(SourceDirectory(), file));

            foreach (var forbiddenReference in ForbiddenProviderReferences)
                Assert.DoesNotMatch(forbiddenReference, source);
        }
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

        var activitySuiteSource = File.ReadAllText(Path.Combine(SourceDirectory(), "ActivityDesignContractSuite.cs"));
        Assert.Contains("GetRequiredService<IActivityVersionReconciler>()", activitySuiteSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Fixture_contract_signatures_are_provider_neutral()
    {
        var fixtureType = typeof(IDesignPersistenceContractFixture);
        var signatureTypes = fixtureType.GetMethods()
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
            .Concat(fixtureType.GetProperties().Select(property => property.PropertyType))
            .SelectMany(ExpandType)
            .Distinct()
            .ToArray();

        Assert.DoesNotContain(signatureTypes, type => ForbiddenProviderAssembly(type.Assembly.GetName().Name));
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
        assemblyName?.StartsWith("Groundwork.", StringComparison.OrdinalIgnoreCase) == true ||
        assemblyName?.StartsWith("MongoDB.", StringComparison.OrdinalIgnoreCase) == true ||
        assemblyName?.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase) == true ||
        assemblyName?.Contains("SqlClient", StringComparison.OrdinalIgnoreCase) == true ||
        assemblyName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    private static string SourceDirectory([CallerFilePath] string sourcePath = "") =>
        Path.GetDirectoryName(sourcePath) ?? throw new InvalidOperationException("Could not resolve the contract-suite source directory.");
}
