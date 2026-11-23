using Xunit;
using Xunit.Abstractions;
using System.Reflection;

namespace Elsa.Architecture.Tests;

public sealed class ShippingHashAssembliesTests(ITestOutputHelper output)
{
    private static string Configuration => typeof(ShippingHashAssembliesTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
    [Fact]
    public void Inventory_includes_hosts_and_all_Studio_CSharp_roots()
    {
        var projects = ShippingHashAssemblies.Discover(RepoPaths.RepoRoot, Configuration);
        Assert.Contains(projects, project => project.Path.Replace('\\', '/').Contains("Essentials/apps/Elsa.Workbench/", StringComparison.Ordinal));
        Assert.Contains(projects, project => project.Path.Replace('\\', '/').Contains("Essentials/apps/Elsa.Foundation.Host/", StringComparison.Ordinal));
        Assert.Contains(projects, project => project.Path.Replace('\\', '/').Contains("Studio/src/", StringComparison.Ordinal));
        Assert.DoesNotContain(projects, project => !ModuleRoots.IsNotTestFile(RepoPaths.RepoRoot, project.Path));
    }

    [Fact]
    public void A_missing_or_newer_source_cannot_borrow_a_shipping_assembly()
    {
        var root = Path.Join(RepoPaths.RepoRoot, ".agent-prefs", "s9-hash-assembly-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Join(root, "Elsa.Fixture.csproj");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            var path = Path.Join(root, "bin", "Debug", "net10.0", "Elsa.Fixture.dll");
            var entry = new ShippingHashAssemblies.Project(project, path);
            Assert.Contains(ShippingHashAssemblies.ValidateInputs(root, [entry], Configuration, _ => [project]), error => error.StartsWith("Missing shipping assembly", StringComparison.Ordinal));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, FixtureCompiler.Compile("Elsa.Fixture", "public sealed class Fixture { }"));
            File.SetLastWriteTimeUtc(project, DateTime.UtcNow.AddMinutes(-2));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-1));
            Assert.Empty(ShippingHashAssemblies.ValidateInputs(root, [entry], Configuration, _ => [project]));
            var source = Path.Join(root, "Fixture.cs");
            File.WriteAllText(source, "public sealed class Fixture { public int NewHashedMember { get; init; } }");
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow);
            Assert.Contains(ShippingHashAssemblies.ValidateInputs(root, [entry], Configuration, _ => [project, source]), error => error.StartsWith("Stale shipping assembly", StringComparison.Ordinal));
        }
        finally
        {
            var target = Path.GetFullPath(root);
            var workspace = Path.GetFullPath(Path.Join(RepoPaths.RepoRoot, ".agent-prefs")) + Path.DirectorySeparatorChar;
            Assert.StartsWith(workspace, target, StringComparison.Ordinal);
            Directory.Delete(target, recursive: true);
        }
    }

    [Fact]
    public void Every_shipping_CSharp_assembly_can_be_inspected_by_resolved_hash_identity()
    {
        using var inventory = ShippingHashAssemblies.Load(RepoPaths.RepoRoot, Configuration);
        Assert.True(inventory.Errors.Count == 0, string.Join('\n', inventory.Errors));
        var graph = JsonHashReferenceGraph.Scan(inventory.Assemblies);
        Assert.True(graph.Errors.Count == 0, string.Join('\n', graph.Errors));
        Assert.Contains(graph.References, reference => reference.Role == "json-hash-input");
        Assert.Contains(graph.References, reference => reference.Role == "digest-primitive");
        var evidence = Path.Join(Path.GetTempPath(), "elsa-s9-hash-inventory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Join(evidence, "references.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            Projects = inventory.Projects.Select(project => new { Path = Path.GetRelativePath(RepoPaths.RepoRoot, project.Path).Replace('\\', '/'), project.ImageSha256, project.ImageMvid }),
            graph.Definitions, graph.References
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine($"Inspected {inventory.Projects.Count} shipping projects, {graph.Definitions.Count} definitions and {graph.References.Count} resolved references. Inventory: {evidence}");
    }

    [Fact]
    public void Loaded_module_identity_detects_a_same_path_replacement()
    {
        var root = NewRoot();
        try
        {
            var path = Path.Join(root, "Elsa.Replaced.dll");
            var first = FixtureCompiler.Compile("Elsa.Replaced", "public class Original { }");
            File.WriteAllBytes(path, first);
            var loaded = Assembly.Load(first);
            Assert.True(ShippingHashAssemblies.MatchesImage(loaded, path));
            File.WriteAllBytes(path, FixtureCompiler.Compile("Elsa.Replaced", "public class Changed { public int NewHashedMember { get; init; } }"));
            Assert.False(ShippingHashAssemblies.MatchesImage(loaded, path));
        }
        finally { Remove(root); }
    }

    [Fact]
    public void Invalid_preflight_loads_no_other_valid_shipping_image()
    {
        var root = NewRoot();
        try
        {
            var directory = Path.Join(root, "Essentials", "src", "First");
            Directory.CreateDirectory(directory);
            var first = Path.Join(directory, "Elsa.PreflightValid.csproj");
            File.WriteAllText(first, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var binary = InternalsVisibleToGrants.BuiltAssemblyPath(first, Configuration)!;
            Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
            File.WriteAllBytes(binary, FixtureCompiler.Compile("Elsa.PreflightValid", "public class Valid { }"));
            File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddMinutes(-2));
            var other = Path.Join(root, "Essentials", "src", "Missing");
            Directory.CreateDirectory(other);
            File.WriteAllText(Path.Join(other, "Elsa.PreflightMissing.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            using var inventory = ShippingHashAssemblies.Load(root, Configuration, project => [project.Path]);
            Assert.NotEmpty(inventory.Errors);
            Assert.Empty(inventory.Assemblies);
            Assert.DoesNotContain(System.Runtime.Loader.AssemblyLoadContext.Default.Assemblies, assembly => assembly.GetName().Name == "Elsa.PreflightValid");
        }
        finally { Remove(root); }
    }

    [Fact]
    public void Evaluated_linked_sources_and_imported_settings_participate_in_freshness()
    {
        var root = NewRoot();
        try
        {
            var project = Path.Join(root, "Elsa.Linked.csproj");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var linked = Path.Join(root, "Linked.cs");
            var imported = Path.Join(root, "Shared.props");
            File.WriteAllText(linked, "public class Linked { }");
            File.WriteAllText(imported, "<Project />");
            using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new
            {
                Properties = new { MSBuildAllProjects = imported },
                Items = new { Compile = new[] { new { FullPath = linked, DefiningProjectFullPath = project } }, AdditionalFiles = Array.Empty<object>(), EditorConfigFiles = Array.Empty<object>(), Analyzer = Array.Empty<object>() }
            }));
            var inputs = EvaluatedHashInputs.Parse(json.RootElement, project, [imported]);
            Assert.Contains(linked, inputs);
            Assert.Contains(imported, inputs);
            var binary = Path.Join(root, "Elsa.Linked.dll");
            File.WriteAllBytes(binary, FixtureCompiler.Compile("Elsa.Linked", "public class Linked { }"));
            foreach (var path in inputs) File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-2));
            File.SetLastWriteTimeUtc(binary, DateTime.UtcNow.AddMinutes(-1));
            var entry = new ShippingHashAssemblies.Project(project, binary);
            foreach (var path in new[] { linked, imported })
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                Assert.Contains(ShippingHashAssemblies.ValidateInputs(root, [entry], Configuration, _ => inputs), error => error.StartsWith("Stale shipping assembly", StringComparison.Ordinal));
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-2));
            }
        }
        finally { Remove(root); }
    }

    private static string NewRoot()
    {
        var root = Path.Join(RepoPaths.RepoRoot, ".agent-prefs", "s9-hash-assembly-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Remove(string root)
    {
        var target = Path.GetFullPath(root);
        var workspace = Path.GetFullPath(Path.Join(RepoPaths.RepoRoot, ".agent-prefs")) + Path.DirectorySeparatorChar;
        Assert.StartsWith(workspace, target, StringComparison.Ordinal);
        Directory.Delete(target, recursive: true);
    }
}
