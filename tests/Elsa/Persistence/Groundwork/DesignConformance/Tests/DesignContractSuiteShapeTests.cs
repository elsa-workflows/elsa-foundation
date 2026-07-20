using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Elsa.Events.Core.Contracts;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

public class DesignContractSuiteShapeTests
{
    private static readonly IReadOnlySet<string> AllowedAssemblyNames = new HashSet<string>(
        [
            "Elsa.Activities.Design.Core",
            "Elsa.Activities.Design.Persistence.Core",
            "Elsa.Activities.Design.Reconciliation.Core",
            "Elsa.Events.Core",
            "Elsa.Expressions.Core",
            "Elsa.Persistence.Core",
            "Elsa.Persistence.Groundwork.DesignConformance.Tests",
            "Elsa.Primitives",
            "Elsa.Serialization.Core",
            "Elsa.Workflows.Design.Core",
            "Elsa.Workflows.Design.Persistence.Core",
            "Elsa.Workflows.Design.Validations.Core",
            "Microsoft.Extensions.DependencyInjection",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.VisualStudio.TestPlatform.ObjectModel",
            "System.Collections",
            "System.ComponentModel",
            "System.Linq",
            "System.Memory",
            "System.Private.CoreLib",
            "System.Runtime",
            "System.Security.Cryptography",
            "System.Text.Json",
            "System.Xml.XDocument",
            "xunit.assert",
            "xunit.core"
        ],
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> AllowedPackageReferences = new HashSet<string>(
        [
            "Microsoft.Extensions.DependencyInjection",
            "Microsoft.NET.Test.Sdk",
            "coverlet.collector",
            "xunit",
            "xunit.runner.visualstudio"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> AllowedProjectReferences = new HashSet<string>(
        [
            "../../../../../../src/Elsa/Activities/Design/Reconciliation/Core/Elsa.Activities.Design.Reconciliation.Core.csproj",
            "../../../../../../src/Elsa/Activities/Design/Persistence/Core/Elsa.Activities.Design.Persistence.Core.csproj",
            "../../../../../../src/Elsa/Persistence/Core/Elsa.Persistence.Core.csproj",
            "../../../../../../src/Elsa/Primitives/Hosting/Elsa.Primitives.Hosting.csproj",
            "../../../../../../src/Elsa/Primitives/Primitives/Elsa.Primitives.csproj",
            "../../../../../../src/Elsa/Serialization/Core/Elsa.Serialization.Core.csproj",
            "../../../../../../src/Elsa/Workflows/Design/Persistence/Core/Elsa.Workflows.Design.Persistence.Core.csproj"
        ],
        StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Shared_assembly_and_fixture_contracts_are_provider_neutral()
    {
        Assert.True(typeof(WorkflowDesignContractSuite).IsAbstract);
        Assert.True(typeof(ActivityDesignContractSuite).IsAbstract);

        var referencedAssemblies = Assembly.GetExecutingAssembly()
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)
            .ToArray();
        AssertAllowedReferences("assembly", referencedAssemblies, AllowedAssemblyNames);

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

        AssertAllowedReferences(
            "fixture signature assembly",
            signatureTypes.Select(type => type.Assembly.GetName().Name!),
            AllowedAssemblyNames);
    }

    [Fact]
    public void Shared_project_allows_only_declared_core_projects_and_test_packages()
    {
        var project = XDocument.Load(ProjectFilePath());
        var packageReferences = Includes(project, "PackageReference");
        var projectReferences = Includes(project, "ProjectReference")
            .Select(NormalizeProjectReference)
            .ToArray();

        AssertAllowedReferences("package", packageReferences, AllowedPackageReferences);
        AssertAllowedReferences("project", projectReferences, AllowedProjectReferences);
    }

    [Theory]
    [InlineData("Elsa.Persistence.Groundwork")]
    [InlineData("Groundwork.Documents")]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Microsoft.Data.Sqlite")]
    public void Provider_assemblies_are_outside_the_allowlist(string assemblyName)
    {
        Assert.Contains(assemblyName, UnexpectedReferences([assemblyName], AllowedAssemblyNames));
    }

    [Theory]
    [InlineData("Groundwork.Documents")]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite")]
    public void Unused_provider_packages_are_outside_the_allowlist(string packageName)
    {
        Assert.Contains(packageName, UnexpectedReferences([packageName], AllowedPackageReferences));
    }

    [Theory]
    [InlineData("../../../../../../src/Elsa/Persistence/Groundwork/Elsa.Persistence.Groundwork.csproj")]
    [InlineData("../../../../../../src/Elsa/Persistence/EntityFrameworkCore/Elsa.Persistence.EntityFrameworkCore.csproj")]
    public void Provider_projects_are_outside_the_allowlist(string projectReference)
    {
        var normalized = NormalizeProjectReference(projectReference);

        Assert.Contains(normalized, UnexpectedReferences([normalized], AllowedProjectReferences));
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

    private static void AssertAllowedReferences(
        string kind,
        IEnumerable<string> actual,
        IReadOnlySet<string> allowed)
    {
        var unexpected = UnexpectedReferences(actual, allowed);
        Assert.True(
            unexpected.Length == 0,
            $"Shared design conformance {kind} references must be provider-neutral. Unexpected: {string.Join(", ", unexpected)}.");
    }

    private static string[] UnexpectedReferences(
        IEnumerable<string> actual,
        IReadOnlySet<string> allowed) =>
        actual
            .Where(reference => !allowed.Contains(reference))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string[] Includes(XDocument project, string itemName) =>
        project
            .Descendants()
            .Where(element => element.Name.LocalName == itemName)
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();

    private static string NormalizeProjectReference(string reference) =>
        reference.Replace('\\', '/');

    private static string ProjectFilePath([CallerFilePath] string sourceFilePath = "") =>
        Path.Combine(
            Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("The design conformance test source directory could not be resolved."),
            "Elsa.Persistence.Groundwork.DesignConformance.Tests.csproj");
}
