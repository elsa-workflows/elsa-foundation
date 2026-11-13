using CShells.Lifecycle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

/// <summary>Compiled only into an explicitly selected, disposable UX qualification host.</summary>
internal sealed class DashboardQualificationBootstrap(IShellRegistry registry, IConfiguration configuration,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    private volatile bool ready;
    internal bool Ready => ready;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(stoppingToken);
        await QualificationPrincipals.InitializeAsync(registry, "default", configuration, stoppingToken);
        ready = true;
    }
}
