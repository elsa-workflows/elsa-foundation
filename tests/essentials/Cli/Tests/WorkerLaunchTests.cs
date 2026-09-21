using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>
/// What the worker launch is allowed to carry (FR-002), and — the part that matters for the commands a
/// later slice adds — what it has no way to carry.
/// </summary>
public sealed class WorkerLaunchTests
{
    private readonly HostLayout host = new("/hosts/contoso", "Contoso.Host", "/hosts/contoso/Contoso.Host.runtimeconfig.json", "/hosts/contoso/Contoso.Host.deps.json");

    [Fact]
    public void The_launch_carries_the_hosts_own_two_files_and_the_worker_and_nothing_else()
    {
        Assert.Equal(
            [
                "exec",
                "--runtimeconfig", "/hosts/contoso/Contoso.Host.runtimeconfig.json",
                "--depsfile", "/hosts/contoso/Contoso.Host.deps.json",
                "/tools/dotnet-elsa/Elsa.Cli.Worker.dll"
            ],
            WorkerProcess.Arguments(host, "/tools/dotnet-elsa/Elsa.Cli.Worker.dll"));
    }

    /// <summary>
    /// Nothing a command was asked to do reaches the argument list: the request travels over stdin, and the
    /// launch is built from the host layout alone. `apply`/`validate` (#1876) take a connection string, and
    /// process arguments are world-readable — this is the property that keeps one from landing there by a
    /// later edit, and it is asserted here rather than left to the signature to imply.
    /// </summary>
    [Fact]
    public void The_launch_is_built_from_the_host_layout_alone()
    {
        var arguments = WorkerProcess.Arguments(host, "/tools/dotnet-elsa/Elsa.Cli.Worker.dll");

        Assert.All(arguments, argument => Assert.DoesNotContain("Password", argument, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(6, arguments.Count);
        Assert.Equal(
            [host.RuntimeConfig, host.DepsFile, "/tools/dotnet-elsa/Elsa.Cli.Worker.dll"],
            arguments.Where(argument => !argument.StartsWith("--", StringComparison.Ordinal) && argument != "exec"));
    }
}
