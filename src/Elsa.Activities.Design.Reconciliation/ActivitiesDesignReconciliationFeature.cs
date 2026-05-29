using CShells.Features;
using Elsa.Activities.Design.Reconciliation.Contracts;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Reconciliation.Options;
using Elsa.Activities.Design.Reconciliation.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Reconciliation;

public abstract class ActivitiesDesignReconciliationFeature : IShellFeature
{
    public ActivityVersionReconcilerOptions ReconcilerOptions { get; set; } = new();

    public ActivityVersionReconcilerStartupTaskOptions StartupTaskOptions { get; set; } = new();

    public virtual IEnumerable<IActivityReconciliationSource> Sources { get; set; } = [];

    public virtual void ConfigureServices(IServiceCollection services)
    {
        foreach (var source in Sources)
            services.AddSingleton(source);

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(ReconcilerOptions));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(StartupTaskOptions));

        // Hasher is replaceable per §2.6.2: register a singleton default; provider modules
        // may override by registering their own implementation first.
        services.AddSingleton<IActivityDefinitionHasher, DefaultActivityDefinitionHasher>();

        services.AddScoped<IActivityVersionReconciler, ActivityVersionReconciler>();
    }
}
