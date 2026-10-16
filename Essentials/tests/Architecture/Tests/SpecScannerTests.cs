using Elsa.Maps.Generator;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class SpecScannerTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory(nameof(SpecScannerTests));
    private readonly string specDirectory;

    public SpecScannerTests()
    {
        File.WriteAllText(Path.Join(root.FullName, "Elsa.Server.slnx"), "<Solution />");
        specDirectory = Path.Join(root.FullName, "specs", "001-fixture");
        Directory.CreateDirectory(specDirectory);
    }

    [Theory]
    [InlineData("## Out of scope\nDeliberate exclusions belong here.", "-")]
    [InlineData("Retained context, deferred alternatives and superseded designs are out of scope; construct-only is historical.", "-")]
    [InlineData("**Notes**: Awaiting owner confirmation.", "Awaiting owner confirmation.")]
    [InlineData("**Notes**: Deferred until the provider is ready.\n## Out of scope\nSuperseded alternatives.", "Deferred until the provider is ready.")]
    [InlineData("**Notes**:No space after the colon.", "No space after the colon.")]
    [InlineData("**Notes**:\t  Review the migration.  \r\n", "Review the migration.")]
    [InlineData("**Notes**:", "-")]
    [InlineData("**Notes**:  \r\n## Out of scope\nDeferred alternatives.", "-")]
    [InlineData("**Notes**: First note.\n**Notes**: Second note.", "First note.")]
    public void Notes_come_only_from_explicit_metadata(string text, string expected)
    {
        var spec = ReadSpec(text);

        Assert.Equal(expected, spec.Notes);
    }

    [Fact]
    public void Explicit_notes_preserve_the_other_spec_facts()
    {
        File.WriteAllText(Path.Join(specDirectory, "plan.md"), "Constitution Check verdict: PASS\n");
        File.WriteAllText(Path.Join(specDirectory, "tasks.md"), "- [x] T001 Done\n- [ ] T002 Open\n");

        var spec = ReadSpec("# Feature Specification: Fixture\n**Status**: Implemented\n**Notes**: Extra verification required.\n");

        Assert.Equal(new SpecFacts("001-fixture", "Fixture", "Implemented", "PASS", 1, 1, "Extra verification required."), spec);
    }

    [Fact]
    public void A_folder_without_a_spec_has_no_inferred_notes()
    {
        var spec = Assert.Single(SpecScanner.Read(RepoContext.Discover(root.FullName)));

        Assert.Equal("-", spec.Notes);
    }

    private SpecFacts ReadSpec(string text)
    {
        File.WriteAllText(Path.Join(specDirectory, "spec.md"), text);
        return Assert.Single(SpecScanner.Read(RepoContext.Discover(root.FullName)));
    }

    public void Dispose() => root.Delete(recursive: true);
}
