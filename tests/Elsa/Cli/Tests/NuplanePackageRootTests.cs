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

    /// <summary>
    /// The other shape User Story 2's own Independent Test names: a host whose <c>--packages</c> root
    /// carries a <c>store-state.json</c> recording the active set, rather than relying on completion
    /// markers alone. This drives a real state file through the CLI end to end, and checks the module it
    /// names produces the same SQL as the already-tested marker-probe path for the same module.
    /// </summary>
    [Fact]
    public void A_module_recorded_in_a_store_state_file_is_discovered_and_scripted_the_same_way()
    {
        Install("Acme.Widgets", "1.4.2", complete: true);
        var installPath = Path.Join(packages.Path, "local", "Acme.Widgets", "1.4.2");
        WriteStateFile("Acme.Widgets", "1.4.2", installPath);

        var list = DotnetElsa.Run("persistence", "list", "--host", DotnetElsa.Host("NuplaneHost"), "--packages", packages.Path);

        Assert.Equal(ToolExitCode.Success, list.ExitCode);
        Assert.Contains("Acme.Widgets", list.Output, StringComparison.Ordinal);

        using var fromState = new TempDirectory("elsa-cli-nuplane-state-artifact-");
        var script = DotnetElsa.Run(
            "persistence", "script",
            "--host", DotnetElsa.Host("NuplaneHost"),
            "--packages", packages.Path,
            "--provider", "PostgreSql",
            "--modules", "Acme.Widgets",
            "--output", fromState.Path);
        Assert.Equal(ToolExitCode.Success, script.ExitCode);

        // The already-tested --packages path (NuplaneInstallRoot.Probe, no state file): a second root with
        // the same completed install and no store-state.json beside it.
        using var probeRoot = new TempDirectory("elsa-cli-nuplane-probe-packages-");
        using var fromProbe = new TempDirectory("elsa-cli-nuplane-probe-artifact-");
        InstallInto(probeRoot.Path, "Acme.Widgets", "1.4.2", complete: true);
        Assert.Equal(
            ToolExitCode.Success,
            DotnetElsa.Run(
                "persistence", "script",
                "--host", DotnetElsa.Host("NuplaneHost"),
                "--packages", probeRoot.Path,
                "--provider", "PostgreSql",
                "--modules", "Acme.Widgets",
                "--output", fromProbe.Path).ExitCode);

        Assert.True(
            File.ReadAllBytes(fromState.File("01-acme-widgets.sql")).AsSpan()
                .SequenceEqual(File.ReadAllBytes(fromProbe.File("01-acme-widgets.sql"))),
            "The SQL a store-state-resolved module produces differs from the SQL the same module produces through the --packages probe path.");
    }

    /// <summary>Lays out one package the way Nuplane's own install store does: feed, id, version, and a completion marker.</summary>
    private void Install(string package, string version, bool complete) =>
        InstallInto(packages.Path, package, version, complete);

    private static void InstallInto(string root, string package, string version, bool complete)
    {
        var directory = Path.Join(root, "local", package, version, "lib", "net10.0");
        Directory.CreateDirectory(directory);
        File.Copy(typeof(WidgetsDbContext).Assembly.Location, Path.Join(directory, $"{package}.dll"));
        if (complete)
            File.WriteAllText(Path.Join(root, "local", package, version, NuplaneInstallRoot.ReadyMarker), "");
    }

    /// <summary>
    /// Writes a <c>store-state.json</c> by hand, in the shape <c>Nuplane.Store.State.StoreStateRecord</c> and
    /// <c>Nuplane.Abstractions.ActivePackageDescriptor</c> serialize to (camelCase, no enum converter — the
    /// default <c>Root</c> package role is left out rather than guessed as a numeric value): one active
    /// version and one matching descriptor, which is exactly what <c>ActivePackageCatalogMapper.MapActivePackages</c>
    /// requires to report a package as active. No graph activation record is written; with none present,
    /// <c>NuplaneHostIntegratedLoader</c> falls back to grouping by the descriptor's own (default) graph
    /// generation identity, which for one package is a graph of one.
    /// </summary>
    private void WriteStateFile(string packageId, string version, string installPath)
    {
        const string timestamp = "2026-09-21T00:00:00Z";
        var json = JsonSerializer.Serialize(new
        {
            activeVersionById = new Dictionary<string, string> { [packageId] = version },
            lastKnownGoodById = new Dictionary<string, string>(),
            lastFailureById = new Dictionary<string, object>(),
            lastSuccessfulSourceSnapshots = new Dictionary<string, object>(),
            updatedAt = timestamp,
            activePackageDescriptorsById = new Dictionary<string, object>
            {
                [packageId] = new
                {
                    packageId,
                    version,
                    feedName = "local",
                    sourceName = "local",
                    installPath,
                    activatedAtUtc = timestamp,
                    activationCorrelationId = "test-correlation"
                }
            },
            activeGraphsById = new Dictionary<string, object>()
        });
        File.WriteAllText(Path.Join(packages.Path, NuplaneInstallRoot.StateFileName), json);
    }
}
