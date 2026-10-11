using Elsa.Versioning.Calculator.Tests.Support;

namespace Elsa.Versioning.Calculator.Tests;

/// <summary>
/// A tool package (<c>PackAsTool</c>) carries the builds of the projects it references instead of declaring ranges on
/// them, so FR-006a's premise does not hold for it: whatever it carries is package-affecting for it.
/// </summary>
public sealed class ToolPackageTests : SyntheticHistory
{
    /// <summary>The worker is not packable; its files reach users only inside the tool, so they are the tool's inputs.</summary>
    [Fact]
    public void A_change_to_an_unpackable_project_the_tool_carries_advances_the_tool()
    {
        Edit("src/Cli/Worker/Worker.cs");

        var computation = CommitAndCompute();

        Assert.Equal(["dotnet-elsa"], computation.Affected);
        Assert.Equal("4.0.6", VersionOf(computation, "dotnet-elsa"));
        Assert.Equal(["src/Cli/Worker/Worker.cs (changed)"], computation["dotnet-elsa"].Reasons);
    }

    /// <summary>A packable project the tool carries advances with the tool.</summary>
    [Fact]
    public void A_change_to_a_package_the_tool_carries_advances_both()
    {
        Edit("src/Http/Http.cs");

        var computation = CommitAndCompute();

        Assert.Equal(["Elsa.Http", "dotnet-elsa"], computation.Affected);
        Assert.Equal("4.0.5", VersionOf(computation, "Elsa.Http"));
        Assert.Equal("4.0.6", VersionOf(computation, "dotnet-elsa"));
    }

    /// <summary>
    /// A package the tool carries can advance with no file changing, when its line moves; the tool, which holds that
    /// build, moves with it.
    /// </summary>
    [Fact]
    public void A_carried_package_that_advances_without_a_file_change_advances_the_tool()
    {
        Repo.Project("Elsa.Http").References.Add("src/Primitives/Elsa.Primitives.csproj");
        Publish(CommitAndCompute("Reference the primitives"));
        Repo.ElsaContractsVersion = "4.1";

        var computation = CommitAndCompute("Contracts 4.1");

        Assert.Equal(["Elsa.Events.Core", "Elsa.Primitives", "dotnet-elsa"], computation.Affected);
        Assert.Equal(["carries Elsa.Primitives, which advances; a tool package holds the builds of what it references"], computation["dotnet-elsa"].Reasons);
    }

    /// <summary>Without <c>PackAsTool</c> the same references are ordinary ranges, and FR-006a leaves the package alone.</summary>
    [Fact]
    public void Without_pack_as_tool_a_referenced_change_does_not_repack_the_package()
    {
        Repo.Project("dotnet-elsa").Body = string.Empty;
        Publish(CommitAndCompute("Stop packing as a tool"));
        Edit("src/Http/Http.cs");

        Assert.Equal(["Elsa.Http"], CommitAndCompute().Affected);
    }
}
