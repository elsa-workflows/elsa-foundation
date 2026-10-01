using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Handlers;
using Elsa.Activities.Design.Reconciliation.Options;
using Elsa.Activities.Design.Reconciliation.Services;
using Elsa.Events.Core.Extensions;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Reconciliation;

/// <summary>
/// Activity-design reconciliation feature. Registers the reconciler, its options, the single
/// startup task that drives a pass, and the universal <see cref="CollectActivityVersions"/>
/// that resolves every registered <see cref="IActivityReconciliationSource"/> from DI. Source
/// modules (CLR scanner, JSON catalog, …) contribute by registering their own
/// <see cref="IActivityReconciliationSource"/> via their own feature (§2.6.1) — this feature
/// owns none and is no longer extended by inheritance (FR-021, gate G13).
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Design")]
[ManifestFeatureCategory("Reconciliation")]
[ShellFeature(
    name: "ActivitiesDesignReconciliation",
    DisplayName = "Activities Design Reconciliation",
    Description = "Universal activity-design reconciliation pass; discovers IActivityReconciliationSource contributions from DI."
)]
public class ActivitiesDesignReconciliationFeature : IShellFeature
{
    public ActivityVersionReconcilerOptions ReconcilerOptions { get; set; } = new();

    /// <summary>Retired settings only (#2192); a value set here refuses to start. See <see cref="ActivityVersionReconcilerStartupTaskOptions"/>.</summary>
    public ActivityVersionReconcilerStartupTaskOptions StartupTaskOptions { get; set; } = new();

    public virtual void ConfigureServices(IServiceCollection services)
    {
        RefuseRetiredLockTimeout();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(ReconcilerOptions));

        // The content hasher + entity factories are registered by the persistence feature (entity
        // construction lives with persistence); the reconciler consumes the factories.
        services.AddScoped<IActivityVersionReconciler, ActivityVersionReconciler>();

        services.AddScoped<IStartupTask, ActivityVersionReconcilerStartupTask>();

        services.AddEventHandler<ActivityVersionsReconciling, CollectActivityVersions>();
    }

    private void RefuseRetiredLockTimeout()
    {
#pragma warning disable CS0618 // The refusal is the whole point of keeping the obsolete property.
        if (StartupTaskOptions.LockTimeoutMs is not { } lockTimeoutMs)
            return;

        throw new InvalidOperationException(
            $"'{nameof(StartupTaskOptions)}:{nameof(StartupTaskOptions.LockTimeoutMs)}' on '{GetType().Name}' is set to '{lockTimeoutMs}' and is retired. " +
            "The activity version reconciler takes no lock any more: it runs on every node at shell start, because each node must " +
            "reconcile the assemblies and catalogs it loaded itself (#2192). Remove this setting.");
#pragma warning restore CS0618
    }
}
