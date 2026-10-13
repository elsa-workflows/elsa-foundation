using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Framework §2.23.3: implementations are internal and are tested through <c>InternalsVisibleTo</c> to their own
/// domain's test assemblies. A project never opens its internals to another production project, and never to the tests
/// of another domain.
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

    [Fact]
    public void InternalsVisibleTo_grants_go_only_to_the_granting_domains_test_assemblies()
    {
        var violations = InternalsVisibleToGrants.Violations(RepoRoot, InternalsVisibleToExceptions);

        Assert.True(violations.Count == 0,
            "InternalsVisibleTo may name only the granting project's own domain's test assemblies:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    /// <summary>Pins that the sweep reads the real grants, so the guard above going green means "checked".</summary>
    [Fact]
    public void The_grant_sweep_finds_the_repositorys_existing_grants()
    {
        var grants = InternalsVisibleToGrants.Find(RepoRoot).Select(grant => (grant.Project, grant.Target)).ToHashSet();

        Assert.Contains(("Elsa.Cli.Worker", "Elsa.Cli.Tests"), grants);
        Assert.Contains(("Elsa.Persistence.EntityFramework", "Elsa.Persistence.EntityFramework.Tests"), grants);
        Assert.Contains(("Elsa.ExtensionBuilder.Api", "Elsa.ExtensionBuilder.Api.Tests"), grants);
        Assert.Contains(("Elsa.Workbench", "Elsa.Modularity.Tests"), grants);
    }

    /// <summary>The detector itself, against a sandbox repository holding one grant of every kind it must judge.</summary>
    [Fact]
    public void Each_kind_of_grant_is_judged_by_domain()
    {
        using var sandbox = new GrantSandbox();
        sandbox.Project("Essentials/tests/Tasks/Tests/Elsa.Tasks.Tests.csproj");
        sandbox.Project("Essentials/tests/Events/Tests/Elsa.Events.Tests.csproj");
        sandbox.Project("Essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj");
        sandbox.Project("Essentials/tests/Modularity/Tests/Elsa.Modularity.Tests.csproj");
        sandbox.Project(
            "Essentials/src/Tasks/Elsa.Tasks/Elsa.Tasks.csproj",
            """<ItemGroup><InternalsVisibleTo Include="Elsa.Tasks.Tests" /><InternalsVisibleTo Include="Elsa.Events.Tests" /></ItemGroup>""");
        sandbox.Project("Essentials/src/Events/Elsa.Events/Elsa.Events.csproj");
        sandbox.File("Essentials/src/Events/Elsa.Events/AssemblyInfo.cs", """[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Elsa.Tasks")]""");
        sandbox.File("Essentials/src/Events/Elsa.Events/Commented.cs", """// [assembly: InternalsVisibleTo("Elsa.Tasks")]""");
        sandbox.File("Essentials/src/Events/Elsa.Events/Quoted.cs", """class Quoted { const string Text = "[assembly: InternalsVisibleTo(\"Elsa.Tasks\")]"; }""");
        sandbox.Project(
            "Essentials/src/Locking/Core/Elsa.Locking.Core.csproj",
            """<ItemGroup><AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo"><_Parameter1>Elsa.Unknown.Tests</_Parameter1></AssemblyAttribute></ItemGroup>""");
        sandbox.Project(
            "Essentials/src/Workflows/Design/Core/Elsa.Workflows.Design.Core.csproj",
            """<ItemGroup><InternalsVisibleTo Include="Elsa.Workflows.Runtime.Tests" /></ItemGroup>""");
        sandbox.Project(
            "Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj",
            """<ItemGroup><InternalsVisibleTo Include="Elsa.Modularity.Tests" /><InternalsVisibleTo Include="Elsa.Tasks.Tests" /></ItemGroup>""");
        sandbox.File("Directory.Build.props", """<Project><ItemGroup><InternalsVisibleTo Include="Elsa.Tasks.Tests" /></ItemGroup></Project>""");

        var violations = InternalsVisibleToGrants.Violations(sandbox.Root, InternalsVisibleToExceptions);

        Assert.Equal(
        [
            "Directory.Build.props declares InternalsVisibleTo for every project that imports it; declare it in the granting project instead",
            "Elsa.Events -> Elsa.Tasks (Essentials/src/Events/Elsa.Events/AssemblyInfo.cs): Essentials/src/Tasks/Elsa.Tasks/Elsa.Tasks.csproj is not a test project; a project never opens its internals to another production project",
            "Elsa.Locking.Core -> Elsa.Unknown.Tests (Essentials/src/Locking/Core/Elsa.Locking.Core.csproj): names no project of this repository",
            "Elsa.Tasks -> Elsa.Events.Tests (Essentials/src/Tasks/Elsa.Tasks/Elsa.Tasks.csproj): Essentials/tests/Events/Tests/Elsa.Events.Tests.csproj tests domain 'Events', not the granting project's domain 'Tasks'",
            "Elsa.Workbench -> Elsa.Tasks.Tests (Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj): Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj belongs to no domain, so it has no own test assemblies to grant to",
            "Elsa.Workflows.Design.Core -> Elsa.Workflows.Runtime.Tests (Essentials/src/Workflows/Design/Core/Elsa.Workflows.Design.Core.csproj): Essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj tests domain 'Workflows.Runtime', not the granting project's domain 'Workflows.Design'",
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
    [InlineData("tools/maps/Elsa.Maps.Generator/Elsa.Maps.Generator.csproj", null)]
    public void A_path_maps_to_its_constitution_domain(string path, string? domain) =>
        Assert.Equal(domain, ElsaDomains.Of(path));

    /// <summary>A throwaway repository tree for the detector proof, cleaned up on dispose.</summary>
    private sealed class GrantSandbox : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("elsa-ivt-guard-").FullName;

        public void Project(string relativePath, string items = "") =>
            File($"{relativePath}", $"""<Project Sdk="Microsoft.NET.Sdk">{items}</Project>""");

        public void File(string relativePath, string content)
        {
            var path = Path.Join(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, content);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
