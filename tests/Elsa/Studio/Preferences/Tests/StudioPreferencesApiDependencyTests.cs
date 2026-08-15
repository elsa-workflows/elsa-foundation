using System.Reflection;
using System.Xml.Linq;
using Elsa.Studio.Preferences.Api;
using Xunit;

namespace Elsa.Studio.Preferences.Tests;

public sealed class StudioPreferencesApiDependencyTests
{
    [Fact]
    public void Production_api_does_not_reference_fastendpoints_assemblies()
    {
        var references = typeof(StudioPreferencesApiFeature).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(
            references,
            reference => reference.Name?.Contains("FastEndpoints", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void Production_api_contains_no_fastendpoints_endpoint_bases_or_discovery_interfaces()
    {
        var offenders = GetLoadableTypes(typeof(StudioPreferencesApiFeature).Assembly)
            .Where(type => HasFastEndpointsBase(type) || HasFastEndpointsInterface(type))
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Production_project_has_no_fastendpoints_package_or_project_reference()
    {
        var projectPath = FindProjectPath();
        var project = XDocument.Load(projectPath);
        var references = project.Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => include is not null)
            .ToArray();

        Assert.DoesNotContain(
            references,
            include => include!.Contains("FastEndpoints", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasFastEndpointsBase(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.FullName?.Contains("FastEndpoints", StringComparison.OrdinalIgnoreCase) == true ||
                current.Assembly.GetName().Name?.Contains("FastEndpoints", StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }

        return false;
    }

    private static bool HasFastEndpointsInterface(Type type) => type.GetInterfaces().Any(@interface =>
        @interface.FullName?.Contains("FastEndpoints", StringComparison.OrdinalIgnoreCase) == true ||
        @interface.Assembly.GetName().Name?.Contains("FastEndpoints", StringComparison.OrdinalIgnoreCase) == true);

    private static IReadOnlyCollection<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }
    }

    private static string FindProjectPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Elsa",
                "Studio",
                "Preferences",
                "Api",
                "Elsa.Studio.Preferences.Api.csproj");

            if (File.Exists(candidate))
                return candidate;
        }

        throw new DirectoryNotFoundException(
            "Could not locate Elsa.Studio.Preferences.Api.csproj from the test output directory.");
    }
}
