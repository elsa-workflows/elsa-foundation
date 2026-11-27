using System.Reflection;
using System.Text.Json;
using Elsa.Migration.Host.Tests.Support;
using Xunit;

namespace Elsa.Migration.Host.Tests;

/// <summary>
/// Spec 1236 US3 and R-5: the host's composition (<c>shells.json</c>) and its reasons (<c>composition-reasons.json</c>) agree,
/// every composed feature resolves from the host's references, nothing of Elsa 3 is in its closure, and none of the Workbench
/// extras FR-002 names is composed. These read the committed files; they start no host.
/// </summary>
public sealed class CompositionTests
{
    // The feature families FR-002 keeps out of the host, matched as parts of a CShells feature name.
    private static readonly string[] ExcludedFamilies =
    [
        "Bpmn", "Weaver", "Copilot", "Agent", "Decisions", "Connections", "Dashboard", "ExtensionBuilder", "Secrets",
        "StudioPreferences", "Standalone", "Diagnostics", "Compensation", "Attention", "Modularity", "Liquid"
    ];

    [Fact]
    public void Every_composed_feature_has_a_reason_and_every_reason_names_a_composed_feature()
    {
        var composed = ComposedFeatures();
        var reasons = Reasons();

        var withoutReason = composed.Where(feature => !reasons.ContainsKey(feature)).ToArray();
        var notComposed = reasons.Keys.Where(feature => !composed.Contains(feature)).ToArray();

        Assert.True(withoutReason.Length == 0, $"Composed in shells.json without a reason in composition-reasons.json: {string.Join(", ", withoutReason)}.");
        Assert.True(notComposed.Length == 0, $"composition-reasons.json names features shells.json does not compose: {string.Join(", ", notComposed)}.");
        var blank = reasons.Where(reason => string.IsNullOrWhiteSpace(reason.Value)).Select(reason => reason.Key).ToArray();
        Assert.True(blank.Length == 0, $"Features with an empty reason: {string.Join(", ", blank)}.");
    }

    [Fact]
    public void Every_composed_feature_is_provided_by_an_assembly_the_host_references()
    {
        var available = AvailableFeatureNames();

        var unresolved = ComposedFeatures().Where(feature => !available.Contains(feature)).ToArray();

        Assert.True(unresolved.Length == 0, $"shells.json composes features no assembly the host references provides: {string.Join(", ", unresolved)}.");
    }

    [Fact]
    public void None_of_the_excluded_Workbench_feature_families_is_composed()
    {
        var extras = ComposedFeatures()
            .Where(feature => ExcludedFamilies.Any(family => feature.Contains(family, StringComparison.Ordinal)))
            .ToArray();

        Assert.True(extras.Length == 0, $"shells.json composes features spec 1236 FR-002 excludes: {string.Join(", ", extras)}.");
    }

    [Fact]
    public void The_host_closure_holds_no_Elsa_3_package_or_project()
    {
        var assetsFile = Path.Combine(HostPaths.ProjectFolder, "obj", "project.assets.json");
        Assert.True(File.Exists(assetsFile), $"'{assetsFile}' is missing; restore the host first.");
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsFile));

        var elsa3 = assets.RootElement.GetProperty("libraries").EnumerateObject()
            .Select(library => (Id: library.Name, Type: library.Value.GetProperty("type").GetString()))
            .Where(library => IsElsa3(library.Id, library.Type))
            .Select(library => library.Id)
            .ToArray();

        Assert.True(elsa3.Length == 0, $"The host's dependency closure holds Elsa 3: {string.Join(", ", elsa3)}.");
    }

    /// <summary>An <c>Elsa3.*</c> package or project, or an <c>Elsa*</c> package of major version 3 (they share Elsa 4's ids).</summary>
    private static bool IsElsa3(string library, string? type)
    {
        var separator = library.IndexOf('/', StringComparison.Ordinal);
        var name = library[..separator];
        var version = library[(separator + 1)..];
        if (name.StartsWith("Elsa3", StringComparison.OrdinalIgnoreCase))
            return true;

        return type == "package" && name.StartsWith("Elsa", StringComparison.OrdinalIgnoreCase) && version.StartsWith("3.", StringComparison.Ordinal);
    }

    private static HashSet<string> ComposedFeatures()
    {
        using var shells = JsonDocument.Parse(File.ReadAllText(Path.Combine(HostPaths.ProjectFolder, "shells.json")));
        return shells.RootElement.GetProperty("CShells").GetProperty("Shells").GetProperty("default").GetProperty("Features")
            .EnumerateObject().Select(feature => feature.Name).ToHashSet(StringComparer.Ordinal);
    }

    private static Dictionary<string, string> Reasons()
    {
        using var reasons = JsonDocument.Parse(File.ReadAllText(Path.Combine(HostPaths.ProjectFolder, "composition-reasons.json")));
        return reasons.RootElement.EnumerateObject().ToDictionary(reason => reason.Name, reason => reason.Value.GetString() ?? "", StringComparer.Ordinal);
    }

    /// <summary>
    /// The CShells feature names in the Elsa assemblies of this test's output, which holds the host's references: the set the
    /// host's <c>WithHostAssemblies()</c> discovers from. Read through attribute metadata, so nothing is instantiated.
    /// </summary>
    private static HashSet<string> AvailableFeatureNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "Elsa.*.dll"))
        {
            Type[] types;
            try
            {
                types = Assembly.LoadFrom(file).GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.Where(type => type is not null).ToArray()!;
            }
            catch (BadImageFormatException)
            {
                continue;
            }

            foreach (var type in types)
            {
                foreach (var attribute in type.GetCustomAttributesData())
                {
                    if (attribute.AttributeType.FullName == "CShells.Features.ShellFeatureAttribute"
                        && attribute.ConstructorArguments.FirstOrDefault().Value is string name)
                        names.Add(name);
                }
            }
        }

        return names;
    }
}
