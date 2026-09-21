using Acme.Widgets;
using Elsa.Cli.Worker;
using System.Text.Json;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>
/// A host whose modules arrive as packages rather than in its own dependency file (spec 171 User Story 2,
/// FR-006): the module assembly exists only under a <c>--packages</c> root, and is resolved by booting the
/// host's own Nuplane loader rather than by anything this tool implements itself.
/// </summary>
/// <remarks>
/// The fixture host carries Nuplane, the persistence policy assembly and one provider engine, and no
/// module at all — so a module named here can only have come through the loader.
/// </remarks>
public sealed class NuplanePackageRootTests : IDisposable
{
    private readonly TempDirectory packages = new("elsa-cli-nuplane-packages-");
    private readonly TempDirectory output = new("elsa-cli-nuplane-artifact-");

    public void Dispose()
    {
        packages.Dispose();
        output.Dispose();
    }

    [Fact]
    public void A_host_with_no_module_of_its_own_lists_none_until_a_package_root_supplies_one()
    {
        var run = DotnetElsa.Run("persistence", "list", "--host", DotnetElsa.Host("NuplaneHost"));

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.Contains("0 module(s).", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_module_installed_under_a_package_root_is_discovered_through_the_hosts_own_loader()
    {
        Install("Acme.Widgets", "1.4.2", complete: true);

        var run = DotnetElsa.Run("persistence", "list", "--host", DotnetElsa.Host("NuplaneHost"), "--packages", packages.Path);

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.Contains("Acme.Widgets", run.Output, StringComparison.Ordinal);
        Assert.Contains("__EFMigrationsHistory_AcmeWidgets", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The manifest states where each version came from (FR-049), and the two sources appear side by side
    /// here: the engine out of the host's dependency file, the module out of the package it resolved from.
    /// </summary>
    [Fact]
    public void The_manifest_records_a_package_resolved_version_for_a_module_the_deps_file_does_not_list()
    {
        Install("Acme.Widgets", "1.4.2", complete: true);

        var run = DotnetElsa.Run(
            "persistence", "script",
            "--host", DotnetElsa.Host("NuplaneHost"),
            "--packages", packages.Path,
            "--provider", "PostgreSql",
            "--modules", "Acme.Widgets",
            "--output", output.Path);

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(output.File(MigrationPlan.FileName)));
        var module = manifest.RootElement.GetProperty("modules")[0].GetProperty("package");
        Assert.Equal("Acme.Widgets", module.GetProperty("id").GetString());
        Assert.Equal("1.4.2", module.GetProperty("version").GetString());
        Assert.Equal("resolved-nupkg", module.GetProperty("source").GetString());
        Assert.Equal("host-deps-file", manifest.RootElement.GetProperty("engine").GetProperty("source").GetString());
    }

    /// <summary>
    /// The same module, the same provider, resolved two entirely different ways — out of a package root
    /// here, out of the host's own dependency file there — produces the same SQL. If the loader path
    /// bound a different copy of anything, this is where it would show.
    /// </summary>
    [Fact]
    public void The_sql_is_the_same_as_the_one_a_deps_file_host_produces_for_the_same_module()
    {
        Install("Acme.Widgets", "1.4.2", complete: true);
        using var fromDepsFile = new TempDirectory("elsa-cli-deps-artifact-");

        Assert.Equal(
            ToolExitCode.Success,
            DotnetElsa.Run(
                "persistence", "script",
                "--host", DotnetElsa.Host("NuplaneHost"),
                "--packages", packages.Path,
                "--provider", "PostgreSql",
                "--modules", "Acme.Widgets",
                "--output", output.Path).ExitCode);
        Assert.Equal(
            ToolExitCode.Success,
            DotnetElsa.Run(
                "persistence", "script",
                "--host", DotnetElsa.Host("MinimalHost"),
                "--provider", "PostgreSql",
                "--modules", "Acme.Widgets",
                "--output", fromDepsFile.Path).ExitCode);

        Assert.True(
            File.ReadAllBytes(output.File("01-acme-widgets.sql")).AsSpan()
                .SequenceEqual(File.ReadAllBytes(fromDepsFile.File("01-acme-widgets.sql"))),
            "The SQL a package-resolved module produces differs from the SQL the same module produces from a deps file.");
    }

    /// <summary>
    /// An extraction that never completed is treated as not installed, not as a corrupt install (User Story
    /// 2, scenario 3) — and with nothing else installed, the root is empty, which is exit 3 rather than a
    /// download (ADR 0076 D10).
    /// </summary>
    [Fact]
    public void An_install_with_no_completion_marker_is_not_part_of_the_set()
    {
        Install("Acme.Widgets", "1.4.2", complete: false);

        var run = DotnetElsa.Run("persistence", "list", "--host", DotnetElsa.Host("NuplaneHost"), "--packages", packages.Path);

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("packages-empty", run.Error, StringComparison.Ordinal);
    }

    /// <summary>Lays out one package the way Nuplane's own install store does: feed, id, version, and a completion marker.</summary>
    private void Install(string package, string version, bool complete)
    {
        var directory = Path.Join(packages.Path, "local", package, version, "lib", "net10.0");
        Directory.CreateDirectory(directory);
        File.Copy(typeof(WidgetsDbContext).Assembly.Location, Path.Join(directory, $"{package}.dll"));
        if (complete)
            File.WriteAllText(Path.Join(packages.Path, "local", package, version, NuplaneInstallRoot.ReadyMarker), "");
    }
}
