using CShells.Features;
using Elsa.Jobs.Core;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Jobs;

/// <summary>Optional shell composition; ordinary .NET hosts use AddElsaJobs directly.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Jobs")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(name: "Jobs", DisplayName = "Jobs", Description = "Processes registered standalone durable jobs.",
    DependsOn = new object[] { "Tasks" })]
public class JobsFeature : IShellFeature
{
    public string PersistencePartition { get; set; } = "default";
    public string? TenantId { get; set; }
    public string[] Queues { get; set; } = ["default"];
    public bool EnableWorker { get; set; } = true;
    public bool MaterializeSchedules { get; set; } = true;
    public int MaximumConcurrentAttempts { get; set; } = 2;
    public int PollIntervalMilliseconds { get; set; } = 250;
    public int LeaseDurationSeconds { get; set; } = 30;
    public int RenewalIntervalMilliseconds { get; set; } = 10_000;
    public int ShutdownGracePeriodSeconds { get; set; } = 30;
    public int MaximumOccurrencesPerMaterialization { get; set; } = 32;
    public int MaterializationIntervalMilliseconds { get; set; } = 250;
    public int MaterializationTimeoutSeconds { get; set; } = 30;

    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddElsaJobs(options =>
        {
            options.EnableWorker = EnableWorker;
            options.MaterializeSchedules = MaterializeSchedules;
            if (EnableWorker) options.WorkerScopes.Add(new JobScope(PersistencePartition, TenantId));
            options.Queues.Clear();
            foreach (var queue in Queues) options.Queues.Add(queue);
            options.MaximumConcurrentAttempts = MaximumConcurrentAttempts;
            options.PollInterval = TimeSpan.FromMilliseconds(PollIntervalMilliseconds);
            options.LeaseDuration = TimeSpan.FromSeconds(LeaseDurationSeconds);
            options.RenewalInterval = TimeSpan.FromMilliseconds(RenewalIntervalMilliseconds);
            options.ShutdownGracePeriod = TimeSpan.FromSeconds(ShutdownGracePeriodSeconds);
            options.MaximumOccurrencesPerMaterialization = MaximumOccurrencesPerMaterialization;
            options.MaterializationInterval = TimeSpan.FromMilliseconds(MaterializationIntervalMilliseconds);
            options.MaterializationTimeout = TimeSpan.FromSeconds(MaterializationTimeoutSeconds);
        });
    }
}
