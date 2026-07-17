using System.Xml.Linq;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class ReusableActivityArchitectureTests
{
    [Fact]
    public void Elsa4_legacy_workflow_as_activity_projects_and_model_are_absent()
    {
        Assert.False(File.Exists(FullPath("src/Elsa/Activities/Composition/Design/Elsa.Activities.Composition.Design.csproj")));
        Assert.False(File.Exists(FullPath("src/Elsa/Activities/Composition/Runtime/Elsa.Activities.Composition.Runtime.csproj")));
        Assert.False(File.Exists(FullPath("src/Elsa/Workflows/Design/Core/Models/WorkflowActivityOptions.cs")));
        Assert.False(File.Exists(FullPath("src/Elsa/Workflows/Primitives/Models/WorkflowIdentity.cs")));
        Assert.False(File.Exists(FullPath("tests/Elsa/Activities/Composition/Tests/Elsa.Activities.Composition.Tests.csproj")));
    }

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
            "Elsa.Persistence.Core",
            "Elsa.Persistence.Groundwork",
            "Elsa.Persistence.Groundwork.Composition",
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
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}Elsa3{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
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
    public void Elsa3_reusable_marker_detection_is_confined_to_the_one_way_import_boundary()
    {
        var hits = Directory.EnumerateFiles(FullPath("src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                           !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains("UsableAsActivity", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(RepoRoot, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(hits);
        Assert.All(hits, file => Assert.StartsWith("src/Elsa3/", file, StringComparison.Ordinal));
        Assert.Contains(hits, file => file.EndsWith("Models/Elsa3WorkflowDefinition.cs", StringComparison.Ordinal));
        Assert.Contains(hits, file => file.EndsWith("Services/ReusableActivityCollectionAnalyzer.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Runtime_activity_activation_uses_stable_wire_identity_only()
    {
        var descriptor = File.ReadAllText(FullPath(
            "src/Elsa/Activities/Runtime/Core/Models/RuntimeActivityDescriptor.cs"));
        var activationStrategy = File.ReadAllText(FullPath(
            "src/Elsa/Activities/Runtime/Core/Contracts/IActivityActivationStrategy.cs"));
        var activator = File.ReadAllText(FullPath(
            "src/Elsa/Activities/Runtime/Contracts/IActivityActivator.cs"));
        var executableNode = File.ReadAllText(FullPath(
            "src/Elsa/Workflows/Runtime/Core/Models/ExecutableNode.cs"));

        Assert.Contains("ConsumerKey", descriptor, StringComparison.Ordinal);
        Assert.Contains("SchemaVersion", descriptor, StringComparison.Ordinal);
        Assert.Contains("IActivityActivationStrategy", activationStrategy, StringComparison.Ordinal);
        Assert.Contains("RuntimeActivityDescriptor", activationStrategy, StringComparison.Ordinal);
        Assert.Contains("DescriptorSchemaVersion", executableNode, StringComparison.Ordinal);
        Assert.Contains("DescriptorPayload", executableNode, StringComparison.Ordinal);
        Assert.DoesNotContain("AssemblyQualifiedName", activationStrategy, StringComparison.Ordinal);
        Assert.DoesNotContain("AssemblyQualifiedName", activator, StringComparison.Ordinal);
        Assert.DoesNotContain("AssemblyQualifiedName", executableNode, StringComparison.Ordinal);
    }

    [Fact]
    public void Universal_activity_version_contracts_do_not_expose_clr_descriptor_type_identity()
    {
        string[] relativePaths =
        [
            "src/Elsa/Activities/Design/Core/Contracts/IActivityDefinitionVersion.cs",
            "src/Elsa/Activities/Design/Core/Models/ActivityDefinitionVersionModel.cs",
            "src/Elsa/Activities/Design/Reconciliation/Core/Models/ActivityVersionReconciliationModel.cs",
            "src/Elsa/Activities/Design/Api/Commands/AddDefinition.cs",
            "src/Elsa/Activities/Design/Api/Commands/AddVersion.cs",
            "src/Elsa/Activities/Design/Api/Models/ActivityDefinitionVersionDetailsView.cs"
        ];

        foreach (var relativePath in relativePaths)
        {
            var text = File.ReadAllText(FullPath(relativePath));
            Assert.Contains("ProviderKey", text, StringComparison.Ordinal);
            Assert.Contains("ConsumerKey", text, StringComparison.Ordinal);
            Assert.DoesNotContain("DescriptorType", text, StringComparison.Ordinal);
        }
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
