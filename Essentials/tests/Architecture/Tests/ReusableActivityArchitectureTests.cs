using System.Xml.Linq;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

public sealed class ReusableActivityArchitectureTests
{
    [Fact]
    public void Elsa4_legacy_workflow_as_activity_projects_and_model_are_absent()
    {
        Assert.False(File.Exists(FullPath("Essentials/src/Activities/Composition/Design/Elsa.Activities.Composition.Design.csproj")));
        Assert.False(File.Exists(FullPath("Essentials/src/Activities/Composition/Runtime/Elsa.Activities.Composition.Runtime.csproj")));
        Assert.False(File.Exists(FullPath("Essentials/src/Workflows/Design/Core/Models/WorkflowActivityOptions.cs")));
        Assert.False(File.Exists(FullPath("Essentials/src/Workflows/Primitives/Models/WorkflowIdentity.cs")));
        Assert.False(File.Exists(FullPath("Essentials/tests/Activities/Composition/Tests/Elsa.Activities.Composition.Tests.csproj")));
    }

    [Fact]
    public void Graph_runtime_references_only_stable_runtime_and_expression_contracts() =>
        AssertProjectReferences(
            "Essentials/src/Activities/Graph/Runtime/Elsa.Activities.Graph.Runtime.csproj",
            "Elsa.Activities.Runtime.Core",
            // Boundary expressions use portable evaluator/type contracts, never a concrete language engine.
            "Elsa.Expressions.Core",
            "Elsa.Serialization.Core",
            "Elsa.Workflows.Runtime.Core");

    [Fact]
    public void Graph_design_may_project_workflow_design_structure_but_does_not_reference_runtime_implementations() =>
        AssertProjectReferences(
            "Essentials/src/Activities/Graph/Design/Elsa.Activities.Graph.Design.csproj",
            "Elsa.Activities.Design.Core",
            "Elsa.Activities.Runtime.Core",
            // The provider uses the provider-neutral Design structure projector to preserve
            // authored parent/slot/order. This remains Design -> Design; Graph Runtime stays clean.
            "Elsa.Workflows.Design.Core",
            // CompleteOutsideGraphDraftValidator (spec 237) contributes an IDraftValidator; Design -> Design .Core.
            "Elsa.Workflows.Design.Validations.Core",
            "Elsa.Workflows.Publishing.Core",
            "Elsa.Workflows.Runtime.Core");

    /// <summary>
    /// The EF counterpart commits the publication in order through the Activities Design and Runtime
    /// EF modules' own staging seams, each in its own context. It also owns the activity-upgrade
    /// bridge, whose apply commits both Design catalogs as one act, so it reaches the Workflows Design EF lane
    /// as well — exactly three EF modules, and never another persistence family.
    /// </summary>
    [Fact]
    public void Publishing_entity_framework_bridge_commits_through_the_design_and_runtime_EF_seams_only() =>
        AssertProjectReferences(
            "Essentials/src/Workflows/Publishing/Persistence/EntityFrameworkCore/Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.csproj",
            "Elsa.Activities.Design.Persistence.Core",
            "Elsa.Activities.Design.Persistence.EntityFrameworkCore",
            "Elsa.Persistence.EntityFramework",
            "Elsa.Workflows.Design.Persistence.EntityFrameworkCore",
            "Elsa.Workflows.Publishing.Core",
            "Elsa.Workflows.Runtime.Core",
            "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore");

    // The EF bridge commits one import across both Design lanes and its own ledger, and reaches Runtime
    // only for the persistence access-context contract, so it cannot write Runtime templates or source
    // references.
    [Fact]
    public void Elsa3_import_ef_bridge_references_design_ef_lanes_and_runtime_core_contracts_only() =>
        AssertProjectReferences(
            "Extensions/Elsa3/src/Activities/Design/Import/Persistence/EntityFrameworkCore/Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.csproj",
            "Elsa.Activities.Design.Persistence.EntityFrameworkCore",
            "Elsa.Persistence.EntityFramework",
            "Elsa.Serialization.Core",
            "Elsa.Workflows.Design.Persistence.EntityFrameworkCore",
            "Elsa.Workflows.Runtime.Core",
            "Elsa3.Activities.Design.Import");

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
}
