using Elsa.Cli.Worker;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>
/// The <c>--packages</c> probe for a root with no state file (FR-006): a completed install is one carrying
/// the completion marker, a staging directory is not a feed, and two versions of one package with nothing
/// to choose between them are refused rather than guessed.
/// </summary>
public sealed class PackageRootProbeTests : IDisposable
{
    private readonly TempDirectory root = new("elsa-cli-packages-");

    public void Dispose() => root.Dispose();

    [Fact]
    public void Only_completed_installs_are_part_of_the_set()
    {
        Install("local", "Contoso.Widgets", "1.0.0", complete: true);
        Install("local", "Contoso.Gadgets", "2.0.0", complete: false);

        var installed = NuplaneInstallRoot.Probe(root.Path);

        var package = Assert.Single(installed);
        Assert.Equal("Contoso.Widgets", package.Id);
        Assert.Equal("1.0.0", package.Version);
        Assert.Equal("local", package.FeedName);
    }

    /// <summary>
    /// An extraction in progress is not a corrupt install; it is one that has not happened yet, and the
    /// staging directory it uses must not be read as a feed of its own.
    /// </summary>
    [Fact]
    public void The_staging_directory_is_skipped_at_every_level()
    {
        Install(".tmp", "Contoso.Widgets", "9.9.9", complete: true);
        Install("local", ".tmp", "1.0.0", complete: true);
        Directory.CreateDirectory(Path.Join(root.Path, "local", "Contoso.Widgets", ".tmp"));
        File.WriteAllText(Path.Join(root.Path, "local", "Contoso.Widgets", ".tmp", NuplaneInstallRoot.ReadyMarker), "");
        Install("local", "Contoso.Widgets", "1.0.0", complete: true);

        Assert.Equal(["Contoso.Widgets"], NuplaneInstallRoot.Probe(root.Path).Select(package => package.Id));
    }

    [Fact]
    public void Two_installed_versions_of_one_package_are_refused_by_version()
    {
        Install("local", "Contoso.Widgets", "1.0.0", complete: true);
        Install("local", "Contoso.Widgets", "2.0.0", complete: true);

        var refusal = Assert.Throws<WorkerRefusal>(() => NuplaneInstallRoot.Probe(root.Path));

        Assert.Equal(ToolExitCode.ResolutionFailure, refusal.ExitCode);
        Assert.Equal("packages-ambiguous", refusal.Code);
        Assert.Equal(["'Contoso.Widgets' is installed at 1.0.0, 2.0.0."], refusal.Details);
    }

    [Fact]
    public void An_empty_root_finds_nothing_rather_than_failing_to_read_it()
    {
        Assert.Empty(NuplaneInstallRoot.Probe(root.Path));
    }

    [Fact]
    public void A_root_that_does_not_exist_is_refused_by_path()
    {
        Assert.Equal("packages-root-missing", Assert.Throws<WorkerRefusal>(() => NuplaneInstallRoot.Probe(Path.Join(root.Path, "nowhere"))).Code);
    }

    /// <summary>
    /// A root with nothing installed and nothing downloadable is exit 3, not an empty success: the tool
    /// never populates a package root (ADR 0076 D10), so an empty one cannot be worked around by running it.
    /// </summary>
    [Fact]
    public void The_tool_refuses_an_empty_package_root_rather_than_populating_it()
    {
        var run = DotnetElsa.Run("persistence", "list", "--host", DotnetElsa.Host("MinimalHost"), "--packages", root.Path);

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("packages-empty", run.Error, StringComparison.Ordinal);
        Assert.Contains("No command downloads packages", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two roots resolved two different ways is the one case with no safe answer: a state file records which
    /// packages a host loads together and a bare directory does not, and a run loads exactly one package set.
    /// Refused naming both, rather than quietly loading one of them.
    /// </summary>
    [Fact]
    public void A_root_with_a_state_file_beside_one_without_is_refused_naming_both()
    {
        Install("local", "Contoso.Widgets", "1.0.0", complete: true);
        using var stateRoot = new TempDirectory("elsa-cli-packages-state-");
        File.WriteAllText(Path.Join(stateRoot.Path, NuplaneInstallRoot.StateFileName), "{}");

        var run = DotnetElsa.Run(
            "persistence", "list",
            "--host", DotnetElsa.Host("MinimalHost"),
            "--packages", stateRoot.Path,
            "--packages", root.Path);

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("packages-roots-inconsistent", run.Error, StringComparison.Ordinal);
        Assert.Contains(stateRoot.Path, run.Error, StringComparison.Ordinal);
        Assert.Contains(root.Path, run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_package_installed_under_two_roots_is_refused_rather_than_ordered()
    {
        Install("local", "Contoso.Widgets", "1.0.0", complete: true);
        using var second = new TempDirectory("elsa-cli-packages-second-");
        Directory.CreateDirectory(Path.Join(second.Path, "local", "Contoso.Widgets", "1.0.0"));
        File.WriteAllText(Path.Join(second.Path, "local", "Contoso.Widgets", "1.0.0", NuplaneInstallRoot.ReadyMarker), "");

        var run = DotnetElsa.Run(
            "persistence", "list",
            "--host", DotnetElsa.Host("MinimalHost"),
            "--packages", root.Path,
            "--packages", second.Path);

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("packages-ambiguous", run.Error, StringComparison.Ordinal);
    }

    private void Install(string feed, string package, string version, bool complete)
    {
        var directory = Path.Join(root.Path, feed, package, version);
        Directory.CreateDirectory(Path.Join(directory, "lib", "net10.0"));
        if (complete)
            File.WriteAllText(Path.Join(directory, NuplaneInstallRoot.ReadyMarker), "");
    }
}
