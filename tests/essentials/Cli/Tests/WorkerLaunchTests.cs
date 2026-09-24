using Elsa.Cli.Worker;
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

    /// <summary>
    /// <c>--restore</c> gives the tool a second kind of secret to keep out of the launch: a feed's
    /// configured <c>Credentials</c> reference. This pins that the request has nowhere for one to land —
    /// the flag travels as a bare boolean, and everything the restore actually needs (which feeds, which
    /// patterns, which credentials) is read by the worker out of the host's own <c>appsettings.json</c>,
    /// on the other side of the process boundary.
    /// </summary>
    /// <remarks>
    /// Asserted as the whole field set rather than as an absence, for the same reason
    /// <see cref="The_launch_is_built_from_the_host_layout_alone"/> counts the arguments: a later edit that
    /// adds a field which could carry feed configuration has to change this list, and changing it is the
    /// moment to ask whether the value belongs on this side of the boundary at all. Only
    /// <see cref="WorkerRequest.Connection"/> carries a secret, and D7 already accounts for it.
    /// </remarks>
    [Fact]
    public void A_restore_travels_as_a_flag_and_the_request_has_nowhere_for_a_feed_credential_to_land()
    {
        var fields = typeof(WorkerRequest).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal);

        Assert.Equal(
            [
                "Command", "Connection", "ConnectionEnv", "ContextSource", "ContextVersion", "DepsFile", "Environment", "HostDirectory", "HostName",
                "Output", "PackageRoots", "Provider", "Resource", "Restore", "Schema", "Selection", "Shell", "Shells", "Version"
            ],
            fields);
        Assert.Equal(typeof(bool), typeof(WorkerRequest).GetProperty(nameof(WorkerRequest.Restore))!.PropertyType);
    }
}
