using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.DependencyInjection;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Configuration;
using Elsa.Workflows.Runtime.ReferenceGarbageCollection.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.ReferenceGarbageCollection;

/// <summary>
/// Opt-in host feature that schedules the ADR 0040 reference garbage collector. The GC service itself
/// (<see cref="IWorkflowExecutableReferenceGarbageCollector"/>) is part of the runtime composition root and can be
/// invoked manually (e.g. from an admin endpoint or a test); this feature adds the periodic trigger — a recurring
/// background pump that runs one two-query sweep per tick, dropping expired/retired references then pruning the
/// artifacts no live reference points at.
/// </summary>
/// <remarks>
/// Delivered as an <see cref="IRecurringTask"/>, so it depends on the Tasks feature for its execution lifecycle.
/// Scheduling is host policy: without this feature the runtime still resolves the GC service, but nothing drives it
/// on a timer — a host may instead call <see cref="IWorkflowExecutableReferenceGarbageCollector.SweepAsync(System.Threading.CancellationToken)"/>
/// from its own scheduler.
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Infrastructure")]
[ShellFeature(
    name: "WorkflowsRuntimeReferenceGarbageCollection",
    DisplayName = "Workflows Runtime Reference Garbage Collection",
    Description = "Background pump that periodically prunes expired/retired workflow-executable source references and the content-addressed artifacts no live reference points at any more (ADR 0040). Compose alongside the Tasks feature.",
    DependsOn = new object[] { "Tasks" })]
public sealed class WorkflowsRuntimeReferenceGarbageCollectionFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Sweep interval (minutes)", Description = "Baseline minutes between reference GC sweeps while the host is healthy.", Category = "Runtime", DefaultValue = "5")]
    public double SweepIntervalMinutes { get; set; } = 5;

    [ManifestSetting(DisplayName = "Max backoff interval (minutes)", Description = "Upper bound the sweep interval widens to under sustained failure.", Category = "Runtime", DefaultValue = "30")]
    public double MaxBackoffIntervalMinutes { get; set; } = 30;

    [ManifestSetting(DisplayName = "Artifact creation grace (minutes)", Description = "Minimum artifact age before physical garbage collection may consider it.", Category = "Runtime", DefaultValue = "5")]
    public double ArtifactCreationGracePeriodMinutes { get; set; } = 5;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddPersistenceCore();
        services.Configure<WorkflowExecutableReferenceGarbageCollectionOptions>(options =>
        {
            options.SweepInterval = TimeSpan.FromMinutes(SweepIntervalMinutes);
            options.MaxBackoffInterval = TimeSpan.FromMinutes(MaxBackoffIntervalMinutes);
            options.ArtifactCreationGracePeriod = TimeSpan.FromMinutes(ArtifactCreationGracePeriodMinutes);
        });
        services.Configure<WorkflowExecutableGarbageCollectionOptions>(options =>
        {
            options.ArtifactCreationGracePeriod = TimeSpan.FromMinutes(ArtifactCreationGracePeriodMinutes);
        });

        services.TryAddScoped<IWorkflowExecutableReferenceGarbageCollector, WorkflowExecutableReferenceGarbageCollector>();
        services.AddSingleton<IRecurringTask>(sp => new WorkflowExecutableReferenceGarbageCollectionPumpTask(
            sp.GetRequiredService<IPersistenceScopeRunner>(),
            sp.GetRequiredService<IOptions<WorkflowExecutableReferenceGarbageCollectionOptions>>(),
            sp.GetRequiredService<ILogger<WorkflowExecutableReferenceGarbageCollectionPumpTask>>()));
    }
}
