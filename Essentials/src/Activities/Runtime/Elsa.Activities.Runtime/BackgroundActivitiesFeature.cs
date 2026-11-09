using CShells.Features;
using Elsa.Activities.Runtime.Services;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Runtime;

/// <summary>Runs independently leased native leaf activity work using the composed persistence provider.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(name: "BackgroundActivities", DisplayName = "Background Activities",
    Description = "Executes eligible native activities on independently leased workers and repairs durable result delivery.",
    DependsOn = new object[] { "ActivitiesRuntime", "Tasks", "WorkflowsRuntimeResumption" })]
public sealed class BackgroundActivitiesFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Run workers", Description = "Run the local worker pump. Disable on coordinator-only hosts when another host consumes the same durable work store.", Category = "Runtime")]
    public bool RunWorkers { get; set; } = true;

    [ManifestSetting(DisplayName = "Worker concurrency", Description = "Maximum activity bodies concurrently executing on this host (1-256).", Category = "Runtime")]
    public int MaxConcurrency { get; set; } = 4;

    [ManifestSetting(DisplayName = "Poll interval (seconds)", Description = "Seconds between durable work discovery sweeps.", Category = "Runtime")]
    public double PollIntervalSeconds { get; set; } = 1;

    [ManifestSetting(DisplayName = "Worker lease (seconds)", Description = "Claim visibility before work becomes reclaimable after worker loss.", Category = "Runtime")]
    public double LeaseDurationSeconds { get; set; } = 30;

    [ManifestSetting(DisplayName = "Claim renewal (seconds)", Description = "Renewal and cancellation observation interval, shorter than the lease duration.", Category = "Runtime")]
    public double RenewalIntervalSeconds { get; set; } = 10;

    public void ConfigureServices(IServiceCollection services)
    {
        var settings = new BackgroundActivityWorkerOptions
        {
            MaxConcurrency = MaxConcurrency,
            PollInterval = TimeSpan.FromSeconds(PollIntervalSeconds),
            LeaseDuration = TimeSpan.FromSeconds(LeaseDurationSeconds),
            RenewalInterval = TimeSpan.FromSeconds(RenewalIntervalSeconds)
        };
        settings.Validate();
        services.Configure<BackgroundActivityWorkerOptions>(options =>
        {
            options.Enabled = true;
            options.MaxConcurrency = settings.MaxConcurrency;
            options.PollInterval = settings.PollInterval;
            options.LeaseDuration = settings.LeaseDuration;
            options.RenewalInterval = settings.RenewalInterval;
        });
        services.TryAddSingleton<BackgroundActivityWorker>();
        if (RunWorkers)
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IBackgroundTask, BackgroundActivityWorkerPump>());
    }
}
