using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Implementations are internal and are tested by their own domain's test assemblies, which reach them through
/// <c>InternalsVisibleTo</c>. A project never opens its internals to another production project, and never to the tests
/// of another domain: either would let code outside the domain depend on what the domain is free to change.
/// </summary>
public sealed partial class ArchitectureGuardTests
{
    /// <summary>
    /// The one grant outside the rule. Hosts belong to no domain, and the Workbench keeps a narrow exception for its
    /// host-only module-management registry builder (<c>ModuleManagementRegistryBuilder</c>), exercised by
    /// <c>ModuleManagementRegistryBuilderTests</c>. A new entry needs the architects' agreement.
    /// </summary>
    private static readonly HashSet<(string Project, string Target)> InternalsVisibleToExceptions =
    [
        ("Elsa.Workbench", "Elsa.Modularity.Tests")
    ];

    /// <summary>The configuration this test assembly was built in, which is the one whose production assemblies it reads.</summary>
    private static string BuildConfiguration { get; } =
        Path.GetFileName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))!;

    [Fact]
    public void InternalsVisibleTo_grants_go_only_to_the_granting_domains_test_assemblies()
    {
        var violations = InternalsVisibleToGrants.Violations(RepoRoot, BuildConfiguration, InternalsVisibleToExceptions);

        Assert.True(violations.Count == 0,
            "InternalsVisibleTo may name only the granting project's own domain's test assemblies:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// The guard reads every production project's compiled assembly, so building this suite has to build every production
    /// project; otherwise a clean checkout would fail the guard for a project nothing built, or a stale build would pass
    /// it. A production project outside this suite's reference graph, directly or transitively, fails here by name.
    /// </summary>
    [Fact]
    public void Every_production_project_is_built_before_the_architecture_tests()
    {
        var built = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([Path.GetFullPath(Path.Join(RepoRoot, "Essentials", "tests", "Architecture", "Tests", "Elsa.Architecture.Tests.csproj"))]);
        while (pending.TryPop(out var project))
        {
            if (built.Add(project))
                foreach (var reference in ProjectGraph.ReferencedProjectPaths(project))
                    pending.Push(reference);
        }

        var production = ModuleRoots.Resolve(RepoRoot, ModuleRoots.Production)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path))
            .Select(Path.GetFullPath)
            .ToArray();
        var unbuilt = production.Where(project => !built.Contains(project)).Select(project => Path.GetRelativePath(RepoRoot, project)).Order(StringComparer.Ordinal).ToArray();

        Assert.True(production.Length > 100, $"Only {production.Length} production projects were found; the sweep has stopped finding them.");
        Assert.True(unbuilt.Length == 0,
            "Add each as a build-only reference (ReferenceOutputAssembly=\"false\") to Elsa.Architecture.Tests.csproj:" + Environment.NewLine +
            string.Join(Environment.NewLine, unbuilt));
    }

    /// <summary>Pins that the guard reads the real compiled grants, so its going green means "checked".</summary>
    [Fact]
    public void The_compiled_grant_sweep_finds_the_repositorys_existing_grants()
    {
        var grants = InternalsVisibleToGrants.FindCompiled(RepoRoot, BuildConfiguration).Select(grant => (grant.Project, grant.Target)).ToHashSet();

        Assert.Contains(("Elsa.Cli.Worker", "Elsa.Cli.Tests"), grants);
        Assert.Contains(("Elsa.Persistence.EntityFramework", "Elsa.Persistence.EntityFramework.Tests"), grants);
        Assert.Contains(("Elsa.ExtensionBuilder.Api", "Elsa.ExtensionBuilder.Api.Tests"), grants);
        Assert.Contains(("Elsa.Workbench", "Elsa.Modularity.Tests"), grants);
    }

    /// <summary>
    /// The compiled reader sees a grant however its source spelled it: through a using alias, with a constant, with a
    /// concatenation, with <c>nameof</c>, or with a public key after the name.
    /// </summary>
    [Fact]
    public void A_compiled_grant_is_read_whatever_form_its_source_took()
    {
        using var sandbox = new GrantSandbox();
        var assembly = sandbox.Assembly(
            "Essentials/src/Tasks/Elsa.Tasks/bin/Release/net10.0/Elsa.Tasks.dll",
            """
            using Grant = System.Runtime.CompilerServices.InternalsVisibleToAttribute;

            [assembly: Grant(Names.Events + ".Tests")]
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo(nameof(Elsa) + ".Tasks.Tests")]
            [assembly: System.Runtime.CompilerServices.InternalsVisibleToAttribute("Elsa.Signed.Tests, PublicKey=0024000004800000940000000602000000240000525341310004000001000100c547cac37abd99c8db225ef2f6c8a3602f3b3606cc9891605d02baa56104f4cfc0734aa39b93bf7852f7d9266654753cc297e7d2edfe0bac1cdcf9f717241550e0a7b191195b7667bb4f64bcb8e2121380fd1d9d46ad2d92d2d15605093924cceaf74c4861eff62abf69b9291ed0a340e113be11e6a7d3113e92484cf7045cc7")]

            internal static class Names { public const string Events = "Elsa.Events"; }
            namespace Elsa { internal static class Marker { } }
            """);

        Assert.Equal(
            ["Elsa.Events.Tests", "Elsa.Signed.Tests", "Elsa.Tasks.Tests"],
            InternalsVisibleToGrants.CompiledTargets(assembly).Order(StringComparer.Ordinal));
    }

    /// <summary>The detector itself, against a sandbox repository with built assemblies holding one grant of every kind.</summary>
    [Fact]
    public void Each_kind_of_grant_is_judged_by_domain()
    {
        using var sandbox = new GrantSandbox();
        sandbox.Project("Essentials/tests/Tasks/Tests/Elsa.Tasks.Tests.csproj");
        sandbox.Project("Essentials/tests/Events/Tests/Elsa.Events.Tests.csproj");
        sandbox.Project("Essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj");
        sandbox.Project("Essentials/tests/Modularity/Tests/Elsa.Modularity.Tests.csproj");

        // Declared in the project file for one target; the other grant has no declaration the scan can see.
        sandbox.Project(
            "Essentials/src/Tasks/Elsa.Tasks/Elsa.Tasks.csproj",
            """<ItemGroup><InternalsVisibleTo Include="Elsa.Tasks.Tests" /></ItemGroup>""");
        sandbox.Assembly(
            "Essentials/src/Tasks/Elsa.Tasks/bin/Release/net10.0/Elsa.Tasks.dll",
            """
            using Grant = System.Runtime.CompilerServices.InternalsVisibleToAttribute;
            [assembly: Grant("Elsa.Tasks.Tests")]
            [assembly: Grant(Names.Events + ".Tests")]
            internal static class Names { public const string Events = "Elsa.Events"; }
            """);

        // A computed argument in source, compiled into a grant to a production project.
        const string computed =
            """
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo(Names.Target)]
            internal static class Names { public const string Target = "Elsa." + nameof(Tasks); }
            namespace Tasks { internal static class Marker { } }
            """;
        sandbox.Project("Essentials/src/Events/Elsa.Events/Elsa.Events.csproj");
        sandbox.File("Essentials/src/Events/Elsa.Events/AssemblyInfo.cs", computed);
        sandbox.Assembly("Essentials/src/Events/Elsa.Events/bin/Release/net10.0/Elsa.Events.dll", computed);

        // A production project that was never built.
        sandbox.Project("Essentials/src/Locking/Core/Elsa.Locking.Core.csproj");

        sandbox.Project("Essentials/src/Workflows/Design/Core/Elsa.Workflows.Design.Core.csproj");
        sandbox.Assembly(
            "Essentials/src/Workflows/Design/Core/bin/Release/net10.0/Elsa.Workflows.Design.Core.dll",
            """[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Elsa.Workflows.Runtime.Tests")]""");

        sandbox.Project(
            "Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj",
            """<ItemGroup><InternalsVisibleTo Include="Elsa.Modularity.Tests" /><InternalsVisibleTo Include="Elsa.Tasks.Tests" /></ItemGroup>""");
        sandbox.Assembly(
            "Essentials/apps/Elsa.Workbench/bin/Release/net10.0/Elsa.Workbench.dll",
            """
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Elsa.Modularity.Tests")]
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Elsa.Tasks.Tests")]
            """);

        // A test project's grants are read from source: decoys in a comment and a string are no grant, and a
        // computed argument fails closed.
        sandbox.File("Essentials/tests/Tasks/Tests/Commented.cs", """// [assembly: InternalsVisibleTo("Elsa.Tasks")]""");
        sandbox.File("Essentials/tests/Tasks/Tests/Quoted.cs", """class Quoted { const string Text = "[assembly: InternalsVisibleTo(\"Elsa.Tasks\")]"; }""");
        sandbox.File(
            "Essentials/tests/Tasks/Tests/Grants.cs",
            """
            using Opens = System.Runtime.CompilerServices.InternalsVisibleToAttribute;
            [assembly: Opens(Names.Target)]
            static class Names { public const string Target = "Elsa.Other"; }
            """);

        sandbox.File("Directory.Build.props", """<Project><ItemGroup><InternalsVisibleTo Include="Elsa.Tasks.Tests" /></ItemGroup></Project>""");

        // A source changed after the last build, so the compiled assembly no longer says what the source declares.
        sandbox.Project("Essentials/src/Caching/Core/Elsa.Caching.Core.csproj");
        var stale = sandbox.Assembly("Essentials/src/Caching/Core/bin/Release/net10.0/Elsa.Caching.Core.dll", "internal static class Cache { }");
        sandbox.File("Essentials/src/Caching/Core/Cache.cs", "internal static class Cache { }");
        File.SetLastWriteTimeUtc(Path.Join(sandbox.Root, "Essentials/src/Caching/Core/Cache.cs"), File.GetLastWriteTimeUtc(stale).AddHours(1));

        var violations = InternalsVisibleToGrants.Violations(sandbox.Root, "Release", InternalsVisibleToExceptions);

        Assert.Equal(
        [
            "Directory.Build.props declares InternalsVisibleTo for every project that imports it; declare it in the granting project instead",
            "Elsa.Caching.Core: the compiled assembly at Essentials/src/Caching/Core/bin/Release/net10.0/Elsa.Caching.Core.dll is older than Essentials/src/Caching/Core/Cache.cs; build it again, because a stale assembly can hide a grant or report one that is gone",
            "Elsa.Events -> Elsa.Tasks (Essentials/src/Events/Elsa.Events/AssemblyInfo.cs, computed argument): Essentials/src/Tasks/Elsa.Tasks/Elsa.Tasks.csproj is not a test project; a project never opens its internals to another production project",
            "Elsa.Locking.Core: no compiled assembly at Essentials/src/Locking/Core/bin/Release/net10.0/Elsa.Locking.Core.dll; build it first (the Architecture tests build every production project), because its grants are read from it",
            "Elsa.Tasks -> Elsa.Events.Tests (compiled into Essentials/src/Tasks/Elsa.Tasks/bin/Release/net10.0/Elsa.Tasks.dll; not declared in the project file or its own sources, so a linked file, an imported build file or a computed argument): Essentials/tests/Events/Tests/Elsa.Events.Tests.csproj tests domain 'Events', not the granting project's domain 'Tasks'",
            "Elsa.Tasks.Tests (Essentials/tests/Tasks/Tests/Grants.cs): an InternalsVisibleTo argument that is not a string literal cannot be judged; write the assembly name as a literal",
            "Elsa.Workbench -> Elsa.Tasks.Tests (Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj): Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj belongs to no domain, so it has no own test assemblies to grant to",
            "Elsa.Workflows.Design.Core -> Elsa.Workflows.Runtime.Tests (compiled into Essentials/src/Workflows/Design/Core/bin/Release/net10.0/Elsa.Workflows.Design.Core.dll; not declared in the project file or its own sources, so a linked file, an imported build file or a computed argument): Essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj tests domain 'Workflows.Runtime', not the granting project's domain 'Workflows.Design'",
        ],
            violations);
    }

    [Theory]
    [InlineData("Essentials/src/Tasks/Elsa.Tasks/Elsa.Tasks.csproj", "Tasks")]
    [InlineData("Essentials/tests/Tasks/Tests/Elsa.Tasks.Tests.csproj", "Tasks")]
    [InlineData("Essentials/src/Workflows/Runtime/Http/Elsa.Workflows.Runtime.Http.csproj", "Workflows.Runtime")]
    [InlineData("Essentials/tests/Workflows/Runtime/Http/Tests/Elsa.Workflows.Runtime.Http.Tests.csproj", "Workflows.Runtime")]
    [InlineData("Extensions/ExtensionBuilder/src/Api/Elsa.ExtensionBuilder.Api.csproj", "Extensions/ExtensionBuilder")]
    [InlineData("Extensions/ExtensionBuilder/tests/Api/Tests/Elsa.ExtensionBuilder.Api.Tests.csproj", "Extensions/ExtensionBuilder")]
    [InlineData("Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj", null)]
    [InlineData("Essentials/build/Analyzers/Elsa.Architecture.Analyzers.csproj", null)]
    [InlineData("tools/maps/Elsa.Maps.Generator/Elsa.Maps.Generator.csproj", null)]
    public void A_path_maps_to_its_domain(string path, string? domain) =>
        Assert.Equal(domain, ElsaDomains.Of(path));

    /// <summary>A throwaway repository tree for the detector proofs, cleaned up on dispose.</summary>
    private sealed class GrantSandbox : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("elsa-ivt-guard-").FullName;

        public void Project(string relativePath, string items = "") =>
            File(relativePath, $"""<Project Sdk="Microsoft.NET.Sdk">{items}</Project>""");

        public void File(string relativePath, string content)
        {
            var path = Path.Join(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, content);
        }

        /// <summary>Compiles <paramref name="source"/> into the built assembly at <paramref name="relativePath"/>.</summary>
        public string Assembly(string relativePath, string source)
        {
            var path = Path.Join(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllBytes(path, FixtureCompiler.Compile(Path.GetFileNameWithoutExtension(path), source));
            return path;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
