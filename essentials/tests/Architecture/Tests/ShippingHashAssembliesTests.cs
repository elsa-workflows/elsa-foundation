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
        Assert.Contains(projects, project => project.Path.Replace('\\', '/').Contains("apps/Elsa.Workbench/", StringComparison.Ordinal));
        Assert.Contains(projects, project => project.Path.Replace('\\', '/').Contains("apps/Elsa.Foundation.Host/", StringComparison.Ordinal));
        Assert.Contains(projects, project => project.Path.Replace('\\', '/').Contains("Studio/src/", StringComparison.Ordinal));
        Assert.Contains(projects, project => project.Path.Replace('\\', '/').Contains("extensions/Connections/studio/", StringComparison.Ordinal));
        Assert.DoesNotContain(projects, project => !ModuleRoots.IsNotTestFile(RepoPaths.RepoRoot, project.Path));
        var preferences = projects.Where(project => project.Path.Replace('\\', '/').Contains("essentials/src/Studio/Preferences/", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(preferences);
        Assert.All(preferences,
            project => Assert.False(ShippingHashAssemblies.IsStudioProject(RepoPaths.RepoRoot, project.Path)));
        Assert.All(projects.Where(project => project.Path.Replace('\\', '/').Contains("Studio/src/", StringComparison.Ordinal) ||
            project.Path.Replace('\\', '/').Contains("/studio/", StringComparison.Ordinal)),
            project => Assert.True(ShippingHashAssemblies.IsStudioProject(RepoPaths.RepoRoot, project.Path)));
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
        using (var stream = File.Create(Path.Join(evidence, "projects.json"))) System.Text.Json.JsonSerializer.Serialize(stream,
            inventory.Projects.Select(project => new { Path = Path.GetRelativePath(RepoPaths.RepoRoot, project.Path).Replace('\\', '/'), project.ImageSha256, project.ImageMvid }));
        using (var stream = File.Create(Path.Join(evidence, "hash-sites.json"))) System.Text.Json.JsonSerializer.Serialize(stream,
            graph.References.Where(reference => reference.Role != "ordinary"));
        WriteLines(Path.Join(evidence, "definitions.jsonl.gz"), graph.Definitions.Values);
        WriteLines(Path.Join(evidence, "references.jsonl.gz"), graph.References);
        var sourceErrors = new List<string>();
        WriteLines(Path.Join(evidence, "source-documents.jsonl.gz"), VerifiedDocuments());
        File.WriteAllLines(Path.Join(evidence, "source-errors.txt"), sourceErrors);
        Assert.True(sourceErrors.Count == 0, $"Shipping source provenance failed ({sourceErrors.Count} findings). Evidence: {evidence}\n" + string.Join('\n', sourceErrors.Take(50)));
        output.WriteLine($"Inspected {inventory.Projects.Count} shipping projects, {graph.Definitions.Count} definitions and {graph.References.Count} resolved references. Inventory: {evidence}");

        IEnumerable<object> VerifiedDocuments()
        {
            var maps = new Dictionary<string, string> { ["/_/"] = RepoPaths.RepoRoot };
            foreach (var project in inventory.Projects)
            {
                var sources = JsonHashSourceDocuments.Read(project.AssemblyPath, RepoPaths.RepoRoot, maps);
                if (sources.ImageMvid != project.ImageMvid) sourceErrors.Add($"Source/image MVID mismatch: {project.Path}");
                sourceErrors.AddRange(sources.Errors);
                foreach (var document in sources.Documents.Values)
                    yield return new { Project = Path.GetRelativePath(RepoPaths.RepoRoot, project.Path).Replace('\\', '/'), project.ImageMvid, document.Path, document.Sha256 };
            }
        }

        static void WriteLines<T>(string path, IEnumerable<T> items)
        {
            using var file = File.Create(path);
            using var compressed = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionLevel.Fastest);
            foreach (var item in items)
            {
                System.Text.Json.JsonSerializer.Serialize(compressed, item);
                compressed.WriteByte((byte)'\n');
            }
        }
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
            var directory = Path.Join(root, "essentials", "src", "First");
            Directory.CreateDirectory(directory);
            var first = Path.Join(directory, "Elsa.PreflightValid.csproj");
            File.WriteAllText(first, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var binary = InternalsVisibleToGrants.BuiltAssemblyPath(first, Configuration)!;
            Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
            File.WriteAllBytes(binary, FixtureCompiler.Compile("Elsa.PreflightValid", "public class Valid { }"));
            File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddMinutes(-2));
            var other = Path.Join(root, "essentials", "src", "Missing");
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
            Directory.CreateDirectory(Path.Join(root, "Project"));
            Directory.CreateDirectory(Path.Join(root, "Shared"));
            var project = Path.Join(root, "Project", "Elsa.Linked.csproj");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var linked = Path.Join(root, "Shared", "Linked.cs");
            var imported = Path.Join(root, "Shared.props");
            File.WriteAllText(linked, "public class Linked { }");
            File.WriteAllText(imported, "<Project />");
            using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new
            {
                Properties = new { MSBuildAllProjects = imported },
                Items = new { Compile = new[] { new { FullPath = linked, DefiningProjectFullPath = project } }, AdditionalFiles = Array.Empty<object>(), EditorConfigFiles = Array.Empty<object>(), Analyzer = Array.Empty<object>() }
            }));
            var inputs = EvaluatedHashInputs.Parse(json.RootElement, project, []);
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

    [Fact]
    public void Resolved_import_boundaries_exclude_unrelated_settings()
    {
        var root = NewRoot();
        try
        {
            var included = Path.Join(root, "Imported.props");
            var unrelated = Path.Join(root, "Unrelated.Tests.props");
            File.WriteAllText(included, "<Project />"); File.WriteAllText(unrelated, "<Project />");
            var xml = "<Project><!--\n================\n" + included + "\n================\n--></Project>";
            var inputs = EvaluatedHashInputs.ParseImports(xml);
            Assert.Contains(included, inputs);
            Assert.DoesNotContain(unrelated, inputs);
        }
        finally { Remove(root); }
    }

    [Fact]
    public void A_changed_referenced_constant_source_invalidates_a_previously_built_consumer()
    {
        var root = NewRoot();
        try
        {
            var producer = Path.Join(root, "Producer.cs");
            var project = Path.Join(root, "Consumer.csproj");
            var producerProject = Path.Join(root, "Producer.csproj");
            var binary = Path.Join(root, "Elsa.Consumer.dll");
            File.WriteAllText(Path.Join(root, "Directory.Build.props"), "<Project />");
            File.WriteAllText(Path.Join(root, "Directory.Packages.props"), "<Project />");
            File.WriteAllText(producer, "public static class Keys { public const string Key = \"old\"; }");
            File.WriteAllText(producerProject, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include=\"Producer.cs\" /></ItemGroup></Project>");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><ProjectReference Include=\"Producer.csproj\" /></ItemGroup></Project>");
            File.WriteAllBytes(binary, FixtureCompiler.Compile("Elsa.Consumer", "public static class Consumer { public const string Embedded = \"old\"; }"));
            File.SetLastWriteTimeUtc(project, DateTime.UtcNow.AddMinutes(-3));
            File.SetLastWriteTimeUtc(producer, DateTime.UtcNow.AddMinutes(-2));
            File.SetLastWriteTimeUtc(binary, DateTime.UtcNow.AddMinutes(-1));
            var closed = new EvaluatedHashInputs(Configuration).Read(project);
            Assert.Contains(producerProject, closed);
            Assert.Contains(producer, closed);
            File.SetLastWriteTimeUtc(binary, DateTime.UtcNow);
            var entry = new ShippingHashAssemblies.Project(project, binary);
            Assert.Empty(ShippingHashAssemblies.ValidateInputs(root, [entry], Configuration, _ => closed));
            File.WriteAllText(producer, "public static class Keys { public const string Key = \"changed\"; }");
            File.SetLastWriteTimeUtc(producer, DateTime.UtcNow.AddMinutes(1));
            Assert.Contains(ShippingHashAssemblies.ValidateInputs(root, [entry], Configuration, _ => closed), error => error.StartsWith("Stale shipping assembly", StringComparison.Ordinal));
        }
        finally { Remove(root); }
    }

    [Fact]
    public void Selected_SDK_configuration_participates_in_freshness()
    {
        var root = NewRoot();
        try
        {
            var project = Path.Join(root, "Elsa.SdkSelection.csproj");
            var binary = Path.Join(root, "Elsa.SdkSelection.dll");
            var selection = Path.Join(root, "global.json");
            File.WriteAllText(Path.Join(root, "Directory.Build.props"), "<Project />");
            File.WriteAllText(Path.Join(root, "Directory.Packages.props"), "<Project />");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(selection, File.ReadAllText(Path.Join(RepoPaths.RepoRoot, "global.json")));
            var inputs = new EvaluatedHashInputs(Configuration).Read(project);
            Assert.Contains(selection, inputs);
            File.WriteAllBytes(binary, FixtureCompiler.Compile("Elsa.SdkSelection", "public class Consumer { }"));
            var entry = new ShippingHashAssemblies.Project(project, binary);
            Assert.Empty(ShippingHashAssemblies.ValidateInputs(root, [entry], Configuration, _ => inputs));
            File.SetLastWriteTimeUtc(selection, DateTime.UtcNow.AddMinutes(1));
            Assert.Contains(ShippingHashAssemblies.ValidateInputs(root, [entry], Configuration, _ => inputs), error => error.StartsWith("Stale shipping assembly", StringComparison.Ordinal));
        }
        finally { Remove(root); }
    }

    [Fact]
    public void A_shared_external_contract_keeps_identity_before_either_host_has_loaded_it()
    {
        var root = NewRoot();
        var compiler = new System.Runtime.Loader.AssemblyLoadContext("S9 fixture compiler " + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var suffix = Guid.NewGuid().ToString("N");
            var commonName = "S9.Common." + suffix;
            var backendName = "S9.Backend." + suffix;
            var studioName = "S9.Studio." + suffix;
            var commonPath = Path.Join(root, commonName + ".dll");
            File.WriteAllBytes(commonPath, FixtureCompiler.Compile(commonName, "namespace FixtureCommon; public class Argument { }"));
            var common = compiler.LoadFromAssemblyPath(commonPath);
            var backend = WriteProject("essentials/src/Fixture", backendName,
                FixtureCompiler.Compile(backendName, "public static class Backend { public static void Call(FixtureCommon.Argument value) { } }", [common]));
            var backendAssembly = compiler.LoadFromAssemblyPath(backend.AssemblyPath);
            var studio = WriteProject("Studio/src/Fixture", studioName,
                FixtureCompiler.Compile(studioName, "public class Derived : FixtureCommon.Argument { public void Run() => Backend.Call(this); }", [common, backendAssembly]));
            WriteDependencies(backend, [(commonName, commonPath)]);
            WriteDependencies(studio, [(commonName, commonPath), (backendName, backend.AssemblyPath)]);
            Assert.DoesNotContain(System.Runtime.Loader.AssemblyLoadContext.Default.Assemblies, assembly => assembly.GetName().Name == commonName);
            using var inventory = ShippingHashAssemblies.Load(root, Configuration, project => [project.Path]);
            Assert.True(inventory.Errors.Count == 0, string.Join('\n', inventory.Errors));
            var graph = JsonHashReferenceGraph.Scan(inventory.Assemblies);
            Assert.True(graph.Errors.Count == 0, string.Join('\n', graph.Errors));
            var backendContract = inventory.Assemblies.Single(assembly => assembly.GetName().Name == backendName).GetType("Backend")!.GetMethod("Call")!.GetParameters()[0].ParameterType;
            var studioContract = inventory.Assemblies.Single(assembly => assembly.GetName().Name == studioName).GetType("Derived")!.BaseType;
            Assert.Same(backendContract, studioContract);

            ShippingHashAssemblies.Project WriteProject(string directory, string name, byte[] image)
            {
                var project = Path.Join(root, directory, name + ".csproj");
                Directory.CreateDirectory(Path.GetDirectoryName(project)!);
                File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
                var binary = InternalsVisibleToGrants.BuiltAssemblyPath(project, Configuration)!;
                Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
                File.WriteAllBytes(binary, image);
                return new(project, binary);
            }

            static void WriteDependencies(ShippingHashAssemblies.Project project, (string Name, string Path)[] dependencies)
            {
                var target = ".NETCoreApp,Version=v10.0";
                var name = Path.GetFileNameWithoutExtension(project.AssemblyPath);
                var libraries = dependencies.Append((Name: name, Path: project.AssemblyPath)).ToArray();
                foreach (var dependency in dependencies)
                    File.Copy(dependency.Path, Path.Join(Path.GetDirectoryName(project.AssemblyPath)!, dependency.Name + ".dll"));
                var manifest = new
                {
                    runtimeTarget = new { name = target },
                    targets = new Dictionary<string, object> { [target] = libraries.ToDictionary(item => item.Name + "/1.0.0", item => (object)new
                    {
                        runtime = new Dictionary<string, object> { [item.Name + ".dll"] = new { } }
                    }) },
                    libraries = libraries.ToDictionary(item => item.Name + "/1.0.0", _ => new { type = "project", serviceable = false, sha512 = "" })
                };
                File.WriteAllText(Path.ChangeExtension(project.AssemblyPath, ".deps.json"), System.Text.Json.JsonSerializer.Serialize(manifest));
            }
        }
        finally { compiler.Unload(); Remove(root); }
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
