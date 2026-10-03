using Elsa.Cli.Worker;
using System.Reflection;
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
    /// A package can carry byte-for-byte copies of host contracts. They bind to the right identity only when
    /// the CLI forwards the host's configured Nuplane sharing policy to the host-integrated loader.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Host_shared_assembly_policy_is_used_for_probe_and_state_loads(bool useStateFile)
    {
        using var host = new RestoreHost();
        var installPath = NuplanePackageRootFixture.InstallInto(packages.Path, "Acme.Widgets", "1.4.2", complete: true);
        var libraryDirectory = Path.Join(installPath, "lib", "net10.0");
        var persistenceAssemblyPath = Path.Join(host.Path, "Elsa.Persistence.EntityFramework.dll");
        Assert.True(File.Exists(persistenceAssemblyPath), "The Nuplane fixture host must carry the persistence contract assembly.");
        File.Copy(persistenceAssemblyPath, Path.Join(libraryDirectory, Path.GetFileName(persistenceAssemblyPath)));
        if (useStateFile)
            NuplanePackageRootFixture.WriteStateFile(packages.Path, "Acme.Widgets", "1.4.2", installPath);

        // With no settings, the default loader options preserve the private contract copy. Reflection sees
        // its EfModuleAttribute as a different CLR type, so the host discovers no module.
        File.Delete(Path.Join(host.Path, HostAppSettings.BaseFileName));
        File.Delete(Path.Join(host.Path, HostAppSettings.OverlayFileName("Development")));
        var withoutSharing = List(host.Path);
        Assert.Equal(ToolExitCode.Success, withoutSharing.ExitCode);
        Assert.Contains("0 module(s).", withoutSharing.Output, StringComparison.Ordinal);

        var persistenceMajorVersion = AssemblyName.GetAssemblyName(persistenceAssemblyPath).Version!.Major;
        WriteSharedAssemblyPolicy(host.Path, persistenceAssemblyPath, "Development", persistenceMajorVersion);
        var withSharing = List(host.Path);
        Assert.Equal(ToolExitCode.Success, withSharing.ExitCode);
        Assert.Contains("Acme.Widgets", withSharing.Output, StringComparison.Ordinal);

        // Nuplane's binder records a dropped/malformed shared-identity entry and its options validator
        // rejects the host policy instead of silently reverting to an empty sharing list.
        WriteSharedAssemblyPolicy(host.Path, persistenceAssemblyPath, "Development", "invalid");
        var malformedPolicy = List(host.Path);
        Assert.Equal(ToolExitCode.ResolutionFailure, malformedPolicy.ExitCode);
        Assert.Contains("packages-loader-policy-invalid", malformedPolicy.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Acme.Widgets", malformedPolicy.Text, StringComparison.Ordinal);
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
    /// markers alone. The install here is deliberately left without its <c>.nuplane-ready</c> marker, so
    /// <see cref="NuplaneInstallRoot.Probe"/> — the marker-based fallback — cannot find it; only
    /// <c>NuplaneLoader.FromStateAsync</c> reading <c>store-state.json</c> can. That the SQL it produces
    /// matches the marker-probe path (a separate, completed install, proven elsewhere) shows the state-file
    /// route resolves the same module the same way, not that either route alone was exercised. The second
    /// half proves the state file is load-bearing: with it removed, the identical marker-less install is no
    /// longer discovered at all.
    /// </summary>
    [Fact]
    public void A_module_recorded_in_a_store_state_file_is_discovered_and_scripted_the_same_way()
    {
        Install("Acme.Widgets", "1.4.2", complete: false);
        var installPath = Path.Join(packages.Path, "local", "Acme.Widgets", "1.4.2");
        NuplanePackageRootFixture.WriteStateFile(packages.Path, "Acme.Widgets", "1.4.2", installPath);

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
        // a completed install and no store-state.json beside it.
        using var probeRoot = new TempDirectory("elsa-cli-nuplane-probe-packages-");
        using var fromProbe = new TempDirectory("elsa-cli-nuplane-probe-artifact-");
        NuplanePackageRootFixture.InstallInto(probeRoot.Path, "Acme.Widgets", "1.4.2", complete: true);
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

        // Negative: the same root, minus the state file, has only the marker-less install left. The
        // marker-based probe cannot see it either, so discovery must fail rather than silently fall back.
        File.Delete(Path.Join(packages.Path, NuplaneInstallRoot.StateFileName));

        var listWithoutState = DotnetElsa.Run("persistence", "list", "--host", DotnetElsa.Host("NuplaneHost"), "--packages", packages.Path);

        Assert.Equal(ToolExitCode.ResolutionFailure, listWithoutState.ExitCode);
        Assert.Contains("packages-empty", listWithoutState.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Acme.Widgets", listWithoutState.Output, StringComparison.Ordinal);
    }

    /// <summary>Lays out one package the way Nuplane's own install store does: feed, id, version, and a completion marker.</summary>
    private void Install(string package, string version, bool complete) =>
        NuplanePackageRootFixture.InstallInto(packages.Path, package, version, complete);

    private CliRun List(string hostDirectory) =>
        DotnetElsa.Run(
            "persistence", "list",
            "--host", hostDirectory,
            "--environment", "Development",
            "--packages", packages.Path);

    private static void WriteSharedAssemblyPolicy(string hostDirectory, string assemblyPath, string environment, object majorVersion)
    {
        var identity = AssemblyName.GetAssemblyName(assemblyPath);
        var token = identity.GetPublicKeyToken();
        var publicKeyToken = token is { Length: > 0 } ? Convert.ToHexString(token) : null;
        var settings = new
        {
            Nuplane = new
            {
                Loading = new
                {
                    SharedAssemblies = new[]
                    {
                        new { identity.Name, PublicKeyToken = publicKeyToken, MajorVersion = majorVersion }
                    }
                }
            }
        };
        File.WriteAllText(
            Path.Join(hostDirectory, HostAppSettings.OverlayFileName(environment)),
            JsonSerializer.Serialize(settings));
    }
}
