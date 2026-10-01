using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>Tests that set the test process's exit code, which no other test may run beside.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessExitCodeCollection
{
    public const string Name = "Process exit code";
}

/// <summary>
/// Spec 183, FR-007 and its 2026-10-01 note: a host app that runs its host with
/// <see cref="ClusterMembershipHostExtensions.RunWithMembershipExitCode"/> ends with exit code 1 once the membership
/// provider recorded that it stopped the host, and leaves the exit code as it was otherwise. The exit code is the test
/// process's own, so the test restores it.
/// </summary>
[Collection(ProcessExitCodeCollection.Name)]
public sealed class ClusterMembershipHostStopTests
{
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 7)]
    public void A_host_run_with_the_membership_exit_code_ends_with_1_only_after_a_recorded_stop(bool recorded, int expected)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        var stop = new ClusterMembershipHostStop();
        builder.Services.AddSingleton(stop);
        builder.Services.AddHostedService(services => new StopsAtOnce(services.GetRequiredService<IHostApplicationLifetime>(), recorded ? stop : null));
        var host = builder.Build();
        var previous = Environment.ExitCode;
        try
        {
            Environment.ExitCode = 7;

            host.RunWithMembershipExitCode();

            Assert.Equal(expected, Environment.ExitCode);
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }

    /// <summary>Stops the host as soon as it starts, having recorded a displacement first when it is given the stop to record it in.</summary>
    private sealed class StopsAtOnce(IHostApplicationLifetime lifetime, ClusterMembershipHostStop? stop) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            stop?.Record(new MemberLapse(MemberLapseReason.Displaced, DateTimeOffset.UtcNow));
            lifetime.StopApplication();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
