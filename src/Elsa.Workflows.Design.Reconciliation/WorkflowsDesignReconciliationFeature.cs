using CShells.Features;
using Elsa.Events.Core.Extensions;
using Elsa.Workflows.Design.Reconciliation.Contracts;
using Elsa.Workflows.Design.Reconciliation.Core;
using Elsa.Workflows.Design.Reconciliation.Options;
using Elsa.Workflows.Design.Reconciliation.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Reconciliation;

/// <summary>
/// Workflow-design reconciliation base feature. Mirrors the Activities-side pattern: abstract
/// base that registers the reconciler service, options, and the universal handler; concrete
/// features extend by overriding <see cref="Sources"/> to bring their own
/// <see cref="IWorkflowReconciliationSource"/> implementations. Per framework §2.5,
/// inheritance is the sanctioned cross-feature structural coupling — source-variant features
/// (Elsa3 import, file-system, git, CRM pull, …) extend this base rather than ship as
/// sibling sub-packages.
/// </summary>
public abstract class WorkflowsDesignReconciliationFeature : IShellFeature
{
    public WorkflowVersionReconcilerOptions ReconcilerOptions { get; set; } = new();

    public WorkflowVersionReconcilerStartupTaskOptions StartupTaskOptions { get; set; } = new();

    public virtual IEnumerable<IWorkflowReconciliationSource> Sources { get; set; } = [];

    public virtual void ConfigureServices(IServiceCollection services)
    {
        foreach (var source in Sources)
            services.AddSingleton(source);

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(ReconcilerOptions));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(StartupTaskOptions));

        services.AddScoped<IWorkflowVersionReconciler, WorkflowsVersionReconciler>();

        services.AddEventHandlersFrom(typeof(WorkflowsDesignReconciliationFeature).Assembly);
    }
}
