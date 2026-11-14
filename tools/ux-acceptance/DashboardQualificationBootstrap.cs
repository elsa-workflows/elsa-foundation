using CShells.Lifecycle;
using CShells.Features;
using CShells.AspNetCore.Resolution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

/// <summary>Compiled only into an explicitly selected, disposable UX qualification host.</summary>
internal sealed class DashboardQualificationBootstrap(IShellRegistry registry, IConfiguration configuration,
    IHostApplicationLifetime lifetime, IRuntimeFeatureCatalog featureCatalog) : BackgroundService
{
    internal const string ReadinessPath = "/__qualification/ready";

    internal static void ExcludeReadinessProbe(WebRoutingShellResolverOptions options) =>
        options.ExcludePaths = [..(options.ExcludePaths ?? []), ReadinessPath];

    private volatile bool ready;
    internal bool Ready => ready;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(stoppingToken);
        // Discovery can publish configuration. Settle it before capturing the shell generation,
        // as the normal Workbench warmup does, rather than racing first discovery with activation.
        await featureCatalog.GetSnapshotAsync(stoppingToken);
        await QualificationPrincipals.InitializeAsync(registry, "default", configuration, stoppingToken);
        ready = true;
    }
}
