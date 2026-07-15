using System.Xml.Linq;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class ReusableActivityArchitectureTests
{
    [Fact]
    public void Graph_runtime_references_only_runtime_core_contracts() =>
        AssertProjectReferences(
            "src/Elsa/Activities/Graph/Runtime/Elsa.Activities.Graph.Runtime.csproj",
            "Elsa.Activities.Runtime.Core",
            "Elsa.Workflows.Runtime.Core");

    [Fact]
    public void Graph_design_may_project_workflow_design_structure_but_does_not_reference_runtime_implementations() =>
        AssertProjectReferences(
            "src/Elsa/Activities/Graph/Design/Elsa.Activities.Graph.Design.csproj",
            "Elsa.Activities.Design.Core",
            "Elsa.Activities.Runtime.Core",
            // The provider uses the provider-neutral Design structure projector to preserve
            // authored parent/slot/order. This remains Design -> Design; Graph Runtime stays clean.
            "Elsa.Workflows.Design.Core",
            "Elsa.Workflows.Publishing.Core",
            "Elsa.Workflows.Runtime.Core");

    [Fact]
    public void Publishing_groundwork_bridge_is_the_only_activity_publication_cross_domain_adapter() =>
        AssertProjectReferences(
            "src/Elsa/Workflows/Publishing/Persistence/Groundwork/Elsa.Workflows.Publishing.Persistence.Groundwork.csproj",
            "Elsa.Activities.Design.Core",
            "Elsa.Activities.Design.Persistence.Core",
            "Elsa.Activities.Design.Persistence.Groundwork",
            "Elsa.Persistence.Groundwork",
            "Elsa.Persistence.Groundwork.Querying",
            "Elsa.Serialization.Core",
            "Elsa.Workflows.Design.Core",
            "Elsa.Workflows.Design.Persistence.Core",
            "Elsa.Workflows.Design.Persistence.Groundwork",
            "Elsa.Workflows.Publishing.Core",
            "Elsa.Workflows.Runtime.Core");

    [Fact]
    public void Elsa3_import_groundwork_bridge_references_design_contracts_and_not_runtime() =>
        AssertProjectReferences(
            "src/Elsa3/Activities/Design/Import/Persistence/Groundwork/Elsa3.Activities.Design.Import.Persistence.Groundwork.csproj",
            "Elsa.Activities.Design.Persistence.Core",
            "Elsa.Persistence.Groundwork.Querying",
            "Elsa.Serialization.Core",
            "Elsa.Workflows.Design.Persistence.Core",
            "Elsa3.Activities.Design.Import");

    [Fact]
    public void New_reusable_activity_surface_does_not_reference_legacy_workflow_as_activity_types()
    {
        string[] roots =
        [
            "src/Elsa/Activities/Graph",
            "src/Elsa/Workflows/Publishing/Persistence/Groundwork",
            "src/Elsa3/Activities/Design/Import/Persistence/Groundwork"
        ];
        string[] forbiddenTokens =
        [
            "WorkflowDefinitionActivity",
            "WorkflowActivityOptions",
            "UsableAsActivity",
            "WorkflowIdentity"
        ];
        var reusableFiles = roots
            .Select(FullPath)
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Where(file => Path.GetExtension(file) is ".cs" or ".csproj")
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                           !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Concat(Directory.EnumerateFiles(FullPath("src"), "ReusableActivity*.cs", SearchOption.AllDirectories))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var violations = reusableFiles
            .SelectMany(file => forbiddenTokens
                .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetRelativePath(RepoRoot, file)}: {token}"))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "New reusable-activity code must not depend on the legacy workflow-as-activity surface:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Runtime_activity_construction_uses_stable_wire_identity_only()
    {
        var descriptor = File.ReadAllText(FullPath(
            "src/Elsa/Activities/Runtime/Core/Models/RuntimeActivityDescriptor.cs"));
        var constructor = File.ReadAllText(FullPath(
            "src/Elsa/Activities/Runtime/Core/Contracts/IActivityConstructor.cs"));
        var executableNode = File.ReadAllText(FullPath(
            "src/Elsa/Workflows/Runtime/Core/Models/ExecutableNode.cs"));

        Assert.Contains("ConsumerKey", descriptor, StringComparison.Ordinal);
        Assert.Contains("SchemaVersion", descriptor, StringComparison.Ordinal);
        Assert.DoesNotContain("DescriptorType", constructor, StringComparison.Ordinal);
        Assert.DoesNotContain("DescriptorType", executableNode, StringComparison.Ordinal);
        Assert.DoesNotContain("DescriptorPayload", executableNode, StringComparison.Ordinal);
    }

    private static void AssertProjectReferences(string relativeProjectPath, params string[] expectedReferences)
    {
        var projectPath = FullPath(relativeProjectPath);
        Assert.True(File.Exists(projectPath), $"Expected project '{relativeProjectPath}' to exist.");

        var actual = XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .OfType<string>()
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Replace('\\', '/')))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expected = expectedReferences.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, actual);
    }

    private static string FullPath(string relativePath) =>
        Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                    return directory.FullName;

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find repository root.");
        }
    }
}
