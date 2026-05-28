using CShells.Features;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Reconciliation.Options;
using Elsa.Activities.Design.Reconciliation.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Reconciliation;

[ShellFeature(
    name: "ActivitiesDesignReconciliation"
)]
public class ActivitiesDesignReconciliationFeature : IShellFeature
{
    public ActivityVersionReconcilerOptions ReconcilerOptions { get; set; } = new();

    public ActivityVersionReconcilerStartupTaskOptions StartupTaskOptions { get; set; } = new();

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(ReconcilerOptions));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(StartupTaskOptions));
        services.AddScoped<IActivityVersionReconciler, ActivityVersionReconciler>();
    }
}
