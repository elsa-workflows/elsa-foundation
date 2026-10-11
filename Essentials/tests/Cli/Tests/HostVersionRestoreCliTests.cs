using Elsa.Cli.Worker;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>
/// A module whose floor the host cannot satisfy (issue #1951, ADR 0077), as <c>--restore</c> reports it: the
/// module depends on a package the host declares it provides, the host's own deps file carries that package
/// at a version outside the range, and Nuplane refuses the module with <c>host-version-unsatisfied</c>
/// rather than installing it to fail later at a missing member.
/// </summary>
/// <remarks>
/// <para>
/// The dependency is <c>Elsa.Persistence.EntityFramework</c>, which both fixture hosts carry as a project
/// reference, declared host-provided under the <c>Elsa.</c> prefix — the shape a real host gives its own
/// packages. Its version is read out of the copied host's deps file rather than written down, because it is
/// this repository's build version and the assertion is that Nuplane's message names exactly that.
/// </para>
/// <para>
/// Out of process, like the rest of the suite, and that matters more here than usual: the worker runs as
/// <c>dotnet exec --depsfile &lt;host&gt;.deps.json</c>, and Nuplane reads host versions from the deps files
/// the runtime actually loaded. A refusal naming the host's version is the proof that the check ran against
/// the host rather than against the tool.
/// </para>
/// </remarks>
public sealed class HostVersionRestoreCliTests : IDisposable
{
    private const string Module = "Acme.Widgets";
    private const string Version = "1.4.2";
    private const string HostPackage = "Elsa.Persistence.EntityFramework";
    private const string UnsatisfiableRange = "[999.0.0, )";
    private const string ElsaDeclaredHostProvided = """[ "Elsa." ]""";

    private readonly RestoreHost host = new();
    private readonly TempDirectory output = new("elsa-cli-host-version-artifact-");

    public void Dispose()
    {
        host.Dispose();
        output.Dispose();
    }

    /// <summary>
    /// The case the verdict exists for. Reported as its own refusal carrying Nuplane's message verbatim —
    /// the module, the dependency, the range and the host's version — rather than as "this package could not
    /// be installed", which is true and useless: the package is on the feed, and the host is what is too old.
    /// </summary>
    [Fact]
    public void A_module_whose_floor_the_host_cannot_satisfy_is_refused_naming_the_range_and_the_hosts_version()
    {
        host.FeedDependingOn(Module, Version, HostPackage, UnsatisfiableRange);
        host.Configure(ModuleFeed, hostProvidedPackages: ElsaDeclaredHostProvided);
        var hostVersion = host.Deps.ForPackage(HostPackage)!.Version;

        var run = Restore(host);

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("restore-host-version-unsatisfied", run.Error, StringComparison.Ordinal);
        Assert.Contains($"{Module} (host-version-unsatisfied): Package '{Module}@{Version}'", run.Error, StringComparison.Ordinal);
        Assert.Contains($"but the host provides '{HostPackage} {hostVersion}'", run.Error, StringComparison.Ordinal);
        Assert.Contains("999.0.0", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("restore-incomplete", run.Error, StringComparison.Ordinal);
        Assert.Null(host.Recorded(Module));
        Assert.Empty(Directory.EnumerateFileSystemEntries(output.Path));
    }

    /// <summary>
    /// The direction that must not be mistaken for the one above: the same module, the same declaration,
    /// and a range the host's own version satisfies restores exactly as it did before the check existed —
    /// and the host-provided dependency is still never acquired. Without this the refusal test would pass
    /// just as well against a check that refused every declared dependency.
    /// </summary>
    [Fact]
    public void A_module_whose_floor_the_host_satisfies_is_restored_without_acquiring_the_host_package()
    {
        var hostVersion = host.Deps.ForPackage(HostPackage)!.Version;
        host.FeedDependingOn(Module, Version, HostPackage, $"[{hostVersion}, )");
        host.Configure(ModuleFeed, hostProvidedPackages: ElsaDeclaredHostProvided);

        var run = Restore(host);

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.Contains("restore: 1 package(s) installed under", run.Error, StringComparison.Ordinal);
        Assert.NotNull(host.Recorded(Module));
        Assert.Null(host.Recorded(HostPackage));
    }

    /// <summary>
    /// Two independent faults in one cycle, on two different modules: one needs a newer host, the other an
    /// engine selection the host has not made. Fixing either alone would leave the run refused for the other,
    /// so the refusal names both — the host-version code leads, because it is the fault no configuration key
    /// fixes, and every capability refusal is listed beside it under its own stage with its own remedy.
    /// </summary>
    [Fact]
    public void A_cycle_with_both_a_host_version_and_a_capability_refusal_reports_both()
    {
        const string gadgets = "Acme.Gadgets";
        using var engineless = new RestoreHost("NuplaneCapabilityHost");
        engineless.FeedWithEngineCapability(Module, Version);
        engineless.FeedDependingOn(gadgets, "1.0.0", HostPackage, UnsatisfiableRange);
        engineless.Configure(ModuleFeed, hostProvidedPackages: ElsaDeclaredHostProvided);

        var run = Restore(engineless);

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("restore-host-version-unsatisfied", run.Error, StringComparison.Ordinal);
        Assert.Contains($"{gadgets} (host-version-unsatisfied): Package '{gadgets}@1.0.0'", run.Error, StringComparison.Ordinal);
        Assert.Contains($"{Module} (capability-unselected)", run.Error, StringComparison.Ordinal);
        Assert.Contains("Nuplane:Capabilities:ef-provider", run.Error, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(output.Path));
    }

    /// <summary>A directory feed beside the host that serves every fixture package and never an engine.</summary>
    private static string ModuleFeed =>
        """
          "local-packages": {
            "DirectoryPath": "packages",
            "IncludePatterns": [ "Acme.*" ]
          }
        """;

    private CliRun Restore(RestoreHost target) =>
        DotnetElsa.Run(
            "persistence", "script",
            "--host", target.Path,
            "--provider", "PostgreSql",
            "--modules", Module,
            "--output", output.Path,
            "--restore");
}
