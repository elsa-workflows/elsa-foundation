using CShells.Features;
using Elsa.Activities.Design.Reconciliation.Core;
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
public class ActivitiesDesignReconciliationFeature : IShellFeature
{
    public ActivityVersionReconcilerOptions ReconcilerOptions { get; set; } = new();

    public ActivityVersionReconcilerStartupTaskOptions StartupTaskOptions { get; set; } = new();

    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(ReconcilerOptions));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(StartupTaskOptions));

        // Hasher is replaceable per §2.6.2: register a scoped default; provider modules
        // may override by registering their own implementation first. Scoped per §2.5.1 —
        // it executes (hashes) rather than holding application-wide static state.
        services.AddScoped<IActivityDefinitionHasher, DefaultActivityDefinitionHasher>();

        services.AddScoped<IActivityVersionReconciler, ActivityVersionReconciler>();

        services.AddScoped<IStartupTask, ActivityVersionReconcilerStartupTask>();

        services.AddEventHandler<OnActivityVersionsReconciling, CollectActivityVersions>();
    }
}
